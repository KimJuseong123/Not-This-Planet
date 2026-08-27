using UnityEngine;
using Metagame.Core;

namespace Metagame.Gameplay
{
    /// <summary>
    /// 우주비행사의 이동과 물리 상호작용을 연결한다.
    ///
    ///  · 골반에 매달린 보이지 않는 스프링이 몸무게를 떠받쳐 서 있게 한다 (다리는 거들 뿐)
    ///  · 골반을 세계 기준 조인트로 세우고, 가는 방향으로 돌린다
    ///  · 다리는 걷기 주기에 맞춰 조인트 목표 자세를 흔든다 (애니메이션 클립 없음)
    ///  · 팔은 카메라가 보는 쪽으로 뻗되, 근처에 잡을 것이 있으면 그쪽으로 휘어 간다
    ///  · 잡은 물체는 질량을 무시하고 (ForceMode.Acceleration) 손 앞으로 끌려온다
    ///
    /// 이동 자체는 목표 속도를 직접 따라가 안정적으로 조종하고, 잡기·등반·충돌 반응은 물리에 맡긴다.
    /// </summary>
    public class PlayerRig : MonoBehaviour
    {
        public Doll Doll;
        public OrbitCamera Cam;
        public bool IsLocal = true;
        public string Nickname = "비행사";

        // ── 상태 ────────────────────────────────────────────────
        public bool Grounded { get; private set; }
        public float GroundDistance { get; private set; }
        public Vector3 GroundVelocity { get; private set; }
        public float Speed { get; private set; }
        public float MaxHealth { get { return Tuning.MaxHealth; } }
        public float Health { get; private set; }
        public float Health01 { get { return MaxHealth <= 0f ? 0f : Mathf.Clamp01(Health / MaxHealth); } }
        public bool IsDead { get { return Health <= 0f; } }
        public bool Hanging { get { return LeftHand.Hanging || RightHand.Hanging; } }
        public bool Carrying { get { return LeftHand.Carrying || RightHand.Carrying; } }
        public bool Ragdolled { get; private set; }      // 크게 넘어져 조종이 잠깐 풀린 상태
        public float StepPhase { get; private set; }
        /// <summary>공중에서 자세제어 분사기를 쓰고 있는가 (HUD·연기 표시용).</summary>
        public bool Thrusting { get; private set; }
        public float AirTime { get; private set; }
        public Grabbable HeldTool { get { return toolHeld; } }
        public float ToolCharge { get { return toolCharge; } }
        public bool ChargingTool { get { return chargingTool; } }

        public Hand LeftHand { get { return Doll.Left.Hand; } }
        public Hand RightHand { get { return Doll.Right.Hand; } }
        public Rigidbody Hips { get { return Doll.Hips; } }
        public Vector3 Position { get { return Doll.Hips.position; } }
        public Vector3 FootPosition { get { return Doll.Hips.position - Vector3.up * RagdollBuilder.HipsY; } }

        InputFrame input;
        Vector3 faceDir = Vector3.forward;
        float jumpLock;
        float regrabLock;
        float uprightBlend = 1f;
        float thrustPuff;
        float fallPeakFootHeight;
        Grabbable toolHeld;
        float toolCharge;
        bool toolActionHeld;
        bool chargingTool;
        float toolThrowRegrabLock;
        Rigidbody pendingThrowBody;
        Vector3 pendingThrowVelocity;
        Rigidbody groundBody;
        readonly RaycastHit[] rayHits = new RaycastHit[8];

        public static PlayerRig Create(string nickname, Vector3 footPos, Quaternion facing,
                                       Color suit, Color accent, bool useModel = true)
        {
            var go = new GameObject("플레이어 " + nickname);
            var rig = go.AddComponent<PlayerRig>();
            rig.Nickname = nickname;
            bool model = useModel && SpaceSuit.Available;
            rig.Doll = RagdollBuilder.Build(nickname, footPos, facing, suit, accent, !model);
            rig.Doll.Root.SetParent(go.transform, true);
            rig.ResetHealth();
            rig.fallPeakFootHeight = footPos.y;
            rig.faceDir = facing * Vector3.forward;
            return rig;
        }

        public void SetInput(InputFrame frame) { input = frame; }
        public InputFrame CurrentInput { get { return input; } }

        /// <summary>체크포인트 부활이나 새 라운드에서 체력을 초기화한다.</summary>
        public void ResetHealth()
        {
            Health = MaxHealth;
        }

        /// <summary>외부 피해 오브젝트와 낙하 판정이 공통으로 사용하는 피해 진입점.</summary>
        public void ApplyDamage(float amount, string source = "피해")
        {
            if (IsDead || float.IsNaN(amount) || float.IsInfinity(amount) || amount <= 0f) return;

            Health = Mathf.Clamp(Health - amount, 0f, MaxHealth);
            if (Health <= 0f)
            {
                Ragdolled = true;
                Debug.Log("[SpaceFlop] " + Nickname + " 체력 소진 (" + source + ")");
            }
        }

        // ── 부활 / 순간이동 ─────────────────────────────────────
        public void Teleport(Vector3 footPos, Quaternion facing)
        {
            LeftHand.Release();
            RightHand.Release();
            Doll.Teleport(footPos + Vector3.up * RagdollBuilder.HipsY, facing);
            faceDir = facing * Vector3.forward;
            uprightBlend = 1f;
            Ragdolled = false;
            StepPhase = 0f;
            AirTime = 0f;
            fallPeakFootHeight = footPos.y;
            ResetToolState();
            toolThrowRegrabLock = 0f;
            pendingThrowBody = null;
            if (Cam != null)
            {
                Cam.Yaw = facing.eulerAngles.y;
                Cam.SnapToTarget();
            }
        }

        void FixedUpdate()
        {
            if (Doll == null || Doll.Hips == null) return;
            if (!JointTools.IsFinite(Doll.Hips.position)) { return; }

            // Release가 예약한 조인트 제거가 끝난 다음 틱에 속도를 넣어야 투척이 구속에 먹히지 않는다.
            if (pendingThrowBody != null)
            {
                if (!pendingThrowBody.isKinematic) pendingThrowBody.linearVelocity = pendingThrowVelocity;
                pendingThrowBody = null;
            }

            ProbeGround();
            UpdateSpeed();
            Hover();
            Locomotion();
            Balance();
            DriveLegs();
            DriveArms();
        }

        // ── 바닥 ────────────────────────────────────────────────
        void ProbeGround()
        {
            bool wasGrounded = Grounded;
            float fallSpeed = Mathf.Max(0f, -Doll.Hips.linearVelocity.y);

            groundBody = null;
            GroundVelocity = Vector3.zero;
            GroundDistance = float.MaxValue;

            Vector3 origin = Doll.Hips.position;
            int n = Physics.RaycastNonAlloc(origin, Vector3.down, rayHits, Tuning.GroundProbe,
                                            RagdollBuilder.WorldMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                if (rayHits[i].distance >= GroundDistance) continue;
                GroundDistance = rayHits[i].distance;
                groundBody = rayHits[i].collider.attachedRigidbody;
            }

            Grounded = GroundDistance < Tuning.RideHeight + 0.22f && jumpLock <= 0f;
            if (groundBody != null) GroundVelocity = groundBody.linearVelocity;

            float footHeight = FootPosition.y;
            if (!Grounded)
            {
                fallPeakFootHeight = Mathf.Max(fallPeakFootHeight, footHeight);
            }
            else if (!wasGrounded)
            {
                float fallHeight = Mathf.Max(0f, fallPeakFootHeight - footHeight);
                ApplyFallDamage(fallHeight, fallSpeed);
                fallPeakFootHeight = footHeight;
            }
            else
            {
                fallPeakFootHeight = footHeight;
            }

            if (Grounded) AirTime = 0f; else AirTime += Time.fixedDeltaTime;

            // 착지 먼지 — 진공이라 흩어지지 않고 그대로 포물선을 그린다
            if (Grounded && !wasGrounded && fallSpeed > 1.6f)
                Puff.Regolith(FootPosition, Mathf.Clamp01(fallSpeed / 7f));
        }

        void ApplyFallDamage(float fallHeight, float impactSpeed)
        {
            if (fallHeight < Tuning.FallDamageMinHeight || impactSpeed < Tuning.FallDamageMinSpeed)
                return;

            float damage = Tuning.FallDamageBase +
                           (impactSpeed - Tuning.FallDamageMinSpeed) * Tuning.FallDamagePerSpeed;
            ApplyDamage(damage, "낙하");
        }

        void UpdateSpeed()
        {
            Vector3 v = Doll.Hips.linearVelocity;
            v.y = 0f;
            Speed = v.magnitude;

            if (jumpLock > 0f) jumpLock -= Time.fixedDeltaTime;
            if (regrabLock > 0f) regrabLock -= Time.fixedDeltaTime;

            // 머리가 골반보다 낮으면 완전히 자빠진 것 — 잠깐 조종을 놓았다가 천천히 일어난다
            bool down = Doll.Head.position.y < Doll.Hips.position.y + 0.12f;
            Ragdolled = down && !Hanging;
            float want = Ragdolled ? 0.18f : 1f;
            uprightBlend = Mathf.MoveTowards(uprightBlend, want, Time.fixedDeltaTime * (want > uprightBlend ? 0.8f : 4f));
        }

        // ── 서 있기 ─────────────────────────────────────────────
        void Hover()
        {
            if (!Grounded || Hanging) return;

            float offset = Tuning.RideHeight - GroundDistance;
            if (offset < -0.2f) return;

            float vy = Doll.Hips.linearVelocity.y - (groundBody != null ? groundBody.linearVelocity.y : 0f);
            float f = offset * Tuning.RideSpring - vy * Tuning.RideDamp;
            f = Mathf.Clamp(f, 0f, Tuning.RideMaxForce) * uprightBlend;

            Doll.Hips.AddForce(Vector3.up * f, ForceMode.Force);
            if (groundBody != null)
                groundBody.AddForceAtPosition(Vector3.down * f, Doll.Hips.position, ForceMode.Force);
        }

        void Locomotion()
        {
            Vector3 fwd = Cam != null ? Cam.PlanarForward : Vector3.forward;
            Vector3 right = Cam != null ? Cam.PlanarRight : Vector3.right;

            Vector3 wish = right * input.Move.x + fwd * input.Move.y;
            if (wish.sqrMagnitude > 1f) wish.Normalize();

            float top = input.Run ? Tuning.RunSpeed : Tuning.WalkSpeed;
            Vector3 targetVel = wish * top;
            targetVel.x += GroundVelocity.x;
            targetVel.z += GroundVelocity.z;

            if (Hanging)
            {
                // 매달린 동안에는 손 조인트가 이동을 결정해야 하므로 기존 물리 제어를 유지한다.
                Vector3 v = Doll.Hips.linearVelocity;
                Vector3 dv = new Vector3(targetVel.x - v.x, 0f, targetVel.z - v.z);
                Vector3 force = JointTools.Clamp(dv * Tuning.Accel * Tuning.AirControl, Tuning.AccelMax);
                if (JointTools.IsFinite(force))
                {
                    Doll.Hips.AddForce(force * 0.6f, ForceMode.Force);
                    Doll.Chest.AddForce(force * 0.4f, ForceMode.Force);
                }
            }
            else
            {
                DriveMovementVelocity(targetVel);
            }

            // 등짐의 자세제어 분사기가 공중에서 방향을 잡아준다는 설정
            Thrusting = !Grounded && !Hanging && wish.sqrMagnitude > 0.04f;
            if (Thrusting)
            {
                thrustPuff -= Time.fixedDeltaTime;
                if (thrustPuff <= 0f)
                {
                    thrustPuff = 0.09f;
                    Puff.Thruster(Doll.Chest.position - Doll.Chest.transform.forward * 0.26f, -wish);
                }
            }

            if (wish.sqrMagnitude > 0.02f)
                faceDir = Vector3.Slerp(faceDir, wish.normalized, 1f - Mathf.Exp(-7f * Time.fixedDeltaTime));

            // 점프 — 몸 전체에 같은 속도를 얹어야 인형이 통째로 뜬다
            if (input.Jump && Grounded && !Ragdolled && jumpLock <= 0f)
            {
                for (int i = 0; i < Doll.Bodies.Count; i++)
                {
                    var b = Doll.Bodies[i];
                    var bv = b.linearVelocity;
                    bv.y = Mathf.Max(bv.y, 0f) + Tuning.JumpSpeed;
                    b.linearVelocity = bv;
                }
                jumpLock = 0.30f;
                Grounded = false;
                Puff.Regolith(FootPosition, 0.45f);
            }
        }

        /// <summary>
        /// 모든 몸 부위의 수평 속도를 같은 목표로 맞춰 이동 중 몸통이 뒤처지거나
        /// 관성 때문에 미끄러지는 문제를 막는다. 회전과 수직 속도는 물리 조인트에 맡긴다.
        /// </summary>
        void DriveMovementVelocity(Vector3 targetVelocity)
        {
            Vector3 target = new Vector3(targetVelocity.x, 0f, targetVelocity.z);
            float acceleration = Grounded ? Tuning.GroundAcceleration : Tuning.AirAcceleration;
            float deceleration = Grounded ? Tuning.GroundDeceleration : Tuning.AirAcceleration;

            for (int i = 0; i < Doll.Bodies.Count; i++)
            {
                var body = Doll.Bodies[i];
                if (body == null) continue;

                Vector3 current = body.linearVelocity;
                Vector3 planar = new Vector3(current.x, 0f, current.z);
                float rate = target.sqrMagnitude < planar.sqrMagnitude ? deceleration : acceleration;
                planar = Vector3.MoveTowards(planar, target, rate * Time.fixedDeltaTime);
                body.linearVelocity = new Vector3(planar.x, current.y, planar.z);
            }
        }

        void Balance()
        {
            Quaternion desired = Quaternion.LookRotation(faceDir, Vector3.up);

            // 공중에서는 힘을 빼야 팔다리가 허우적거린다. 저중력이라 그 시간이 길다.
            float k = (Grounded ? 1f : 0.26f) * uprightBlend;
            if (Hanging) k = 0.40f;

            JointTools.SetDrive(Doll.Balance,
                                Tuning.UprightSpring * k,
                                Tuning.UprightDamp * Mathf.Max(k, 0.25f),
                                Tuning.UprightMaxTorque);
            JointTools.SetTargetLocalRotation(Doll.Balance, desired, Quaternion.identity);

            // 달릴수록 앞으로 기운다
            float lean = Mathf.Clamp(Speed / Tuning.RunSpeed, 0f, 1f) * 11f;
            JointTools.SetTargetLocalRotation(Doll.ChestJoint,
                                              Quaternion.AngleAxis(lean, Vector3.right),
                                              Quaternion.identity);

            // 고개는 카메라 피치를 조금 따라간다
            float pitch = Cam != null ? Mathf.Clamp(Cam.Pitch, -26f, 26f) : 0f;
            JointTools.SetTargetLocalRotation(Doll.HeadJoint,
                                              Quaternion.AngleAxis(pitch * 0.5f, Vector3.right),
                                              Quaternion.identity);
        }

        // ── 걷기 ────────────────────────────────────────────────
        void DriveLegs()
        {
            bool walking = Grounded && !Ragdolled && Speed > 0.40f;

            if (walking)
            {
                float rate = Mathf.Clamp(Speed / Tuning.WalkSpeed, 0.55f, 1.8f);
                StepPhase += Time.fixedDeltaTime * (Mathf.PI * 2f / Tuning.StepPeriod) * rate;
                if (StepPhase > Mathf.PI * 2f) StepPhase -= Mathf.PI * 2f;
            }
            else
            {
                StepPhase = Mathf.MoveTowardsAngle(StepPhase * Mathf.Rad2Deg, 0f,
                                                   240f * Time.fixedDeltaTime) * Mathf.Deg2Rad;
            }

            float swing = walking ? Mathf.Lerp(22f, 40f, Mathf.Clamp01(Speed / Tuning.RunSpeed)) : 0f;
            PoseLeg(Doll.LeftLeg, StepPhase, swing, walking);
            PoseLeg(Doll.RightLeg, StepPhase + Mathf.PI, swing, walking);
        }

        void PoseLeg(Leg leg, float phase, float swing, bool walking)
        {
            float thigh, knee;

            if (!Grounded && !walking)
            {
                // 저중력에서 오래 떠 있다 — 다리를 살짝 웅크리면 '둥실' 하는 그림이 된다
                thigh = -14f; knee = 34f;
            }
            else if (!walking)
            {
                thigh = 0f; knee = 4f;                          // 서 있을 땐 곧게
            }
            else
            {
                thigh = -Mathf.Sin(phase) * swing;              // 음수 = 앞으로 차기
                knee = Mathf.Max(0f, Mathf.Sin(phase + 1.1f)) * (swing * 1.7f);
            }

            if (Ragdolled) { thigh *= 0.2f; knee *= 0.2f; }

            JointTools.SetTargetLocalRotation(leg.Hip, Quaternion.AngleAxis(thigh, Vector3.right), leg.HipStart);
            JointTools.SetTargetLocalRotation(leg.Knee, Quaternion.AngleAxis(knee, Vector3.right), leg.KneeStart);
            JointTools.SetTargetLocalRotation(leg.Ankle,
                Quaternion.AngleAxis(Mathf.Clamp(-thigh * 0.35f, -20f, 20f), Vector3.right), leg.AnkleStart);
        }

        // ── 팔 ──────────────────────────────────────────────────
        void DriveArms()
        {
            Vector3 aim = Cam != null ? Cam.AimDirection : Doll.Chest.transform.forward;
            if (input.ArmsUp) aim = Vector3.Slerp(aim, Vector3.up, 0.65f).normalized;

            if (toolThrowRegrabLock > 0f) toolThrowRegrabLock -= Time.fixedDeltaTime;

            // 도구는 반드시 양손이 같은 대상을 잡고 있어야 한다. 조인트가 하나라도 끊기면
            // 남은 손도 함께 놓아 다음 클릭에서 다시 양손으로 잡게 한다.
            if (toolHeld != null && (LeftHand.Held != toolHeld || RightHand.Held != toolHeld))
            {
                LeftHand.Release();
                RightHand.Release();
                ResetToolState();
            }

            if (toolHeld != null)
            {
                // Q를 놓는 순간에만 놓기/투척을 처리하고, 충전 중에는 두 손의 운반 자세를 유지한다.
                if (ProcessToolAction(aim)) return;
                DriveArm(Doll.Left, true, aim);
                DriveArm(Doll.Right, true, aim);
                return;
            }

            chargingTool = false;
            toolCharge = 0f;
            toolActionHeld = false;

            // 좌클릭과 우클릭 중 어느 쪽이든 움직이는 도구를 조준하면 양손으로 함께 잡는다.
            if ((input.GrabLeft || input.GrabRight) && toolThrowRegrabLock <= 0f && TryGrabTool(aim))
            {
                DriveArm(Doll.Left, true, aim);
                DriveArm(Doll.Right, true, aim);
                return;
            }

            DriveArm(Doll.Left, input.GrabLeft, aim);
            DriveArm(Doll.Right, input.GrabRight, aim);
            ClimbAssist();
        }

        bool TryGrabTool(Vector3 aim)
        {
            if (LeftHand.Gripping || RightHand.Gripping) return false;

            Grabbable targetGrabbable;
            Rigidbody body;
            Vector3 point;
            bool found = false;

            // 두 버튼이 동시에 들어오는 경우에도 한쪽 검색이 빗나갔다고 실패하지 않도록
            // 클릭된 손을 순서대로 살핀다. 도구를 찾은 손이 어느 쪽인지는 중요하지 않다.
            if (input.GrabLeft)
                found = FindTool(Doll.Left, LeftHand, aim, out targetGrabbable, out body, out point);
            else
            {
                targetGrabbable = null;
                body = null;
                point = Vector3.zero;
            }

            if (!found && input.GrabRight)
                found = FindTool(Doll.Right, RightHand, aim, out targetGrabbable, out body, out point);
            if (!found) return false;

            bool left = LeftHand.GripTo(targetGrabbable, body);
            bool right = RightHand.GripTo(targetGrabbable, body);
            if (left && right)
            {
                toolHeld = targetGrabbable;
                return true;
            }

            if (left) LeftHand.Release();
            if (right) RightHand.Release();
            return false;
        }

        static bool FindTool(Arm arm, Hand hand, Vector3 aim,
                             out Grabbable targetGrabbable, out Rigidbody body, out Vector3 point)
        {
            Vector3 shoulder = arm.ShoulderAnchor != null ? arm.ShoulderAnchor.position : hand.Body.position;
            Vector3 target = shoulder + aim * Tuning.ArmReach;
            targetGrabbable = Hand.Search(target, Tuning.GrabAssistRange, shoulder, aim,
                                          Tuning.GrabAssistAngle, out point, out body);
            hand.Aimed = targetGrabbable;
            hand.AimedPoint = point;
            return IsUsableTool(targetGrabbable, body);
        }

        static bool IsUsableTool(Grabbable target, Rigidbody body)
        {
            // 고정 레일·케이블은 기존의 한 손 매달리기를 유지하고, 실제로 운반할 수 있는
            // 비-키네마틱 물체만 도구로 취급한다.
            return target != null && !target.Climbable && body != null && !body.isKinematic;
        }

        bool ProcessToolAction(Vector3 aim)
        {
            if (input.ToolAction)
            {
                toolCharge = Mathf.Clamp01(toolCharge + Time.fixedDeltaTime / Tuning.ToolChargeDuration);
                toolActionHeld = true;
                chargingTool = true;
                return false;
            }

            if (!toolActionHeld) { chargingTool = false; return false; }

            if (toolCharge >= Tuning.ToolDropChargeThreshold)
                ThrowTool(aim, toolCharge);
            else
                DropTool();
            return true;
        }

        void DropTool()
        {
            LeftHand.Release();
            RightHand.Release();
            ResetToolState();
            toolThrowRegrabLock = Tuning.ToolThrowRegrabLock;
        }

        void ThrowTool(Vector3 aim, float charge)
        {
            var target = toolHeld;
            Rigidbody body = LeftHand.Held == target ? LeftHand.HeldBody : RightHand.HeldBody;
            Vector3 velocity = body != null ? body.linearVelocity : Vector3.zero;
            Vector3 direction = aim.sqrMagnitude > 1e-4f ? aim.normalized : Doll.Chest.transform.forward;

            LeftHand.Release();
            RightHand.Release();
            ResetToolState();
            toolThrowRegrabLock = Tuning.ToolThrowRegrabLock;

            if (body != null && !body.isKinematic)
            {
                pendingThrowBody = body;
                pendingThrowVelocity = velocity + direction * (Tuning.ToolThrowSpeed * Mathf.Clamp01(charge));
            }
        }

        void ResetToolState()
        {
            toolHeld = null;
            toolCharge = 0f;
            toolActionHeld = false;
            chargingTool = false;
        }

        void DriveArm(Arm arm, bool reaching, Vector3 aim)
        {
            var hand = arm.Hand;

            if (!reaching)
            {
                if (hand.Gripping) { hand.Release(); regrabLock = Tuning.RegrabLock; }
                hand.Aimed = null;
                JointTools.SetDrive(arm.Shoulder, 26f, 4f);
                JointTools.SetDrive(arm.Elbow, 18f, 3f);
                JointTools.SetTargetLocalRotation(arm.Shoulder, Quaternion.identity, arm.ShoulderStart);
                JointTools.SetTargetLocalRotation(arm.Elbow, Quaternion.identity, arm.ElbowStart);
                return;
            }

            // 어깨를 굳혀 팔을 조준 방향으로 세운다
            JointTools.SetDrive(arm.Shoulder, 220f, 18f);
            JointTools.SetDrive(arm.Elbow, 90f, 9f);

            Vector3 shoulder = arm.ShoulderAnchor != null ? arm.ShoulderAnchor.position : Doll.Chest.position;
            Vector3 target = shoulder + aim * Tuning.ArmReach;
            bool toolTarget = false;

            // ── 자석 손 ────────────────────────────────────────
            // 뻗은 손끝 근처에 잡을 것이 있으면 손 목표를 그쪽으로 옮긴다.
            // 목표를 팔 길이 안으로 자르는 것이 중요하다 — 넘기면 팔이 뻗다 못해 뜯긴다.
            if (!hand.Gripping)
            {
                Vector3 p; Rigidbody b;
                var g = Hand.Search(target, Tuning.GrabAssistRange, shoulder, aim, Tuning.GrabAssistAngle, out p, out b);
                hand.Aimed = g;
                hand.AimedPoint = p;
                toolTarget = IsUsableTool(g, b);
                if (g != null)
                {
                    Vector3 dir = p - shoulder;
                    if (dir.sqrMagnitude > 1e-4f)
                        target = shoulder + dir.normalized * Mathf.Min(dir.magnitude, Tuning.ArmReach);
                }
            }
            else
            {
                hand.Aimed = hand.Held;
                hand.AimedPoint = hand.Body.position;
            }

            Vector3 armDir = target - shoulder;
            if (armDir.sqrMagnitude < 1e-4f) armDir = aim;
            armDir.Normalize();

            Quaternion aimWorld = Quaternion.FromToRotation(Vector3.down, armDir);
            Quaternion aimLocal = Quaternion.Inverse(Doll.Chest.rotation) * aimWorld;
            JointTools.SetTargetLocalRotation(arm.Shoulder, aimLocal, arm.ShoulderStart);
            JointTools.SetTargetLocalRotation(arm.Elbow, Quaternion.identity, arm.ElbowStart);

            if (hand.Carrying) { Carry(hand, target); return; }

            Vector3 dp = target - hand.Body.position;
            Vector3 dv = Doll.Chest.linearVelocity - hand.Body.linearVelocity;
            Vector3 f = JointTools.Clamp(dp * Tuning.HandForce + dv * Tuning.HandDamp, Tuning.HandMaxForce);
            if (!JointTools.IsFinite(f)) return;

            if (hand.Hanging)
            {
                // 고정물을 잡은 손은 이미 붙어 있다 — 힘을 몸쪽에 실어야 몸이 딸려 올라간다
                Doll.Chest.AddForce(-f * Tuning.HangPull, ForceMode.Force);
            }
            else
            {
                hand.Body.AddForce(f, ForceMode.Force);
                Doll.Chest.AddForce(-f * 0.25f, ForceMode.Force);
                // 운반 가능한 도구는 TryGrabTool이 양손 성공 여부를 확인한 뒤에만 잡는다.
                if (regrabLock <= 0f && !toolTarget) hand.TryGrip();
            }
        }

        /// <summary>
        /// 들고 있는 물체를 손 앞으로 끌어온다.
        /// ForceMode.Acceleration 이라 <b>질량이 상관없다</b> — 55kg 궤짝이 5kg 통조림처럼 온다.
        /// 이 게임에서 "물건 들기가 쉽다" 는 말의 실체가 이 함수다.
        /// </summary>
        void Carry(Hand hand, Vector3 target)
        {
            var b = hand.HeldBody;
            if (b == null) return;

            int hands = hand.Held != null ? Mathf.Max(1, hand.Held.HandCount) : 1;
            float bonus = hands >= 2 ? Tuning.TwoHandBonus : 1f;

            Vector3 dp = target - hand.Body.position;
            Vector3 dv = Doll.Chest.linearVelocity - b.linearVelocity;
            Vector3 a = (dp * Tuning.CarryAccel + dv * Tuning.CarryDamp) * (bonus / hands);
            a = JointTools.Clamp(a, Tuning.CarryAccelMax);
            if (!JointTools.IsFinite(a)) return;

            b.AddForce(a, ForceMode.Acceleration);
            b.AddForce(-Physics.gravity * (Tuning.CarrySupport / hands), ForceMode.Acceleration);

            // 반작용은 아주 조금만 — 무거운 걸 잡아도 몸이 딸려가지 않는다
            Vector3 react = JointTools.Clamp(-a * b.mass * Tuning.CarryReaction, Tuning.HandMaxForce);
            Doll.Chest.AddForce(react, ForceMode.Force);
        }

        void ClimbAssist()
        {
            if (!Hanging) return;

            float best = float.MinValue;
            if (LeftHand.Hanging) best = Mathf.Max(best, LeftHand.Body.position.y);
            if (RightHand.Hanging) best = Mathf.Max(best, RightHand.Body.position.y);

            // 손이 골반보다 위에 있을 때만 기어오를 수 있다
            float over = best - (Doll.Hips.position.y + 0.16f);
            if (over <= 0f) return;

            float pull = Tuning.ClimbPull * Mathf.Clamp01(over / 0.35f);
            if (input.ArmsUp || input.Move.y > 0.1f) pull *= 2.2f;

            Doll.Chest.AddForce(Vector3.up * pull, ForceMode.Force);
            Doll.Hips.AddForce(Vector3.up * pull * 0.7f, ForceMode.Force);
        }
    }
}
