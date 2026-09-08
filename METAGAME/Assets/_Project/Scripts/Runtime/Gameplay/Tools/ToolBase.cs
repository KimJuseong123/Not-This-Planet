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

        /// <summary>실제로 껐던 몸. <see cref="SuspendCollisions"/> 의 주석이 이유를 적었다.</summary>
        Rigidbody suspendedBody;
        bool collisionsSuspended;

        /// <summary>원격이 알려 준 휴대 상태. <see cref="ApplyRemoteHeld"/> 참고.</summary>
        bool remoteHeld;

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
            remoteHeld = false;   // 내 손이 우선이다 — ApplyRemoteHeld 의 주석
            SuspendCollisions();
            currentInput = new ToolInputFrame();
            // 카메라가 없다고 도구 자신의 forward 로 대신하면 안 된다 — 도구는 래그돌 손에
            // 들려 흔들리므로 사람이 보는 곳과 무관해진다. 조준 창구는 카메라가 있으면
            // 카메라를, 없으면 클라이언트가 보낸 각도를 쓴다(-147).
            currentAim = new ToolAimContext(owner, owner != null ? owner.Cam : null,
                                            owner != null ? owner.AimForward : transform.forward);
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
            remoteHeld = false;
            currentInput = new ToolInputFrame();
            currentAim = new ToolAimContext(null, null, transform.forward);
        }


        /// <summary>
        /// 원격이 정한 휴대 상태를 <b>이 기계의 그림에</b> 반영한다 (S15P21B202-352).
        ///
        /// <para><b>왜 필요한가.</b> 잡기·장착 판정은 서버에서만 돈다(-145) — 참가자의
        /// <c>PlayerRig</c> 는 입력을 <c>[Command]</c> 로 보내고 결과를 받아 그리는 그림일
        /// 뿐이라 이 기계에서는 <see cref="Equip"/> 가 <b>한 번도 불리지 않는다</b>.
        /// 그래서 <c>WorldSync</c> 가 뿌리 자세를 나르기 시작한 뒤에도(-352) 도구는
        /// 어긋난 채로 남았다: 방망이의 자식 <c>Visual_ZUpToYUp</c> 이 손에 쥔 각도
        /// (<c>+0.45/-0.55/+0.35 · -35°</c>)로 안 돌아가고, 휴대 중에 꺼야 할 충돌이 켜진
        /// 채라 남의 도구가 내 몸을 막았다.</para>
        ///
        /// <para><b>이 기계가 주인이면 무시한다.</b> 자기 손의 도구는 로컬
        /// <see cref="Equip"/> 가 이미 옳게 세워 두었고, 그 위에 원격 값을 덮으면 잡은
        /// 순간과 놓은 순간에 한 프레임씩 튄다.</para>
        ///
        /// <para><b>소유권을 옮기지 않는다</b> — <see cref="Owner"/> 와 <see cref="Body"/> 는
        /// 건드리지 않고 <see cref="IsEquipped"/> 도 그대로다. 판정은 서버 몫이고 여기서
        /// 바꾸는 것은 보이는 것뿐이다.</para>
        ///
        /// <para><b>파생 클래스의 계약</b>(AI 리뷰 지적, 2026-09-08): 이 길로 오면
        /// <see cref="OnEquipped"/>·<see cref="OnUnequipped"/> 가 <see cref="Owner"/> 와
        /// <see cref="Body"/> 가 <c>null</c> 인 채로 불린다. 그러므로 두 훅은 <b>그 둘을
        /// 참조하지 않거나 null 을 견뎌야 한다.</b> 지금 파생은 방망이와 용접 건 둘뿐이고
        /// 전수 확인했다 — 방망이의 <c>OnUnequipped</c> 는 <c>Body != null</c> 로 감싸고,
        /// 용접 건은 두 훅에서 그 둘을 아예 쓰지 않는다(비주얼·빔·꽃잎 애니메이터만
        /// 만진다). <c>NetworkReceivedStateTests</c> 가 두 도구 모두에 이 길을 태워
        /// 이 계약을 지킨다.</para>
        /// </summary>
        public void ApplyRemoteHeld(bool held)
        {
            if (IsEquipped) return;
            if (remoteHeld == held) return;

            remoteHeld = held;

            if (held)
            {
                SuspendCollisions();
                OnEquipped();
                return;
            }

            RestoreCollisions();
            OnUnequipped();
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
        /// 충돌을 끄고 켤 몸.
        ///
        /// <para>장착 판정이 건네준 <see cref="Body"/> 가 있으면 그것을 쓴다. 없으면
        /// 이 오브젝트의 <c>Rigidbody</c> 로 물러난다 — <see cref="ApplyRemoteHeld"/> 로
        /// 오는 길에는 <see cref="Body"/> 가 <c>null</c> 이다(그 기계에서는
        /// <see cref="Equip"/> 가 불리지 않는다). 그 경우를 빼먹으면 <b>참가자 화면에서
        /// 충돌이 안 꺼져</b> 남이 든 도구가 내 몸을 막는다 — 자세를 서버가 몰아 주므로
        /// 그 도구는 키네마틱이고, 키네마틱은 사람을 밀어낸다.</para>
        /// </summary>
        Rigidbody CollisionBody => Body != null ? Body : GetComponent<Rigidbody>();

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
            var body = CollisionBody;
            if (collisionsSuspended || body == null) return;

            // **끈 몸을 기억해 둔다**(AI 리뷰 지적, 2026-09-08). 껐을 때와 되돌릴 때
            // <see cref="CollisionBody"/> 가 서로 다른 몸을 가리킬 수 있다 — 원격
            // 경로에서는 자기 몸이고, 그 사이에 이 기계가 장착하면 그때부터는
            // <see cref="Body"/> 다(도구 컴포넌트가 자식에 있으면 부모의 몸일 수 있다).
            // 다른 몸에 되돌리면 껐던 쪽이 **영원히 충돌 없는 상태로 남는다.**
            suspendedBody = body;
            restoreCollisions = body.detectCollisions;
            collisionsSuspended = true;
            body.detectCollisions = false;
        }

        /// <summary>휴대 전 상태로 되돌린다. 끈 적이 없으면 아무것도 하지 않는다.</summary>
        void RestoreCollisions()
        {
            if (!collisionsSuspended) return;

            collisionsSuspended = false;

            // 껐던 그 몸으로만 되돌린다 — SuspendCollisions 의 주석 참고.
            if (suspendedBody != null) suspendedBody.detectCollisions = restoreCollisions;
            suspendedBody = null;
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

        /// <summary>
        /// 기준점을 갈아 끼운다(S15P21B202-277).
        ///
        /// <b>프리팹의 <c>AttachPoints/Muzzle</c> 을 믿을 수 없는 경우가 있다.</b> 용접
        /// 건의 그 자리는 코드로 만든 임시 총에 맞춰 놓은 값인데, 장착하면 실제 모델만
        /// 옮겨져 기준점이 보이는 총에서 1.34m 떨어졌다. 그런 도구는 보이는 모델에서
        /// 끝점을 구해 여기로 넣는다.
        ///
        /// <see cref="Muzzle"/> 을 읽는 곳이 빔 · 용접 · 절단 · 파티클로 여러 군데라,
        /// <b>기준점 하나를 바꾸면 전부 따라온다</b> — 읽는 쪽마다 고치면 하나를 빼먹는다.
        /// </summary>
        protected void SetMuzzle(Transform replacement)
        {
            if (replacement == null) return;

            muzzle = replacement;
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
