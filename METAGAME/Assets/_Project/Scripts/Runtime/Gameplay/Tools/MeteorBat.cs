using System;
using UnityEngine;
using Metagame.Core;
using Metagame.Gameplay.Hazards;

namespace Metagame.Gameplay
{
    /// <summary>
    /// 운석 방망이. 떨어지는 운석을 쳐내고, 동료와 소품을 직접 때릴 수도 있다(기획 §18).
    ///
    /// 아트 에셋은 S15P21B202-187([Art])이 만들었고 <b>로직이 없었다</b> —
    /// 자산 목록에 "휘두르기 애니메이션·타격 판정·운석 반사·장착·네트워크 로직은
    /// 포함하지 않는다" 고 적혀 있다. 이 클래스가 그중 판정·반사·장착을 채운다.
    ///
    /// <b>콜라이더로 맞히지 않는다.</b> 기획 §18.1 이 "실제 방망이 메시가 운석과 정확히
    /// 닿지 않아도 넓은 에너지 궤적으로 판정을 납득시킨다" 고 정한다. 그래서 판정은
    /// 기하 두 가지다 — 낙하 지점까지의 거리와 착탄까지 남은 시간.
    ///
    /// <b>완벽한 프레임·등급·치명타가 없다.</b> §18.1 이 그렇게 정하므로 맞으면 맞고
    /// 아니면 헛스윙이다. 난이도는 "맞을 자리에 서 있어야 한다" 는 것 하나로 만든다 —
    /// 타격 보조 반경(2.5m)이 운석 충격 반경(2m)보다 조금 넓을 뿐이다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MeteorBat : ToolBase
    {
        [Header("1인칭 휴대 포즈")]
        [SerializeField] Vector3 heldVisualLocalPosition = new Vector3(0.45f, -0.55f, 0.35f);
        [SerializeField] Vector3 heldVisualLocalEuler = new Vector3(0f, 0f, -35f);

        Transform heldVisual;
        Vector3 droppedVisualLocalPosition;
        Quaternion droppedVisualLocalRotation;
        bool droppedVisualPoseCaptured;

        /// <summary>
        /// 소품을 찾는 겹침 질의 버퍼. 매 스윙 새로 만들지 않는다
        /// (<see cref="Parts.WeldBead"/> 와 같은 방식이다).
        /// </summary>
        const int ProbeSize = 32;
        static readonly Collider[] Probe = new Collider[ProbeSize];

        float cooldown;
        float swingElapsed = -1f;
        Vector3 swingAim = Vector3.forward;

        /// <summary>헛스윙 뒤 남은 대기 시간(초). 0 이면 휘두를 수 있다.</summary>
        public float Cooldown => cooldown;

        /// <summary>지금 휘두르는 중인가. 동작이 도는 동안만 참이다.</summary>
        public bool IsSwinging => swingElapsed >= 0f;

        /// <summary>휘두르기 진행도 0~1. 휘두르지 않으면 0 이다.</summary>
        public float SwingProgress01 =>
            IsSwinging ? Mathf.Clamp01(swingElapsed / PhysicsScale.BatSwingSeconds) : 0f;

        public bool CanSwing => cooldown <= 0f;

        /// <summary>가장 최근 스윙에서 쳐낸 운석. 헛스윙이면 null.</summary>
        public Meteor LastDeflected { get; private set; }

        /// <summary>가장 최근 스윙에서 때린 동료. 없으면 null.</summary>
        public PlayerRig LastStruck { get; private set; }

        /// <summary>가장 최근 스윙에서 밀친 소품. 없으면 null(S15P21B202-245).</summary>
        public Rigidbody LastPushed { get; private set; }

        /// <summary>가장 최근 스윙이 무엇이든 맞혔는가. 불꽃·타격음이 붙을 신호다.</summary>
        public bool LastSwingConnected { get; private set; }

        /// <summary>휘둘렀을 때. 애니메이션과 소리가 여기에 붙는다(맞았는지 여부 포함).</summary>
        public event Action<MeteorBat, bool> Swung;

        protected override void OnEquipped()
        {
            ApplyHeldVisualPose();
            cooldown = 0f;
            swingElapsed = -1f;
            LastDeflected = null;
            LastStruck = null;
            LastPushed = null;
            LastSwingConnected = false;
        }

        protected override void OnUnequipped()
        {
            RestoreDroppedVisualPose();
            cooldown = 0f;
            swingElapsed = -1f;
            LastDeflected = null;
            LastStruck = null;
            LastPushed = null;
            LastSwingConnected = false;
        }

        /// <summary>
        /// 헛스윙 대기를 센다. 런타임에서는 <see cref="OnToolInput"/> 이, 검증은 직접 부른다.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0f) return;

            if (cooldown > 0f) cooldown = Mathf.Max(0f, cooldown - deltaTime);

            if (swingElapsed < 0f) return;

            swingElapsed += deltaTime;

            // 동작이 끝나면 자세를 놓는다. PlayerRig 의 운반 자세가 다음 틱부터 다시
            // 도구를 끌어당기므로, 여기서 되돌리는 코드를 따로 쓰지 않는다.
            if (swingElapsed >= PhysicsScale.BatSwingSeconds) swingElapsed = -1f;
        }

        protected override void OnToolInput(ToolInputFrame input, ToolAimContext aim)
        {
            // PlayerRig 가 물리 틱마다 부르므로 간격은 고정 스텝이다.
            Tick(Time.fixedDeltaTime);

            // <b>누른 순간에만</b> 휘두른다. 유지로 판정하면 좌클릭을 붙잡고 있는 것이
            // 가장 좋은 전략이 되어, §18.1 의 "너무 일찍 휘두르면 헛스윙한다" 가 사라진다.
            if (!input.PrimaryPressed) return;

            var origin = Owner != null ? Owner.Position : Muzzle.position;
            Swing(origin, aim.Direction);
        }

        /// <summary>
        /// 한 번 휘두른다.
        ///
        /// <paramref name="origin"/> 은 플레이어 위치, <paramref name="facing"/> 은 조준
        /// 방향이다. 둘을 인수로 받는 이유는 <see cref="PlasmaWeldingTool.WeldTick"/> 과
        /// 같다 — 검증이 입력 프레임과 장착 절차를 통째로 흉내내면 정작 보고 싶은 기하
        /// 판정이 묻힌다.
        ///
        /// 운석을 먼저 본다. 방망이의 목적이 그것이고(§18.1), 동료 타격(§18.3)은 곁가지다.
        /// 둘 다 사거리 안이면 운석을 고른다.
        /// </summary>
        /// <returns>무엇이든 맞혔는가.</returns>
        public bool Swing(Vector3 origin, Vector3 facing)
        {
            LastDeflected = null;
            LastStruck = null;
            LastPushed = null;
            LastSwingConnected = false;

            if (!CanSwing) return false;

            var direction = LaunchDirection(facing);

            // <b>운석 → 동료 → 소품</b> 순서다. 운석이 방망이의 목적이고(§18.1), 동료
            // 타격은 기획에 있는 기능이며(§18.3), 소품은 곁가지다. 소품이 앞에 오면
            // 발밑 상자 때문에 운석을 놓친다.
            var meteor = FindTarget(origin);
            if (meteor != null)
            {
                meteor.Deflect(direction * PhysicsScale.BatDeflectSpeed);
                LastDeflected = meteor;
                LastSwingConnected = true;
            }
            else
            {
                var player = FindPlayer(origin, facing);
                if (player != null)
                {
                    player.KnockDown(direction, PhysicsScale.BatPlayerKnockSpeed);
                    LastStruck = player;
                    LastSwingConnected = true;
                }
                else
                {
                    var prop = FindProp(origin, facing);
                    if (prop != null)
                    {
                        prop.AddForce(direction * PhysicsScale.BatPropImpulse,
                                      ForceMode.Impulse);
                        LastPushed = prop;
                        LastSwingConnected = true;
                    }
                }
            }

            // 맞혔으면 대기가 없다. 기획 §18.1 이 헛스윙에만 대기를 두었고, 맞히면 목표가
            // 날아가 버리므로 연타할 것이 남지 않는다.
            if (!LastSwingConnected) cooldown = PhysicsScale.BatMissCooldownSeconds;

            // 동작을 시작한다. <b>판정 뒤에 시작한다</b> — 동작이 판정을 좌우하면
            // 기획 §18.1 의 "완벽한 프레임 판정은 없다" 가 깨진다.
            swingElapsed = 0f;
            swingAim = facing;

            ShowSwing(direction);

            Swung?.Invoke(this, LastSwingConnected);
            return LastSwingConnected;
        }

        /// <summary>
        /// 휘두르는 동안 도구를 조준선에서 벗어난 방향에 둔다(기획 §18.1 "한 번 휘두른다").
        ///
        /// <see cref="PlayerRig"/> 가 이 방향으로 도구의 위치와 회전을 함께 정하므로,
        /// 값을 흔들면 도구가 카메라 주위로 호를 그린다. <b>팔도 같은 방향을 따라간다</b> —
        /// 팔을 따로 움직이는 코드를 쓰지 않아도 된다.
        ///
        /// 휘두르지 않을 때는 조준을 그대로 돌려주므로 운반 자세가 유지된다.
        /// </summary>
        public override Vector3 CarryAim(Vector3 aim)
        {
            if (!IsSwinging) return aim;

            // 스윙을 시작한 순간의 조준을 쓴다. 휘두르는 중에 카메라를 돌리면 궤적이
            // 따라 휘어져 무엇을 쳤는지 알 수 없다.
            return SwingAim(swingAim, SwingProgress01);
        }

        /// <summary>
        /// 휘두르기 궤적. 진행도 0~1 을 방향으로 바꾼다.
        ///
        /// <b>위에서 앞아래로 내려찍는다.</b> 야구처럼 수평으로 돌리지 않는 이유는 목표가
        /// 떨어지는 운석이라서다 — 위에서 오는 것을 맞이하는 궤적이어야 하고, §18.2 의
        /// "약간의 위쪽 각도를 더해" 반사하는 것과도 맞는다.
        ///
        /// 조준의 <b>수평 성분</b>을 축으로 삼는다. 방망이가 위를 보든 아래를 보든 같은
        /// 궤적이어야 하고, 그것은 <see cref="LaunchDirection"/> 이 반사 방향을 정하는
        /// 방식과 같은 규칙이다.
        ///
        /// 상태를 갖지 않으므로 검증이 진행도를 직접 넣어 궤적을 잠글 수 있다.
        /// </summary>
        public static Vector3 SwingAim(Vector3 aim, float progress01)
        {
            var forward = new Vector3(aim.x, 0f, aim.z);
            if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;
            forward.Normalize();

            var right = Vector3.Cross(Vector3.up, forward);
            if (right.sqrMagnitude < 1e-4f) right = Vector3.right;
            right.Normalize();

            // 가속하며 내려간다. 등속으로 두면 휘두르는 것이 아니라 미끄러지는 것으로 보인다.
            var t = Mathf.Clamp01(progress01);
            var eased = t * t;

            var pitch = Mathf.Lerp(PhysicsScale.BatSwingUpDegrees,
                                   PhysicsScale.BatSwingDownDegrees, eased);

            // right 축을 기준으로 양의 각도가 아래를 향하므로 부호를 뒤집는다.
            return Quaternion.AngleAxis(-pitch, right) * forward;
        }

        /// <summary>
        /// 휘두른 것을 눈에 보이게 한다.
        ///
        /// <b>소리는 붙이지 못한다.</b> 프로젝트에 <c>AudioSource</c> 사용처가 0곳이라
        /// 재생 경로 자체가 없다. <see cref="Swung"/> 이벤트를 붙일 자리로 남긴다 —
        /// 소리 시스템이 생기면 그것을 구독한다([Sound]).
        ///
        /// 맞았을 때와 헛스윙을 <b>구분해서</b> 낸다. 둘이 같으면 대기 1초 동안 클릭이
        /// 먹히지 않는 이유를 플레이어가 알 수 없다.
        /// </summary>
        void ShowSwing(Vector3 direction)
        {
            var origin = Muzzle != null ? Muzzle.position : transform.position;

            // 궤적. 기획 §18.1 "넓은 에너지 궤적으로 판정을 납득시킨다".
            Puff.Thruster(origin, direction);

            if (!LastSwingConnected) return;

            // 맞았으면 맞은 자리에서 한 번 더. 쳐낸 운석이 날아가는 것이 가장 강한
            // 피드백이므로 그것을 가리지 않을 만큼만 낸다.
            var point = LastDeflected != null
                ? LastDeflected.transform.position
                : LastPushed != null
                    ? LastPushed.worldCenterOfMass
                    : origin + direction * 0.5f;

            Puff.Thruster(point, direction);
        }

        /// <summary>
        /// 쳐낼 운석. 없으면 null.
        ///
        /// 사거리 안에서 <b>가장 먼저 착탄할</b> 것을 고른다(기획 §18.1 "같은 범위에
        /// 운석이 여러 개면 가장 먼저 착탄할 운석을 선택한다"). 가장 가까운 것이 아니다 —
        /// 플레이어가 막아야 하는 것은 곧 떨어질 쪽이다.
        /// </summary>
        public static Meteor FindTarget(Vector3 origin)
        {
            Meteor best = null;
            var bestSeconds = float.MaxValue;

            for (var i = 0; i < Meteor.All.Count; i++)
            {
                var meteor = Meteor.All[i];
                if (meteor == null || meteor.HasImpacted) continue;
                if (!meteor.TryPredictImpact(out var seconds, out var point)) continue;

                // 너무 이르면 헛스윙이다. 이 조건이 §18.1 의 "너무 일찍 휘두르면
                // 헛스윙하지만 다시 시도할 수 있다" 를 만든다.
                if (seconds > PhysicsScale.BatSwingWindowSeconds) continue;

                // 낙하 지점까지의 거리는 <b>수평으로</b> 잰다. 운석은 아직 공중에 있고
                // 플레이어는 지면에 있으므로, 3차원 거리로 재면 높이 차가 판정을 먹는다.
                var offset = point - origin;
                offset.y = 0f;
                if (offset.magnitude > PhysicsScale.BatAssistRadius) continue;

                if (seconds >= bestSeconds) continue;

                bestSeconds = seconds;
                best = meteor;
            }

            return best;
        }

        /// <summary>
        /// 방망이가 닿는 동료. 없으면 null(기획 §18.3).
        ///
        /// 조준 방향 <b>앞쪽</b>만 본다. 뒤에 선 동료가 맞으면 플레이어가 이유를 알 수 없다.
        /// </summary>
        PlayerRig FindPlayer(Vector3 origin, Vector3 facing)
        {
            var forward = facing;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f) return null;
            forward.Normalize();

            PlayerRig best = null;
            var bestDistance = PhysicsScale.BatStrikeReach;

            for (var i = 0; i < PlayerRig.All.Count; i++)
            {
                var player = PlayerRig.All[i];
                if (player == null || player == Owner) continue;

                var offset = player.Position - origin;
                offset.y = 0f;

                var distance = offset.magnitude;
                if (distance > bestDistance || distance < 1e-4f) continue;
                if (Vector3.Dot(offset / distance, forward) <= 0f) continue;

                bestDistance = distance;
                best = player;
            }

            return best;
        }

        /// <summary>
        /// 방망이가 밀칠 소품. 없으면 null(S15P21B202-245).
        ///
        /// <b>기획에 없는 기능이다.</b> §18.3 이 정한 것은 플레이어 타격이고, "굴러다니는
        /// 소품을 밀친다" 는 언급이 없다. 넣기로 정한 것이므로 무엇을 빼는지가 규칙의
        /// 전부다.
        ///
        /// <b>레이어 하나로 가를 수 없다.</b> <see cref="GameLayers.Prop"/> 에는 때려서는
        /// 안 되는 것들이 섞여 있다 — 확인한 목록이다.
        ///
        /// <code>
        /// TransportVehicle(견인 조종 모듈) + 발판 + 좌석   §14.5 가 "마지막 우주선 파츠"
        /// 플라즈마 용접기 · 방망이 자신                     §18.3 "장비를 떨어뜨리게 … 않는다"
        /// 해제된 고정못                                     소품으로 본다 — 때려진다
        /// 자연물 · 화물 상자                                이것이 소품이다
        /// </code>
        ///
        /// 그래서 컴포넌트로 걸러낸다. <see cref="Parts.WeldBead"/> 가 "레이어와
        /// <c>SpaceshipPart</c> 컴포넌트를 둘 다 본다" 로 같은 문제를 푼 선례가 있다
        /// (S15P21B202-226).
        ///
        /// 조준 방향 <b>앞쪽</b>만 본다 — <see cref="FindPlayer"/> 와 같은 규칙이다.
        /// </summary>
        Rigidbody FindProp(Vector3 origin, Vector3 facing)
        {
            var forward = facing;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f) return null;
            forward.Normalize();

            var count = UnityEngine.Physics.OverlapSphereNonAlloc(
                origin, PhysicsScale.BatStrikeReach, Probe,
                GameLayers.PropMask, QueryTriggerInteraction.Ignore);

            Rigidbody best = null;
            var bestDistance = PhysicsScale.BatStrikeReach;

            for (var i = 0; i < count; i++)
            {
                var collider = Probe[i];
                if (collider == null) continue;

                var body = collider.attachedRigidbody;
                if (!CanPush(body)) continue;

                var offset = body.worldCenterOfMass - origin;
                offset.y = 0f;

                var distance = offset.magnitude;
                if (distance > bestDistance || distance < 1e-4f) continue;
                if (Vector3.Dot(offset / distance, forward) <= 0f) continue;

                bestDistance = distance;
                best = body;
            }

            return best;
        }

        /// <summary>밀칠 수 있는 것인가. 빼는 목록이 규칙의 전부다.</summary>
        bool CanPush(Rigidbody body)
        {
            if (body == null || body.isKinematic) return false;

            // 자기 자신. 휴대 중에는 충돌이 꺼져 있지만(S15P21B202-237) 내려놓은 다른
            // 방망이가 있을 수도 있고, 어느 쪽이든 도구는 대상이 아니다.
            if (body.GetComponentInParent<ToolBase>() != null) return false;

            // 파츠와 비드. 비드는 파츠의 자식 콜라이더이므로 부모에서 함께 걸린다.
            if (body.GetComponentInParent<Parts.SpaceshipPart>() != null) return false;

            // 견인 조종 모듈. 파츠이면서 사람이 타고 있을 수도 있다.
            if (body.GetComponentInParent<Parts.TransportVehicle>() != null) return false;

            return true;
        }

        /// <summary>
        /// 쳐내는 방향. 조준의 <b>수평 성분</b>에 고정 각도를 더한다(기획 §18.2).
        ///
        /// 이것이 §18.2 의 "카메라가 지나치게 위나 아래를 보더라도 수직 각도는 자동
        /// 보정한다" 다. 보정을 따로 계산하지 않는다 — 수평 성분만 쓰면 위를 보든 아래를
        /// 보든 결과가 같으므로, 규칙이 하나로 끝난다.
        ///
        /// 조준이 거의 수직이면 수평 성분이 사라진다. 그때는 들고 있는 사람이 보는
        /// 쪽으로 보내고, 그마저 없으면 도구의 정면을 쓴다.
        /// </summary>
        Vector3 LaunchDirection(Vector3 facing)
        {
            var horizontal = new Vector3(facing.x, 0f, facing.z);

            if (horizontal.sqrMagnitude < 1e-4f && Owner != null)
                horizontal = new Vector3(Owner.FacingDirection.x, 0f, Owner.FacingDirection.z);

            if (horizontal.sqrMagnitude < 1e-4f)
                horizontal = new Vector3(transform.forward.x, 0f, transform.forward.z);

            if (horizontal.sqrMagnitude < 1e-4f) horizontal = Vector3.forward;

            horizontal.Normalize();

            var radians = PhysicsScale.BatLaunchDegrees * Mathf.Deg2Rad;
            return (horizontal * Mathf.Cos(radians) + Vector3.up * Mathf.Sin(radians)).normalized;
        }

        // --- 휴대 포즈 -----------------------------------------------------

        void ApplyHeldVisualPose()
        {
            ResolveHeldVisual();
            if (heldVisual == null) return;

            if (!droppedVisualPoseCaptured)
            {
                droppedVisualLocalPosition = heldVisual.localPosition;
                droppedVisualLocalRotation = heldVisual.localRotation;
                droppedVisualPoseCaptured = true;
            }

            heldVisual.SetLocalPositionAndRotation(
                heldVisualLocalPosition, Quaternion.Euler(heldVisualLocalEuler));
        }

        void RestoreDroppedVisualPose()
        {
            if (!droppedVisualPoseCaptured || heldVisual == null) return;

            heldVisual.SetLocalPositionAndRotation(
                droppedVisualLocalPosition, droppedVisualLocalRotation);
        }

        /// <summary>
        /// 휴대 중에 자세를 바꿀 자식.
        ///
        /// [Art] 가 FBX 의 Z-up 축을 <c>Visual_ZUpToYUp</c> 에서 보정해 두었으므로
        /// (자산 목록 5-1) 그 자식을 찾는다. 이름이 바뀌면 첫 번째 자식으로 물러난다 —
        /// 용접기가 <c>ReplacementModel</c> 에 쓰는 것과 같은 방식이다.
        /// </summary>
        void ResolveHeldVisual()
        {
            if (heldVisual != null) return;

            heldVisual = transform.Find("Visual_ZUpToYUp");
            if (heldVisual == null && transform.childCount > 0) heldVisual = transform.GetChild(0);
        }
    }
}
