using System;
using Metagame.Core;
using Metagame.Gameplay.Interaction;
using UnityEngine;

namespace Metagame.Gameplay.Parts
{
    /// <summary>
    /// 파츠를 바닥에 붙들고 있는 고정쇠. 상호작용 프레임워크의 첫 구현체다.
    ///
    /// 꾹 눌러 해제하면 소속 파츠에 알리고, 필수 고정쇠가 모두 풀리면 파츠가 Free 로 넘어간다.
    /// 해제되면 파츠에서 분리되어 AnchorPin 외형과 Rigidbody를 가진 소품이 된다.
    ///
    /// 콜라이더는 <see cref="GameLayers.Interactable"/> 레이어에 두어야 한다.
    /// 이 레이어는 충돌 매트릭스에서 전부 꺼져 있어 물리에는 영향을 주지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PartClamp : InteractableBase
    {
        /// <summary>홀드 시간을 지정하지 않았을 때 쓰는 기본값.</summary>
        public const float DefaultHoldSeconds = 1.5f;
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

        Collider[] _colliders;
        Rigidbody _releasedBody;
        bool _initialized;

        public SpaceshipPart Part => _part;

        public bool IsRequired => _isRequired;

        /// <summary>
        /// 필수 여부를 코드에서 지정한다. 레벨 구성 스크립트와 테스트가 쓴다.
        /// 장식용 고정쇠는 풀지 않아도 파츠가 Free 로 넘어간다.
        /// </summary>
        public void SetRequired(bool required) => _isRequired = required;

        public bool IsReleased { get; private set; }

        public Rigidbody ReleasedBody => _releasedBody;

        public event Action<PartClamp> Released;

        void Awake() => Initialize();

        /// <summary>
        /// 소속 파츠와 콜라이더를 찾고 홀드 시간 기본값을 채운다. 여러 번 불러도 안전하다.
        /// Awake 는 EditMode 에서 호출되지 않으므로 테스트와 <see cref="SpaceshipPart"/> 가 직접 부른다.
        /// </summary>
        public void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            if (_part == null) _part = GetComponentInParent<SpaceshipPart>();
            _colliders = GetComponents<Collider>();

            if (HoldSeconds <= 0f) SetHoldSeconds(DefaultHoldSeconds);
        }

        public override InteractionAvailability GetAvailability(Interactor actor)
        {
            Initialize();

            var occupancy = base.GetAvailability(actor);
            if (!occupancy.IsAllowed) return occupancy;

            return IsReleased
                ? InteractionAvailability.Denied(InteractionFailure.NotReady)
                : InteractionAvailability.Allowed;
        }

        public override void OnCompleted(Interactor actor) => Release();

        /// <summary>
        /// 고정쇠를 해제한다. 이미 풀려 있으면 아무 일도 하지 않는다.
        /// 상호작용 없이 연출이나 디버그에서 직접 부를 수도 있다.
        /// </summary>
        public void Release()
        {
            Initialize();

            if (IsReleased) return;
            IsReleased = true;

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
            var visual = FindSceneAnchorPin();
            if (visual != null)
            {
                visual.SetParent(transform, true);
                visual.name = "Visual";
                return;
            }

            // 스킨이 없는 테스트/그레이박스 씬에서도 물리 오브젝트가 보이도록
            // 작은 원통을 대체 외형으로 만든다. 실제 Stage1 씬에서는 카탈로그의
            // PF_Stage1_AnchorPin 인스턴스가 위 경로로 연결된다.
            if (!Application.isPlaying) return;

            var fallback = Prim.Pipe("Visual", transform.position, 0.20f, 0.54f,
                                     new Color(0.34f, 0.38f, 0.44f), 0.2f, transform);
            Prim.Decor(fallback);
        }

        Transform FindSceneAnchorPin()
        {
            if (_part == null || string.IsNullOrEmpty(name)) return null;

            // Stage1SkinBuilder가 AnchorPins 그룹에
            // "<파츠 이름>_<고정쇠 이름>" 규칙으로 PF_Stage1_AnchorPin을 배치한다.
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
            Metagame.Gameplay.Grabbable.Mark(gameObject, "해제된 고정쇠");
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
