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
        [SerializeField] bool _isDockedToPad;
        public bool IsDockedToPad => _isDockedToPad;

        /// <summary>도킹 판정은 조립대/서버만 수행하고, 참가자는 받은 상태를 적용한다.</summary>
        public void ApplyDockedToPad(bool docked)
        {
            Initialize();
            if (_isDockedToPad == docked) return;
            _isDockedToPad = docked;
            if (docked && !_body.isKinematic)
            {
                _body.linearVelocity = Vector3.zero;
                _body.angularVelocity = Vector3.zero;
            }
            ApplyRepresentation(_state);
        }

        /// <summary>
        /// 이 파츠가 놓였을 때의 질량. 씬을 만드는 도구가 <see cref="PartSpec"/> 의 값을 넣는다.
        ///
        /// 예전에는 네 파츠가 모두 <see cref="PhysicsScale.PartMass"/>(500kg) 하나를 썼다.
        /// 크기가 두 배 넘게 차이 나는데 질량이 같으면 적재도 발사 하중도 외형과 어긋난다.
        /// 0 이면 명세가 없는 파츠로 보고 예전 상수를 그대로 쓴다 — 테스트가 만드는
        /// 일반 파츠가 이 경로를 탄다.
        /// </summary>
        [SerializeField] float _placedMass;

        [Header("용접 비드 한도")]
        [Tooltip("이 파츠에 생성할 수 있는 비드 덩어리 총 개수. 한도를 낮춰도 기존 비드는 제거하지 않습니다.")]
        [Min(1)] [SerializeField] int weldBeadsPerPart = PhysicsScale.WeldBeadsPerPart;
        [Tooltip("한 기둥에 이어 붙일 수 있는 비드 덩어리 개수. 기존 끝 덩어리는 최대 크기까지 자랄 수 있습니다.")]
        [Min(1)] [SerializeField] int weldBeadBlobsPerChain = PhysicsScale.WeldBeadBlobsPerChain;

        public int WeldBeadsPerPart => Mathf.Max(1, weldBeadsPerPart);
        public int WeldBeadBlobsPerChain => Mathf.Max(1, weldBeadBlobsPerChain);

        readonly List<PartClamp> _clamps = new();
        readonly List<PartSocket> _sockets = new();

        Rigidbody _body;
        bool _initialized;
        Collider[] _hullColliders;

        public bool TryGetHullBounds(out Bounds bounds)
        {
            Initialize();
            if (_hullColliders == null) _hullColliders = GetComponentsInChildren<Collider>(true);
            bounds = default;
            var found = false;
            foreach (var collider in _hullColliders)
            {
                if (collider == null || !collider.enabled || collider.isTrigger
                    || collider.attachedRigidbody != _body || !collider.gameObject.activeInHierarchy
                    || collider.GetComponentInParent<PartClamp>() != null) continue;
                if (!found) { bounds = collider.bounds; found = true; }
                else bounds.Encapsulate(collider.bounds);
            }
            return found;
        }

        /// <summary>위치를 옮기지 않고 목표 회전에서의 외곽을 계산한다. 실제 콜라이더 크기/피벗을 존중한다.</summary>
        public bool TryGetHullBoundsAtRotation(Quaternion rotation, out Bounds bounds)
        {
            TryGetHullBounds(out bounds); // 콜라이더 캐시 초기화
            var found = false;
            var delta = rotation * Quaternion.Inverse(transform.rotation);
            foreach (var collider in _hullColliders)
            {
                if (collider == null || !collider.enabled || collider.isTrigger
                    || collider.attachedRigidbody != _body || !collider.gameObject.activeInHierarchy
                    || collider.GetComponentInParent<PartClamp>() != null) continue;
                var local = true;
                Bounds source;
                if (collider is BoxCollider box) source = new Bounds(box.center, box.size);
                else if (collider is MeshCollider mesh && mesh.sharedMesh != null) source = mesh.sharedMesh.bounds;
                else { source = collider.bounds; local = false; }
                for (var corner = 0; corner < 8; corner++)
                {
                    var point = source.center + Vector3.Scale(source.extents, new Vector3(
                        (corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f,
                        (corner & 4) == 0 ? -1f : 1f));
                    if (local) point = collider.transform.TransformPoint(point);
                    point = transform.position + delta * (point - transform.position);
                    if (!found) { bounds = new Bounds(point, Vector3.zero); found = true; }
                    else bounds.Encapsulate(point);
                }
            }
            return found;
        }

        /// <summary>남은 고정못을 지면에 묶어 두는 조인트들. 부분 부양이 이것으로 표현된다.</summary>
        readonly List<ConfigurableJoint> _clampJoints = new();

        public PartState State => _state;

        /// <summary>조립 자세는 견인 빔 축이 아닌 실제 몸체를 기준으로 판정한다.</summary>
        public Vector3 AssemblyUp => GetComponent<TransportVehicle>() is TransportVehicle vehicle
            ? vehicle.AssemblyUp : transform.up;

        public bool CanReceiveAssembly => IsDockedToPad || State is PartState.Placed or PartState.Welded
            || (State == PartState.Free && GetComponent<TransportVehicle>() is TransportVehicle vehicle
                && vehicle.CanReceiveAssembly);

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
        void OnEnable() => PartOutlineTargets.Register(this);
        void OnDisable() => PartOutlineTargets.Unregister(this);

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
            // 표현을 먼저 적용하고 그 뒤에 안개에서 뺀다 (S15P21B202-540). 순서가
            // 반대면, 표현 전환이 나중에 재질을 갈아끼우는 날 예외가 조용히 날아간다.
            ApplyRepresentation(_state);
            ExemptFromFog();
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
        /// 파츠는 안개를 받지 않는다 (S15P21B202-540).
        ///
        /// <para><b>왜 사람과 같이 빼는가.</b> 사람만 또렷하고 그가 나르는 파츠만
        /// 회색이면 어색하다. 그리고 파츠는 이 게임의 목표물이다 — 어디에 있는지
        /// 읽히는 편이 맞다. 리포에 이미 같은 판단이 있다: 유도등과 유도광선이
        /// <c>GuidanceNoFog</c> 로 안개를 피한다.</para>
        ///
        /// <para><b>잡는 동안만 빼지 않는 이유.</b> 잡기는 입력으로 서버에 가고
        /// (<c>InputFrame.GrabLeft</c>), 참가자 기계에는 결과 위치만 내려온다 —
        /// <c>Grabbable.NotifyGrabbed</c> 가 거기서는 돌지 않는다. 잡는 동안만
        /// 빼면 <b>내가 든 것만 또렷하고 팀원이 든 것은 그대로 회색</b>이라
        /// 고치려던 문제가 남는다.</para>
        /// </summary>
        void ExemptFromFog() => Core.Prim.SetFogExempt(gameObject, true);

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
            if (_isDockedToPad && next == PartState.Carried) return false;
            if (!IsTransitionAllowed(_state, next)) return false;
            if (next == PartState.Free && RemainingRequiredClamps > 0) return false;

            var previous = _state;
            _state = next;
            ApplyRepresentation(next);

            // 상태가 바뀌면 지면 구속도 다시 잡는다. Locked 를 벗어나면 전부 사라져야 한다 —
            // 남겨 두면 자유 파츠가 여전히 한 점에 묶여 들리지 않는다.
            RefreshClampConstraints();

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

            if (RemainingRequiredClamps > 0)
            {
                // 아직 남았다. 풀린 방향만 들리도록 물리 표현과 구속을 다시 잡는다.
                ApplyRepresentation(_state);
                RefreshClampConstraints();
                return;
            }

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

            // 용접한 파츠도 절단하면 다시 놓인 상태가 된다(기획 §16.3 "절단한 구간은
            // … 재조립"). 예전에는 Welded 가 종착이라 완전 절단해도 다시 들 수 없었고,
            // 그러면 §16.3 의 재배치가 성립하지 않는다.
            //
            // Carried 로 바로 가지 않는 이유는 절단한 순간 파츠가 손에 들리는 것이
            // 아니기 때문이다 — 떨어지거나 견인빔에 받쳐 있다. 그 뒤에 집으면 된다.
            PartState.Welded => to == PartState.Placed,

            _ => false,
        };

        /// <summary>
        /// 운석 충격파가 이 파츠를 민다(2026-09-18).
        ///
        /// <para><b>속도로 준다.</b> 놓인 파츠는 <see cref="PhysicsScale.PartMass"/>
        /// (500kg)이라 <c>AddExplosionForce</c> 의 900N 을 한 물리 스텝 받아 봐야
        /// 0.036m/s 다 — 기체(80kg)가 폭발력으로 안 움직여
        /// <see cref="TransportVehicle.TakeMeteorImpact"/> 가 속도를 직접 주는 것과
        /// 같은 계산이고, 파츠는 그보다 여섯 배 무겁다.</para>
        ///
        /// <para><b>회전도 같이 준다.</b> 미는 방향과 수직인 수평축으로 넘어간다.
        /// 회전이 없으면 무거운 상자가 미끄러지기만 해서 "밀렸다" 로 보이지
        /// <b>"맞았다"</b> 로 보이지 않는다.</para>
        ///
        /// <para><b>끊을지는 여전히 솔버가 정한다.</b> 여기서 주는 것은 속도뿐이고,
        /// 그 속도가 조인트에 만드는 힘이 <c>breakForce</c> 를 넘는지는 용접 정도가
        /// 정한다 — 상수가 결과를 정하지 않는다
        /// (<see cref="Hazards.Meteor"/> 의 클래스 주석과 같은 원칙).</para>
        /// </summary>
        /// <param name="point">착탄 지점.</param>
        /// <param name="reach">이 거리에서 0 이 되도록 선형으로 준다.</param>
        /// <param name="speed">착탄점에서의 속도 변화(m/s).</param>
        /// <param name="spin">착탄점에서의 각속도 변화(rad/s).</param>
        /// <returns>실제로 밀었으면 true.</returns>
        public bool TakeMeteorBlast(Vector3 point, float reach, float speed, float spin)
        {
            if (_body == null) _body = GetComponent<Rigidbody>();
            if (_body == null || _body.isKinematic) return false;

            // <b>고정쇠에 박힌 파츠는 움직이지 않는다</b> — <see cref="PartState.Locked"/>
            // 의 정의가 "운석에 맞아도 움직이지 않는다" 다. 견인 중인 파츠도 뺀다:
            // 빔이 자세를 잡고 있는데 여기서 속도를 얹으면 둘이 같은 강체를 두고 다툰다.
            if (_state is PartState.Locked or PartState.Carried) return false;

            var offset = _body.worldCenterOfMass - point;
            var falloff = reach > 0f ? Mathf.Clamp01(1f - offset.magnitude / reach) : 1f;
            if (falloff <= 0f) return false;

            // 착탄점이 지면이고 파츠의 무게중심은 그보다 위에 있으므로, 이 방향에는
            // 위로 뜨는 성분이 이미 들어 있다 — 따로 더하지 않는다.
            var away = offset.sqrMagnitude > 1e-6f ? offset.normalized : Vector3.up;

            var axis = Vector3.Cross(Vector3.up, away);
            if (axis.sqrMagnitude < 1e-6f) axis = Vector3.right;

            _body.linearVelocity += away * (speed * falloff);
            _body.angularVelocity += axis.normalized * (spin * falloff);
            return true;
        }

        /// <summary>놓였을 때의 질량. 명세가 없으면 예전 고정값으로 돌아간다.</summary>
        public float PlacedMass => _placedMass > 0f ? _placedMass : PhysicsScale.PartMass;

        /// <summary>씬을 만드는 도구가 파츠별 질량을 넣는다.</summary>
        public void SetPlacedMass(float mass) => _placedMass = Mathf.Max(0f, mass);

        /// <summary>
        /// 파츠에 <b>덧붙은</b> 질량(kg). 지금은 용접 비드(<see cref="WeldBead"/>)가 유일한 출처다.
        ///
        /// <see cref="Rigidbody.mass"/> 를 밖에서 직접 더할 수 없다. 이 클래스가 상태가
        /// 바뀔 때마다 <see cref="ApplyRepresentation"/> 에서 질량을 <b>다시 쓰기</b> 때문이다 —
        /// 밖에서 더해 두면 다음 상태 전환에 조용히 사라진다. 그래서 질량의 주인을 하나로
        /// 두고 덧붙는 몫만 여기로 넘긴다.
        /// </summary>
        [SerializeField] float _extraMass;

        public float ExtraMass => _extraMass;

        /// <summary>덧붙은 질량을 갈아 끼우고 물리 표현을 다시 적용한다.</summary>
        public void SetExtraMass(float mass)
        {
            Initialize();

            var next = Mathf.Max(0f, mass);
            if (Mathf.Approximately(next, _extraMass)) return;

            _extraMass = next;
            ApplyRepresentation(_state);
        }

        /// <summary>고정못이 하나라도 풀렸는가. 풀린 것이 하나도 없으면 예전처럼 꿈쩍하지 않는다.</summary>
        public bool HasReleasedClamp
        {
            get
            {
                Initialize();

                for (var i = 0; i < _clamps.Count; i++)
                    if (_clamps[i] != null && _clamps[i].IsRequired && _clamps[i].IsReleased)
                        return true;

                return false;
            }
        }

        /// <summary>지금 파츠를 지면에 묶고 있는 구속 수. 남은 고정못 수와 같아야 한다.</summary>
        public int ClampConstraintCount
        {
            get
            {
                var live = 0;
                for (var i = 0; i < _clampJoints.Count; i++)
                    if (_clampJoints[i] != null) live++;

                return live;
            }
        }

        /// <summary>
        /// 남은 고정못마다 파츠를 지면에 묶는 구속을 하나씩 건다.
        ///
        /// 기획 §14.4 는 "제거된 방향은 들리지만 남은 고정못 방향은 지면에 붙어 있다",
        /// "고정못 하나만 남으면 해당 지점을 중심으로 크게 기울 수 있다" 고 정한다.
        /// 조인트 하나가 그 둘을 모두 표현한다 — 위치만 묶고 회전은 풀어 두면(점 구속),
        /// 넷이 남았을 때는 네 점이 함께 잡아 강체가 되고 하나만 남으면 그 점을 중심으로
        /// 자유롭게 기운다. 조인트 종류를 상황마다 바꿀 필요가 없다.
        ///
        /// 끊어지지 않게 둔다. 기획이 "고정못을 힘으로 강제 제거할 수는 없다" 고 정하므로,
        /// 용접 전 조립을 잡는 조인트(끊어져야 하는 쪽)와 성격이 반대다.
        ///
        /// <c>connectedBody</c> 가 null 이면 <c>connectedAnchor</c> 는 <b>월드 좌표</b>다
        /// (S15P21B202-132 에서 이 성질 때문에 하차한 탑승자가 맵 원점으로 빨려갔다).
        /// 여기서는 그 성질이 바로 필요한 것이다 — 고정못이 박힌 자리에 묶는다.
        /// </summary>
        public void RefreshClampConstraints()
        {
            Initialize();

            for (var i = 0; i < _clampJoints.Count; i++)
                if (_clampJoints[i] != null)
                    DestroyImmediate(_clampJoints[i]);

            _clampJoints.Clear();

            // 고정 상태가 아니거나 아직 아무것도 안 풀렸으면 구속이 필요 없다.
            if (_state != PartState.Locked || !HasReleasedClamp) return;

            for (var i = 0; i < _clamps.Count; i++)
            {
                var clamp = _clamps[i];
                if (clamp == null || !clamp.IsRequired || clamp.IsReleased) continue;

                var joint = gameObject.AddComponent<ConfigurableJoint>();
                joint.connectedBody = null;
                joint.autoConfigureConnectedAnchor = false;
                joint.anchor = transform.InverseTransformPoint(clamp.transform.position);
                joint.connectedAnchor = clamp.transform.position;

                joint.xMotion = ConfigurableJointMotion.Locked;
                joint.yMotion = ConfigurableJointMotion.Locked;
                joint.zMotion = ConfigurableJointMotion.Locked;

                // 회전은 풀어 둔다. 하나만 남았을 때 그 점을 중심으로 기울기 위해서다.
                joint.angularXMotion = ConfigurableJointMotion.Free;
                joint.angularYMotion = ConfigurableJointMotion.Free;
                joint.angularZMotion = ConfigurableJointMotion.Free;

                joint.enablePreprocessing = false;
                joint.breakForce = float.PositiveInfinity;
                joint.breakTorque = float.PositiveInfinity;

                _clampJoints.Add(joint);
            }
        }

        /// <summary>
        /// 물리 표현을 지금 상태로 다시 쓴다 (S15P21B202-389).
        ///
        /// <para><b>물리를 이어받을 때 쓴다.</b> 참가자 화면에서는
        /// <c>WorldEvents.RemoteDecides</c> 가 참이라 <see cref="ApplyRepresentation"/> 이
        /// 무조건 <c>isKinematic = true</c> 를 준다. 그 사람이 새 방장이 되면 그 깃발이
        /// 내려가는데, 그것만으로는 <b>이미 써 둔 값이 바뀌지 않는다</b> — 다시 써야 한다.
        /// 그러지 않으면 세계가 그림인 채로 멈춘다.</para>
        ///
        /// <para>구속도 함께 다시 잡는다. 고정못을 하나 풀었을 때 하는 것과 같은 쌍이다
        /// (<see cref="NotifyClampReleased"/> 참고) — 표현만 고치고 구속을 두면 자유 파츠가
        /// 여전히 한 점에 묶여 들리지 않는다.</para>
        /// </summary>
        public void RefreshRepresentation()
        {
            Initialize();
            ApplyRepresentation(_state);
            RefreshClampConstraints();
        }

        void ApplyRepresentation(PartState state)
        {
            if (_isDockedToPad)
            {
                if (_body == null) _body = GetComponent<Rigidbody>();
                gameObject.layer = GameLayers.Part;
                _body.isKinematic = true;
                _body.mass = PlacedMass + _extraMass;
                return;
            }
            // Converted engines keep their vehicle physics, including during bead mass updates
            // and host migration. Component.enabled does not prevent these direct calls.
            if (GetComponent<PartTractorActivator>() is PartTractorActivator activator && activator.IsActivated)
            {
                if (_body == null) _body = GetComponent<Rigidbody>();
                gameObject.layer = GameLayers.Part;
                _body.isKinematic = WorldEvents.RemoteDecides;
                _body.mass = TransportVehicle.HullMass + _extraMass;
                return;
            }

            var (layer, mass, isKinematic) = RepresentationOf(state, PlacedMass);

            // 레이어는 이 오브젝트에만 준다. 자식의 고정쇠는 Interactable 레이어에 있어야
            // Interactor 가 찾을 수 있으므로 함께 바꾸면 안 된다.
            gameObject.layer = layer;

            if (_body == null) _body = GetComponent<Rigidbody>();

            // 고정못이 하나라도 풀렸으면 kinematic 을 풀어 부분 부양을 허용한다(기획 §14.4).
            // 하나도 안 풀린 파츠는 예전 그대로 kinematic 이다 — 들 것이 없고, 그래야
            // 저중력장·운석이 손대지 않는다.
            //
            // **참가자 화면에서는 절대 풀지 않는다** (S15P21B202-352). 그 화면은 그림이고
            // 자세는 서버가 보낸 것을 따라간다 — `WorldSync.PrepareClient` 가 tracked 전부를
            // `isKinematic = true` 로 세우는 것과 같은 뜻이다("서버가 보낸 자세만 따라간다").
            // 여기서 풀면 그 뒤로 로컬 중력(-5)이 파츠를 끌어내려 서버 자세와 매 프레임
            // 다투고, 참가자 화면에서만 파츠가 떨리거나 처진다. 고정못을 뽑기 시작하는
            // 순간부터 그랬다 — `HasReleasedClamp` 가 참이 되는 자리가 여기다.
// 두 곳에서 막는 이유(AI 리뷰 질문, 2026-09-08). WorldSync.FollowOnClient 도
            // 매 프레임 kinematic 을 세우지만 그것은 **자기 Update 차례**에서다. 이 줄은
            // 그 사이에 이 컴포넌트가 스스로 푸는 것을 막는다 — 풀린 한 물리 스텝에
            // 로컬 중력·충격이 들어가면 서버 자세와 다투어 처지고 떨린다.
            // 견인 엔진도 위 전용 물리 분기에서 같은 권한 규칙을 적용한다.
            _body.isKinematic = WorldEvents.RemoteDecides || (isKinematic && !HasReleasedClamp);

            // 견인 중에는 덧붙은 질량을 더하지 않는다. CarriedPartMass 는 크기와 무관하게
            // "플레이어를 압사시키지 않는 값" 이라 파츠 질량조차 무시하는 자리다 —
            // 여기에 비드 무게를 더하면 비드를 쌓은 파츠만 견인이 달라진다.
            _body.mass = state == PartState.Carried ? mass : mass + _extraMass;

            // 무게중심(centerOfMass)은 <b>대입하지 않는다.</b> Unity 가 자식 콜라이더의
            // 모양과 배치에서 자동 계산하고, S15P21B202-166 이 파츠별 컴파운드 콜라이더를
            // 실측해 넣은 것이 그 자동 계산을 쓰기 위해서다. 한 번이라도 대입하면
            // 자동 계산이 영구히 꺼져, 이후 비드를 붙여도 무게중심이 따라오지 않는다.
            _body.maxAngularVelocity = PhysicsScale.PartMaxAngularSpeed;
        }

        /// <summary>
        /// 자유 상태에서는 취급 질량을 낮추고, 빔 견인 중에는 견인 질량을 쓴다.
        /// 고정된 동안에는 kinematic 이라 운석에 맞아도 밀리지 않는다.
        /// 값의 근거는 S15P21B202-62 기록 문서를 본다.
        /// </summary>
        public static (int Layer, float Mass, bool IsKinematic) RepresentationOf(PartState state) =>
            RepresentationOf(state, PhysicsScale.PartMass);

        /// <param name="placedMass">
        /// 놓였거나 고정된 상태에서 쓸 질량. 견인 중 질량은 압사를 막는 값이라
        /// 파츠 크기와 무관하게 <see cref="PhysicsScale.CarriedPartMass"/> 로 유지한다.
        /// </param>
        public static (int Layer, float Mass, bool IsKinematic) RepresentationOf(
            PartState state, float placedMass) => state switch
        {
            PartState.Locked => (GameLayers.Part, placedMass, true),
            PartState.Free => (GameLayers.Part, placedMass * PhysicsScale.FreePartMassScale, false),
            PartState.Carried => (GameLayers.PartCarried, PhysicsScale.CarriedPartMass, false),
            _ => (GameLayers.Part, placedMass, false),
        };
    }
}
