using System.Collections.Generic;
using Metagame.Gameplay.Parts;
using UnityEngine;

namespace Metagame.Gameplay.Interaction
{
    /// <summary>원격 도구의 동작 스크립트가 꺼져 있어도 시각 객체는 포함한다.</summary>
    public sealed class OccludedInteractionTargets
    {
        public readonly struct Target
        {
            public readonly Renderer Renderer;
            public readonly int SubMeshCount;
            public readonly float Group;
            public Target(Renderer renderer, int count, float group)
            { Renderer = renderer; SubMeshCount = count; Group = group; }
        }

        readonly List<Component> _roots = new();
        readonly List<(Component Root, Target Draw)> _candidates = new();
        readonly List<Renderer> _renderers = new();
        readonly HashSet<Renderer> _seen = new();
        readonly List<Target> _targets = new();
        readonly Plane[] _planes = new Plane[6];
        float _nextRefresh = float.NegativeInfinity;
        public IReadOnlyList<Target> Targets => _targets;

        public static bool IsEligible(Component root)
        {
            if (root == null || !root.gameObject.activeInHierarchy ||
                root.GetComponentInParent<PlayerRig>() != null) return false;
            var tool = root.GetComponentInParent<ToolBase>();
            if (tool != null && tool.IsHeldForView) return false;
            // 고정못은 CanGrab=false인 동안에도 뽑기 상호작용이 가능하다.
            if (root is Grabbable grab && !grab.CanGrab &&
                grab.GetComponent<PartClamp>() == null) return false;
            return true;
        }

        public bool Collect(Camera camera)
        {
            _targets.Clear();
            _seen.Clear();
            if (camera == null) return false;
            // 런타임 생성/해제된 고정못도 발견하며, 매 프레임 전체 씬 검색은 피한다.
            if (Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + 0.5f;
                _roots.Clear();
                _roots.AddRange(Object.FindObjectsByType<ToolBase>(FindObjectsSortMode.None));
                _roots.AddRange(Object.FindObjectsByType<InteractableBase>(FindObjectsSortMode.None));
                _roots.AddRange(Object.FindObjectsByType<SpaceshipPart>(FindObjectsSortMode.None));
                _roots.AddRange(Object.FindObjectsByType<Grabbable>(FindObjectsSortMode.None));
                RefreshMeshes();
            }
            GeometryUtility.CalculateFrustumPlanes(camera, _planes);
            foreach (var candidate in _candidates)
            {
                var renderer = candidate.Draw.Renderer;
                if (!IsEligible(candidate.Root) || renderer == null || !renderer.enabled ||
                    !renderer.gameObject.activeInHierarchy || renderer.forceRenderingOff ||
                    (camera.cullingMask & (1 << renderer.gameObject.layer)) == 0 ||
                    !GeometryUtility.TestPlanesAABB(_planes, renderer.bounds)) continue;
                _targets.Add(candidate.Draw);
            }
            return _targets.Count > 0;
        }

        void RefreshMeshes()
        {
            _candidates.Clear();
            for (var i = 0; i < _roots.Count; i++)
            {
                var root = _roots[i];
                if (root == null || root.gameObject.scene.name != "Stage1_Astra") continue;
                _renderers.Clear();
                root.GetComponentsInChildren(false, _renderers);
                foreach (var renderer in _renderers)
                {
                    if (!_seen.Add(renderer)) continue;
                    Mesh mesh = null;
                    if (renderer is SkinnedMeshRenderer skinned) mesh = skinned.sharedMesh;
                    else if (renderer is MeshRenderer &&
                             renderer.TryGetComponent<MeshFilter>(out var filter)) mesh = filter.sharedMesh;
                    if (mesh != null) _candidates.Add((root, new Target(renderer, mesh.subMeshCount, i + 1)));
                }
            }
        }
    }
}