using UnityEngine;

namespace Metagame.Gameplay.Hazards
{
    /// <summary>
    /// Authoritative body corrections stay immediate; only the replica's artwork
    /// retains its previous world pose and eases the error out (state synchronization).
    /// Never move colliders into the visual hierarchy.
    /// </summary>
    public sealed class MeteorReplicaVisual : MonoBehaviour
    {
        Transform _visual;
        Vector3 _beforePosition;
        Quaternion _beforeRotation;
        Vector3 _error;
        Quaternion _rotationError = Quaternion.identity;
        bool _initialized;
        public const float SnapDistance = 2f;

        public Transform Visual => _visual;

        public void Initialize()
        {
            if (_visual != null) return;
            var root = new GameObject("Replica visual").transform;
            root.gameObject.layer = gameObject.layer;
            root.SetParent(transform, false);
            // The production prefab has a separate model branch. Capture it once,
            // including its trail, but leave any physical branch untouched.
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);
                if (child == root || child.GetComponentInChildren<Renderer>(true) == null) continue;
                if (child.GetComponentInChildren<Collider>(true) != null ||
                    child.GetComponentInChildren<Rigidbody>(true) != null) continue;
                child.SetParent(root, false);
            }
            // The generated fallback sphere has its mesh on the physics root.
            var mesh = GetComponent<MeshFilter>();
            var renderer = GetComponent<MeshRenderer>();
            if (mesh != null && renderer != null)
            {
                root.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh.sharedMesh;
                var copy = root.gameObject.AddComponent<MeshRenderer>();
                copy.sharedMaterials = renderer.sharedMaterials;
                copy.shadowCastingMode = renderer.shadowCastingMode;
                copy.receiveShadows = renderer.receiveShadows;
                copy.enabled = renderer.enabled;
                renderer.enabled = false;
            }
            _visual = root;
        }

        public void BeforeCorrection()
        {
            Initialize();
            _beforePosition = _visual.position;
            _beforeRotation = _visual.rotation;
        }

        public void AfterCorrection(bool snap = false)
        {
            if (_visual == null) return;
            var error = _beforePosition - transform.position;
            if (!_initialized || snap || error.sqrMagnitude > SnapDistance * SnapDistance)
            {
                ResetPose();
                return;
            }
            _error = error;
            _rotationError = _beforeRotation * Quaternion.Inverse(transform.rotation);
            Apply();
        }

        public void ResetPose()
        {
            _error = Vector3.zero;
            _rotationError = Quaternion.identity;
            _initialized = true;
            Apply();
        }

        public void Advance(float deltaTime)
        {
            if (_visual == null || deltaTime <= 0f) return;
            if (_error == Vector3.zero && _rotationError == Quaternion.identity) return;
            // Frame-rate independent: half-life ~35ms, faster for larger corrections.
            float decay = Mathf.Exp(-Mathf.Lerp(20f, 40f, Mathf.Clamp01(_error.magnitude)) * deltaTime);
            _error *= decay;
            _rotationError = Quaternion.Slerp(Quaternion.identity, _rotationError, decay);
            if (_error.sqrMagnitude < 0.00000001f) _error = Vector3.zero;
            if (Quaternion.Angle(_rotationError, Quaternion.identity) < 0.01f)
                _rotationError = Quaternion.identity;
            Apply();
        }

        void Apply()
        {
            if (_visual != null)
                _visual.SetPositionAndRotation(transform.position + _error,
                    _rotationError * transform.rotation);
        }

        void LateUpdate() => Advance(Time.deltaTime);
    }
}
