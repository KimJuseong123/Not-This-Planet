using System.Collections.Generic;
using UnityEngine;

namespace Metagame.Gameplay.Stage2
{
    public sealed partial class Stage2Yard
    {
        // Bounded world props use the yard's existing authoritative snapshot channel.
        const int MaxLooseTools = 16;
        int _nextToolId = 10000;
        readonly Dictionary<int, Rigidbody> _looseTools = new();
        readonly List<Stage2Target> _looseToolTargets = new();
        readonly List<int> _removedToolIds = new();
        public IReadOnlyList<Stage2Target> LooseToolTargets => _looseToolTargets;

        void ThrowWrench(uint id, Worker worker, Vector3 aim)
        {
            if (worker.State.Tool != YardTool.Wrench || _state.Pilot == id || _state.Climber == id
                || _state.LadderCarrier == id) return;
            worker.LastUse = Time.time;
            if (_looseTools.Count >= MaxLooseTools) RemoveLooseTool(_looseToolTargets[0].Id);
            Vector3 origin = worker.Rig.Position + Vector3.up * .45f;
            Vector3 direction = aim.normalized;
            // Do not spawn on the other side of a wall when the player stands close to it.
            float distance = .85f;
            foreach (var hit in Physics.RaycastAll(origin, direction, distance, ~0, QueryTriggerInteraction.Ignore))
                if (hit.collider.GetComponentInParent<PlayerRig>() != worker.Rig)
                    distance = Mathf.Min(distance, Mathf.Max(.05f, hit.distance - .2f));
            var body = CreateLooseTool(_nextToolId++, origin + direction * distance, Quaternion.LookRotation(direction));
            foreach (var ownerCollider in worker.Rig.GetComponentsInChildren<Collider>())
                Physics.IgnoreCollision(body.GetComponent<Collider>(), ownerCollider);
            body.linearVelocity = direction * 8f + Vector3.up * 1.5f;
            body.angularVelocity = body.transform.right * 12f;
            worker.State.Tool = YardTool.None;
        }

        Rigidbody CreateLooseTool(int id, Vector3 position, Quaternion rotation)
        {
            var root = new GameObject("Thrown Stage2 wrench " + id);
            root.transform.SetParent(transform);
            root.transform.SetPositionAndRotation(position, rotation);
            BuildWrenchVisual(root.transform, GetComponentInChildren<Renderer>().sharedMaterial);
            var collider = root.AddComponent<BoxCollider>();
            collider.center = new Vector3(0, .07f, 0);
            collider.size = new Vector3(.2f, .52f, .08f);
            var body = root.AddComponent<Rigidbody>();
            body.mass = 1f;
            body.isKinematic = !_authority;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            var target = root.AddComponent<Stage2Target>();
            target.Configure(id, YardTargetKind.Wrench, 0, "떨어진 렌치 줍기");
            _looseTools.Add(id, body);
            _looseToolTargets.Add(target);
            _targetMap.Add(id, target);
            return body;
        }

        void RemoveLooseTool(int id)
        {
            if (!_looseTools.TryGetValue(id, out var body)) return;
            _looseTools.Remove(id);
            _looseToolTargets.Remove(_targetMap[id]);
            _targetMap.Remove(id);
            body.gameObject.SetActive(false);
            Destroy(body.gameObject);
        }

        void CaptureLooseTools()
        {
            _removedToolIds.Clear();
            foreach (var pair in _looseTools)
                if (pair.Value.position.y < -10f) _removedToolIds.Add(pair.Key);
            foreach (int id in _removedToolIds) RemoveLooseTool(id);
            if (_state.LooseTools.Length != _looseTools.Count)
                _state.LooseTools = new YardLooseToolState[_looseTools.Count];
            int index = 0;
            foreach (var pair in _looseTools)
                _state.LooseTools[index++] = new YardLooseToolState
                { Id = pair.Key, Position = pair.Value.position, Rotation = pair.Value.rotation };
        }

        void ApplyLooseTools(YardLooseToolState[] states)
        {
            _removedToolIds.Clear();
            foreach (int id in _looseTools.Keys) _removedToolIds.Add(id);
            if (states != null) foreach (var state in states)
            {
                _removedToolIds.Remove(state.Id);
                if (!_looseTools.TryGetValue(state.Id, out var body))
                    body = CreateLooseTool(state.Id, state.Position, state.Rotation);
                body.position = state.Position;
                body.rotation = state.Rotation;
                _nextToolId = Mathf.Max(_nextToolId, state.Id + 1);
            }
            foreach (int id in _removedToolIds) RemoveLooseTool(id);
        }

        public static void BuildWrenchVisual(Transform parent, Material material)
        {
            WrenchPiece(parent, Vector3.zero, new Vector3(.06f, .38f, .035f), material);
            WrenchPiece(parent, new Vector3(0, .22f, 0), new Vector3(.18f, .06f, .05f), material);
            WrenchPiece(parent, new Vector3(-.07f, .27f, 0), new Vector3(.04f, .1f, .05f), material);
            WrenchPiece(parent, new Vector3(.07f, .27f, 0), new Vector3(.04f, .1f, .05f), material);
        }

        static void WrenchPiece(Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            var piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
            piece.transform.SetParent(parent, false);
            piece.transform.localPosition = position;
            piece.transform.localScale = scale;
            var collider = piece.GetComponent<Collider>();
            collider.enabled = false;
            Destroy(collider);
            piece.GetComponent<Renderer>().sharedMaterial = material;
        }
    }
}
