using UnityEngine;

namespace Metagame.Gameplay.Parts
{
    /// <summary>조립 판정은 AssemblyDock에 두고, 씬에 배치한 모델의 고정턱과 상태등만 갱신한다.</summary>
    [DisallowMultipleComponent]
    public sealed class AssemblyDockVisual : MonoBehaviour
    {
        [SerializeField] private Transform[] _jaws;
        [SerializeField] private Vector3[] _openPositions;
        [SerializeField] private Vector3[] _closedPositions;
        [SerializeField] private Renderer[] _statusRenderers;
        [SerializeField] private Material _waitingMaterial;
        [SerializeField] private Material _lockedMaterial;
        private bool _initialized, _shownDocked;

        public void Configure(Transform[] jaws, Vector3[] openPositions, Vector3[] closedPositions,
            Renderer[] statusRenderers, Material waiting, Material locked)
        {
            _jaws = jaws;
            _openPositions = openPositions;
            _closedPositions = closedPositions;
            _statusRenderers = statusRenderers;
            _waitingMaterial = waiting;
            _lockedMaterial = locked;
            _initialized = false;
        }

        private void OnEnable() => _initialized = false;

        public void ShowDocked(bool docked, float deltaTime)
        {
            if (_jaws == null || _openPositions == null || _closedPositions == null) return;
            var count = Mathf.Min(_jaws.Length, Mathf.Min(_openPositions.Length, _closedPositions.Length));
            var blend = _initialized ? 1f - Mathf.Exp(-12f * Mathf.Max(0f, deltaTime)) : 1f;
            for (var i = 0; i < count; i++)
            {
                if (_jaws[i] == null) continue;
                var target = docked ? _closedPositions[i] : _openPositions[i];
                _jaws[i].localPosition = Vector3.Lerp(_jaws[i].localPosition, target, blend);
            }
            if (!_initialized || docked != _shownDocked)
            {
                var material = docked ? _lockedMaterial : _waitingMaterial;
                if (_statusRenderers != null && material != null)
                    foreach (var status in _statusRenderers)
                    {
                        if (status == null) continue;
                        var materials = status.sharedMaterials;
                        if (materials.Length < 2) continue;
                        materials[1] = material;
                        status.sharedMaterials = materials;
                    }
            }
            _shownDocked = docked;
            _initialized = true;
        }
    }
}
