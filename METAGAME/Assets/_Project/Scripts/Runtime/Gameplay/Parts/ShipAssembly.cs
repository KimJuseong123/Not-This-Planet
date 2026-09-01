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
    /// 탑을 따라 올라가는 방법은 <see cref="LaunchTest"/> 와 같다 —
    /// <see cref="PartSocket.ConnectedTo"/> 사슬이다. 두 곳이 다른 방법을 쓰면
    /// "탑승은 됐는데 시험이 파츠를 못 찾는" 어긋남이 생긴다.
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

            var engine = FindEngine();
            if (engine == null)
            {
                Blocker = "주 추진 엔진 모듈이 조립대에 없다";
                return;
            }

            CollectTower(engine);

            if (_tower.Count < PartSpec.All.Length)
            {
                Blocker = $"파츠가 {_tower.Count}/{PartSpec.All.Length} 층만 쌓였다";
                return;
            }

            // 순서는 PartSpec.All 이 정한다. 기획 §7.1 의 조립 순서를 그 표가 갖고 있다.
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
                var tilt = Vector3.Angle(_tower[i].transform.up, Vector3.up);
                if (tilt <= PhysicsScale.BoardingMaxTilt) continue;

                Blocker = $"{_tower[i].name} 이 {tilt:0}도 기울어 있다";
                return;
            }

            Engine = engine;
            Blocker = "";
            IsBoardable = true;
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

        void CollectTower(SpaceshipPart engine)
        {
            var current = engine;

            while (current != null)
            {
                _tower.Add(current);

                PartSocket top = null;
                var sockets = current.Sockets;
                for (var i = 0; i < sockets.Count; i++)
                    if (sockets[i] != null && sockets[i].Role == SocketRole.Top)
                        top = sockets[i];

                var next = top != null && top.ConnectedTo != null ? top.ConnectedTo.Owner : null;
                if (next == null || _tower.Contains(next)) break;

                current = next;
            }
        }
    }
}
