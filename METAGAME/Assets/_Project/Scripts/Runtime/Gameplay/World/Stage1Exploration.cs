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
            public string Clue { get; private set; }
            internal void SetClue(string clue) => Clue = clue;
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
            // The variant keeps the tablet, but its clues must describe the newly authored routes.
            if (sorted.Length > 0 && sorted[0] != null && sorted[0].GetComponent<AstraV2ManualPart>() != null)
                definitions = CreateAstraV2Definitions();
            AuthoredMapSettings authored = null;
            if (sorted.Length > 0 && sorted[0] != null)
                foreach (var root in sorted[0].gameObject.scene.GetRootGameObjects())
                { authored = root.GetComponentInChildren<AuthoredMapSettings>(); if (authored != null) break; }
            for (var i = 0; i < definitions.Length; i++)
            {
                var info = definitions[i];
                if (authored != null && authored.explorationClues != null && i < authored.explorationClues.Length
                    && !string.IsNullOrWhiteSpace(authored.explorationClues[i])) info.SetClue(authored.explorationClues[i]);
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

        public void ToggleTablet()
        {
            SetTabletOpen(!TabletOpen);
        }

        public void PreviousTabletPage()
        {
            SetTabletPage(TabletPage - 1);
        }

        public void NextTabletPage()
        {
            SetTabletPage(TabletPage + 1);
        }

        public void SetTabletOpen(bool open)
        {
            if (TabletOpen == open) return;

            TabletOpen = open;
            TabletStateChanged?.Invoke(TabletOpen);
        }

        /// <summary>새 라운드에서 발견 상태와 태블릿을 초기화한다.</summary>
        public void ResetRun()
        {
            DiscoveredCount = 0;
            DiscoveryNotice = string.Empty;
            _noticeUntil = 0f;

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
            PartDiscovered?.Invoke(info);
            return true;
        }

        static PartInfo[] CreateAstraV2Definitions() => new[]
        {
            new PartInfo(1, PartKind.Robot, "SF_Stage1_Spaceship_Robot", "PF_Stage1_Spaceship_Robot",
                "주 추진 모듈", "주황빛 공터. 고정못을 풀고 롤러와 손으로 중앙 조립대까지 옮긴다"),
            new PartInfo(2, PartKind.HoverPod, "SF_Stage1_Spaceship_HoverPod", "PF_Stage1_Spaceship_HoverPod",
                "부양·안정화 모듈", "청록빛 절벽. 돌부리를 올라 고정못을 푼 뒤 화물대 손잡이를 잡아 내린다. 놓으면 멈춘다"),
            new PartInfo(3, PartKind.Capsule, "SF_Stage1_Spaceship_Capsule", "PF_Stage1_Spaceship_Capsule",
                "조종석 캡슐", "보랏빛 밀실. 바늘을 홈에 맞추면 발판이 나타난다. 동료가 밟는 동안 롤러로 꺼낸다"),
            new PartInfo(4, PartKind.TractorCraft, "SF_Stage1_TractorCraft", "PF_Stage1_TractorCraft",
                "상단 조립·견인 모듈", "연두빛 낮은 밀실. 눕힌 파츠를 함께 밀어 모퉁이를 돌린다. 밖까지 견인 없이 운반할 수 있다"),
        };

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
