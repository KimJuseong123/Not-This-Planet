using System;
using UnityEngine;
using Metagame.Core;
using Metagame.Gameplay.Hazards;

namespace Metagame.Gameplay
{
    /// <summary>
    /// 운석 방망이. 떨어지는 운석을 쳐내고, 동료를 직접 때릴 수도 있다(기획 §18).
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

        float cooldown;

        /// <summary>헛스윙 뒤 남은 대기 시간(초). 0 이면 휘두를 수 있다.</summary>
        public float Cooldown => cooldown;

        public bool CanSwing => cooldown <= 0f;

        /// <summary>가장 최근 스윙에서 쳐낸 운석. 헛스윙이면 null.</summary>
        public Meteor LastDeflected { get; private set; }

        /// <summary>가장 최근 스윙에서 때린 동료. 없으면 null.</summary>
        public PlayerRig LastStruck { get; private set; }

        /// <summary>가장 최근 스윙이 무엇이든 맞혔는가. 불꽃·타격음이 붙을 신호다.</summary>
        public bool LastSwingConnected { get; private set; }

        /// <summary>휘둘렀을 때. 애니메이션과 소리가 여기에 붙는다(맞았는지 여부 포함).</summary>
        public event Action<MeteorBat, bool> Swung;

        protected override void OnEquipped()
        {
            ApplyHeldVisualPose();
            cooldown = 0f;
            LastDeflected = null;
            LastStruck = null;
            LastSwingConnected = false;
        }

        protected override void OnUnequipped()
        {
            RestoreDroppedVisualPose();
            cooldown = 0f;
            LastDeflected = null;
            LastStruck = null;
            LastSwingConnected = false;
        }

        /// <summary>
        /// 헛스윙 대기를 센다. 런타임에서는 <see cref="OnToolInput"/> 이, 검증은 직접 부른다.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0f || cooldown <= 0f) return;

            cooldown = Mathf.Max(0f, cooldown - deltaTime);
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
            LastSwingConnected = false;

            if (!CanSwing) return false;

            var direction = LaunchDirection(facing);

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
            }

            // 맞혔으면 대기가 없다. 기획 §18.1 이 헛스윙에만 대기를 두었고, 맞히면 목표가
            // 날아가 버리므로 연타할 것이 남지 않는다.
            if (!LastSwingConnected) cooldown = PhysicsScale.BatMissCooldownSeconds;

            Swung?.Invoke(this, LastSwingConnected);
            return LastSwingConnected;
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
            var bestDistance = PhysicsScale.BatPlayerReach;

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
