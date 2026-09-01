using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Metagame.Core;
using Metagame.Gameplay;
using Metagame.Gameplay.Parts;

namespace Metagame.Bootstrap
{
    /// <summary>
    /// 사람 손 없이 핵심 시스템을 순서대로 돌려보는 통합 점검.
    /// 실행:  SpaceFlop.exe -batchmode -nographics -space-selftest -logFile out.log
    /// 종료 코드 0 = 전부 통과.
    ///
    /// 이 프로토타입에서 특별히 지켜야 하는 것 셋:
    ///   1) 2등신 비율 — 헬멧이 키의 절반쯤이어야 한다
    ///   2) 저중력 산술 — 점프 높이/사거리와 코스의 단차·구덩이 폭이 물려 있다
    ///   3) 관대한 잡기 — 조준이 빗나가도 잡히고, 무거운 물체는 고정물처럼 짚는다
    /// </summary>
    public class SpaceSelfTest : MonoBehaviour
    {
        readonly List<string> fails = new List<string>();
        int checks;

        void Check(bool ok, string what)
        {
            checks++;
            if (ok) Debug.Log("[SELFTEST]  OK   " + what);
            else { fails.Add(what); Debug.Log("[SELFTEST]  FAIL " + what); }
        }

        GameSession session;
        LevelBuilder level;
        PlayerRig rig;
        Doll doll;

        static readonly Vector3 Pad = new Vector3(-8f, 0.05f, 0f);      // 착륙장의 빈 자리

        IEnumerator Start()
        {
            yield return new WaitForSeconds(0.5f);

            session = GameSession.Instance;
            var boot = Object.FindAnyObjectByType<SpaceBootstrap>();
            level = boot != null ? boot.Level : null;
            rig = session != null ? session.LocalPlayer : null;
            doll = rig != null ? rig.Doll : null;

            Check(session != null, "세션 존재");
            Check(level != null, "레벨 존재");
            Check(rig != null, "우주비행사 존재");
            if (session == null || level == null || rig == null) { Finish(); yield break; }

            session.ScriptedInput = true;
            rig.Cam.Pitch = 0f;
            rig.Cam.Yaw = 90f;                     // +X 를 본다

            yield return Body();
            yield return Models();
            yield return World();
            yield return Standing();
            yield return Walking();
            yield return DirectionalControls();
            yield return LowGravityJump();
            yield return Grabbing();
            yield return Carrying();
            yield return PlasmaTool();
            yield return CourseMath();
            yield return FreeLookCamera();
            yield return Machines();
            yield return CheckpointAndRespawn();
            yield return Ending();

            Finish();
        }

        // ── 1. 몸 — 2등신 우주비행사 ────────────────────────────
        IEnumerator Body()
        {
            Check(doll.Bodies.Count == 15, "부위 " + doll.Bodies.Count + "개 (골반·가슴·헬멧 + 팔6 + 다리6 = 15)");
            Check(doll.TotalMass > 30f && doll.TotalMass < 44f,
                  "몸무게 " + doll.TotalMass.ToString("0.0") + "kg (30~44)");

            // 이 캐릭터의 정체성 — 2등신
            float heads = RagdollBuilder.HeadCount;
            Check(heads > 1.9f && heads < 2.4f,
                  "약 " + heads.ToString("0.00") + "등신 (키 " + RagdollBuilder.TotalHeight.ToString("0.00")
                  + "m ÷ 헬멧 " + RagdollBuilder.HelmetD.ToString("0.00") + "m)");
            Check(RagdollBuilder.HelmetD > RagdollBuilder.ChestY - RagdollBuilder.HipsY,
                  "헬멧이 몸통보다 크다 (귀여움의 조건)");

            Check(doll.ChestJoint != null && doll.HeadJoint != null, "몸통 조인트 연결");
            Check(doll.Left.Shoulder != null && doll.Left.Elbow != null
                  && doll.Right.Shoulder != null && doll.Right.Elbow != null, "양팔 조인트 연결");
            Check(doll.LeftLeg.Hip != null && doll.LeftLeg.Knee != null && doll.LeftLeg.Ankle != null
                  && doll.RightLeg.Hip != null && doll.RightLeg.Knee != null && doll.RightLeg.Ankle != null,
                  "양다리 조인트 연결");
            Check(rig.LeftHand != null && rig.RightHand != null, "장갑 두 짝");

            // 팔 목표점이 팔 길이 안에 있어야 한다 — 넘기면 뻗는 순간 팔이 뜯긴다
            float armMax = (RagdollBuilder.ShoulderY - RagdollBuilder.HandY);
            Check(Tuning.ArmReach < armMax,
                  "팔 목표 " + Tuning.ArmReach.ToString("0.00") + "m < 실제 팔 길이 " + armMax.ToString("0.00") + "m");

            // 몸끼리 부딪히면 팔다리가 서로 끼어 그 자리에서 굳는다
            bool selfCollisionOff = true;
            for (int i = 0; i < doll.Colliders.Count && selfCollisionOff; i++)
                for (int k = i + 1; k < doll.Colliders.Count; k++)
                    if (Physics.GetIgnoreCollision(doll.Colliders[i], doll.Colliders[k]) == false)
                    { selfCollisionOff = false; break; }
            Check(selfCollisionOff, "몸끼리는 충돌하지 않는다 (" + doll.Colliders.Count + "개 부위)");

            int wrongLayer = 0;
            for (int i = 0; i < doll.Bodies.Count; i++)
                if (doll.Bodies[i].gameObject.layer != RagdollBuilder.PlayerLayer) wrongLayer++;
            Check(wrongLayer == 0, "모든 부위가 인형 레이어에 있다");

            yield break;
        }

        // ── 1b. 블렌더 모델 ─────────────────────────────────────
        IEnumerator Models()
        {
            Check(SpaceSuit.Available, "우주비행사 메시 프리팹이 Resources 에 있다");
            var suit = doll.Suit;
            Check(suit != null, "메시가 인형에 씌워졌다");

            if (suit != null)
            {
                Check(suit.LinkCount == 25, "본 " + suit.LinkCount + "개가 강체에 물렸다 (25)");
                Check(suit.MeshCount >= 1, "스킨 메시 " + suit.MeshCount + "개");

                // 배율이 어긋나면 여기서 잡힌다 — 랙돌 치수와 모델 치수는 한 몸이다
                float sil = suit.SilhouetteHeight;
                Check(Mathf.Abs(sil - RagdollBuilder.SilhouetteHeight) < 0.14f,
                      "보이는 실루엣 높이 " + sil.ToString("0.00") + "m (설계 "
                      + RagdollBuilder.SilhouetteHeight.ToString("0.00") + "m, 귀 포함)");

                // 프리미티브 몸은 보이면 안 된다 — 두 겹으로 겹쳐 보이면 이 검사가 잡는다
                int visible = 0;
                for (int i = 0; i < doll.Bodies.Count; i++)
                {
                    var r = doll.Bodies[i].GetComponent<Renderer>();
                    if (r != null && r.enabled) visible++;
                }
                Check(visible == 0, "프리미티브 몸은 꺼져 있다 (보이는 것 " + visible + "개)");

                // 콜라이더는 그대로여야 한다 — 메시를 씌운다고 물리가 바뀌면 안 된다
                Check(doll.Colliders.Count == 15, "콜라이더 " + doll.Colliders.Count + "개는 그대로다");
            }

            Check(ShipModel.Available, "우주선 메시 프리팹이 Resources 에 있다");

            yield return new WaitForSeconds(0.4f);
            if (suit != null)
            {
                Check(suit.WorstDrift() < 0.02f,
                      "메시가 강체를 따라간다 (가장 벌어진 본 " + suit.WorstDrift().ToString("0.000") + "m)");

                // T 자세 보정이 빠지면 여기가 30cm 로 벌어진다 — 보이는 손과 잡는 손이 어긋난다
                float gapL = suit.HandGap(rig.LeftHand), gapR = suit.HandGap(rig.RightHand);
                Check(gapL < 0.18f && gapR < 0.18f,
                      "보이는 손이 실제 잡는 손 위에 있다 (왼 " + gapL.ToString("0.00")
                      + "m, 오른 " + gapR.ToString("0.00") + "m)");
            }

            yield break;
        }

        // ── 2. 세계 ─────────────────────────────────────────────
        IEnumerator World()
        {
            float g = Mathf.Abs(Physics.gravity.y);
            Check(g > 1f && g < 4.5f,
                  "중력 " + g.ToString("0.00") + " m/s² — 지구의 " + (g / 9.81f * 100f).ToString("0") + "%");

            Check(Checkpoint.All.Count == 7, "표지 " + Checkpoint.All.Count + "개 (7)");
            Check(Goal.Instance != null, "구조 로켓 존재");
            Check(level.Hatch != null && level.Socket != null, "에어록 해치와 소켓 존재");
            Check(level.BatteryCell != null, "배터리 셀 존재");
            Check(level.Platform != null, "자기부상 화물판 존재");
            Check(!ShipModel.Available || level.Lander != null, "출발 지점에 캡슐이 놓였다");
            Check(level.Seesaw != null, "화물 널빤지 존재");
            Check(level.CableA.Count == 9 && level.CableB.Count == 9,
                  "계류 케이블 2줄 x " + level.CableA.Count + "마디");
            Check(level.Cargo.Count >= 6, "굴러다니는 화물 " + level.Cargo.Count + "개");
            Check(Grabbable.All.Count >= 20, "잡을 수 있는 것 " + Grabbable.All.Count + "개");

            var rail = Grabbable.Find("핸드레일");
            Check(rail != null && rail.Climbable, "핸드레일은 기어오를 수 있는 것으로 표시됐다");

            yield break;
        }

        // ── 3. 서 있기 ──────────────────────────────────────────
        IEnumerator Standing()
        {
            rig.Teleport(Pad, Quaternion.LookRotation(Vector3.right, Vector3.up));
            rig.SetInput(new InputFrame());
            yield return new WaitForSeconds(2.5f);

            // 실루엣 기준으로 잰다 — 부위 중심끼리 빼면 발바닥 두께와 헬멧 반지름이 빠진다
            float sole = doll.LeftLeg.Foot.position.y - RagdollBuilder.FootH * 0.5f;
            float crown = doll.Head.position.y + RagdollBuilder.HelmetD * 0.5f;
            float tall = crown - sole;
            Check(tall > RagdollBuilder.TotalHeight - 0.12f,
                  "몸이 펴져 서 있다 (발바닥~헬멧 꼭대기 " + tall.ToString("0.00")
                  + "m, 설계값 " + RagdollBuilder.TotalHeight.ToString("0.00") + "m · 귀까지는 "
                  + RagdollBuilder.SilhouetteHeight.ToString("0.00") + "m)");

            float up = Vector3.Dot(doll.Hips.transform.up, Vector3.up);
            Check(up > 0.80f, "골반이 곧추서 있다 (up·up = " + up.ToString("0.00") + ")");
            Check(!rig.Ragdolled, "자빠지지 않았다");
            Check(rig.Grounded, "바닥을 딛고 있다");

            float drift = Vector3.Distance(new Vector3(doll.Hips.position.x, 0f, doll.Hips.position.z),
                                           new Vector3(Pad.x, 0f, Pad.z));
            Check(drift < 1.0f, "가만히 두면 제자리에 있다 (" + drift.ToString("0.00") + "m 이동)");

            Check(Mathf.Abs(doll.Hips.position.y - Tuning.RideHeight) < 0.18f,
                  "골반 높이 " + doll.Hips.position.y.ToString("0.00") + "m (목표 " + Tuning.RideHeight + ")");

            Check(rig.Speed < 0.5f, "가만히 서 있으면 속도가 0 이다 (" + rig.Speed.ToString("0.00") + ")");
        }

        // ── 4. 걷기 ─────────────────────────────────────────────
        IEnumerator Walking()
        {
            // 착륙장 서쪽 끝에서 시작한다 — 달리기까지 하면 꽤 멀리 간다
            rig.Teleport(new Vector3(-10f, 0.05f, 0f), Quaternion.LookRotation(Vector3.right, Vector3.up));
            yield return new WaitForSeconds(1.2f);

            Vector3 from = doll.Hips.position;

            var f = new InputFrame();
            f.Move = new Vector2(0f, 1f);
            rig.SetInput(f);
            yield return new WaitForSeconds(3.0f);

            Vector3 to = doll.Hips.position;
            float ahead = to.x - from.x;
            float sideways = Mathf.Abs(to.z - from.z);

            Check(ahead > 3.0f, "앞으로 걸어갔다 (" + ahead.ToString("0.00") + "m > 3.0)");
            Check(sideways < 2.0f, "옆으로 새지 않았다 (" + sideways.ToString("0.00") + "m)");
            Check(!rig.Ragdolled, "걷다가 자빠지지 않았다");
            Check(rig.Speed > 1.4f, "걷는 속도 " + rig.Speed.ToString("0.0") + " m/s");

            // 걷는 중에는 발이 번갈아 들리므로 발 높이로 재면 안 된다 — 상체가 펴져 있는지를 본다
            float torso = doll.Head.position.y - doll.Hips.position.y;
            Debug.Log("[SELFTEST] 진단 상체=" + torso.ToString("0.00")
                      + " 골반up=" + Vector3.Dot(doll.Hips.transform.up, Vector3.up).ToString("0.00")
                      + " 가슴up=" + Vector3.Dot(doll.Chest.transform.up, Vector3.up).ToString("0.00")
                      + " 헬멧-골반=" + (doll.Head.position - doll.Hips.position).ToString("F2"));
            Check(torso > 0.40f, "걷는 동안에도 상체가 서 있다 (골반~헬멧 " + torso.ToString("0.00") + "m > 0.40)");
            Check(doll.Hips.position.y > Tuning.RideHeight * 0.7f,
                  "걷는 동안 주저앉지 않는다 (골반 " + doll.Hips.position.y.ToString("0.00")
                  + "m > " + (Tuning.RideHeight * 0.7f).ToString("0.00") + ")");

            f.Run = true;
            rig.SetInput(f);
            yield return new WaitForSeconds(1.6f);
            Check(rig.Speed > 3.2f, "달리기 속도 " + rig.Speed.ToString("0.0") + " m/s (> 3.2)");

            rig.SetInput(new InputFrame());
            yield return new WaitForSeconds(1.2f);
        }

        // ── 4b. 카메라 기준 방향 이동 ─────────────────────────
        IEnumerator DirectionalControls()
        {
            var labels = new[] { "W 전진", "A 좌우 걸음", "S 뒷걸음", "D 좌우 걸음" };
            var moves = new[]
            {
                new Vector2(0f, 1f),
                new Vector2(-1f, 0f),
                new Vector2(0f, -1f),
                new Vector2(1f, 0f),
            };

            for (int i = 0; i < moves.Length; i++)
            {
                rig.Teleport(Pad, Quaternion.identity);
                rig.Cam.Yaw = 0f;
                rig.Cam.Pitch = 0f;
                rig.Cam.SnapToTarget();

                Vector3 expected = rig.Cam.CameraRelativeMove(moves[i]);
                Vector3 from = rig.Position;
                var input = new InputFrame { Move = moves[i] };
                rig.SetInput(input);
                yield return new WaitForSeconds(0.85f);

                Vector3 displacement = rig.Position - from;
                displacement.y = 0f;
                Vector3 local = rig.MovementDirectionLocal;
                Check(displacement.sqrMagnitude > 0.04f
                      && Vector3.Dot(displacement.normalized, expected.normalized) > 0.65f,
                      labels[i] + "이 카메라 기준으로 이동한다");
                Check(local.sqrMagnitude > 0.05f
                      && Vector2.Dot(local.normalized, moves[i]) > 0.55f,
                      labels[i] + " 보행 포즈 방향이 입력과 일치한다");
                Check(Vector3.Dot(rig.FacingDirection.normalized, rig.Cam.PlanarForward) > 0.85f,
                      labels[i] + " 중 캐릭터 정면은 카메라를 유지한다");

                rig.SetInput(new InputFrame());
                yield return new WaitForSeconds(0.2f);
            }
        }

        // ── 5. 저중력 점프 ──────────────────────────────────────
        IEnumerator LowGravityJump()
        {
            rig.Teleport(Pad, Quaternion.LookRotation(Vector3.right, Vector3.up));
            rig.SetInput(new InputFrame());
            yield return new WaitForSeconds(1.8f);

            float ground = doll.Hips.position.y;
            float expect = LevelBuilder.JumpHeight;

            var f = new InputFrame();
            f.Jump = true;
            rig.SetInput(f);

            float peak = ground;
            float air = 0f;
            for (float t = 0f; t < 3.4f; t += Time.fixedDeltaTime)
            {
                peak = Mathf.Max(peak, doll.Hips.position.y);
                if (t > 0.2f) f.Jump = false;                 // 한 번만 누른다
                rig.SetInput(f);
                if (!rig.Grounded) air = Mathf.Max(air, rig.AirTime);
                yield return new WaitForFixedUpdate();
            }
            rig.SetInput(new InputFrame());

            float rise = peak - ground;
            Check(rise > expect * 0.55f,
                  "제자리 점프로 " + rise.ToString("0.00") + "m 떴다 (이론값 " + expect.ToString("0.00") + "m)");
            Check(rise > 1.0f, "키(1.33m)에 가깝게 뜬다 — 저중력이 눈에 보인다");
            Check(air > 1.2f, "체공 " + air.ToString("0.00") + "초 (> 1.2) — 공중에서 허우적거릴 시간이 있다");

            yield return new WaitForSeconds(2.0f);
            Check(rig.Grounded, "다시 착지했다");
            Check(!rig.Ragdolled, "착지하고 자빠지지 않았다");
        }

        // ── 6. 잡기 — 얼마나 관대한가 ───────────────────────────
        IEnumerator Grabbing()
        {
            rig.Teleport(Pad, Quaternion.LookRotation(Vector3.right, Vector3.up));
            rig.Cam.Pitch = 0f;
            rig.Cam.Yaw = 90f;
            rig.SetInput(new InputFrame());
            yield return new WaitForSeconds(1.8f);

            // (a) 눈앞에 있는 것
            Vector3 shoulder = doll.Right.ShoulderAnchor.position;
            var target = Prim.Box("시험용 손잡이", shoulder + Vector3.right * Tuning.ArmReach,
                                  new Vector3(0.16f, 0.5f, 0.16f), new Color(1f, 0.4f, 0.9f), 0.3f, null);
            Grabbable.Mark(target, "시험용 손잡이", true);

            var f = new InputFrame();
            f.GrabRight = true;
            rig.SetInput(f);
            yield return new WaitForSeconds(1.2f);

            Check(rig.RightHand.Gripping, "오른손이 눈앞의 것을 잡았다");
            Check(rig.RightHand.Held != null && rig.RightHand.Held.Label == "시험용 손잡이",
                  "잡은 것이 무엇인지 안다 (" + (rig.RightHand.Held != null ? rig.RightHand.Held.Label : "없음") + ")");
            Check(rig.RightHand.Hanging, "고정물을 잡으면 매달림 상태가 된다");

            rig.SetInput(new InputFrame());
            yield return new WaitForSeconds(0.4f);
            Check(!rig.RightHand.Gripping, "손을 떼면 놓는다");
            Destroy(target);
            yield return new WaitForSeconds(0.4f);

            // (b) 자석 손 — 조준이 45° 쯤 빗나가도 손이 휘어 간다
            rig.Teleport(Pad, Quaternion.LookRotation(Vector3.right, Vector3.up));
            rig.Cam.Yaw = 90f; rig.Cam.Pitch = 0f;
            yield return new WaitForSeconds(1.6f);

            shoulder = doll.Right.ShoulderAnchor.position;
            Vector3 off = shoulder + Vector3.right * Tuning.ArmReach + Vector3.forward * 0.45f;
            var sideTarget = Prim.Box("빗나간 손잡이", off, new Vector3(0.16f, 0.5f, 0.16f),
                                      new Color(0.4f, 1f, 0.9f), 0.3f, null);
            Grabbable.Mark(sideTarget, "빗나간 손잡이", true);

            f = new InputFrame();
            f.GrabRight = true;
            rig.SetInput(f);
            yield return new WaitForSeconds(0.35f);
            Check(rig.RightHand.Aimed != null && rig.RightHand.Aimed.Label == "빗나간 손잡이",
                  "자석 손이 옆으로 45° 벗어난 것을 조준한다");
            yield return new WaitForSeconds(1.3f);
            Check(rig.RightHand.Gripping,
                  "조준이 빗나가도 잡힌다 (자석 반경 " + Tuning.GrabAssistRange.ToString("0.00") + "m)");

            rig.SetInput(new InputFrame());
            Destroy(sideTarget);
            yield return new WaitForSeconds(0.4f);

            // (c) 표시가 없는 것은 잡히지 않는다 — 아무 벽이나 타면 퍼즐이 사라진다
            rig.Teleport(Pad, Quaternion.LookRotation(Vector3.right, Vector3.up));
            rig.Cam.Yaw = 90f;
            yield return new WaitForSeconds(1.4f);

            shoulder = doll.Right.ShoulderAnchor.position;
            var plain = Prim.Box("맨 벽", shoulder + Vector3.right * Tuning.ArmReach,
                                 new Vector3(0.16f, 0.5f, 0.16f), new Color(0.5f, 0.5f, 0.5f), 0.3f, null);

            f = new InputFrame();
            f.GrabRight = true;
            rig.SetInput(f);
            yield return new WaitForSeconds(1.3f);
            Check(!rig.RightHand.Gripping, "표시가 없는 것은 잡히지 않는다");
            Check(rig.RightHand.Aimed == null, "자석도 표시 없는 것은 노리지 않는다");

            rig.SetInput(new InputFrame());
            Destroy(plain);
            yield return new WaitForSeconds(0.4f);
        }

        // ── 7. 나르기·고정 그립 — 질량에 따라 규칙이 갈리는가 ──────
        // 가벼운 상자는 양손으로 운반되고, 무거운 상자는 들어 올리지 않고
        // 고정물을 짚듯 잡혀야 한다.
        float carryY, carryStartY;
        bool carryGripped, carryFixed, carryStanding, carryPlayerCollisionIgnored;

        IEnumerator Carrying()
        {
            yield return PullTest("가벼운 상자", 5f);
            float lightRise = carryY - carryStartY;
            bool lightGrip = carryGripped;
            bool lightPlayerCollisionIgnored = carryPlayerCollisionIgnored;

            yield return PullTest("무거운 궤짝", 55f);
            float heavyRise = carryY - carryStartY;
            bool heavyPlayerCollisionIgnored = carryPlayerCollisionIgnored;

            Check(lightGrip, "가벼운 상자를 잡았다");
            Check(lightPlayerCollisionIgnored, "들고 있는 가벼운 도구는 플레이어와 충돌하지 않는다");
            bool heavyFixed = carryFixed;
            Check(carryGripped, "55kg 궤짝을 양손으로 짚었다");
            Check(lightRise > 0.20f, "가벼운 상자가 " + lightRise.ToString("0.00") + "m 들려 올라왔다");
            Check(heavyFixed, "55kg 궤짝은 고정물처럼 잡힌다");
            Check(!heavyPlayerCollisionIgnored, "고정물로 짚는 무거운 물체에는 운반 충돌 예외를 적용하지 않는다");
            Check(heavyRise < 0.20f, "55kg 궤짝은 들어 올리지 않는다 (상승 " + heavyRise.ToString("0.00") + "m)");
            Check(heavyRise <= lightRise + 0.10f,
                  "무거운 고정물의 상승량이 가벼운 도구보다 크지 않다 (차이 "
                  + (heavyRise - lightRise).ToString("0.00") + "m)");
            Check(carryStanding || carryFixed, "무거운 물체를 짚어도 플레이어 상태가 유지된다");
        }

        IEnumerator PlasmaTool()
        {
            rig.Teleport(Pad, Quaternion.LookRotation(Vector3.right, Vector3.up));
            rig.Cam.Yaw = 90f;
            rig.Cam.Pitch = 0f;
            rig.SetInput(new InputFrame());
            yield return new WaitForSeconds(1.0f);

            var plasma = Object.FindAnyObjectByType<PlasmaWeldingTool>();
            Check(plasma != null, "플라즈마 용접기 도구 베이스가 연결돼 있다");
            if (plasma == null) yield break;

            var body = plasma.GetComponent<Rigidbody>();
            var previousInterpolation = body != null ? body.interpolation : RigidbodyInterpolation.None;
            Vector3 spawn = rig.Doll.Right.ShoulderAnchor.position + Vector3.right * Tuning.ArmReach;
            plasma.transform.SetPositionAndRotation(spawn, Quaternion.LookRotation(Vector3.right, Vector3.up));
            if (body != null)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }

            var pick = new InputFrame { GrabLeft = true };
            rig.SetInput(pick);
            yield return new WaitForSeconds(0.8f);
            Check(rig.HeldTool == plasma.GetComponent<Grabbable>(), "플라즈마 용접기를 집었다");
            Check(rig.ActiveTool == plasma, "PlayerRig가 ToolBase를 통해 현재 도구를 노출한다");
            Check(body != null && body.isKinematic, "장착 중 도구는 키네마틱이다");
            Check(body != null && body.interpolation == RigidbodyInterpolation.Interpolate,
                  "장착 중 도구는 보간된다");

            // 집기 클릭은 도구 기능으로 소비한다. 버튼을 먼저 놓아야 기능 입력을 시작한다.
            rig.SetInput(new InputFrame());
            yield return new WaitForSeconds(0.2f);

            Vector3 beamDirection = rig.Cam.AimDirection.sqrMagnitude > 1e-4f
                ? rig.Cam.AimDirection.normalized
                : Vector3.forward;
            Vector3 wallUp = Mathf.Abs(Vector3.Dot(beamDirection, Vector3.up)) > 0.98f
                ? Vector3.right
                : Vector3.up;
            var wall = Prim.Box("도구 빔 검사용 벽", plasma.Muzzle.position + beamDirection * 0.35f,
                                new Vector3(0.25f, 0.25f, 0.06f), new Color(0.2f, 0.2f, 0.2f), 0.2f);
            wall.transform.rotation = Quaternion.LookRotation(beamDirection, wallUp);
            wall.layer = GameLayers.BeamQuery;

            var weld = new InputFrame { ToolPrimary = true };
            rig.SetInput(weld);
            yield return new WaitForSeconds(0.2f);
            Check(plasma.BeamMode == PlasmaBeamMode.Weld && plasma.BeamActive,
                  "좌클릭 유지로 용접 빔이 켜진다");
            Check(Vector3.Distance(plasma.Muzzle.position, plasma.BeamEnd) < Tuning.ToolBeamRange - 0.05f,
                  "용접 빔이 콜라이더 앞에서 멈춘다");

            var cut = new InputFrame { ToolSecondary = true };
            rig.SetInput(cut);
            yield return new WaitForSeconds(0.2f);
            Check(plasma.BeamMode == PlasmaBeamMode.Cut && plasma.BeamActive,
                  "우클릭 유지로 절단 빔이 켜진다");
            Check(plasma.CurrentBeamColor != new Color(0.08f, 0.75f, 1f, 1f),
                  "절단 빔은 용접 빔과 다른 색상이다");

            rig.SetInput(new InputFrame());
            yield return new WaitForSeconds(0.2f);
            Check(!plasma.BeamActive, "도구 버튼을 놓으면 빔이 사라진다");

            Vector3 previous = body != null ? body.position : Vector3.zero;
            float largestStep = 0f;
            var move = new InputFrame { Move = new Vector2(0f, 1f) };
            rig.SetInput(move);
            for (int i = 0; i < 60; i++)
            {
                yield return new WaitForFixedUpdate();
                if (body == null) continue;
                largestStep = Mathf.Max(largestStep, Vector3.Distance(previous, body.position));
                previous = body.position;
            }
            Check(largestStep < 0.20f,
                  "도구 이동이 보간되어 한 물리 틱의 이동량이 안정적이다 (최대 "
                  + largestStep.ToString("0.000") + "m)");
            Check(rig.Doll.Hips.position.y < Pad.y + 2.5f,
                  "도구를 든 채 플레이어가 비정상적으로 날아가지 않는다");

            // Q 짧게 누르고 놓아 도구를 내려놓는다.
            rig.SetInput(new InputFrame { ToolAction = true, ToolUtility = true });
            yield return new WaitForFixedUpdate();
            rig.SetInput(new InputFrame());
            yield return new WaitForSeconds(0.25f);
            Check(rig.ActiveTool == null, "Q를 놓으면 도구가 해제된다");
            Check(body != null && body.interpolation == previousInterpolation,
                  "도구 해제 후 원래 보간 설정이 복구된다");

            Object.Destroy(wall);
            yield return new WaitForSeconds(0.2f);
        }

        IEnumerator PullTest(string label, float mass)
        {
            rig.Teleport(Pad, Quaternion.LookRotation(Vector3.right, Vector3.up));
            rig.Cam.Yaw = 90f; rig.Cam.Pitch = 0f;
            rig.SetInput(new InputFrame());
            yield return new WaitForSeconds(1.6f);

            Vector3 spot = rig.FootPosition + Vector3.right * 1.0f + Vector3.up * 0.42f;
            var go = Prim.Box(label, spot, new Vector3(0.8f, 0.8f, 0.8f),
                              new Color(0.8f, 0.6f, 0.3f), 0.35f, null);
            go.GetComponent<Collider>().material = Prim.GroundPhysics;

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.linearDamping = 0.05f;
            rb.angularDamping = 0.25f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            Grabbable.Mark(go, label);

            yield return new WaitForSeconds(0.8f);          // 놓인 자리에 가라앉게
            carryStartY = rb.position.y;

            var f = new InputFrame();
            f.GrabRight = true;
            f.GrabLeft = true;
            rig.SetInput(f);
            yield return new WaitForSeconds(1.6f);

            carryGripped = rig.LeftHand.Gripping && rig.RightHand.Gripping
                           && rig.LeftHand.Held != null && rig.RightHand.Held == rig.LeftHand.Held
                           && rig.LeftHand.Held.Label == label;
            carryFixed = rig.LeftHand.Hanging && rig.RightHand.Hanging;
            carryPlayerCollisionIgnored = false;
            var objectCollider = go.GetComponent<Collider>();
            if (objectCollider != null)
            {
                carryPlayerCollisionIgnored = true;
                for (int i = 0; i < doll.Colliders.Count; i++)
                {
                    if (doll.Colliders[i] != null &&
                        !Physics.GetIgnoreCollision(doll.Colliders[i], objectCollider))
                    {
                        carryPlayerCollisionIgnored = false;
                        break;
                    }
                }
            }
            carryY = rb.position.y;
            carryStanding = !rig.Ragdolled && doll.Hips.position.y > Tuning.RideHeight * 0.7f;
            Debug.Log("[SELFTEST] 나르기 진단 " + label + " " + mass + "kg — 시작 y="
                      + carryStartY.ToString("0.00") + " → " + carryY.ToString("0.00")
                      + ", 잡음=" + carryGripped + ", 골반 y=" + doll.Hips.position.y.ToString("0.00"));

            rig.SetInput(new InputFrame());
            yield return new WaitForSeconds(0.3f);
            Destroy(go);
            yield return new WaitForSeconds(0.4f);
        }

        // ── 8. 저중력 산술과 코스 설계 ──────────────────────────
        IEnumerator CourseMath()
        {
            float jump = LevelBuilder.JumpHeight;
            float range = LevelBuilder.JumpRange;
            Debug.Log("[SELFTEST] 저중력 산술 — 점프 높이 " + jump.ToString("0.00")
                      + "m, 달려 뛴 사거리 " + range.ToString("0.00") + "m");

            // 절벽은 점프로 넘을 수 없어야 "기어오르기" 가 퍼즐이 된다
            float below = GroundHeightAt(new Vector3(39f, 12f, 0f));
            float above = GroundHeightAt(new Vector3(44f, 12f, 0f));
            float step = above - below;
            Check(step > jump + 0.6f,
                  "절벽 단차 " + step.ToString("0.00") + "m > 점프 높이 " + jump.ToString("0.00") + "m");

            var rail = Grabbable.Find("핸드레일");
            Check(rail != null, "핸드레일이 거기 있다");
            if (rail != null)
            {
                var col = rail.GetComponent<Collider>();
                float railBottom = col != null ? col.bounds.min.y : rail.transform.position.y;
                float standReach = below + RagdollBuilder.ShoulderY + Tuning.ArmReach + Tuning.GrabRadius;

                Check(railBottom > standReach,
                      "레일 아랫면 " + railBottom.ToString("0.00") + "m 은 서서는 안 닿는다 ("
                      + standReach.ToString("0.00") + "m) — 뛰어야 한다");
                Check(railBottom < standReach + jump,
                      "뛰면 닿는다 (" + (standReach + jump).ToString("0.00") + "m 까지)");
                Check(above - railBottom < 0.8f,
                      "레일이 절벽 턱 바로 아래에 있다 (턱까지 " + (above - railBottom).ToString("0.00") + "m)");
            }

            // 크레이터 징검다리는 뛰어 넘을 수 있어야 한다
            Check(GroundHeightAt(new Vector3(16.5f, 12f, 0f)) < -50f, "크레이터 사이는 비어 있다 (x=16.5)");
            Check(GroundHeightAt(new Vector3(20.5f, 12f, 0f)) > 0.5f, "첫 봉우리는 있다 (x=20.5)");
            Check(GroundHeightAt(new Vector3(27.5f, 12f, 0f)) > 1.0f, "둘째 봉우리는 있다 (x=27.5)");
            Check(5.0f < range, "첫 도약 5.0m < 사거리 " + range.ToString("0.00") + "m");

            // 균열과 화물판 구간은 사거리보다 넓어야 케이블·발판이 쓸모가 있다
            Check(GroundHeightAt(new Vector3(68f, 12f, 0f)) < -50f, "케이블 균열은 비어 있다 (x=68)");
            Check(20f > range, "균열 폭 20m > 사거리 " + range.ToString("0.00") + "m — 케이블을 써야 한다");
            Check(GroundHeightAt(new Vector3(99f, 12f, 5.5f)) < -50f, "화물판 구간은 비어 있다");
            Check(14f > range, "화물판 구간 14m > 사거리 " + range.ToString("0.00") + "m — 판을 타야 한다");

            yield break;
        }

        static float GroundHeightAt(Vector3 from)
        {
            RaycastHit hit;
            if (Physics.Raycast(from, Vector3.down, out hit, 200f,
                                RagdollBuilder.WorldMask, QueryTriggerInteraction.Ignore))
                return hit.point.y;
            return -999f;
        }

        // ── 8b. Alt 자유 시점 ───────────────────────────────────
        IEnumerator FreeLookCamera()
        {
            var cam = rig.Cam;
            cam.SetFreeLook(false);
            cam.Yaw = 90f;
            cam.Pitch = 0f;
            yield return null;

            Vector3 aim0 = cam.AimDirection;
            Vector3 fwd0 = cam.PlanarForward;

            cam.SetFreeLook(true);
            cam.Steer(new Vector2(50f, 6f));          // 시점을 확 돌린다
            yield return null;

            Vector3 aim1 = cam.AimDirection;
            Vector3 fwd1 = cam.PlanarForward;
            Check(Mathf.Abs(Mathf.DeltaAngle(cam.ViewYaw, 90f)) > 30f,
                  "Alt 를 누르면 시점이 돌아간다 (" + Mathf.DeltaAngle(cam.ViewYaw, 90f).ToString("0") + "도)");
            Check(Vector3.Angle(cam.AimDirection, aim0) > 30f, "Alt 중 팔이 현재 카메라 방향을 따른다");
            Check(Vector3.Angle(cam.PlanarForward, fwd0) > 30f, "Alt 중 걷는 방향이 현재 카메라를 따른다");

            cam.SetFreeLook(false);
            yield return new WaitForSeconds(1.2f);
            Check(Mathf.Abs(Mathf.DeltaAngle(cam.ViewYaw, cam.Yaw)) < 1f,
                  "Alt 를 놓아도 현재 시점과 조작 기준이 일치한다");
            Check(Vector3.Angle(cam.AimDirection, aim1) < 1f,
                  "Alt 를 놓아도 카메라 기준 조작 방향을 유지한다");
            Check(Vector3.Angle(cam.PlanarForward, fwd1) < 1f,
                  "Alt 를 놓아도 카메라 기준 이동 방향을 유지한다");
        }

        // ── 9. 기계 장치 ────────────────────────────────────────
        IEnumerator Machines()
        {
            // 두 시점만 비교하면 사인 곡선의 꼭대기 근처에서 재는 순간 거의 안 움직인 것으로 나온다.
            // (앞 단계 길이가 조금만 바뀌어도 위상이 밀려 결과가 뒤집혔다.)
            // 3초 동안 훑어 최대 이동폭을 본다 — 주기(9초)의 어디서 시작하든 성립한다.
            Vector3 lo = level.Platform.transform.position, hi = lo;
            for (float t = 0f; t < 3f; t += 0.25f)
            {
                yield return new WaitForSeconds(0.25f);
                Vector3 p = level.Platform.transform.position;
                lo = Vector3.Min(lo, p);
                hi = Vector3.Max(hi, p);
            }
            float travel = Vector3.Distance(lo, hi);
            Check(travel > 1.0f, "화물판이 실제로 움직인다 (3초 동안 " + travel.ToString("0.00") + "m)");

            var tail = level.CableA[level.CableA.Count - 1];
            float expected = 10.25f - 0.52f * 9f;
            Check(Mathf.Abs(tail.position.y - expected) < 0.8f,
                  "케이블 끝이 " + tail.position.y.ToString("0.00") + "m 에 늘어져 있다 (예상 " + expected.ToString("0.00") + ")");
            Check(Mathf.Abs(tail.position.x - 63f) < 0.7f, "케이블이 곧게 늘어져 있다");

            yield return VehicleControls();

            // 소켓 → 해치
            Check(!level.Hatch.IsOpen, "처음엔 해치가 닫혀 있다");
            Check(!level.Socket.Powered, "처음엔 전원이 없다");
            bool blockedBefore = Physics.Raycast(new Vector3(118f, 5.6f, 0f), Vector3.right, 5f,
                                                 RagdollBuilder.WorldMask, QueryTriggerInteraction.Ignore);
            Check(blockedBefore, "닫힌 해치가 길을 막고 있다");

            Check(level.BatteryCell.GetComponent<Grabbable>().CanGrab, "배터리 셀은 들 수 있다");

            level.Socket.Insert();
            yield return new WaitForFixedUpdate();
            Check(level.Socket.Powered, "배터리 셀을 꽂았다");
            Check(!level.BatteryCell.GetComponent<Grabbable>().CanGrab, "꽂힌 셀은 다시 뽑히지 않는다");

            yield return new WaitForSeconds(2.4f);
            Check(level.Hatch.IsOpen, "해치가 열렸다 (" + level.Hatch.Current01.ToString("0.00") + ")");
            Check(level.Hatch.transform.position.y < level.Hatch.ClosedPos.y - 3f, "해치가 바닥 아래로 내려갔다");

            bool blocked = Physics.Raycast(new Vector3(118f, 5.6f, 0f), Vector3.right, 5f,
                                           RagdollBuilder.WorldMask, QueryTriggerInteraction.Ignore);
            Check(!blocked, "해치가 열리면 통로가 뚫린다");
        }

        IEnumerator VehicleControls()
        {
            var vehicle = Object.FindAnyObjectByType<TransportVehicle>();
            Check(vehicle != null, "운반선이 존재한다");
            if (vehicle == null) yield break;

            rig.Teleport(vehicle.transform.position + Vector3.forward * 1.5f, Quaternion.identity);
            rig.Cam.Yaw = 0f;
            rig.Cam.Pitch = 0f;
            rig.Cam.SnapToTarget();

            bool boarded = vehicle.TryBoard(rig);
            Check(boarded, "운반선에 탑승했다");
            if (!boarded)
            {
                rig.Teleport(Pad, Quaternion.LookRotation(Vector3.right, Vector3.up));
                yield break;
            }

            vehicle.SetFacingDirection(rig.Cam.PlanarForward);
            Vector3 initialForward = vehicle.ExitForward;
            Vector3 sideDirection = rig.Cam.CameraRelativeMove(new Vector2(1f, 0f));
            vehicle.SetDriveVector(sideDirection);
            vehicle.Tick(Time.fixedDeltaTime);
            Check(Vector3.Dot(vehicle.HorizontalVelocity, sideDirection) > 0.01f,
                  "운반선 A/D 입력은 카메라 기준 위치 이동으로 전달된다");
            Check(Vector3.Angle(vehicle.ExitForward, initialForward) < 1f,
                  "운반선 A/D 이동이 선체 정면을 회전시키지 않는다");

            rig.Cam.Steer(new Vector2(45f, 0f));
            vehicle.SetFacingDirection(rig.Cam.PlanarForward);
            Check(Vector3.Dot(vehicle.ExitForward, rig.Cam.PlanarForward) > 0.98f,
                  "정지 중 카메라를 돌리면 운반선 정면도 갱신된다");

            vehicle.ForceReset();
            rig.Teleport(Pad, Quaternion.LookRotation(Vector3.right, Vector3.up));
            yield return new WaitForFixedUpdate();
        }

        // ── 10. 표지 · 부활 ─────────────────────────────────────
        IEnumerator CheckpointAndRespawn()
        {
            rig.Teleport(new Vector3(80f, 4.75f, 0f), Quaternion.LookRotation(Vector3.right, Vector3.up));
            yield return new WaitForSeconds(1.2f);
            Check(session.CheckpointIndex == 3,
                  "표지 3 에 도달했다 (지금 " + session.CheckpointIndex + ")");

            int fallsBefore = session.Falls;
            rig.Teleport(new Vector3(68f, -60f, 0f), Quaternion.identity);
            yield return new WaitForSeconds(1.5f);

            Check(session.Falls == fallsBefore + 1, "떨어진 것을 셌다");
            Check(doll.Hips.position.y > 0f, "균열에서 되살아났다");
            Check(Mathf.Abs(doll.Hips.position.x - 80f) < 2.5f,
                  "마지막 표지로 되돌아왔다 (x=" + doll.Hips.position.x.ToString("0.0") + ")");

            yield return new WaitForSeconds(1.5f);
            Check(!rig.Ragdolled, "되살아난 뒤 다시 서 있다");
        }

        // ── 11. 골 ──────────────────────────────────────────────
        IEnumerator Ending()
        {
            Check(!session.Cleared, "아직 로켓에 닿지 않았다");
            Vector3 door = Goal.Instance != null ? Goal.Instance.Entrance : new Vector3(136f, 4.7f, 0f);
            rig.Teleport(door + Vector3.up * 0.05f, Quaternion.LookRotation(Vector3.right, Vector3.up));
            yield return new WaitForSeconds(1.2f);
            Check(session.Cleared, "구조 로켓에 닿으면 판이 끝난다");
            Check(session.ClearTime > 0f, "기록이 남는다 (" + session.ClearTime.ToString("0.0") + "초)");

            bool finite = true;
            for (int i = 0; i < doll.Bodies.Count; i++)
                if (!JointTools.IsFinite(doll.Bodies[i].position)) finite = false;
            Check(finite, "몸이 숫자로 남아 있다 (NaN 없음)");

            float spread = 0f;
            for (int i = 0; i < doll.Bodies.Count; i++)
                spread = Mathf.Max(spread, Vector3.Distance(doll.Bodies[i].position, doll.Hips.position));
            Check(spread < 1.1f, "몸이 흩어지지 않았다 (가장 먼 부위 " + spread.ToString("0.00") + "m)");

            yield break;
        }

        void Finish()
        {
            Debug.Log("[SELFTEST] ── " + (checks - fails.Count) + " / " + checks + " 통과 ──");
            for (int i = 0; i < fails.Count; i++) Debug.Log("[SELFTEST] 실패: " + fails[i]);
            Debug.Log(fails.Count == 0 ? "[SELFTEST] PASS" : "[SELFTEST] FAIL " + fails.Count + "건");
            Application.Quit(fails.Count == 0 ? 0 : 1);
        }
    }
}
