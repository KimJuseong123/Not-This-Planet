using System;
using System.Collections.Generic;
using Metagame.Gameplay.Parts;
using UnityEngine;

namespace Metagame.Gameplay
{
    /// <summary>
    /// 1장 탐험 단계의 진행 상태를 소유한다.
    ///
    /// 파츠를 실제로 운반하거나 조립하는 책임은 기존 파츠 시스템에 남겨 두고,
    /// 여기서는 "무엇을 찾아냈는가"와 태블릿 표시 상태만 관리한다. 발견은
    /// 플레이어가 파츠 가까이 다가가는 것으로 판정해, 기지 주변을 직접 살피는
    /// 첫 번째 튜토리얼 루프를 만든다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Stage1Exploration : MonoBehaviour
    {
        public const int RequiredPartCount = 4;
        public const int TabletPageCount = 4;
        public const float DefaultDiscoveryRadius = 4.5f;
        const float DefaultNoticeSeconds = 3f;

        public enum PartKind
        {
            HoverPod,
            Robot,
            Capsule,
            TractorCraft,
        }

        /// <summary>태블릿 카드에 표시할 Stage1 파츠 한 칸.</summary>
        public sealed class PartInfo
        {
            public int Number { get; }
            public PartKind Kind { get; }
            public string AssetName { get; }
            public string PrefabName { get; }
            public string DisplayName { get; }
            public string Clue { get; }
            public SpaceshipPart Part { get; private set; }
            public bool IsDiscovered { get; private set; }

            public string StatusText
            {
                get
                {
                    if (!IsDiscovered) return "미발견";
                    if (Part == null) return "씬 배치 확인";

                    var tractor = Part.GetComponent<PartTractorActivator>();
                    if (tractor != null && tractor.IsActivated) return "견인 장치 활성화";

                    return Part.State switch
                    {
                        PartState.Locked => "발견",
                        PartState.Free => "고정 해제",
                        PartState.Carried => "운반 중",
                        PartState.Placed => "배치됨",
                        PartState.Welded => "조립 완료",
                        _ => "확인 필요",
                    };
                }
            }

            public PartInfo(int number, PartKind kind, string assetName, string prefabName,
                            string displayName, string clue)
            {
                Number = number;
                Kind = kind;
                AssetName = assetName;
                PrefabName = prefabName;
                DisplayName = displayName;
                Clue = clue;
            }

            internal void Bind(SpaceshipPart part)
            {
                Part = part;
            }

            internal bool Discover()
            {
                if (IsDiscovered) return false;
                IsDiscovered = true;
                return true;
            }

            internal void Reset()
            {
                IsDiscovered = false;
            }
        }

        [SerializeField, Min(0f)] float _discoveryRadius = DefaultDiscoveryRadius;
        [SerializeField, Min(0f)] float _noticeSeconds = DefaultNoticeSeconds;

        readonly List<PartInfo> _parts = new(RequiredPartCount);
        PlayerRig _player;
        float _noticeUntil;

        public IReadOnlyList<PartInfo> Parts => _parts;
        public int DiscoveredCount { get; private set; }
        public bool IsComplete => DiscoveredCount >= RequiredPartCount;
        public bool TabletOpen { get; private set; }
        public TabletGuideState Guide { get; } = new TabletGuideState();

        /// <summary>필요한 순간에 뜨는 한 줄 (S15P21B202-665).</summary>
        public OnboardingNotices Notices { get; } = new OnboardingNotices();
        public int TabletPage { get; private set; }
        public float DiscoveryRadius => _discoveryRadius;
        public string DiscoveryNotice { get; private set; }
        public bool HasActiveNotice => !string.IsNullOrEmpty(DiscoveryNotice) && Time.time < _noticeUntil;
        public event Action<PartInfo> PartDiscovered;
        public event Action<bool> TabletStateChanged;
        public event Action<int> TabletPageChanged;

        /// <summary>
        /// 씬에 배치된 파츠를 Part_1~Part_4 순서로 카탈로그 정의에 연결한다.
        /// 이 검색은 부트스트랩 시 한 번만 수행하며 프레임 루프에서는 반복하지 않는다.
        /// </summary>
        public void Configure(SpaceshipPart[] sceneParts)
        {
            _parts.Clear();

            var sorted = sceneParts == null ? Array.Empty<SpaceshipPart>() : (SpaceshipPart[])sceneParts.Clone();
            Array.Sort(sorted, CompareParts);

            var definitions = CreateDefinitions();
            for (var i = 0; i < definitions.Length; i++)
            {
                var info = definitions[i];
                if (i < sorted.Length) info.Bind(sorted[i]);
                _parts.Add(info);
            }

            ResetRun();
        }

        /// <summary>플레이어를 연결하고 탐험을 새 라운드 상태로 시작한다.</summary>
        public void Begin(PlayerRig player)
        {
            _player = player;
            ResetRun();
        }

        /// <summary>
        /// 세션이 매 프레임 호출한다. 발견 판정은 4개 파츠만 대상으로 하므로
        /// 물리 쿼리나 할당 없이 작은 고정 비용으로 처리한다.
        /// </summary>
        public void Tick()
        {
            if (_player == null) return;
            DiscoverAt(_player.Position);
        }

        /// <summary>플레이어 위치가 파츠의 발견 반경에 들어왔는지 검사한다.</summary>
        public int DiscoverAt(Vector3 playerPosition)
        {
            var discovered = 0;
            var radiusSquared = _discoveryRadius * _discoveryRadius;

            foreach (var info in _parts)
            {
                if (info == null || info.IsDiscovered || info.Part == null) continue;

                var offset = info.Part.transform.position - playerPosition;
                offset.y = 0f;
                if (offset.sqrMagnitude > radiusSquared) continue;

                if (Discover(info)) discovered++;
            }

            return discovered;
        }

        /// <summary>연출·테스트에서 특정 파츠를 발견 처리할 때 사용한다.</summary>
        public bool TryDiscover(SpaceshipPart part)
        {
            if (part == null) return false;

            foreach (var info in _parts)
            {
                if (info == null || info.Part != part) continue;
                return Discover(info);
            }

            return false;
        }

        /// <summary>
        /// 안내를 흘린다 (S15P21B202-665). <b>태블릿이 열려 있는 동안에는 부르지
        /// 않는다</b> — 그 사이에 사라지면 아무도 못 본 것이 된다.
        ///
        /// <para>파츠를 쌓은 순간은 이벤트를 구독하는 대신 <b>훑어서</b> 본다.
        /// 파츠는 씬을 찾은 뒤에야 붙으므로(<see cref="Configure"/>) 구독 시점을
        /// 맞추기가 더 까다롭다.</para>
        /// </summary>
        public void TickOnboarding(float deltaTime)
        {
            Notices.Raise(OnboardingCue.Start);
            for (var i = 0; i < _parts.Count; i++)
            {
                var part = _parts[i]?.Part;
                if (part != null && part.State == Metagame.Gameplay.Parts.PartState.Placed)
                {
                    Notices.Raise(OnboardingCue.PartStacked);
                    break;
                }
            }
            if (!TabletOpen) Notices.Tick(deltaTime);
        }

        public void ToggleTablet()
        {
            SetTabletOpen(!TabletOpen);
        }

        /// <summary>
        /// 앞 쪽으로. <b>진행도에서 더 앞으로 가면 안내로 되돌아간다</b>
        /// (S15P21B202-657) — 안내 4쪽 · 진행도 1쪽 · 조작 3쪽이 한 줄이다.
        /// </summary>
        public void PreviousTabletPage()
        {
            if (TabletOpen && Guide.Visible) { Guide.Move(-1); return; }
            if (TabletOpen && TabletPage == 0 && Guide.Enabled)
            {
                Guide.OpenAt(TabletGuideState.PageCount - 1);
                return;
            }
            SetTabletPage(TabletPage - 1);
        }

        /// <summary>
        /// 뒤 쪽으로. <b>마지막 안내에서 더 넘기면 진행도로 나간다</b> (S15P21B202-657).
        /// 확인 키를 따로 누르지 않아도 된다.
        /// </summary>
        public void NextTabletPage()
        {
            if (TabletOpen && Guide.Visible)
            {
                if (Guide.Move(1)) SetTabletPage(0);
                return;
            }
            SetTabletPage(TabletPage + 1);
        }

        /// <summary>
        /// 안내를 건너뛰거나 다시 연다 (S15P21B202-657).
        ///
        /// <para><b>더 이상 관문이 아니다.</b> 넘기다 보면 저절로 빠져나오므로 이것은
        /// 지름길일 뿐이다 — 안내를 다 읽지 않고 진행도로 바로 가고 싶을 때 쓴다.</para>
        /// </summary>
        public void ActivateTabletGuide()
        {
            if (!TabletOpen || !Guide.Enabled) return;
            if (Guide.Visible)
            {
                if (Guide.Confirm()) SetTabletPage(0);
            }
            else Guide.Open();
        }

        public void SetTabletOpen(bool open)
        {
            if (TabletOpen == open) return;

            TabletOpen = open;
            if (open)
            {
                // 그 순간에 맞는 안내가 있으면 **그 쪽이 이미 펴져 있다** (S15P21B202-665).
                // 태블릿을 대신 열어 주지는 않는다 — 1인칭 화면을 가로채면 운석에 맞는다.
                var page = Notices.TakeGuidePage();
                if (page >= 0) Guide.OpenAt(page);
                else Guide.OnTabletOpened();
                Notices.OnTabletOpened();
            }
            TabletStateChanged?.Invoke(TabletOpen);
        }

        /// <summary>새 라운드에서 발견 상태와 태블릿을 초기화한다.</summary>
        public void ResetRun()
        {
            DiscoveredCount = 0;
            DiscoveryNotice = string.Empty;
            _noticeUntil = 0f;
            Guide.ResetRun();
            Notices.ResetRun();

            SetTabletPage(0);

            foreach (var info in _parts)
                if (info != null)
                    info.Reset();

            SetTabletOpen(false);
        }

        void SetTabletPage(int page)
        {
            var next = Mathf.Clamp(page, 0, TabletPageCount - 1);
            if (TabletPage == next) return;

            TabletPage = next;
            TabletPageChanged?.Invoke(TabletPage);
        }

        public string CurrentHint
        {
            get
            {
                if (IsComplete) return "네 파츠를 모두 찾았다! 태블릿을 닫고 고정쇠를 풀어 보자";
                if (DiscoveredCount == 0) return "Tab 태블릿으로 파츠 실루엣을 확인하고 기지 주변을 살펴보자";
                return "파츠 " + DiscoveredCount + " / " + RequiredPartCount + " 발견 — 나머지 건물도 찾아보자";
            }
        }

        bool Discover(PartInfo info)
        {
            if (!info.Discover()) return false;

            DiscoveredCount++;
            DiscoveryNotice = "파츠 발견 · " + info.DisplayName;
            _noticeUntil = Time.time + _noticeSeconds;
            // 찾았으면 다음에 할 일은 고정못 빼기다 (S15P21B202-665).
            Notices.Raise(OnboardingCue.PartFound);
            PartDiscovered?.Invoke(info);
            return true;
        }

        static PartInfo[] CreateDefinitions() => new[]
        {
            new PartInfo(1, PartKind.Robot,
                "SF_Stage1_Spaceship_Robot", "PF_Stage1_Spaceship_Robot",
                "주 추진·견인 모듈", "고정못을 모두 풀면 파츠를 옮기는 견인 장치로 작동하는 정비 로봇"),
            new PartInfo(2, PartKind.HoverPod,
                "SF_Stage1_Spaceship_HoverPod", "PF_Stage1_Spaceship_HoverPod",
                "부양·안정화 모듈", "납작한 부양 추진부. 완성된 구조선의 하부를 안정화하는 파츠"),
            new PartInfo(3, PartKind.Capsule,
                "SF_Stage1_Spaceship_Capsule", "PF_Stage1_Spaceship_Capsule",
                "조종석 캡슐", "둥근 조종석 캡슐. 기지의 작은 관측실처럼 보이는 파츠"),
            new PartInfo(4, PartKind.TractorCraft,
                "SF_Stage1_TractorCraft", "PF_Stage1_TractorCraft",
                "상단 조립 모듈", "작은 비행접시 모양의 최상단 조립 파츠"),
        };

        static int CompareParts(SpaceshipPart left, SpaceshipPart right)
        {
            if (left == null) return right == null ? 0 : 1;
            if (right == null) return -1;

            var leftNumber = PartNumber(left.name);
            var rightNumber = PartNumber(right.name);
            var comparison = leftNumber.CompareTo(rightNumber);
            return comparison != 0 ? comparison : string.CompareOrdinal(left.name, right.name);
        }

        static int PartNumber(string partName)
        {
            if (string.IsNullOrEmpty(partName) || !partName.StartsWith("Part_", StringComparison.Ordinal))
                return int.MaxValue;

            return int.TryParse(partName.Substring("Part_".Length), out var number)
                ? number
                : int.MaxValue;
        }
    }
}
