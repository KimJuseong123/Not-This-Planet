using UnityEngine;
using Metagame.Core;

namespace Metagame.Gameplay
{
    /// <summary>
    /// 모든 손 장비가 공유하는 런타임 수명주기와 입력 경계.
    /// 파생 도구는 PlayerRig나 Unity Input을 직접 알 필요 없이 이 베이스를 통해
    /// 장착 상태, 조준 방향, 의미 입력을 받는다.
    /// </summary>
    [DisallowMultipleComponent]
    public abstract class ToolBase : MonoBehaviour
    {
        [Header("장착 기준점")]
        [Tooltip("비우면 AttachPoints/Muzzle 또는 Muzzle 이름의 자식을 자동으로 찾습니다.")]
        [SerializeField] Transform muzzle;

        ToolInputFrame currentInput;
        ToolAimContext currentAim;

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
