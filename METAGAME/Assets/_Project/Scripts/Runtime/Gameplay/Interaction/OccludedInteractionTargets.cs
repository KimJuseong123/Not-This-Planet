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

            /// <summary>
            /// 이 덩어리의 테두리 색 (S15P21B202-724).
            ///
            /// <para><b>덩어리마다 다를 수 있다.</b> 렌더러 피처가 덩어리 하나에 마스크와
            /// 블릿을 한 벌씩 돌리므로, 색을 그 벌의 속성 블록에 실으면 된다 —
            /// 셰이더도 마스크 포맷도 안 건드린다.</para>
            ///
            /// <para><b>캐시하지 않는다.</b> 처음에는 <see cref="RefreshMeshes"/> 에서
            /// 한 번 정해 담았는데, 그것은 0.5초마다만 돈다 — <c>downed</c> 는 그 사이에
            /// 바뀌는 SyncVar 라 <b>쓰러져도 최대 0.5초 동안 초록이었다.</b> 쓰러진 것을
            /// 알아채라고 만든 기능에서 가장 중요한 순간이 늦는다. AI 리뷰가 잡았다.</para>
            /// </summary>
            public readonly Color Outline;

            public Target(Renderer renderer, int count, float group, Color outline)
            { Renderer = renderer; SubMeshCount = count; Group = group; Outline = outline; }
        }

        /// <summary>도구·파츠. -591 이 정한 색이다.</summary>
        public static readonly Color ObjectColor = new Color(1f, 0.78f, 0.2f, 0.95f);

        /// <summary>멀쩡한 사람 (S15P21B202-724).</summary>
        public static readonly Color CrewColor = new Color(0.36f, 1f, 0.55f, 0.95f);

        /// <summary>
        /// 쓰러진 사람. <b><c>GameHud.TeamStatus.TeamDownedColor</c> 와 같은 값이다</b> —
        /// 팀 목록과 세계가 같은 뜻에 같은 색을 써야 한다 (S15P21B202-724).
        /// </summary>
        public static readonly Color DownedColor = new Color(1f, 0.24f, 0.28f, 0.95f);

        /// <summary>그 사람에게 칠할 색. 쓰러졌으면 빨강이다.</summary>
        public static Color ColorFor(PlayerRig rig) =>
            rig != null && rig.Downed ? DownedColor : CrewColor;

        /// <summary>
        /// 그 사람에게 테두리를 그리는가 (S15P21B202-724).
        ///
        /// <para><b>내 몸에는 안 그린다.</b> 1인칭에서는 인형이 이미 꺼져 있어 안
        /// 나오지만, 기절·밀기·탑승은 3인칭이라 내 등에 테두리가 붙어 화면을 먹는다.
        /// 규칙을 그 우연에 기대지 않는다.</para>
        ///
        /// <para><paramref name="localViewer"/> 가 <c>null</c> 이면(검증·컷씬처럼 누가
        /// '나' 인지 모를 때) <b>다 그린다</b> — 빼는 쪽으로 틀리면 아무도 안 보인다.</para>
        ///
        /// <para>보는 쪽을 인수로 받는 이유는 <b>전역 상태 없이 잴 수 있게</b> 하기
        /// 위해서다. 처음에는 안에서 <c>GameSession.Instance</c> 를 읽었는데, 그러면
        /// 앞 검사가 남긴 세션에 따라 결과가 달라진다 — 실제로 그렇게 깨졌다.</para>
        /// </summary>
        public static bool IsCrewTarget(PlayerRig rig, PlayerRig localViewer) =>
            rig != null && rig.gameObject.activeInHierarchy && rig != localViewer;

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
            if (root == null || !root.gameObject.activeInHierarchy) return false;

            // **사람은 통째로 하나의 대상이다** (S15P21B202-724). 아래의 "PlayerRig 아래는
            // 제외" 는 손에 든 도구가 테두리를 그리지 않게 하려던 것이라, 리그 자신은
            // 그 규칙에 걸리면 안 된다.
            if (root is PlayerRig rig)
                return IsCrewTarget(rig, GameSession.Instance != null
                    ? GameSession.Instance.LocalPlayer : null);

            if (root.GetComponentInParent<PlayerRig>() != null) return false;
            if (root is MeteorBat || root is PlasmaWeldingTool)
                return !((ToolBase)root).IsHeldForView;
            return root is SpaceshipPart part && PartSpec.Find(part.name) != null;
        }

        public bool Collect(Camera camera)
        {
            _targets.Clear();
            _seen.Clear();
            if (camera == null) return false;
            // 도구와 핵심 파츠만 수집하며, 매 프레임 전체 씬 검색은 피한다.
            if (Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + 0.5f;
                _roots.Clear();
                _roots.AddRange(Object.FindObjectsByType<MeteorBat>(FindObjectsSortMode.None));
                _roots.AddRange(Object.FindObjectsByType<PlasmaWeldingTool>(FindObjectsSortMode.None));
                _roots.AddRange(Object.FindObjectsByType<SpaceshipPart>(FindObjectsSortMode.None));
                // **사람은 이미 명부가 있다** (S15P21B202-724). FindObjectsByType 을 한 번
                // 더 돌 이유가 없고, 아래의 씬 이름 검사에도 걸리면 안 된다 — 플레이어는
                // 런타임에 만들어져 어느 씬에 들어갈지 이 파일이 알 수 없다.
                for (var i = 0; i < PlayerRig.All.Count; i++)
                    if (PlayerRig.All[i] != null) _roots.Add(PlayerRig.All[i]);
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
                // **색은 매 수집마다 다시 읽는다.** 담아 두면 쓰러진 순간을 놓친다.
                var draw = candidate.Draw;
                _targets.Add(candidate.Root is PlayerRig owner
                    ? new Target(draw.Renderer, draw.SubMeshCount, draw.Group, ColorFor(owner))
                    : draw);
            }
            return _targets.Count > 0;
        }

        void RefreshMeshes()
        {
            _candidates.Clear();
            for (var i = 0; i < _roots.Count; i++)
            {
                var root = _roots[i];
                if (root == null) continue;
                // 씬 이름 검사는 **씬에 놓인 것**에만 건다. 사람은 런타임에 만들어진다.
                if (!(root is PlayerRig) && root.gameObject.scene.name != "Stage1_Astra") continue;
                _renderers.Clear();
                root.GetComponentsInChildren(false, _renderers);
                foreach (var renderer in _renderers)
                {
                    // 파츠 자식으로 배치된 고정못도 파츠 외형에 섞이지 않게 제외한다.
                    if (renderer.GetComponentInParent<PartClamp>() != null) continue;
                    if (!_seen.Add(renderer)) continue;
                    Mesh mesh = null;
                    if (renderer is SkinnedMeshRenderer skinned) mesh = skinned.sharedMesh;
                    else if (renderer is MeshRenderer &&
                             renderer.TryGetComponent<MeshFilter>(out var filter)) mesh = filter.sharedMesh;
                    if (mesh == null) continue;
                    // 색은 여기서 안 정한다 — 이 함수는 0.5초마다만 돈다. Collect 가
                    // 매번 루트에서 다시 읽는다.
                    _candidates.Add((root, new Target(renderer, mesh.subMeshCount, i + 1,
                                                      ObjectColor)));
                }
            }
        }
    }
}
