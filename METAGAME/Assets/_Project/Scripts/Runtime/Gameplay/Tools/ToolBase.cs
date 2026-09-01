using UnityEngine;
using Metagame.Core;

namespace Metagame.Gameplay
{
    /// <summary>
    /// 모든 손 장비가 공유하는 런타임 수명주기와 입력 경계.
    /// 파생 도구는 PlayerRig나 Unity Input을 직접 알 필요 없이 이 베이스를 통해
    /// 장착 상태, 조준 방향, 의미 입력을 받는다.
    ///
    /// <b>휴대 중에는 도구가 다른 것과 부딪히지 않는다</b>(S15P21B202-237).
    /// 1인칭으로 들고 아래를 볼 때 도구 콜라이더가 지면을 파고들면, PhysX 는 그 겹침을
    /// 풀기 위해 <b>비키네마틱인 쪽 — 플레이어 —</b> 를 밀어낸다. 겹침이 깊을수록 밀어내는
    /// 속도가 커져 공중으로 튄다.
    ///
    /// 도구는 <see cref="Tuning.FixedGripMass"/>(40kg)보다 가벼워 손이 잡을 때 키네마틱이
    /// 되므로(<see cref="Player.Hand"/>) 도구 쪽은 밀려나지 않는다. 그래서 밀리는 것은
    /// 언제나 플레이어다.
    ///
    /// <b>이 처리를 파생 클래스에 두지 않는다.</b> 예전에는
    /// <see cref="PlasmaWeldingTool"/> 이 자기 <c>OnEquipped</c> 안에서 직접 껐고,
    /// S15P21B202-233 이 <see cref="MeteorBat"/> 을 만들 때 그 세 줄을 <b>빠뜨려</b>
    /// 방망이를 들고 바닥을 보면 플레이어가 날아갔다. 도구 전체의 규칙이므로 상속하는
    /// 쪽이 기억할 일이 아니다.
    /// </summary>
    [DisallowMultipleComponent]
    public abstract class ToolBase : MonoBehaviour
    {
        [Header("장착 기준점")]
        [Tooltip("비우면 AttachPoints/Muzzle 또는 Muzzle 이름의 자식을 자동으로 찾습니다.")]
        [SerializeField] Transform muzzle;

        ToolInputFrame currentInput;
        ToolAimContext currentAim;

        /// <summary>휴대 전의 <c>detectCollisions</c>. 해제할 때 이 값으로 돌린다.</summary>
        bool restoreCollisions;
        bool collisionsSuspended;

        public PlayerRig Owner { get; private set; }
        public Rigidbody Body { get; private set; }
        public Transform Muzzle { get { return muzzle != null ? muzzle : transform; } }
        public bool IsEquipped { get; private set; }

        protected virtual void Awake()
        {
            ResolveMuzzle();
        }

        protected virtual void OnValidate()
        {
            ResolveMuzzle();
        }

        /// <summary>플레이어가 도구를 장착할 때 PlayerRig가 호출한다.</summary>
        public void Equip(PlayerRig owner, Rigidbody body)
        {
            if (IsEquipped) Unequip();

            Owner = owner;
            Body = body;
            IsEquipped = true;
            SuspendCollisions();
            currentInput = new ToolInputFrame();
            currentAim = new ToolAimContext(owner, owner != null ? owner.Cam : null,
                                            owner != null && owner.Cam != null
                                                ? owner.Cam.AimDirection
                                                : transform.forward);
            ResolveMuzzle();
            OnEquipped();
        }

        /// <summary>플레이어가 도구를 내려놓거나 투척할 때 호출한다.</summary>
        public void Unequip()
        {
            if (!IsEquipped) return;

            OnUnequipped();
            RestoreCollisions();
            IsEquipped = false;
            Owner = null;
            Body = null;
            currentInput = new ToolInputFrame();
            currentAim = new ToolAimContext(null, null, transform.forward);
        }

        /// <summary>
        /// PlayerRig가 물리 틱마다 의미 입력과 조준 상태를 전달한다.
        /// </summary>
        public void SetInput(ToolInputFrame input, ToolAimContext aim)
        {
            currentInput = input;
            currentAim = aim;
            if (IsEquipped) OnToolInput(input, aim);
        }

        /// <summary>
        /// 휴대 중 충돌을 끈다. 원래 값을 기억해 두고 <see cref="RestoreCollisions"/> 가
        /// 되돌린다.
        ///
        /// 두 번 불려도 <b>처음 값</b>을 지킨다. 장착 중에 다시 장착되는 경로가 있으면
        /// 그때 이미 꺼진 <c>false</c> 를 원래 값으로 기억해 버려, 내려놓아도 충돌이
        /// 돌아오지 않는다.
        /// </summary>
        void SuspendCollisions()
        {
            if (collisionsSuspended || Body == null) return;

            restoreCollisions = Body.detectCollisions;
            collisionsSuspended = true;
            Body.detectCollisions = false;
        }

        /// <summary>휴대 전 상태로 되돌린다. 끈 적이 없으면 아무것도 하지 않는다.</summary>
        void RestoreCollisions()
        {
            if (!collisionsSuspended) return;

            collisionsSuspended = false;
            if (Body != null) Body.detectCollisions = restoreCollisions;
        }

        /// <summary>
        /// 이번 틱에 도구를 어느 방향에 둘지. 기본은 조준 그대로다.
        ///
        /// <see cref="PlayerRig"/> 는 <b>방향 하나로</b> 도구의 위치와 회전을 정한다 —
        /// 카메라 앞 <see cref="Tuning.ToolHoldDistance"/> 지점에 두고 그 방향을 바라보게
        /// 한다. 그래서 이 값을 흔들면 도구가 카메라 주위로 <b>호를 그린다.</b>
        /// 방망이의 휘두르기가 그것이다(S15P21B202-243).
        ///
        /// <b>조준 자체는 바뀌지 않는다.</b> <see cref="ToolAimContext.Direction"/> 은 원래
        /// 조준을 그대로 받는다 — 쳐낸 운석이 날아가는 방향(기획 §18.2)이 스윙 위상에
        /// 따라 달라지면 플레이어가 겨눌 수 없다.
        /// </summary>
        public virtual Vector3 CarryAim(Vector3 aim) => aim;

        /// <summary>장착 직후 한 번 호출되는 도구별 초기화 훅.</summary>
        protected virtual void OnEquipped() { }

        /// <summary>해제 직전 호출되는 도구별 정리 훅.</summary>
        protected virtual void OnUnequipped() { }

        /// <summary>도구별 기능 입력을 처리하는 훅.</summary>
        protected virtual void OnToolInput(ToolInputFrame input, ToolAimContext aim) { }

        /// <summary>
        /// 빔·라인·파티클처럼 렌더 프레임마다 갱신할 표현 훅.
        /// 물리 힘이나 Rigidbody 상태 변경은 이 경로에서 수행하지 않는다.
        /// </summary>
        protected virtual void OnToolVisualUpdate(ToolInputFrame input, ToolAimContext aim) { }

        protected virtual void LateUpdate()
        {
            if (IsEquipped) OnToolVisualUpdate(currentInput, currentAim);
        }

        protected virtual void OnDestroy() { }

        void ResolveMuzzle()
        {
            if (muzzle != null) return;

            muzzle = transform.Find("AttachPoints/Muzzle");
            if (muzzle == null) muzzle = transform.Find("Muzzle");
        }
    }

    /// <summary>도구가 바라보는 방향과 현재 플레이어/카메라를 묶은 컨텍스트.</summary>
    public struct ToolAimContext
    {
        public readonly PlayerRig Player;
        public readonly OrbitCamera Camera;
        public readonly Vector3 Direction;

        public ToolAimContext(PlayerRig player, OrbitCamera camera, Vector3 direction)
        {
            Player = player;
            Camera = camera;
            Direction = direction.sqrMagnitude > 1e-4f ? direction.normalized : Vector3.forward;
        }
    }
}
