using System.Collections.Generic;
using UnityEngine;

namespace Metagame.Gameplay.Parts
{
    /// <summary>
    /// 우주선이 탑승 가능한 상태인지 판정한다(기획 S15P21B202-107 §22.1).
    ///
    /// 조건은 셋이다 — 파츠 네 개가 올바른 순서로 쌓였는가, 완성된 탑이 발사 구역 안에
    /// 있는가, 탑이 서 있는가.
    ///
    /// <b>용접 완료 여부는 보지 않는다.</b> 기획 §22.1 이 "용접 완료 여부는 탑승 조건으로
    /// 검사하거나 표시하지 않는다" 고 명시하며, §16.2 의 "파츠를 전부 쌓기만 하면 발사를
    /// 시도할 수 있으며 발사 하중으로 품질을 검증한다" 와 짝이다. 용접하지 않아도 발사를
    /// 시도할 수 있어야 하고, 그래서 하중 시험에서 실패한다. 조건에 넣으면
    /// <see cref="LaunchTest"/> 의 미용접 실패 경로가 도달 불가가 된다.
    ///
    /// 탑을 모으는 방법은 <see cref="LaunchTest"/> 와 같다 — <see cref="PartGraph"/> 다.
    /// 두 곳이 다른 방법을 쓰면 "탑승은 됐는데 시험이 파츠를 못 찾는" 어긋남이 생긴다.
    ///
    /// 예전에는 두 곳이 <c>PartSocket.ConnectedTo</c> 사슬 코드를 <b>각자</b> 갖고 있었다.
    /// S15P21B202-229 에서 탐색을 <see cref="PartGraph"/> 한 곳으로 옮겼다 — 비드로 옆에
    /// 이은 파츠는 소켓 사슬에 없으므로 사슬로는 탑의 절반만 보인다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ShipAssembly : MonoBehaviour
    {
        readonly List<SpaceshipPart> _tower = new();

        [SerializeField]
        [Tooltip("발사 구역의 중심. 비우면 이 오브젝트의 Transform 을 쓴다.")]
        Transform _launchPad;

        /// <summary>최하단(주 추진 엔진 모듈). 완성되지 않았으면 null 이다.</summary>
        public SpaceshipPart Engine { get; private set; }

        /// <summary>지금 모인 탑. 최하단부터 위로 정렬돼 있다.</summary>
        public IReadOnlyList<SpaceshipPart> Tower => _tower;

        /// <summary>탑승할 수 있는가. 매 <see cref="Evaluate"/> 마다 다시 계산된다.</summary>
        public bool IsBoardable { get; private set; }

        /// <summary>탑승할 수 없는 이유. 탑승 가능하면 빈 문자열이다.</summary>
        public string Blocker { get; private set; } = "";

        /// <summary>
        /// 층 하나의 상태. 태블릿이 층별로 색을 칠하는 데 쓴다(기획 §11.1,
        /// S15P21B202-345).
        ///
        /// <b><see cref="Blocker"/> 로는 이것을 만들 수 없다.</b> 그 문장은 <b>첫</b>
        /// 문제에서 멈춘 결과 하나뿐이고, 어느 층의 문제인지도 담고 있지 않다. 그리고
        /// §15.1 이 오류 문구를 금지하므로 문장을 화면에 띄울 수도 없다.
        /// </summary>
        public enum FloorState
        {
            /// <summary>이 층에 아무것도 없다.</summary>
            Empty = 0,

            /// <summary>올바른 파츠가 올라갔다. 아직 용접 전이다.</summary>
            Stacked,

            /// <summary>
            /// 올바른 파츠가 올라가고 <b>그 층의 접합이 끝났다.</b>
            ///
            /// <b>최하단 층은 놓이는 순간 이 상태다.</b> 용접은 접합 하나를 두 파츠가
            /// 나눠 갖는 것이고, <c>PartAssemblyLink</c> 는 완료 시 <b>위쪽</b> 파츠만
            /// <see cref="PartState.Welded"/> 로 올린다. 최하단은 아래에 상대가 없으므로
            /// 영원히 <see cref="PartState.Placed"/> 에 머문다 — 땅에 놓인 것이 곧 그
            /// 층의 접합이 끝난 것이다.
            ///
            /// 이것을 <see cref="Stacked"/> 로 두면 태블릿의 1층이 <b>끝낼 방법 없이</b>
            /// 계속 미완성으로 보인다. 플레이어가 할 일이 없는데 할 일이 남은 것처럼
            /// 보이는 것이 더 나쁘다.
            /// </summary>
            Welded,

            /// <summary>이 층에 다른 파츠가 올라가 있다. 기획 §7.1 의 순서를 어겼다.</summary>
            WrongPart,

            /// <summary>
            /// 올바른 파츠지만 <see cref="PhysicsScale.BoardingMaxTilt"/> 를 넘게 기울었다.
            /// </summary>
            Tilted,
        }

        readonly FloorState[] _floors = new FloorState[PartSpec.All.Length];

        /// <summary>
        /// 층별 상태. 0번이 최하단이고 <see cref="PartSpec.All"/> 과 같은 순서다.
        ///
        /// <b><see cref="IsBoardable"/> 판정과 달리 끝까지 돈다.</b> 판정은 첫 문제에서
        /// 멈추는 것이 옳지만(막힌 것은 이미 막힌 것이다), 태블릿은 3층이 틀렸는데 2층도
        /// 기울어 있는 상태를 <b>둘 다</b> 보여줘야 한다.
        /// </summary>
        public IReadOnlyList<FloorState> Floors => _floors;

        /// <summary>
        /// 다음으로 쌓아야 하는 층(0번이 최하단). 빈 층이 없으면 -1 이다.
        ///
        /// 기획 §11.1 의 "다음으로 쌓아야 함" 표시가 이 값을 쓴다. <b>어디에 놓아야
        /// 하는지는 알려주지 않는다</b> — §11 이 정확한 위치·중심점·정렬 가이드를
        /// 금지한다. 알려주는 것은 순서뿐이고, 그 순서는 §7.1 이 이미 공개한 것이다.
        /// </summary>
        public int NextFloor { get; private set; } = -1;

        public Transform LaunchPad => _launchPad != null ? _launchPad : transform;

        public void Configure(Transform launchPad) => _launchPad = launchPad;

        void FixedUpdate() => Evaluate();

        /// <summary>
        /// 조건을 다시 본다. 런타임에서는 <c>FixedUpdate</c> 가 부르고, EditMode 테스트는
        /// 직접 부른다.
        ///
        /// 매번 다시 계산하는 이유는 조건이 <b>깨질 수 있기</b> 때문이다. 파츠가 떨어지거나
        /// 누가 탑을 밀어 구역을 벗어나거나 기울면 탑승이 다시 막혀야 한다. 한 번 참이면
        /// 계속 참으로 두면, 무너진 탑에 탑승해 발사를 시도하는 상태가 생긴다.
        /// </summary>
        public void Evaluate()
        {
            _tower.Clear();
            Engine = null;
            IsBoardable = false;
            for (var i = 0; i < _floors.Length; i++) _floors[i] = FloorState.Empty;
            NextFloor = 0;

            var engine = FindEngine();
            if (engine == null)
            {
                Blocker = "주 추진 엔진 모듈이 조립대에 없다";
                return;
            }

            PartGraph.Collect(engine, _tower);

            // 층별 상태는 아래 조기 탈출보다 <b>앞에서</b> 구한다. 탑이 덜 쌓였을 때도
            // 태블릿은 쌓인 층들의 상태를 보여줘야 한다(S15P21B202-345).
            EvaluateFloors();

            if (_tower.Count < PartSpec.All.Length)
            {
                Blocker = $"파츠가 {_tower.Count}/{PartSpec.All.Length} 층만 쌓였다";
                return;
            }

            // 순서는 PartSpec.All 이 정한다. 기획 §7.1 의 조립 순서를 그 표가 갖고 있다.
            //
            // PartGraph 가 높이 순으로 정렬해 준 목록이므로 인덱스가 곧 층이다. 예전에는
            // 소켓 사슬을 올라간 순서가 그대로 층이었는데, 그래프에는 "다음 층" 이 없다.
            // 비드로 옆에 붙여도 §7.1 의 쌓는 순서는 그대로 남는다 — 비드는 붙이는
            // 수단이지 순서를 우회하는 길이 아니다.
            for (var i = 0; i < _tower.Count; i++)
            {
                if (_tower[i].name == PartSpec.All[i].Name) continue;

                Blocker = $"{i + 1}층이 {PartSpec.All[i].DisplayName} 이 아니다";
                return;
            }

            if (!IsOnPad(engine))
            {
                Blocker = "우주선이 발사 구역 밖에 있다";
                return;
            }

            for (var i = 0; i < _tower.Count; i++)
            {
                var tilt = Vector3.Angle(_tower[i].AssemblyUp, Vector3.up);
                if (tilt <= PhysicsScale.BoardingMaxTilt) continue;

                Blocker = $"{_tower[i].name} 이 {tilt:0}도 기울어 있다";
                return;
            }

            Engine = engine;
            Blocker = "";
            IsBoardable = true;
        }

        /// <summary>
        /// 층별 상태를 구한다(S15P21B202-345).
        ///
        /// <b>판정 규칙을 새로 만들지 않는다.</b> "틀린 파츠" 와 "기울었다" 의 기준은
        /// <see cref="Evaluate"/> 가 <see cref="IsBoardable"/> 을 정할 때 쓰는 것과 같다 —
        /// <see cref="PartSpec.All"/> 의 이름과 <see cref="PhysicsScale.BoardingMaxTilt"/>.
        /// 기준이 둘이면 태블릿은 초록인데 문은 열리지 않는 상태가 생긴다.
        /// </summary>
        void EvaluateFloors()
        {
            var floors = Mathf.Min(_tower.Count, _floors.Length);

            for (var i = 0; i < floors; i++)
            {
                var part = _tower[i];
                if (part == null) continue;

                if (part.name != PartSpec.All[i].Name)
                {
                    _floors[i] = FloorState.WrongPart;
                    continue;
                }

                var tilt = Vector3.Angle(part.AssemblyUp, Vector3.up);
                if (tilt > PhysicsScale.BoardingMaxTilt)
                {
                    _floors[i] = FloorState.Tilted;
                    continue;
                }

                // 최하단은 아래에 접합할 상대가 없다. 위 <see cref="FloorState.Welded"/>
                // 주석에 이유를 적었다.
                var jointDone = part.State == PartState.Welded
                                || (i == 0 && part.CanReceiveAssembly);

                _floors[i] = jointDone ? FloorState.Welded : FloorState.Stacked;
            }

            NextFloor = -1;
            for (var i = 0; i < _floors.Length; i++)
            {
                if (_floors[i] != FloorState.Empty) continue;
                NextFloor = i;
                break;
            }
        }

        /// <summary>
        /// 발사 구역 안에서 최하단 파츠를 찾는다.
        ///
        /// 아래쪽 소켓이 비어 있는(= 아무것에도 올라가 있지 않은) 파츠가 최하단이다.
        /// 이름으로 찾지 않는 이유는 파츠를 여러 개 놓아 보는 상황에서도 맞아야 하기 때문이다.
        /// </summary>
        SpaceshipPart FindEngine()
        {
            var sockets = PartSocket.All;

            for (var i = 0; i < sockets.Count; i++)
            {
                var socket = sockets[i];
                if (socket == null || socket.Role != SocketRole.Base || socket.IsOccupied) continue;

                var owner = socket.Owner;
                if (owner == null || owner.name != PartSpec.All[0].Name) continue;
                if (!IsOnPad(owner)) continue;

                return owner;
            }

            return null;
        }

        bool IsOnPad(SpaceshipPart part)
        {
            var offset = part.transform.position - LaunchPad.position;
            offset.y = 0f;
            return offset.magnitude <= PhysicsScale.LaunchPadRadius;
        }

    }
}
