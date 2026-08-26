using UnityEngine;

namespace SpaceFlop
{
    /// <summary>
    /// 흐물거리는 우주비행사를 사람이 조종할 수 있는 정도로만 붙잡아 준다.
    ///
    ///  · 골반에 매달린 보이지 않는 스프링이 몸무게를 떠받쳐 서 있게 한다 (다리는 거들 뿐)
    ///  · 골반을 세계 기준 조인트로 세우고, 가는 방향으로 돌린다
    ///  · 다리는 걷기 주기에 맞춰 조인트 목표 자세를 흔든다 (애니메이션 클립 없음)
    ///  · 팔은 카메라가 보는 쪽으로 뻗되, 근처에 잡을 것이 있으면 그쪽으로 휘어 간다
    ///  · 잡은 물체는 질량을 무시하고 (ForceMode.Acceleration) 손 앞으로 끌려온다
    ///
    /// 이 다섯 말고는 전부 물리에 맡긴다 — 그래서 넘어지고, 그래서 웃기다.
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
        public bool Hanging { get { return LeftHand.Hanging || RightHand.Hanging; } }
        public bool Carrying { get { return LeftHand.Carrying || RightHand.Carrying; } }
        public bool Ragdolled { get; private set; }      // 크게 넘어져 조종이 잠깐 풀린 상태
        public float StepPhase { get; private set; }
        /// <summary>공중에서 자세제어 분사기를 쓰고 있는가 (HUD·연기 표시용).</summary>
        public bool Thrusting { get; private set; }
        public float AirTime { get; private set; }

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
            rig.faceDir = facing * Vector3.forward;
            return rig;
        }

        public void SetInput(InputFrame frame) { input = frame; }
        public InputFrame CurrentInput { get { return input; } }

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
            float fallSpeed = -Doll.Hips.linearVelocity.y;

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

            if (Grounded) AirTime = 0f; else AirTime += Time.fixedDeltaTime;

            // 착지 먼지 — 진공이라 흩어지지 않고 그대로 포물선을 그린다
            if (Grounded && !wasGrounded && fallSpeed > 1.6f)
                Puff.Regolith(FootPosition, Mathf.Clamp01(fallSpeed / 7f));
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

            Vector3 v = Doll.Hips.linearVelocity;
            Vector3 dv = new Vector3(targetVel.x - v.x, 0f, targetVel.z - v.z);

            float gain = Tuning.Accel * (Grounded ? 1f : Tuning.AirControl) * uprightBlend;
            Vector3 force = JointTools.Clamp(dv * gain, Tuning.AccelMax);
            if (JointTools.IsFinite(force))
            {
                // 골반만 밀면 상체가 뒤로 처져 달릴 때 몸이 접힌다 — 가슴에도 나눠 싣는다
                Doll.Hips.AddForce(force * 0.6f, ForceMode.Force);
                Doll.Chest.AddForce(force * 0.4f, ForceMode.Force);
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

            DriveArm(Doll.Left, input.GrabLeft, aim);
            DriveArm(Doll.Right, input.GrabRight, aim);
            ClimbAssist();
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

            // ── 자석 손 ────────────────────────────────────────
            // 뻗은 손끝 근처에 잡을 것이 있으면 손 목표를 그쪽으로 옮긴다.
            // 목표를 팔 길이 안으로 자르는 것이 중요하다 — 넘기면 팔이 뻗다 못해 뜯긴다.
            if (!hand.Gripping)
            {
                Vector3 p; Rigidbody b;
                var g = Hand.Search(target, Tuning.GrabAssistRange, shoulder, aim, Tuning.GrabAssistAngle, out p, out b);
                hand.Aimed = g;
                hand.AimedPoint = p;
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
                if (regrabLock <= 0f) hand.TryGrip();
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
