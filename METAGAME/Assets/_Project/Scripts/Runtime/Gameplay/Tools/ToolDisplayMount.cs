using System.Collections.Generic;
using UnityEngine;

namespace Metagame.Gameplay
{
    /// <summary>Keeps an initially displayed tool seated until its first pickup.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class ToolDisplayMount : MonoBehaviour
    {
        [SerializeField] private Transform stand;
        private FixedJoint _joint;
        private Rigidbody _body;
        private Vector3 _initialPosition;
        private Quaternion _initialRotation;
        private bool _released;
        private readonly List<(Collider tool, Collider stand)> _ignoredPairs = new();

        private void Awake()
        {
            _body = GetComponent<Rigidbody>();
            if (stand == null) { enabled = false; return; }
            _initialPosition = _body.position;
            _initialRotation = _body.rotation;
            // A dynamic body remains eligible for PlayerRig's ordinary tool pickup.
            // Do not change gravity, constraints or the carried-body snapshot.
            _joint = gameObject.AddComponent<FixedJoint>();
            _joint.anchor = Vector3.zero;
            _joint.autoConfigureConnectedAnchor = false;
            _joint.connectedAnchor = _body.position;
            _joint.breakForce = Mathf.Infinity;
            _joint.breakTorque = Mathf.Infinity;
            foreach (var toolCollider in GetComponentsInChildren<Collider>())
                foreach (var standCollider in stand.GetComponentsInChildren<Collider>())
                    if (!Physics.GetIgnoreCollision(toolCollider, standCollider))
                    {
                        Physics.IgnoreCollision(toolCollider, standCollider, true);
                        _ignoredPairs.Add((toolCollider, standCollider));
                    }
        }

        private void LateUpdate()
        {
            // A late-joining network ghost can first receive an already-dropped pose,
            // without seeing the original held transition. Never pull it back to the display.
            if (!_released && _body != null && _body.isKinematic &&
                (Vector3.Distance(_body.position, _initialPosition) > .02f ||
                 Quaternion.Angle(_body.rotation, _initialRotation) > 2f))
                Release();
        }

        public void Release()
        {
            if (_released) return;
            _released = true;
            if (_joint != null)
            {
                if (Application.isPlaying) Destroy(_joint);
                else DestroyImmediate(_joint);
                _joint = null;
            }
            foreach (var pair in _ignoredPairs)
                if (pair.tool != null && pair.stand != null)
                    Physics.IgnoreCollision(pair.tool, pair.stand, false);
            _ignoredPairs.Clear();
            enabled = false;
        }

        private void OnDisable() => Release();
    }
}
