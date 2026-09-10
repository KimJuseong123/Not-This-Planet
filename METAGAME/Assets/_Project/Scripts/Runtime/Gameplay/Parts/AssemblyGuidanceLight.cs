using UnityEngine;

namespace Metagame.Gameplay.Parts
{
    /// <summary>조립 판정을 변경하지 않고 기존 층 상태를 조립대의 색으로 보여준다.</summary>
    public sealed class AssemblyGuidanceLight : MonoBehaviour
    {
        [SerializeField] private ShipAssembly _assembly;
        [SerializeField] private SpaceshipPart _tractorPart;
        [SerializeField] private Renderer[] _lamps;
        [SerializeField] private Material[] _floorMaterials;
        [SerializeField] private Material _completeMaterial;
        private int _shown = -2;

        public void Configure(ShipAssembly assembly, Renderer[] lamps, Material[] floorMaterials, Material completeMaterial, SpaceshipPart tractorPart = null)
        {
            _assembly = assembly;
            _tractorPart = tractorPart;
            _lamps = lamps;
            _floorMaterials = floorMaterials;
            _completeMaterial = completeMaterial;
            _shown = -2;
        }

        private void OnEnable() => _shown = -2;
        private void LateUpdate() => Refresh();

        public static int RequiredFloor(System.Collections.Generic.IReadOnlyList<ShipAssembly.FloorState> floors)
        {
            for (int i = 0; i < floors.Count; i++)
                if (floors[i] == ShipAssembly.FloorState.Empty
                    || floors[i] == ShipAssembly.FloorState.WrongPart
                    || floors[i] == ShipAssembly.FloorState.Tilted) return i;
            return -1;
        }

        public void Refresh()
        {
            if (_assembly == null || _lamps == null || _floorMaterials == null) return;
            // ShipAssembly owns the gameplay state; this is presentation only on every peer.
            int next = RequiredSignal(_assembly.Floors, _tractorPart == null || _tractorPart.State != PartState.Locked);
            if (next == _shown) return;
            var material = next < 0 ? _completeMaterial : next < _floorMaterials.Length ? _floorMaterials[next] : null;
            if (material == null) return;
            foreach (var lamp in _lamps)
                if (lamp != null) lamp.sharedMaterial = material;
            _shown = next;
        }

        public static int RequiredSignal(System.Collections.Generic.IReadOnlyList<ShipAssembly.FloorState> floors, bool tractorUnlocked)
        {
            int next = RequiredFloor(floors);
            // Engine first; unlock the tractor before fetching the middle modules.
            // The same green material is used again for final top-module assembly.
            return next > 0 && next < 3 && !tractorUnlocked ? 3 : next;
        }
    }
}
