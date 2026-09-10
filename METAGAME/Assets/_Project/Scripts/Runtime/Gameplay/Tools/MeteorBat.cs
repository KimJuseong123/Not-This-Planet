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
        [Header("월드 휴대 포즈")]
        [Tooltip("3인칭/원격 화면에서 장착 중 보이는 방망이 모델의 로컬 위치입니다.")]
        [SerializeField] Vector3 heldVisualLocalPosition = new Vector3(0.18f, -0.12f, 0.12f);
        [Tooltip("3인칭/원격 화면에서 장착 중 보이는 방망이 모델의 로컬 회전(Euler)입니다.")]
        [SerializeField] Vector3 heldVisualLocalEuler = new Vector3(0f, 0f, -35f);
        [Tooltip("3인칭/원격 화면에서 장착 중에만 적용할 모델 크기입니다.")]
        [SerializeField] Vector3 heldVisualLocalScale = Vector3.one * 0.64f;

        [Header("로컬 1인칭 화면 포즈")]
        [Tooltip("카메라 기준 방망이 모델 위치입니다. Play 중 Inspector에서 바로 조절할 수 있습니다.")]
        [SerializeField] Vector3 firstPersonVisualLocalPosition = new Vector3(0.23f, -0.20f, 0.58f);
        [Tooltip("카메라 기준 방망이 모델 회전(Euler)입니다.")]
        [SerializeField] Vector3 firstPersonVisualLocalEuler = new Vector3(0f, 0f, -35f);
        [Tooltip("카메라 아래로 분리된 방망이 모델의 크기입니다.")]
        [SerializeField] Vector3 firstPersonVisualLocalScale = Vector3.one * 0.64f;

        Transform heldVisual;
        Vector3 droppedVisualLocalPosition;
        Quaternion droppedVisualLocalRotation;
        Vector3 droppedVisualLocalScale;
        Transform droppedVisualParent;
        int droppedVisualSiblingIndex;
        bool droppedVisualPoseCaptured;
        bool firstPersonVisualAttached;

        /// <summary>
        /// 소품을 찾는 겹침 질의 버퍼. 매 스윙 새로 만들지 않는다
        /// (<see cref="Parts.WeldBead"/> 와 같은 방식이다).
        /// </summary>
        const int ProbeSize = 32;
        static readonly Collider[] Probe = new Collider[ProbeSize];

        float cooldown;
        float swingElapsed = -1f;
        Vector3 swingAim = Vector3.forward;

        // --- 궤적 연출(S15P21B202-276) ---
        LineRenderer arc;
        LineRenderer streak;
        Vector3 streakFrom;
        Vector3 streakTo;
        float sinceStreak = -1f;

        /// <summary>궤적 점 버퍼. 매 프레임 새로 만들지 않는다.</summary>
        static readonly Vector3[] ArcBuffer = new Vector3[PhysicsScale.BatArcSamples];

        /// <summary>
        /// 궤적과 줄기가 <b>함께 쓰는</b> 머티리얼.
        ///
        /// 방망이마다 따로 만들면 사람 수만큼 늘고, 색이 매 프레임 바뀌지 않으므로
        /// 나눌 이유가 없다 — <see cref="Hazards.MeteorWarningView"/> 가 표시마다 따로
        /// 갖는 것과 반대인데, 그쪽은 색이 매 프레임 변하기 때문이다.
        /// </summary>
        static Material trailMaterial;

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

        protected override void OnValidate()
        {
            base.OnValidate();

            // Play 중 Inspector에서 값을 조절하면 현재 보이는 모델에 바로 반영한다.
            if (Application.isPlaying && IsEquipped) ApplyHeldVisualPose();
        }

        protected override void OnEquipped()
        {
            cooldown = 0f;
            swingElapsed = -1f;
            LastDeflected = null;
            LastStruck = null;
            LastPushed = null;
            LastSwingConnected = false;
            ApplyHeldVisualPose();
        }

        protected override void OnUnequipped()
        {
            RestoreDroppedVisualPose();
            // A bat that is no longer carried is a regular world prop. PlayerRig also
            // restores its cached state after this hook; keeping this fallback here makes
            // direct ToolBase.Unequip calls obey the same gravity/rotation contract.
            if (Body != null)
            {
                Body.isKinematic = false;
                Body.useGravity = true;
                Body.constraints = RigidbodyConstraints.None;
            }
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
        /// <b>오른손잡이 야구 스윙이다.</b> 오른쪽 뒤 위에서 시작해 정면을 지나 왼쪽 앞으로
        /// 빠진다(S15P21B202-261).
        ///
        /// <code>
        /// 0.00   오른쪽 뒤 위      yaw +110  ·  pitch +45
        /// 0.45   정면, 거의 수평    yaw    0  ·  pitch  +5   ← 맞는 순간
        /// 1.00   왼쪽 앞, 살짝 아래  yaw  -70  ·  pitch -15
        /// </code>
        ///
        /// <b>예전에는 위에서 앞아래로 내려찍었다.</b> 근거는 "목표가 떨어지는 운석"
        /// 이었는데(S15P21B202-243), 인게임에서 보고 바꿨다. 기획 §18 은 궤적 모양을
        /// 정하지 않으므로 손맛 결정이다.
        ///
        /// <b>가속이 앞뒤로 다르다.</b> 맞는 순간까지는 배트가 가속하고(t²), 그 뒤 팔로스루는
        /// 감속한다. 한 곡선으로 처리하면 맞는 순간이 뒤로 밀려 "늦게 휘두르는" 것으로
        /// 보인다.
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

            var t = Mathf.Clamp01(progress01);
            var contact = Mathf.Clamp(PhysicsScale.BatSwingContactAt, 0.05f, 0.95f);

            float yaw, pitch;

            if (t <= contact)
            {
                // 맞는 순간까지 가속한다. 등속이면 휘두르는 것이 아니라 미끄러지는 것으로 보인다.
                var e = t / contact;
                e *= e;

                yaw = Mathf.Lerp(PhysicsScale.BatSwingWindupYawDegrees, 0f, e);
                pitch = Mathf.Lerp(PhysicsScale.BatSwingUpDegrees,
                                   PhysicsScale.BatSwingContactPitchDegrees, e);
            }
            else
            {
                // 팔로스루는 감속한다. 배트가 몸을 감고 멈춘다.
                var u = (t - contact) / (1f - contact);
                var e = 1f - (1f - u) * (1f - u);

                yaw = Mathf.Lerp(0f, PhysicsScale.BatSwingFollowYawDegrees, e);
                pitch = Mathf.Lerp(PhysicsScale.BatSwingContactPitchDegrees,
                                   PhysicsScale.BatSwingDownDegrees, e);
            }

            // pitch 를 먼저 기울이고 그 결과를 세계 위쪽으로 돌린다. right 축을 기준으로
            // 양의 각도가 아래를 향하므로 부호를 뒤집는다.
            var tilted = Quaternion.AngleAxis(-pitch, right) * forward;
            return Quaternion.AngleAxis(yaw, Vector3.up) * tilted;
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

            // 방망이에서 맞은 자리까지 줄기를 남긴다(S15P21B202-276).
            //
            // <b>이 줄기가 "닿았다" 를 만든다.</b> 판정은 15m 떨어진 운석을 쳐내는데
            // (§18.1 이 메시 접촉을 요구하지 않는다), 그 사이에 아무것도 없으면
            // 플레이어는 허공을 휘둘렀다고 본다.
            streakFrom = origin;
            streakTo = point;
            sinceStreak = 0f;
        }

        /// <summary>
        /// 궤적과 줄기를 갱신한다(S15P21B202-276).
        ///
        /// <see cref="Tick"/> 이 아니라 여기서 하는 이유는 물리 스텝(0.01초)마다 선을
        /// 다시 깔 필요가 없기 때문이다. 화면 갱신마다 한 번이면 충분하다.
        /// </summary>
        protected override void OnToolVisualUpdate(ToolInputFrame input, ToolAimContext aim)
        {
            Vector3 visualDirection = IsSwinging
                ? CarryAim(aim.Direction)
                : Owner != null && Owner.Cam != null
                    ? Owner.Cam.transform.forward
                    : aim.Direction;
            UpdateHeldVisualPose(visualDirection);
            EnsureTrails();

            if (sinceStreak >= 0f) sinceStreak += Time.deltaTime;

            DrawArc();
            DrawStreak();
        }

        void DrawArc()
        {
            if (arc == null) return;

            if (!BatSwingLook.ShowsArc(IsSwinging, SwingProgress01))
            {
                arc.enabled = false;
                return;
            }

            var origin = Owner != null ? Owner.Position : transform.position;
            var count = BatSwingLook.ArcPoints(origin, swingAim, SwingProgress01, ArcBuffer);
            if (count < 2)
            {
                arc.enabled = false;
                return;
            }

            arc.positionCount = count;
            for (var i = 0; i < count; i++) arc.SetPosition(i, ArcBuffer[i]);

            arc.enabled = true;
        }

        void DrawStreak()
        {
            if (streak == null) return;

            var fade = BatSwingLook.StreakFade(sinceStreak);
            if (sinceStreak < 0f || fade <= 0f)
            {
                streak.enabled = false;
                return;
            }

            streak.SetPosition(0, streakFrom);
            streak.SetPosition(1, streakTo);

            // 굵기로 줄인다. 알파로만 줄이면 공유 머티리얼이라 다른 방망이까지 흐려진다.
            streak.widthMultiplier = fade;
            streak.enabled = true;
        }

        /// <summary>
        /// 궤적과 줄기를 그릴 선을 만든다.
        ///
        /// <b>[Art] 가 실제 VFX 를 주면 이 메서드만 바뀐다.</b>
        /// <see cref="Hazards.MeteorWarningView.CreateMarker"/> 와 같은 자리다.
        /// </summary>
        void EnsureTrails()
        {
            if (arc != null && streak != null) return;

            if (trailMaterial == null)
                trailMaterial = Prim.Ghost(BatSwingLook.ArcStartColor, 2.6f);

            if (arc == null)
                arc = MakeLine("스윙 궤적", PhysicsScale.BatArcWidth,
                               BatSwingLook.ArcStartColor, BatSwingLook.ArcEndColor);

            if (streak == null)
                streak = MakeLine("타격 줄기", PhysicsScale.BatStreakWidth,
                                  BatSwingLook.StreakColor, BatSwingLook.StreakColor);
        }

        LineRenderer MakeLine(string name, float width, Color from, Color to)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);

            var line = go.AddComponent<LineRenderer>();

            // 세계 좌표로 넣는다. 방망이가 스윙 동안 회전하므로 지역 좌표면 궤적이
            // 방망이와 함께 돌아 버린다.
            line.useWorldSpace = true;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.numCapVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;

            line.widthMultiplier = 1f;
            line.startWidth = width;
            line.endWidth = width * 0.25f;
            line.startColor = from;
            line.endColor = to;
            line.sharedMaterial = trailMaterial;
            line.enabled = false;

            return line;
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
                if (seconds > meteor.BatSwingWindowSeconds) continue;

                // 낙하 지점까지의 거리는 <b>수평으로</b> 잰다. 운석은 아직 공중에 있고
                // 플레이어는 지면에 있으므로, 3차원 거리로 재면 높이 차가 판정을 먹는다.
                var offset = point - origin;
                offset.y = 0f;
                if (offset.magnitude > meteor.BatAssistRadius) continue;

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
        /// <b>기획에 없는 기능이다.</b> §18.3 이 정한 것은 플레이어 타격이고 "굴러다니는
        /// 소품을 밀친다" 는 언급이 없다. 넣기로 정한 것이므로 무엇을 빼는지가 규칙의
        /// 전부다.
        ///
        /// <b>빼는 목록은 기획이 금지한 것뿐이다</b>(S15P21B202-257). §18.3 의 마지막 줄이
        /// 금지의 전부다 — "장비를 떨어뜨리게 하거나 파츠와 용접부에 피해를 주지는 않는다".
        ///
        /// <code>
        /// 우주선 파츠 · 용접 비드      §18.3 — 직접 피해 금지
        /// 견인 조종 모듈              §14.5 "마지막 우주선 파츠이기도 하다"
        /// 밀릴 수 없는 것             Rigidbody 가 없거나 키네마틱 — 물리적 사실
        ///
        /// 내려놓은 도구               *때려진다.* §18.3 의 "장비를 떨어뜨리게" 는 맞은
        ///                            플레이어가 들고 있는 것에 대한 규정이고, 바닥에
        ///                            굴러다니는 용접기와는 무관하다. 245 에서 그 문장을
        ///                            "도구를 못 때린다" 로 넓게 읽은 것이 틀렸다.
        /// 해제된 고정못 · 화물 상자    때려진다
        /// </code>
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
                GameLayers.BatStrikeMask, QueryTriggerInteraction.Ignore);

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
            // 밀릴 수 없는 것은 물리적으로 대상이 아니다.
            if (body == null || body.isKinematic) return false;

            // 들고 있는 이 방망이. 휴대 중에는 충돌이 꺼져 있어(S15P21B202-237) 여기까지
            // 오지 않지만, 자기를 때리는 것은 어느 경로로도 일어나면 안 된다.
            if (body == Body) return false;

            // 파츠와 비드. 기획 §18.3 "파츠와 용접부에 피해를 주지는 않는다".
            // 비드는 파츠의 자식 콜라이더이므로 부모에서 함께 걸린다(S15P21B202-226).
            if (body.GetComponentInParent<Parts.SpaceshipPart>() != null) return false;

            // 견인 조종 모듈. §14.5 가 "마지막 우주선 파츠이기도 하다" 고 정하므로
            // 위와 같은 금지에 들어간다. 사람이 타고 있을 수도 있다.
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

            CaptureDroppedVisualPose();
            UpdateHeldVisualPose(Owner != null && Owner.Cam != null
                ? Owner.Cam.transform.forward
                : transform.forward);
        }

        void RestoreDroppedVisualPose()
        {
            if (!droppedVisualPoseCaptured || heldVisual == null) return;

            RestoreVisualParent();
            heldVisual.SetLocalPositionAndRotation(
                droppedVisualLocalPosition, droppedVisualLocalRotation);
            heldVisual.localScale = droppedVisualLocalScale;
        }

        void CaptureDroppedVisualPose()
        {
            if (droppedVisualPoseCaptured || heldVisual == null) return;

            droppedVisualParent = heldVisual.parent;
            droppedVisualSiblingIndex = heldVisual.GetSiblingIndex();
            droppedVisualLocalPosition = heldVisual.localPosition;
            droppedVisualLocalRotation = heldVisual.localRotation;
            droppedVisualLocalScale = heldVisual.localScale;
            droppedVisualPoseCaptured = true;
        }

        /// <summary>
        /// 로컬 1인칭 모델은 카메라 아래에서 LateUpdate로 움직인다. 물리 본체와
        /// 타격 판정은 기존 FixedUpdate 경로를 그대로 쓰되 화면에서만 지연을 없앤다.
        /// </summary>
        void UpdateHeldVisualPose(Vector3 carryDirection)
        {
            if (heldVisual == null) return;

            var camera = Owner != null && Owner.IsLocal ? Owner.Cam : null;
            if (camera != null && camera.FirstPerson)
            {
                if (!firstPersonVisualAttached)
                {
                    heldVisual.SetParent(camera.transform, false);
                    firstPersonVisualAttached = true;
                }

                ApplyFirstPersonPose(camera.transform, carryDirection);
                return;
            }

            RestoreVisualParent();
            heldVisual.SetLocalPositionAndRotation(
                heldVisualLocalPosition, Quaternion.Euler(heldVisualLocalEuler));
            heldVisual.localScale = heldVisualLocalScale;
        }

        void ApplyFirstPersonPose(Transform cameraTransform, Vector3 carryDirection)
        {
            // 대기 중에는 카메라 로컬 포즈를 그대로 쓴다. FixedUpdate에서 전달된
            // aim을 다시 적용하면 그 오래된 한 스텝 때문에 오히려 화면 지연이 생긴다.
            Vector3 localDirection = IsSwinging
                ? cameraTransform.InverseTransformDirection(carryDirection)
                : Vector3.forward;
            if (localDirection.sqrMagnitude < 1e-4f) localDirection = Vector3.forward;
            localDirection.Normalize();

            Vector3 up = Mathf.Abs(Vector3.Dot(localDirection, Vector3.up)) > 0.98f
                ? Vector3.right
                : Vector3.up;
            Quaternion carryRotation = Quaternion.LookRotation(localDirection, up);

            heldVisual.SetLocalPositionAndRotation(
                carryRotation * firstPersonVisualLocalPosition,
                carryRotation * Quaternion.Euler(firstPersonVisualLocalEuler));
            heldVisual.localScale = firstPersonVisualLocalScale;
        }

        void RestoreVisualParent()
        {
            if (!firstPersonVisualAttached || heldVisual == null) return;

            heldVisual.SetParent(droppedVisualParent, false);
            if (droppedVisualParent != null)
                heldVisual.SetSiblingIndex(Mathf.Clamp(
                    droppedVisualSiblingIndex, 0, droppedVisualParent.childCount - 1));
            firstPersonVisualAttached = false;
        }

        protected override void OnDestroy()
        {
            RestoreVisualParent();
            base.OnDestroy();
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
