using System;
using Metagame.Core;
using Metagame.Gameplay.Interaction;
using UnityEngine;

namespace Metagame.Gameplay.Parts
{
    /// <summary>
    /// 파츠를 바닥에 붙들고 있는 고정못. 상호작용 프레임워크의 첫 구현체다.
    ///
    /// <b>뒤로 이동해 뽑는다</b>(기획 S15P21B202-107 §12.1 "좌클릭을 유지한 채 고정못
    /// 반대 방향으로 뒤로 이동해 뽑는다"). 예전에는 마우스를 위로 16도 들어올려 홀드를
    /// 시작하고 1.5초 기다리는 방식이었다 — §12.1 의 조작이 아니었고, §12.2 의 "1명 약
    /// 8초" 와도 5배 넘게 어긋났다.
    ///
    /// 진행분은 고정못이 갖고 씬에 저장한다. 그래서 §12.1 의 "좌클릭을 놓거나 운석에
    /// 맞아도 빠진 정도는 유지된다" 와 "다른 플레이어가 남은 진행을 이어서 수행할 수
    /// 있다" 가 성립한다 — 누가 뽑았는지는 기록하지 않는다.
    ///
    /// 해제되면 파츠에서 분리되어 AnchorPin 외형과 Rigidbody를 가진 소품이 된다.
    ///
    /// 콜라이더는 <see cref="GameLayers.Interactable"/> 레이어에 두어야 한다.
    /// 이 레이어는 충돌 매트릭스에서 전부 꺼져 있어 물리에는 영향을 주지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PartClamp : InteractableBase
    {
        const float ReleasedMass = 4f;
        const float ReleasedLinearDamping = 0.05f;
        const float ReleasedAngularDamping = 0.25f;
        const float ReleasedSurfaceGap = 0.12f;
        const float ReleasedPopVelocity = 2.4f;
        const float ReleasedPopAngularSpeed = 1.1f;

        [SerializeField]
        [Tooltip("비우면 부모에서 SpaceshipPart 를 찾는다.")]
        SpaceshipPart _part;

        [SerializeField]
        [Tooltip("끄면 풀지 않아도 파츠가 Free 로 넘어간다. 장식용 고정쇠에 쓴다.")]
        bool _isRequired = true;

        [SerializeField, HideInInspector]
        Grabbable _gripTarget;

        [SerializeField, HideInInspector]
        Transform _releasedVisual;

        /// <summary>
        /// 뽑힌 정도를 초로 누적한 값. 직렬화한다.
        ///
        /// 저장하지 않으면 씬을 다시 로드했을 때 반쯤 뽑아 둔 고정못이 처음으로
        /// 돌아간다(<see cref="WeldBead"/> 의 사슬 구조도 같은 이유로 저장한다).
        /// </summary>
        [SerializeField, HideInInspector] float _pulledSeconds;

        Collider[] _colliders;
        Rigidbody _releasedBody;
        bool _initialized;
        int _progressSoundBucket;

        public SpaceshipPart Part => _part;

        /// <summary>실제 팔을 연결할 때 사용하는 숨김 Grabbable 표식.</summary>
        public Grabbable GripTarget => _gripTarget;

        /// <summary>고정쇠가 붙어 있는 파츠의 물리 몸체.</summary>
        public Rigidbody PartBody => _part != null ? _part.GetComponent<Rigidbody>() : null;

        public bool IsRequired => _isRequired;

        /// <summary>
        /// 필수 여부를 코드에서 지정한다. 레벨 구성 스크립트와 테스트가 쓴다.
        /// 장식용 고정쇠는 풀지 않아도 파츠가 Free 로 넘어간다.
        /// </summary>
        public void SetRequired(bool required) => _isRequired = required;

        /// <summary>
        /// 씬에 있는 고정못 전부. <see cref="PartSocket.All"/> 과 같은 방식이다.
        ///
        /// 운석 공세가 "작업 중인 곳" 을 여기서 찾는다(기획 §17.5, S15P21B202-234).
        /// 파츠를 훑지 않는 이유는 <b>당기는 중인지</b>가 파츠가 아니라 고정못의 상태라서다.
        ///
        /// EditMode 에서는 <c>OnEnable</c> 이 호출되지 않으므로 <see cref="Initialize"/>
        /// 가 직접 등록한다.
        /// </summary>
        public static readonly System.Collections.Generic.List<PartClamp> All = new();

        void OnEnable() => Register();

        void OnDisable() => Unregister();

        public void Register()
        {
            if (!All.Contains(this)) All.Add(this);
        }

        public void Unregister() => All.Remove(this);

        public bool IsReleased { get; private set; }

        /// <summary>
        /// 뽑힌 정도 0~1.
        ///
        /// <b>화면에 진행 바로 띄우지 않는다.</b> 기획 §12.1 은 "진행될수록 고정못이
        /// 흔들리고 에너지 선이 점멸한다" 로 표현을 정하며, 그것은 [Art] 의 몫이다.
        /// 이 값은 그 연출과 완료 판정에 쓴다.
        /// </summary>
        public float Progress => Mathf.Clamp01(_pulledSeconds / PhysicsScale.ClampPullSeconds);

        /// <summary>지금 이 고정못을 잡고 있는 인원.</summary>
        public int Pullers { get; private set; }

        /// <summary>
        /// 인원 수에 따른 뽑는 시간(초). 기획 §12.2 의 표를 그대로 따른다.
        /// 1명 8초 · 2명 약 4.5초.
        /// </summary>
        public static float PullSecondsFor(int pullers)
        {
            var count = Mathf.Max(1, pullers);
            var rate = 1f + (count - 1) * PhysicsScale.ClampPullAssist;
            return PhysicsScale.ClampPullSeconds / rate;
        }

        public Rigidbody ReleasedBody => _releasedBody;

        public event Action<PartClamp> Released;
        public static event Action<PartClamp, int> AnyProgressed;

        void Awake() => Initialize();

        /// <summary>
        /// 소속 파츠와 콜라이더를 찾고 홀드 시간 기본값을 채운다. 여러 번 불러도 안전하다.
        /// Awake 는 EditMode 에서 호출되지 않으므로 테스트와 <see cref="SpaceshipPart"/> 가 직접 부른다.
        /// </summary>
        public void Initialize()
        {
            Register();

            if (_initialized) return;
            _initialized = true;

            if (_part == null) _part = GetComponentInParent<SpaceshipPart>();
            _colliders = GetComponentsInChildren<Collider>(true);

            // 외양은 충돌 판정과 같은 Clamp 아래에 둔다. 구형 씬에서 직렬화된 외부
            // 외양이 들어와도 여기서 소유 루트를 바로잡아 렌더러와 Collider가 함께 움직이게 한다.
            // 렌더러가 없는 테스트용 외형이면 조용히 건너뛴다.
            if (_releasedVisual != null)
            {
                if (!_releasedVisual.IsChildOf(transform))
                    _releasedVisual.SetParent(transform, true);
                _releasedVisual.gameObject.SetActive(true);
                AlignReleasedVisual(_releasedVisual);
            }

            // 고정쇠 Collider는 Interactable 레이어라 Hand.Search에는 걸리지 않는다.
            // 별도의 표식만 만들어 명시적 clamp grip 경로에서 사용한다.
            if (_gripTarget == null) _gripTarget = GetComponent<Grabbable>();
            if (_gripTarget == null) _gripTarget = gameObject.AddComponent<Grabbable>();
            _gripTarget.Label = "";
            _gripTarget.CanGrab = false;
            _gripTarget.Climbable = false;
            _gripTarget.Priority = 0f;

            // 홀드로 풀리지 않는다. 진행은 뒤로 이동으로만 만든다(기획 §12.1).
            SetHoldSeconds(0f);
        }

        /// <summary>
        /// Stage1SkinBuilder가 Clamp 아래의 외양을 직접 연결한다.
        /// 외부에 남아 있는 구형 씬 외양도 이 메서드에서 Clamp 아래로 옮긴다.
        /// </summary>
        public void AssignReleasedVisual(Transform visual)
        {
            _releasedVisual = visual;
            if (_releasedVisual == null) return;

            if (!_releasedVisual.IsChildOf(transform))
                _releasedVisual.SetParent(transform, true);
            _releasedVisual.gameObject.SetActive(true);
            AlignReleasedVisual(_releasedVisual);
        }

        /// <summary>
        /// 외양 프리팹의 피벗이 무엇이든 실제 모델의 바닥과 중심을 고정못에 맞춘다.
        ///
        /// 스킨 프리팹은 루트가 모델 중심이 아닐 수 있고, 씬에 저장된 프리팹 인스턴스는
        /// 파츠의 수동 배치 이전 좌표를 들고 있을 수 있다. 루트 위치를 직접 고정하면
        /// 프리팹 피벗 때문에 모델이 반 높이만큼 어긋나므로 렌더러 경계를 기준으로 보정한다.
        /// </summary>
        void AlignReleasedVisual(Transform visual)
        {
            if (visual == null) return;

            MeshVisual.EnsureRenderers(visual.gameObject);
            var renderers = visual.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0) return;

            Bounds bounds = default;
            var hasBounds = false;
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null) continue;

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            if (!hasBounds || !IsFinite(bounds.center) || !IsFinite(bounds.min)) return;

            var delta = new Vector3(
                transform.position.x - bounds.center.x,
                transform.position.y - bounds.min.y,
                transform.position.z - bounds.center.z);
            if (!IsFinite(delta)) return;

            visual.position += delta;
        }

        static bool IsFinite(Vector3 value) =>
            !float.IsNaN(value.x) && !float.IsInfinity(value.x)
            && !float.IsNaN(value.y) && !float.IsInfinity(value.y)
            && !float.IsNaN(value.z) && !float.IsInfinity(value.z);

        /// <summary>
        /// 이 고정못을 잡는다. 정원(기획 §12.2 "최대 2명")이 차 있으면 <c>false</c>.
        ///
        /// 멀티 동기화는 [Network] 몫이므로 지금 실제로 도는 것은 1명 경로다. 정원 판정을
        /// 여기 하나로 모아 두면 그때 고칠 곳이 늘지 않는다.
        /// </summary>
        public bool TryJoinPull()
        {
            Initialize();

            if (IsReleased || Pullers >= PhysicsScale.ClampMaxPullers) return false;

            Pullers++;
            return true;
        }

        /// <summary>손을 뗀다. 진행분은 그대로 남는다(기획 §12.1).</summary>
        public void LeavePull() => Pullers = Mathf.Max(0, Pullers - 1);

        /// <summary>
        /// 뒤로 당긴다. 실제로 진행됐으면 <c>true</c>.
        ///
        /// 호출자(<see cref="PlayerRig"/>)가 방향을 판정한다 — 고정못은 자기가 어느
        /// 방향으로 당겨지는지 알 필요가 없고, 그 판정에는 카메라 기준 이동 입력이
        /// 필요하다.
        ///
        /// 다 뽑히면 <see cref="Release"/> 로 넘어간다.
        /// </summary>
        public bool Pull(float deltaTime)
        {
            Initialize();

            if (IsReleased || deltaTime <= 0f) return false;

            // 인원이 많으면 같은 시간에 더 많이 진행된다. 시간을 나누는 대신 속도를
            // 곱하는 이유는, 도중에 한 명이 붙거나 떠나도 그때까지의 진행분이
            // 그대로 유지되기 때문이다.
            var rate = PhysicsScale.ClampPullSeconds / PullSecondsFor(Pullers);
            _pulledSeconds += deltaTime * rate;

            var bucket = Mathf.FloorToInt(Progress * 10f);
            if (bucket > _progressSoundBucket)
            {
                _progressSoundBucket = bucket;
                AnyProgressed?.Invoke(this, bucket);
            }

            if (_pulledSeconds < PhysicsScale.ClampPullSeconds) return true;

            Release();
            return true;
        }

        /// <summary>진행분을 0 으로 되돌린다. 레벨 초기화에서 쓴다.</summary>
        /// <summary>
        /// 서버가 보낸 진행 상태를 그대로 맞춘다 (S15P21B202-152).
        ///
        /// 다른 사람 화면에서도 고정쇠가 같이 뽑혀야 한다. 판정은 서버가 하고 여기서는
        /// 그 결과만 따라간다 — 클라이언트가 스스로 진행도를 올리면 서버와 어긋난다.
        /// </summary>
        /// <param name="progress">0~1. 서버의 <see cref="Progress"/> 값.</param>
        /// <param name="released">서버에서 이미 풀렸는가.</param>
        public void ApplyNetworkState(float progress, bool released)
        {
            if (released)
            {
                Release();          // 이미 풀려 있으면 아무 일도 하지 않는다
                return;
            }

            _pulledSeconds = Mathf.Clamp01(progress) * PhysicsScale.ClampPullSeconds;
        }

        public void ResetProgress()
        {
            Initialize();
            _pulledSeconds = 0f;
            _progressSoundBucket = 0;
        }

        public override InteractionAvailability GetAvailability(Interactor actor)
        {
            Initialize();

            var occupancy = base.GetAvailability(actor);
            if (!occupancy.IsAllowed) return occupancy;

            if (IsReleased) return InteractionAvailability.Denied(InteractionFailure.NotReady);

            // 기획 §12.2 "같은 고정못은 최대 2명".
            return Pullers >= PhysicsScale.ClampMaxPullers
                ? InteractionAvailability.Denied(InteractionFailure.Occupied)
                : InteractionAvailability.Allowed;
        }

        /// <summary>
        /// <b>홀드로는 풀리지 않는다.</b> 기획 §12.1 이 뒤로 이동해 뽑는 것으로 정하므로
        /// 여기서 해제하면 그 조작을 우회하는 길이 생긴다.
        ///
        /// <see cref="HoldSeconds"/> 를 0 으로 두어 이 훅이 눌린 즉시 불리지만,
        /// 진행은 <see cref="Pull"/> 로만 만든다.
        /// </summary>
        public override void OnCompleted(Interactor actor) { }

        /// <summary>
        /// 고정쇠를 해제한다. 이미 풀려 있으면 아무 일도 하지 않는다.
        /// 상호작용 없이 연출이나 디버그에서 직접 부를 수도 있다.
        /// </summary>
        public void Release()
        {
            Initialize();

            if (IsReleased) return;
            IsReleased = true;
            Pullers = 0;
            _pulledSeconds = PhysicsScale.ClampPullSeconds;
            _progressSoundBucket = 10;

            // 고정쇠는 원래 파츠의 자식이므로 먼저 분리한다. Rigidbody를 파츠의
            // Rigidbody 아래에 둔 채 추가하면 두 물리 몸체가 한 계층에 겹친다.
            transform.SetParent(null, true);
            AttachReleasedVisual();
            PrepareReleasedPhysics();

            Released?.Invoke(this);

            if (_part != null) _part.NotifyClampReleased(this);

            // 현재 카탈로그에는 해제 순간을 위한 사운드 에셋이 없으므로, 기존 진공
            // 레골리스 효과를 짧은 "뽁!" 시각 피드백으로 사용한다.
            if (Application.isPlaying)
                Puff.Regolith(transform.position, 0.75f);
        }

        void AttachReleasedVisual()
        {
            var visual = _releasedVisual != null ? _releasedVisual : FindSceneAnchorPin();
            if (visual != null)
            {
                MeshVisual.EnsureRenderers(visual.gameObject);
                var renderers = visual.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0)
                {
                    visual = null;
                }
                else
                {
                    visual.gameObject.SetActive(true);
                    for (var i = 0; i < renderers.Length; i++)
                        if (renderers[i] != null) renderers[i].enabled = true;

                    visual.SetParent(transform, true);
                    visual.name = "Visual";
                    _releasedVisual = visual;
                    return;
                }
            }

            if (visual == null)
            {
                // 스킨이 없는 테스트/그레이박스 씬에서도 물리 오브젝트가 보이도록
                // 작은 원통을 대체 외형으로 만든다. 실제 Stage1 씬에서는 카탈로그의
                // PF_Stage1_AnchorPin 인스턴스가 위 경로로 연결된다.
                if (!Application.isPlaying) return;

                var fallback = Prim.Pipe("Visual", transform.position, 0.20f, 0.54f,
                                         new Color(0.34f, 0.38f, 0.44f), 0.2f, transform);
                Prim.Decor(fallback);
                _releasedVisual = fallback.transform;
            }
        }

        Transform FindSceneAnchorPin()
        {
            if (_part == null || string.IsNullOrEmpty(name)) return null;

            // 구형 씬 호환용. 새 씬에서는 _releasedVisual이 Clamp 자식으로 직렬화된다.
            // 이전에 AnchorPins 그룹에 "<파츠 이름>_<고정쇠 이름>" 규칙으로 배치했던
            // 프리팹을 찾아 오너 아래로 옮긴다.
            var pin = GameObject.Find(_part.name + "_" + name);
            return pin != null ? pin.transform : null;
        }

        void PrepareReleasedPhysics()
        {
            // 고정쇠의 원래 중심은 파츠 밑면과 같은 높이라 구형 콜라이더의 절반이
            // 바닥 안에 묻혀 있다. 그대로 Dynamic Rigidbody를 켜면 바닥의 한쪽 면을
            // 놓치고 아래로 빠질 수 있으므로, 자신의 반 높이 + 여유만큼 먼저 띄운다.
            LiftClearOfOriginalSeat();
            gameObject.layer = GameLayers.Prop;

            if (_colliders != null)
            {
                foreach (var c in _colliders)
                {
                    if (c == null) continue;
                    c.gameObject.layer = GameLayers.Prop;
                    c.enabled = true;
                    c.isTrigger = false;
                    c.material = Prim.GroundPhysics;
                }
            }

            _releasedBody = GetComponent<Rigidbody>();
            if (_releasedBody == null) _releasedBody = gameObject.AddComponent<Rigidbody>();

            _releasedBody.isKinematic = false;
            _releasedBody.useGravity = true;
            _releasedBody.mass = ReleasedMass;
            _releasedBody.linearDamping = ReleasedLinearDamping;
            _releasedBody.angularDamping = ReleasedAngularDamping;
            _releasedBody.interpolation = RigidbodyInterpolation.Interpolate;
            _releasedBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _releasedBody.maxDepenetrationVelocity = 4f;

            // 모든 고정쇠가 동일하게 위로 "뽁!" 튀도록 초기 속도를 직접 넣는다.
            // 이후에는 중력과 바닥 마찰에 맡겨 자연스럽게 떨어지고 굴러간다.
            _releasedBody.linearVelocity = Vector3.up * ReleasedPopVelocity;
            _releasedBody.angularVelocity = new Vector3(
                ReleasedPopAngularSpeed, ReleasedPopAngularSpeed * 0.35f,
                -ReleasedPopAngularSpeed * 0.55f);

            // 이후에는 일반 소품처럼 손으로 집을 수 있다.
            _gripTarget = Metagame.Gameplay.Grabbable.Mark(gameObject, "해제된 고정쇠");
            _gripTarget.CanGrab = true;
        }

        void LiftClearOfOriginalSeat()
        {
            if (_colliders == null || _colliders.Length == 0) return;

            var bounds = _colliders[0].bounds;
            for (var i = 1; i < _colliders.Length; i++)
            {
                if (_colliders[i] != null) bounds.Encapsulate(_colliders[i].bounds);
            }

            if (float.IsNaN(bounds.extents.y) || float.IsInfinity(bounds.extents.y)) return;

            transform.position += Vector3.up * (bounds.extents.y + ReleasedSurfaceGap);
            Physics.SyncTransforms();
        }
    }
}
