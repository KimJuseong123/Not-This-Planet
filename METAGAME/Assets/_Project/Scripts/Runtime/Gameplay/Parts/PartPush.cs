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
            public float IntentSeconds;    // 밀려는 이동을 유지한 시간
            public float GraceSeconds;     // 의도가 끊긴 뒤 남은 시간
            public Vector3 Direction;      // 마지막으로 민 방향(수평, 이동 입력)
            public bool Active;            // 이번 틱에 미는 사람으로 세는가
            public bool Holding;           // 손으로 잡아서 참여하는가
            public Vector3 Grasp;          // 파츠 표면에서 이 사람에게 가장 가까운 점
        }

        readonly Dictionary<PlayerRig, Pusher> _pushers = new();
        readonly List<PlayerRig> _stale = new();

        SpaceshipPart _part;
        Grabbable _grabbable;
        Rigidbody _body;
        Collider _collider;
        Collider[] _colliders;
        bool _collidersCollected;

        float _lastTopSpeed;
        bool _pushedRecently;

        /// <summary>
        /// 이번 틱에 파츠를 미는 사람 수 — <b>손으로 잡은 사람과 몸으로 미는 사람의
        /// 합</b>이다. 기획 §13.2 의 표가 이 값을 받는다.
        ///
        /// 둘을 합치는 이유는 §13 이 "힘을 합쳐 민다" 이지 "잡는 것과 미는 것이 다른
        /// 기능" 이 아니기 때문이다. 둘이 잡고 하나가 몸으로 밀어도 셋이다.
        /// 같은 사람이 잡고 동시에 몸으로 밀어도 <b>한 명</b>이다.
        /// </summary>
        public int HaulerCount { get; private set; }

        /// <summary>이번 틱의 합산 밀기 방향(수평, 단위). 아무도 없으면 0이다.</summary>
        public Vector3 HaulDirection { get; private set; }

        /// <summary>그중 손으로 잡아서 참여한 사람 수. 양손은 한 명으로 센다.</summary>
        public int PullerCount { get; private set; }

        /// <summary>그중 몸으로만 밀어 참여한 사람 수.</summary>
        public int PusherCount { get; private set; }

        /// <summary>누군가 밀려고 하는가. 실제로 움직이는지와는 별개다.</summary>
        public bool IsHauling => HaulerCount > 0 && HaulDirection.sqrMagnitude > 1e-6f;

        /// <summary>이번 틱에 파츠가 실제로 가속되는가 — 인원이 마찰을 이기는가.</summary>
        public bool IsMoving => IsHauling && HaulerCount >= PartPushTable.MinPeople;

        /// <summary>
        /// 파츠가 움직이기 시작하는 최소 인원.
        ///
        /// 기획 §10.2 의 밀기 카메라가 "밀기가 실제로 걸렸는가" 를 물을 때
        /// <see cref="IsMoving"/> 과 함께 쓴다.
        /// </summary>
        public static int MinPeopleToMove => PartPushTable.MinPeopleToMove;

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

            // **몸통 콜라이더를 자식까지 훑어 모은다.**
            //
            // 파츠 루트에는 콜라이더가 아예 없다 — Stage1PuzzleBuilder 가 PartSpec 의
            // 상자마다 자식 `Body_*` 를 만들어 붙인다(기획 §25.3). 그래서 예전의
            // `GetComponent<Collider>()` 는 **항상 null 이었고, IsTouching 이 늘 false
            // 여서 몸으로 미는 것은 실제 게임에서 한 번도 작동한 적이 없다.**
            // (검증은 콜라이더를 루트에 붙였기 때문에 통과했다 — S15P21B202-394.)
            //
            // **레이어로 가른다.** 자식에는 고정못(`Clamp_*`, GameLayers.Interactable)도
            // 있고 그 콜라이더는 파츠 밑면보다 아래로 내려가 있다. 함께 세면 접촉
            // 판정이 발밑까지 늘어난다. 몸통은 GameLayers.Part 뿐이다.
            if (!_collidersCollected) _colliders = CollectBodyColliders();

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
        /// <paramref name="player"/> 가 참여해 <b>실제로 움직이는</b> 파츠. 없으면 null.
        ///
        /// 기획 §10.2 의 밀기 카메라가 "지금 무엇을 밀고 있는가" 를 물을 때 쓴다.
        /// <see cref="IsHauling"/> 이 아니라 <see cref="IsMoving"/> 을 보는 이유는
        /// 한두 명이 붙어 꿈쩍하지 않을 때 시점이 바뀌면 "시점은 바뀌었는데 아무 일도
        /// 안 일어나는" 화면이 되기 때문이다(S15P21B202-400).
        /// </summary>
        public static PartPush MovingPartFor(PlayerRig player)
        {
            if (player == null) return null;

            for (var i = 0; i < All.Count; i++)
            {
                var push = All[i];
                if (push == null || !push.IsMoving) continue;
                if (push.Participates(player)) return push;
            }

            return null;
        }

        /// <summary>이번 틱에 이 사람이 미는 사람으로 세어졌는가.</summary>
        public bool Participates(PlayerRig player)
        {
            return player != null
                   && _pushers.TryGetValue(player, out var pusher)
                   && pusher.Active;
        }

        /// <summary>
        /// 이 사람이 파츠를 잡거나 짚고 있는 지점(월드). 참여 중이 아니면
        /// 파츠 표면에서 그 사람에게 가장 가까운 점을 즉석에서 계산한다.
        ///
        /// <b>밖에서 다시 재지 않게 여기서 준다.</b> 접촉 판정
        /// (<see cref="IsTouching"/>)이 쓰는 것과 같은 계산이어야 카메라 중심과
        /// 밀기 판정이 어긋나지 않는다.
        /// </summary>
        public Vector3 GraspPointFor(PlayerRig player)
        {
            if (player != null && _pushers.TryGetValue(player, out var pusher)
                && pusher.Active && pusher.Grasp.sqrMagnitude > 0f)
                return pusher.Grasp;

            return ClosestSurfacePoint(player);
        }

        /// <summary>파츠 몸통 콜라이더 중 이 사람의 골반에 가장 가까운 점.</summary>
        Vector3 ClosestSurfacePoint(PlayerRig player)
        {
            var from = player != null && player.Doll != null && player.Doll.Hips != null
                ? player.Doll.Hips.position
                : transform.position;

            if (!_collidersCollected) _colliders = CollectBodyColliders();
            if (_colliders == null || _colliders.Length == 0) return Center();

            var best = Center();
            var bestSqr = float.MaxValue;

            for (var i = 0; i < _colliders.Length; i++)
            {
                var collider = _colliders[i];
                if (collider == null || !collider.enabled) continue;

                var point = collider.ClosestPoint(from);
                var sqr = (point - from).sqrMagnitude;
                if (sqr >= bestSqr) continue;

                bestSqr = sqr;
                best = point;
            }

            return best;
        }

        /// <summary>
        /// 밀 수 있는 상태인가 — <b><see cref="PartState.Free"/> 뿐이다.</b>
        ///
        /// 기획 §13 이 노리는 것은 "약 15m 떨어진 중앙 조립대를 향해 함께 밀기"
        /// (§상세 5번)다. 그 대상은 고정못을 다 뽑아 바닥에서 떨어진 파츠, 즉
        /// <c>Free</c> 다. 나머지는 각자 이유가 있다.
        ///
        /// <list type="bullet">
        /// <item><see cref="PartState.Locked"/> — 고정쇠로 박혀 "운석에 맞아도 움직이지
        /// 않는다". 하나라도 남아 있으면 <c>ConfigurableJoint</c> 가 그 자리에 묶는다</item>
        /// <item><see cref="PartState.Carried"/> — 견인 빔이 잡고 있다</item>
        /// <item><see cref="PartState.Welded"/> — 미는 것이 <b>탑 전체를 미는 것</b>이 된다
        /// (S15P21B202-250 의 기획 보류)</item>
        /// <item><b><see cref="PartState.Placed"/> — 예전에는 여기 포함돼 있었다.
        /// 빼는 이유가 이 목록에서 가장 중요하다.</b></item>
        /// </list>
        ///
        /// <b>Placed 를 뺀 이유.</b> <c>PartAssembler.Assemble</c> 이 <c>Carried</c> 에서
        /// <c>Placed</c> 로 올리면서 <see cref="PartAssemblyLink"/>(FixedJoint)로 아래
        /// 파츠에 묶는다. 그래서 <c>Placed</c> 파츠는 <b>밀 수 있다고 답하면서 실제로는
        /// 절대 움직이지 않았다</b> — 셋이 붙어 8843N 이 들어가는데 2초에 0.0000m 였다
        /// (S15P21B202-400 후속 실측). 조인트가 붙잡는 것을 힘으로 이길 수 없고, 이길
        /// 수 있게 만들면 위에 얹힌 층까지 끌고 가거나 탑이 무너진다.
        ///
        /// 조립대에 올린 파츠는 이미 목적지에 도착한 것이다. 다시 옮기려면 견인 빔으로
        /// 들어 올리는 경로가 있다(<c>Placed → Carried</c> 전이가 허용된다).
        /// </summary>
        public bool CanPush =>
            _part != null && _body != null && !_body.isKinematic
            && _part.State == PartState.Free;

        void FixedUpdate() => Tick(Time.fixedDeltaTime);

        /// <summary>매 물리 스텝 호출한다.</summary>
        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0f) return;

            Initialize();

            if (!CanPush)
            {
                _pushers.Clear();
                HaulerCount = 0;
                HaulDirection = Vector3.zero;
                PullerCount = 0;
                PusherCount = 0;
                _pushedRecently = false;
                return;
            }

            Gather(deltaTime);
            Drive(deltaTime);
        }

        /// <summary>
        /// 미는 사람을 모은다(기획 §13.1).
        ///
        /// <b>손으로 잡은 사람과 몸으로 미는 사람을 한 번에 센다.</b> 참여 자격만 다르고
        /// 그 뒤는 같다 — 인원에 1을 더하고 <b>그 사람의 이동 입력 방향</b>을 합산한다.
        ///
        /// <b>방향을 위치가 아니라 이동 입력에서 얻는 이유.</b> 예전에는 잡은 사람의
        /// 방향을 "파츠 중심에서 그 사람 쪽" 으로 정했다. 그러면 잡은 채로 어느 쪽으로
        /// 걸어가든 파츠는 <b>언제나 사람 쪽으로</b> 왔다 — 셋이 잡고 앞으로 밀면 파츠가
        /// 자기들 쪽으로 와서 몸에 부딪혔고, 가만히 잡고 서 있어도 움직이려 들었다.
        /// 입력에서 얻으면 잡고 밀면 밀리고 잡고 당기면 당겨진다. 규칙이 하나다.
        /// </summary>
        void Gather(float deltaTime)
        {
            var haulers = 0;
            var pullers = 0;
            var pushers = 0;
            var sum = Vector3.zero;

            var players = PlayerRig.All;
            for (var i = 0; i < players.Count; i++)
            {
                var player = players[i];
                if (player == null || player.Doll == null) continue;

                // 기절한 사람과 탑승 중인 사람은 밀지 않는다.
                var able = !player.Downed && !player.IsRiding;

                var wish = player.MoveWish;
                wish.y = 0f;
                var moving = wish.sqrMagnitude > 1e-4f;
                var heading = moving ? wish.normalized : Vector3.zero;

                // 손으로 잡았으면 접촉·조준을 다시 보지 않는다. 잡는 것 자체가
                // 이미 의도적인 행동이고, 잡은 사람은 어느 쪽으로든 끌 수 있다.
                var holding = able && (IsHoldingPart(player.LeftHand)
                                       || IsHoldingPart(player.RightHand));

                // 몸으로 밀 때만 "파츠 쪽으로 향하는 이동인가" 를 본다. 멀어지는 입력은
                // 밀기가 아니다 — §13.1 "당기기는 제공하지 않으며 반대편으로 이동해
                // 반대 방향으로 민다". 손이 비었으면 파츠를 당길 수단이 없다.
                var toPart = Toward(player);
                var bodyPushing = able && !holding
                                  && toPart.sqrMagnitude > 1e-6f
                                  && IsTouching(player)
                                  && moving
                                  && Vector3.Dot(heading, toPart)
                                     >= PhysicsScale.PartPushAimDot;

                var intent = moving && (holding || bodyPushing);

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
                    pusher.Direction = heading;
                    pusher.Holding = holding;
                    pusher.Grasp = ClosestSurfacePoint(player);
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
                haulers++;
                if (pusher.Holding) pullers++; else pushers++;
                sum += pusher.Direction;
            }

            Forget();

            HaulerCount = haulers;
            PullerCount = pullers;
            PusherCount = pushers;

            // 서로 다른 방향으로 밀면 힘 벡터를 합산한다(§13.2). 마주보고 밀면 상쇄된다.
            HaulDirection = sum.sqrMagnitude > 1e-6f ? sum.normalized : Vector3.zero;
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

            var force = PartPushTable.ForceFor(HaulerCount, _body.mass);

            // 인원이 문턱에 못 미치면 **파츠를 건드리지 않는다.**
            //
            // 예전에는 인원과 무관한 고정 가속(1.2 m/s²)으로 속도를 직접 대입했다.
            // 그 값은 지면 마찰이 요구하는 4.75 m/s² 의 4분의 1이라 마찰이 같은 물리
            // 스텝에 전부 지웠고, 파츠는 3초를 밀어도 0.0000m 움직였다 —
            // 협동 밀기가 한 번도 작동한 적이 없었다(S15P21B202-394).
            //
            // 지우는 것을 알면서 매 스텝 넣으면 파츠가 미세하게 떨리고, 아래의
            // _pushedRecently 가 계속 켜져 감쇠와 외부 충격 판별이 내내 헛돈다.
            if (IsHauling && force > 0f)
            {
                var top = PartPushTable.SpeedFor(HaulerCount);
                _lastTopSpeed = top;
                _pushedRecently = true;

                var along = Vector3.Dot(horizontal, HaulDirection);
                if (along >= top) return;   // 이미 더 빠르다 — 건드리지 않는다

                // **남은 여유에 맞춰 힘을 깎는다.** AddForce 는 다음 물리 스텝에야
                // 속도가 되므로, 상한 바로 아래에서 온 힘을 넣으면 그 한 스텝이
                // 표를 넘어선다 — 셋이 미는데 1.2 대신 1.31 m/s 가 나왔다.
                var room = (top - along) * _body.mass / deltaTime;
                var applied = Mathf.Min(force, room);

                // **속도를 대입하지 않고 힘으로 준다.** 속도를 대입하면 마찰이 그것을
                // 지우는지가 접촉점 개수에 달려, 같은 값이 어떤 파츠는 밀고 어떤
                // 파츠는 못 민다 — 평평한 2 x 2 바닥은 8 m/s² 로도 꿈쩍하지 않았고
                // Part_1 은 2.4 에 밀렸다. 힘은 solver 가 마찰과 한 층에서 풀어
                // 형태와 무관하고, 질량이 실제로 의미를 갖는다.
                //
                // 질량중심에 넣어 토크를 만들지 않는다 — 미는 것이 파츠를 넘어뜨리는
                // 것이 되면 §13 이 아니라 §22.4 의 붕괴가 된다.
                _body.AddForce(HaulDirection * applied, ForceMode.Force);
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
            var to = Center();

            var offset = to - from;
            offset.y = 0f;
            return offset.sqrMagnitude < 1e-6f ? Vector3.zero : offset.normalized;
        }

        /// <summary>
        /// 몸통 콜라이더만 모은다 — <see cref="GameLayers.Part"/> 레이어의 것들이다.
        /// 고정못은 <see cref="GameLayers.Interactable"/> 이라 걸러진다.
        /// </summary>
        Collider[] CollectBodyColliders()
        {
            // **한 번만 훑는다.** 이 함수는 GetComponentsInChildren 과 List 할당을
            // 하는데, IsTouching 이 사람 수 x 프레임으로 부른다. Part 레이어
            // 콜라이더가 없는 파츠에서 "비었으면 다시" 로 두면 매 호출마다 훑는다.
            _collidersCollected = true;

            var found = GetComponentsInChildren<Collider>();
            var kept = new List<Collider>(found.Length);

            for (var i = 0; i < found.Length; i++)
            {
                var collider = found[i];
                if (collider == null) continue;
                if (collider.gameObject.layer != GameLayers.Part) continue;

                kept.Add(collider);
            }

            // 루트에 콜라이더가 있고 레이어가 다른 예외적인 구성에서도 판정이 죽지
            // 않게 한다. 아무것도 못 찾았으면 있는 것이라도 쓴다.
            if (kept.Count == 0 && _collider != null) kept.Add(_collider);

            return kept.ToArray();
        }

        /// <summary>
        /// 파츠 전체의 중심. <b>상자 하나가 아니라 모든 상자를 감싼 범위</b>로 잡는다 —
        /// Part_1 처럼 받침만 좁은 파츠에서 첫 상자의 중심을 쓰면 미는 방향이 실제
        /// 몸통과 어긋난다.
        /// </summary>
        Vector3 Center()
        {
            if (_colliders == null || _colliders.Length == 0)
                return _collider != null ? _collider.bounds.center : transform.position;

            var bounds = new Bounds();
            var started = false;

            for (var i = 0; i < _colliders.Length; i++)
            {
                var collider = _colliders[i];
                if (collider == null || !collider.enabled) continue;

                if (!started)
                {
                    bounds = collider.bounds;
                    started = true;
                }
                else
                {
                    bounds.Encapsulate(collider.bounds);
                }
            }

            return started ? bounds.center : transform.position;
        }

        /// <summary>
        /// 파츠에 닿아 있는가.
        ///
        /// 접촉 콜백을 쓰지 않는 이유는 <see cref="PhysicsScale.PartPushContactDistance"/>
        /// 에 적었다 — 인형은 뼈가 열두 개다.
        /// </summary>
        bool IsTouching(PlayerRig player)
        {
            var hips = player.Doll.Hips;
            if (hips == null) return false;

            if (!_collidersCollected) _colliders = CollectBodyColliders();
            if (_colliders == null || _colliders.Length == 0) return false;

            // **가장 가까운 상자로 판정한다.** 첫 상자만 보면 Part_1 의 좁은 받침이
            // 기준이 되어, 넓은 몸통에 몸을 붙이고 걸어도 "닿지 않았다" 가 된다.
            for (var i = 0; i < _colliders.Length; i++)
            {
                var collider = _colliders[i];
                if (collider == null || !collider.enabled) continue;

                var point = collider.ClosestPoint(hips.position);
                var offset = point - hips.position;
                offset.y = 0f;

                if (offset.magnitude <= PhysicsScale.PartPushContactDistance)
                    return true;
            }

            return false;
        }
    }
}
