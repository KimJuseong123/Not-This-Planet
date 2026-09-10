using System.Collections.Generic;
using UnityEngine;

namespace Metagame.Gameplay.Parts
{
    /// <summary>
    /// 파츠를 몸으로 밀거나 손으로 협동 견인하는 것(기획 §13). 파츠에 붙는다.
    ///
    /// 몸으로 미는 동작에는 별도 버튼이 없다. 파츠에 닿은 상태로 파츠 쪽으로 걸어가면
    /// 시작된다(§13.1). 손으로 당기는 동작은 기존 잡기 버튼을 사용한다.
    ///
    /// 몸으로 미는 동작은 별도 버튼 없이 유지하고, 손으로 잡는 협동 견인은 기존
    /// Grabbable 입력을 사용한다. 견인 중인 플레이어가 세 명 미만이면 손을 짚고
    /// 버티기만 하며, 세 명 이상일 때만 파츠 Rigidbody가 당기는 방향으로 움직인다.
    ///
    /// EditMode 에서 <c>Awake</c>·<c>FixedUpdate</c> 가 돌지 않으므로
    /// <see cref="Initialize"/> 와 <see cref="Tick"/> 을 공개한다 —
    /// <see cref="Hazards.Meteor"/> 와 같은 방식이다.
    /// </summary>
    [RequireComponent(typeof(SpaceshipPart))]
    [DisallowMultipleComponent]
    public sealed class PartPush : MonoBehaviour
    {
        /// <summary>살아 있는 밀기 대상. 검증과 진단이 쓴다.</summary>
        public static readonly List<PartPush> All = new();

        /// <summary>한 사람의 밀기 상태.</summary>
        sealed class Pusher
        {
            public float IntentSeconds;    // 파츠 쪽으로 이동한 시간
            public float GraceSeconds;     // 의도가 끊긴 뒤 남은 시간
            public Vector3 Direction;      // 마지막으로 민 방향(수평)
            public bool Active;            // 이번 틱에 미는 사람으로 세는가
        }

        readonly Dictionary<PlayerRig, Pusher> _pushers = new();
        readonly List<PlayerRig> _stale = new();

        SpaceshipPart _part;
        Grabbable _grabbable;
        Rigidbody _body;
        Collider _collider;

        float _lastTopSpeed;
        bool _pushedRecently;

        /// <summary>이번 틱에 미는 사람 수. 기획 §13.2 의 표가 이 값을 받는다.</summary>
        public int PusherCount { get; private set; }

        /// <summary>이번 틱의 합산 밀기 방향(수평, 단위). 아무도 없으면 0이다.</summary>
        public Vector3 PushDirection { get; private set; }

        /// <summary>이번 틱에 손으로 파츠를 붙잡은 플레이어 수. 양손은 한 명으로 센다.</summary>
        public int PullerCount { get; private set; }

        /// <summary>잡고 있는 플레이어들이 파츠에서 바깥으로 당기는 합산 방향.</summary>
        public Vector3 PullDirection { get; private set; }

        /// <summary>최소 인원과 방향이 갖춰져 실제 협동 견인이 가능한가.</summary>
        public bool IsPulled => PullerCount >= PhysicsScale.PartPullMinPlayers
                                && PullDirection.sqrMagnitude > 1e-6f;

        /// <summary>지금 밀리고 있는가.</summary>
        public bool IsPushed => PusherCount > 0;

        void Awake() => Initialize();

        void OnEnable()
        {
            if (!All.Contains(this)) All.Add(this);
        }

        void OnDisable() => All.Remove(this);

        /// <summary>여러 번 불러도 안전하다. Awake 는 EditMode 에서 돌지 않는다.</summary>
        public void Initialize()
        {
            if (_part == null) _part = GetComponent<SpaceshipPart>();
            if (_grabbable == null) _grabbable = GetComponent<Grabbable>();
            if (_body == null) _body = GetComponent<Rigidbody>();
            if (_collider == null) _collider = GetComponent<Collider>();
            if (!All.Contains(this)) All.Add(this);
        }

        /// <summary>
        /// 파츠를 손으로 짚는다. 기존 운석·동료 운반처럼 손 입력을 재사용하지만,
        /// FixedJoint를 즉시 만들지 않는다. 그래야 한두 명이 잡은 상태에서는
        /// 파츠가 물리적으로 먼저 끌려가지 않고, 세 명이 모였을 때만 협동 규칙이
        /// Rigidbody 이동을 허용할 수 있다.
        /// </summary>
        public bool TryGrab(PlayerRig player, Hand hand)
        {
            Initialize();
            _part?.Initialize();
            if (_grabbable == null) _grabbable = GetComponent<Grabbable>();

            if (!CanPush || player == null || hand == null
                || player.Downed || player.IsRiding)
                return false;

            if (_grabbable == null || !_grabbable.CanGrab)
                return false;

            return hand.BraceTo(_grabbable, _body);
        }

        /// <summary>
        /// 밀 수 있는 상태인가.
        ///
        /// <see cref="PartState.Locked"/> 는 고정쇠로 박혀 "운석에 맞아도 움직이지
        /// 않는다", <see cref="PartState.Carried"/> 는 견인 빔이 잡고 있다.
        /// <see cref="PartState.Welded"/> 는 미는 것이 <b>탑 전체를 미는 것</b>이 되므로
        /// 이 이슈에서 제외했다 — 기획 판단이 필요하다(S15P21B202-250).
        /// </summary>
        public bool CanPush =>
            _part != null && _body != null && !_body.isKinematic
            && (_part.State == PartState.Free || _part.State == PartState.Placed);

        void FixedUpdate() => Tick(Time.fixedDeltaTime);

        /// <summary>매 물리 스텝 호출한다.</summary>
        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0f) return;

            Initialize();

            if (!CanPush)
            {
                _pushers.Clear();
                PusherCount = 0;
                PushDirection = Vector3.zero;
                PullerCount = 0;
                PullDirection = Vector3.zero;
                _pushedRecently = false;
                return;
            }

            Gather(deltaTime);
            Drive(deltaTime);
        }

        /// <summary>미는 사람을 모은다(기획 §13.1).</summary>
        void Gather(float deltaTime)
        {
            var count = 0;
            var sum = Vector3.zero;

            var players = PlayerRig.All;
            for (var i = 0; i < players.Count; i++)
            {
                var player = players[i];
                if (player == null || player.Doll == null) continue;

                // 기절한 사람과 탑승 중인 사람은 밀지 않는다.
                var able = !player.Downed && !player.IsRiding;

                var toPart = Toward(player);
                var touching = able && toPart.sqrMagnitude > 1e-6f && IsTouching(player);

                var wish = player.MoveWish;
                wish.y = 0f;

                // 파츠 쪽으로 향하는 이동인가. 멀어지는 입력은 밀기가 아니다 —
                // "당기기는 제공하지 않으며 반대편으로 이동해 반대 방향으로 민다".
                var intent = touching
                             && wish.sqrMagnitude > 1e-4f
                             && Vector3.Dot(wish.normalized, toPart)
                                >= PhysicsScale.PartPushAimDot;

                if (!_pushers.TryGetValue(player, out var pusher))
                {
                    if (!intent) continue;

                    pusher = new Pusher();
                    _pushers[player] = pusher;
                }

                if (intent)
                {
                    pusher.IntentSeconds += deltaTime;
                    pusher.GraceSeconds = PhysicsScale.PartPushReleaseSeconds;
                    pusher.Direction = toPart;
                }
                else
                {
                    // 이동 입력이 멈추거나 접촉이 끊기면 0.5초 뒤에 끝난다(§13.1).
                    pusher.GraceSeconds -= deltaTime;
                    if (pusher.GraceSeconds <= 0f)
                    {
                        pusher.Active = false;
                        pusher.IntentSeconds = 0f;
                        continue;
                    }
                }

                // 0.2초를 채워야 시작한다. 한 번 시작하면 유예 시간 안에는 유지된다.
                if (pusher.IntentSeconds < PhysicsScale.PartPushIntentSeconds && !pusher.Active)
                {
                    pusher.Active = false;
                    continue;
                }

                pusher.Active = true;
                count++;
                sum += pusher.Direction;
            }

            Forget();

            PusherCount = count;

            // 서로 다른 방향으로 밀면 힘 벡터를 합산한다(§13.2).
            PushDirection = sum.sqrMagnitude > 1e-6f ? sum.normalized : Vector3.zero;

            GatherPullers();
        }

        /// <summary>
        /// 손으로 파츠를 짚은 플레이어를 모은다. 같은 플레이어가 양손을 사용해도
        /// 한 명으로 계산한다. 당기는 방향은 파츠 중심에서 플레이어 쪽이다.
        /// </summary>
        void GatherPullers()
        {
            var count = 0;
            var sum = Vector3.zero;

            var players = PlayerRig.All;
            for (var i = 0; i < players.Count; i++)
            {
                var player = players[i];
                if (player == null || player.Doll == null
                    || player.Downed || player.IsRiding)
                    continue;

                if (!IsHoldingPart(player.LeftHand) && !IsHoldingPart(player.RightHand))
                    continue;

                var away = AwayFrom(player);
                if (away.sqrMagnitude <= 1e-6f) continue;

                count++;
                sum += away;
            }

            PullerCount = count;
            PullDirection = sum.sqrMagnitude > 1e-6f ? sum.normalized : Vector3.zero;
        }

        bool IsHoldingPart(Hand hand) => hand != null
                                         && hand.Held == _grabbable
                                         && hand.Gripping;

        /// <summary>더 이상 미는 사람이 아닌 항목을 지운다.</summary>
        void Forget()
        {
            _stale.Clear();

            foreach (var pair in _pushers)
                if (pair.Key == null || (!pair.Value.Active && pair.Value.GraceSeconds <= 0f))
                    _stale.Add(pair.Key);

            for (var i = 0; i < _stale.Count; i++) _pushers.Remove(_stale[i]);
            _stale.Clear();
        }

        /// <summary>
        /// 속도를 몬다(기획 §13.2).
        ///
        /// <b>속도를 대입하지 않는다.</b> 목표보다 느릴 때만 가속한다 — 운석에 맞아
        /// 날아가는 파츠를 손으로 붙잡는 셈이 되면 §13.2 의 "운석 충격은 플레이어의
        /// 밀기보다 훨씬 강한 힘을 적용한다" 가 깨진다(S15P21B202-247 이 그 충격을
        /// 실제로 만들었다).
        /// </summary>
        void Drive(float deltaTime)
        {
            var velocity = _body.linearVelocity;
            var horizontal = new Vector3(velocity.x, 0f, velocity.z);

            if (IsPulled)
            {
                var top = PartPushTable.SpeedFor(PullerCount);
                _lastTopSpeed = top;
                _pushedRecently = true;

                var along = Vector3.Dot(horizontal, PullDirection);
                if (along >= top) return;

                var add = Mathf.Min(PhysicsScale.PartPushAccel * deltaTime, top - along);
                _body.linearVelocity = velocity + PullDirection * add;
                return;
            }

            if (PusherCount > 0)
            {
                var top = PartPushTable.SpeedFor(PusherCount);
                _lastTopSpeed = top;
                _pushedRecently = true;

                var along = Vector3.Dot(horizontal, PushDirection);
                if (along >= top) return;   // 이미 더 빠르다 — 건드리지 않는다

                var add = Mathf.Min(PhysicsScale.PartPushAccel * deltaTime, top - along);
                _body.linearVelocity = velocity + PushDirection * add;
                return;
            }

            if (!_pushedRecently) return;

            // 밀린 것보다 훨씬 빠르면 밀어서 생긴 관성이 아니다 — 맞은 것이다.
            if (horizontal.magnitude
                > _lastTopSpeed * PhysicsScale.PartPushForeignSpeedFactor)
            {
                _pushedRecently = false;
                return;
            }

            // 약 1초 동안 줄어든 뒤 정지(§13.2).
            var blend = 1f - Mathf.Exp(-deltaTime / PhysicsScale.PartPushDampSeconds);
            var damped = horizontal * (1f - blend);

            if (damped.sqrMagnitude < 1e-4f)
            {
                damped = Vector3.zero;
                _pushedRecently = false;
            }

            _body.linearVelocity = new Vector3(damped.x, velocity.y, damped.z);
        }

        /// <summary>사람에서 파츠로 향하는 수평 단위 벡터.</summary>
        Vector3 Toward(PlayerRig player)
        {
            var from = player.FootPosition;
            var to = _collider != null ? _collider.bounds.center : transform.position;

            var offset = to - from;
            offset.y = 0f;
            return offset.sqrMagnitude < 1e-6f ? Vector3.zero : offset.normalized;
        }

        /// <summary>파츠 중심에서 플레이어 쪽으로 향하는 수평 단위 벡터.</summary>
        Vector3 AwayFrom(PlayerRig player)
        {
            var from = _collider != null ? _collider.bounds.center : transform.position;
            var to = player.FootPosition;

            var offset = to - from;
            offset.y = 0f;
            return offset.sqrMagnitude < 1e-6f ? Vector3.zero : offset.normalized;
        }

        /// <summary>
        /// 파츠에 닿아 있는가.
        ///
        /// 접촉 콜백을 쓰지 않는 이유는 <see cref="PhysicsScale.PartPushContactDistance"/>
        /// 에 적었다 — 인형은 뼈가 열두 개다.
        /// </summary>
        bool IsTouching(PlayerRig player)
        {
            if (_collider == null) return false;

            var hips = player.Doll.Hips;
            if (hips == null) return false;

            var point = _collider.ClosestPoint(hips.position);
            var offset = point - hips.position;
            offset.y = 0f;

            return offset.magnitude <= PhysicsScale.PartPushContactDistance;
        }
    }
}
