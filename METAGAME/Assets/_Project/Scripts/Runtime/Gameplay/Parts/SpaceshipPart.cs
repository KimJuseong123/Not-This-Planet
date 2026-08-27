using System;
using System.Collections.Generic;
using UnityEngine;

namespace Metagame.Gameplay.Parts
{
    /// <summary>
    /// 우주선 파츠 하나. 상태와 그에 따른 물리 표현을 한 곳에서 관리한다.
    ///
    /// 견인·적층·용접이 모두 이 상태를 읽고 쓰기 때문에 물리 표현 전환을 각 시스템에
    /// 흩어 두면 "견인은 끝났는데 질량이 안 돌아왔다" 같은 상태가 생긴다. 여기로 모은다.
    ///
    /// 전환은 <see cref="TryTransitionTo"/> 하나로만 일어난다. 허용되지 않은 전환은 거부되고
    /// 상태가 바뀌지 않는다.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    public sealed class SpaceshipPart : MonoBehaviour
    {
        [SerializeField] PartState _state = PartState.Locked;

        readonly List<PartClamp> _clamps = new();
        readonly List<PartSocket> _sockets = new();

        Rigidbody _body;
        bool _initialized;

        public PartState State => _state;

        public IReadOnlyList<PartClamp> Clamps
        {
            get
            {
                Initialize();
                return _clamps;
            }
        }

        /// <summary>이 파츠에 달린 접합 지점.</summary>
        public IReadOnlyList<PartSocket> Sockets
        {
            get
            {
                Initialize();
                return _sockets;
            }
        }

        /// <summary>아직 풀리지 않은 필수 고정쇠의 개수. 0 이 되어야 Locked 에서 벗어난다.</summary>
        public int RemainingRequiredClamps
        {
            get
            {
                Initialize();

                var remaining = 0;
                foreach (var clamp in _clamps)
                    if (clamp != null && clamp.IsRequired && !clamp.IsReleased)
                        remaining++;

                return remaining;
            }
        }

        /// <summary>(이전 상태, 새 상태). UI 표시와 사운드가 여기에 붙는다.</summary>
        public event Action<PartState, PartState> StateChanged;

        void Awake() => Initialize();

        /// <summary>
        /// 자식의 고정쇠를 모으고 현재 상태의 물리 표현을 적용한다.
        /// 여러 번 불러도 안전하다.
        ///
        /// Awake 는 EditMode 에서 호출되지 않으므로 테스트는 이 메서드를 직접 부른다.
        /// 프로퍼티 접근에서도 불러 두어 초기화 순서에 의존하지 않게 했다.
        /// </summary>
        public void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            _body = GetComponent<Rigidbody>();

            RefreshClamps();
            RefreshSockets();
            EnsureGrabbable();
            ApplyRepresentation(_state);
        }

        /// <summary>
        /// 파츠는 손으로 들 수 없고 짚을 수만 있게 한다.
        ///
        /// PlayerRig 의 손 잡기는 Tuning.FixedGripMass(40kg) 보다 가벼운 물체를 들 때
        /// Rigidbody.isKinematic 을 직접 켠다. 견인 중 파츠는 35kg 이라 이 조건에 걸리는데,
        /// 그러면 손과 이 상태머신이 같은 Rigidbody 의 isKinematic 을 두고 다툰다.
        ///
        /// Grabbable.Climbable 을 켜면 PlayerRig 가 고정물 짚기 경로를 타서 isKinematic 을
        /// 건드리지 않는다. 파츠에 매달려 함께 이동하는 것은 그대로 가능하고,
        /// 파츠를 옮기는 수단은 견인 도구 하나로 유지된다.
        /// </summary>
        void EnsureGrabbable() => Grabbable.Mark(gameObject, name, climbable: true);

        /// <summary>
        /// 자식의 고정쇠 목록을 다시 모은다.
        /// 런타임에 고정쇠를 붙이거나 떼어낸 뒤에 호출한다.
        /// </summary>
        public void RefreshClamps()
        {
            _clamps.Clear();
            GetComponentsInChildren(true, _clamps);
            foreach (var clamp in _clamps) clamp.Initialize();
        }

        /// <summary>
        /// 자식의 접합 지점 목록을 다시 모으고 씬 목록에 등록한다.
        /// OnEnable 이 돌지 않는 EditMode 에서도 소켓이 검색되게 하려면 이 등록이 필요하다.
        /// </summary>
        public void RefreshSockets()
        {
            _sockets.Clear();
            GetComponentsInChildren(true, _sockets);
            foreach (var socket in _sockets) socket.Register();
        }

        /// <summary>
        /// 상태 전환을 시도한다. 허용되지 않으면 false 를 돌려주고 아무것도 바꾸지 않는다.
        /// </summary>
        public bool TryTransitionTo(PartState next)
        {
            Initialize();

            if (next == _state) return false;
            if (!IsTransitionAllowed(_state, next)) return false;
            if (next == PartState.Free && RemainingRequiredClamps > 0) return false;

            var previous = _state;
            _state = next;
            ApplyRepresentation(next);
            StateChanged?.Invoke(previous, next);
            return true;
        }

        /// <summary>
        /// 고정쇠가 풀렸을 때 그 고정쇠가 호출한다.
        /// 필수 고정쇠가 모두 풀렸으면 Locked 에서 Free 로 넘어간다.
        /// </summary>
        public void NotifyClampReleased(PartClamp clamp)
        {
            Initialize();

            if (clamp == null || !_clamps.Contains(clamp)) return;
            if (_state != PartState.Locked) return;
            if (RemainingRequiredClamps > 0) return;

            TryTransitionTo(PartState.Free);
        }

        /// <summary>
        /// Locked 에서는 고정쇠를 풀어야만 Free 로 갈 수 있고, Welded 에서는 되돌아가지 않는다.
        /// 놓인 파츠는 용접 전이라면 다시 들 수 있다.
        /// </summary>
        static bool IsTransitionAllowed(PartState from, PartState to) => from switch
        {
            PartState.Locked => to == PartState.Free,
            PartState.Free => to == PartState.Carried,
            PartState.Carried => to == PartState.Placed,
            PartState.Placed => to is PartState.Carried or PartState.Welded,
            _ => false,
        };

        void ApplyRepresentation(PartState state)
        {
            var (layer, mass, isKinematic) = RepresentationOf(state);

            // 레이어는 이 오브젝트에만 준다. 자식의 고정쇠는 Interactable 레이어에 있어야
            // Interactor 가 찾을 수 있으므로 함께 바꾸면 안 된다.
            gameObject.layer = layer;

            if (_body == null) _body = GetComponent<Rigidbody>();
            _body.isKinematic = isKinematic;
            _body.mass = mass;
            _body.maxAngularVelocity = PhysicsScale.PartMaxAngularSpeed;
        }

        /// <summary>
        /// 견인 중에만 가볍고, 나머지 상태에서는 무겁다.
        /// 고정된 동안에는 kinematic 이라 운석에 맞아도 밀리지 않는다.
        /// 값의 근거는 S15P21B202-62 기록 문서를 본다.
        /// </summary>
        public static (int Layer, float Mass, bool IsKinematic) RepresentationOf(PartState state) => state switch
        {
            PartState.Locked => (GameLayers.Part, PhysicsScale.PartMass, true),
            PartState.Carried => (GameLayers.PartCarried, PhysicsScale.CarriedPartMass, false),
            _ => (GameLayers.Part, PhysicsScale.PartMass, false),
        };
    }
}
