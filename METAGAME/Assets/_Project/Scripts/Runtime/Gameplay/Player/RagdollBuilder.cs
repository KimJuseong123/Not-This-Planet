using UnityEngine;
using Metagame.Core;

namespace Metagame.Gameplay
{
    /// <summary>
    /// 코드로 우주비행사 한 명을 조립한다. 프리미티브 + ConfigurableJoint 뿐이고
    /// 애니메이션 클립은 하나도 없다 — 걷기도 조인트 목표 자세로 만든다.
    ///
    /// 비율이 이 캐릭터의 전부다: 키 1.33m 에 헬멧 지름 0.62m — 약 2.1등신.
    /// 머리가 몸통보다 크기 때문에 위쪽이 무겁고, 그래서 균형 스프링이 지구 버전보다
    /// (질량 대비) 세다. 헬멧을 줄이면 귀엽지 않고, 키우면 자꾸 자빠진다.
    ///
    /// 서 있는 자세 기준: 발바닥 y=0. 모든 부위는 처음에 회전이 없다(identity).
    /// </summary>
    public static class RagdollBuilder
    {
        public const int PlayerLayer = 8;                 // 인형 전용 레이어 (지형 탐색에서 자기 몸을 뺀다)
        public static int WorldMask { get { return ~(1 << PlayerLayer); } }

        // ── 부위 치수 (m) ───────────────────────────────────────
        // 이 숫자들은 상상해서 정한 것이 아니라 <b>블렌더 모델 SF_Rabbit_Base 에서 직접 잰 것</b>이다
        // (Tools/모델 구조 찍어보기 → 본 월드 위치 × ModelScale 1.3585).
        // 물리 뼈대를 모델 비율에 맞춰야 메시를 늘이지 않고 그대로 씌울 수 있다 —
        // 반대로 하면(모델을 뼈대에 맞추면) 짧은 다리가 40% 늘어나 캐릭터가 다른 것이 된다.
        //
        // 토끼는 통통한 달걀 몸에 다리가 짧다: 부츠 0~0.14, 몸통 0.20~0.58,
        // 헬멧 0.51~1.15, 귀 1.02~1.33. 그래서 골반이 지구 버전(0.50)보다 훨씬 낮은 0.30 이다.
        public const float FootY = 0.068f, FootLen = 0.190f, FootW = 0.185f, FootH = 0.135f;
        public const float ShinD = 0.185f, ShinTop = 0.208f, ShinBot = 0.125f;
        public const float ThighD = 0.215f, ThighTop = 0.371f, ThighBot = 0.208f;
        public const float LegX = 0.141f;
        public const float HipsY = 0.300f;
        public const float ChestY = 0.490f;
        public const float HeadY = 0.829f;
        public const float HelmetD = 0.636f;              // 헬멧 지름 = 등신 계산의 기준
        /// <summary>귀 끝까지의 실루엣 높이. 등신 계산은 이 값을 쓴다.</summary>
        public const float SilhouetteHeight = 1.33f;
        /// <summary>물리 몸(헬멧 꼭대기)까지의 높이. 귀는 콜라이더가 없다.</summary>
        public const float TotalHeight = HeadY + HelmetD * 0.5f;   // 1.147m
        // 어깨는 모델의 0.204 보다 조금 바깥에 둔다 — 달걀 몸의 반폭이 0.24 라
        // 모델 값 그대로 두면 팔이 몸 안에 파묻힌다.
        public const float ShoulderY = 0.606f, ShoulderX = 0.255f;
        public const float UpperArmTop = 0.606f, UpperArmBot = 0.441f, ArmD = 0.125f;
        public const float LowerArmTop = 0.441f, LowerArmBot = 0.339f;
        public const float HandD = 0.150f, HandY = 0.268f;

        /// <summary>몇 등신인가 — 셀프테스트가 이 값이 2 근처인지 본다.</summary>
        public const float HeadCount = SilhouetteHeight / HelmetD;

        // ── 우주복 색 ───────────────────────────────────────────
        public static readonly Color SuitWhite = new Color(0.93f, 0.94f, 0.96f);
        public static readonly Color SuitGrey = new Color(0.74f, 0.77f, 0.82f);
        public static readonly Color Accent = new Color(0.96f, 0.50f, 0.16f);
        public static readonly Color Rig = new Color(0.28f, 0.30f, 0.36f);
        public static readonly Color VisorGlass = new Color(0.72f, 0.86f, 0.95f, 0.20f);
        public static readonly Color Skin = new Color(0.99f, 0.82f, 0.66f);

        /// <param name="primitiveLook">
        /// true 면 프리미티브로 얼굴·등짐까지 만든다 (블렌더 모델이 없을 때의 대체 룩).
        /// false 면 몸통 Renderer 를 끄고 <see cref="SpaceSuit"/> 메시를 씌운다 —
        /// <b>물리(강체·콜라이더·조인트)는 어느 쪽이든 완전히 같다.</b>
        /// </param>
        public static Doll Build(string name, Vector3 footPos, Quaternion facing, Color suit, Color accent,
                                 bool primitiveLook = true)
        {
            var doll = new Doll();

            var root = new GameObject(name);
            root.transform.position = Vector3.zero;
            doll.Root = root.transform;

            // ── 몸통 ────────────────────────────────────────────
            doll.Hips = Part(doll, Prim.Box("골반", Y(HipsY), new Vector3(0.34f, 0.20f, 0.34f), suit, 0.55f), 8.0f);
            doll.Chest = Part(doll, Prim.Box("가슴", Y(ChestY), new Vector3(0.38f, 0.22f, 0.36f), suit, 0.55f), 10.0f);

            // 헬멧은 투명한 유리 방울이다 — 안에 얼굴이 들어간다
            var helmet = Prim.Ball("헬멧", Y(HeadY), HelmetD, suit, 0.9f);
            helmet.GetComponent<Renderer>().sharedMaterial = Prim.Ghost(VisorGlass, 2.1f, 0f);
            doll.Head = Part(doll, helmet, 5.0f);

            doll.ChestJoint = Join(doll.Chest, doll.Hips, new Vector3(0f, -0.10f, 0f));
            JointTools.Limit(doll.ChestJoint, -30f, 30f, 28f, 24f);
            JointTools.SetDrive(doll.ChestJoint, 430f, 40f);

            doll.HeadJoint = Join(doll.Head, doll.Chest, new Vector3(0f, -0.24f, 0f));
            JointTools.Limit(doll.HeadJoint, -26f, 26f, 40f, 20f);
            JointTools.SetDrive(doll.HeadJoint, 300f, 28f);      // 헬멧이 무겁다 — 목이 세야 한다

            // ── 팔 ──────────────────────────────────────────────
            doll.Left = BuildArm(doll, true, suit, accent);
            doll.Right = BuildArm(doll, false, suit, accent);

            // ── 다리 ────────────────────────────────────────────
            doll.LeftLeg = BuildLeg(doll, true, suit);
            doll.RightLeg = BuildLeg(doll, false, suit);

            // ── 균형 조인트 ─────────────────────────────────────
            // 위치는 전부 자유, 회전만 세계 기준으로 붙잡는다.
            doll.Balance = doll.Hips.gameObject.AddComponent<ConfigurableJoint>();
            doll.Balance.connectedBody = null;
            doll.Balance.autoConfigureConnectedAnchor = false;
            doll.Balance.anchor = Vector3.zero;
            doll.Balance.connectedAnchor = doll.Hips.position;
            doll.Balance.xMotion = doll.Balance.yMotion = doll.Balance.zMotion = ConfigurableJointMotion.Free;
            doll.Balance.angularXMotion = ConfigurableJointMotion.Free;
            doll.Balance.angularYMotion = ConfigurableJointMotion.Free;
            doll.Balance.angularZMotion = ConfigurableJointMotion.Free;
            doll.Balance.axis = Vector3.right;
            doll.Balance.secondaryAxis = Vector3.up;
            doll.Balance.enablePreprocessing = false;
            doll.Balance.configuredInWorldSpace = false;
            JointTools.SetDrive(doll.Balance, Tuning.UprightSpring, Tuning.UprightDamp, Tuning.UprightMaxTorque);

            // ── 장식 ────────────────────────────────────────────
            if (primitiveLook)
            {
                Helmet(doll, suit);
                Torso(doll, suit, accent);
                Hips(doll, accent);
            }

            // ── 마무리 ──────────────────────────────────────────
            // 몸끼리는 충돌시키지 않는다. 팔다리가 서로 끼면 그 자리에서 굳어버린다.
            for (int i = 0; i < doll.Colliders.Count; i++)
                for (int k = i + 1; k < doll.Colliders.Count; k++)
                    Physics.IgnoreCollision(doll.Colliders[i], doll.Colliders[k], true);

            var all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++) all[i].gameObject.layer = PlayerLayer;

            doll.CaptureRestPose();                    // 아직 원점 기준 — 상대 오프셋만 쓸 것이라 괜찮다

            if (!primitiveLook)
            {
                // 프리미티브 몸을 숨기고 그 위에 블렌더 메시를 씌운다.
                // Renderer 만 끈다 — 콜라이더를 지우면 잡기도 서기도 전부 무너진다.
                //
                // 강체의 Renderer 만 끄면 안 된다. 팔 띠·무릎테 같은 장식은 강체가 아니라
                // 그 자식이라 그대로 남아, 모델 위에 주황 고리가 둥둥 떠 있게 된다.
                // (실제로 한 번 그렇게 나왔다.) 그래서 인형 아래 <b>모든</b> Renderer 를 끈다.
                var mine = root.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < mine.Length; i++) mine[i].enabled = false;
                // 인형이 아직 원점의 기준 자세로 서 있는 지금이 오프셋을 재기 좋은 유일한 순간이다
                doll.Suit = SpaceSuit.Attach(doll, root.transform);
            }

            doll.Teleport(footPos + Vector3.up * HipsY, facing);

            return doll;
        }

        static Vector3 Y(float y) { return new Vector3(0f, y, 0f); }

        // ── 헬멧 안팎 ───────────────────────────────────────────
        static void Helmet(Doll doll, Color suit)
        {
            var d = Prim.DecoRoot(doll.Head.transform);

            // 뒤통수 껍데기 — 앞은 트여 있어야 얼굴이 보인다
            var shell = Prim.FitBall(d, "헬멧 뒤판", new Vector3(0f, 0.01f, -0.15f), 0.60f, suit, 0.5f);
            shell.transform.localScale = new Vector3(0.60f, 0.60f, 0.36f);

            // 목테
            Prim.FitPipe(d, "목테", new Vector3(0f, -0.27f, 0f), 0.34f, 0.09f, SuitGrey, 0.3f);

            // 헬멧 리벳 — 방울 아랫부분(목 근처)을 한 바퀴 두른다.
            // 방울은 ZWrite 를 끈 반투명이라 "안에 넣은 것은 전부 그대로 보인다".
            // 그래서 납작한 원반으로 테를 만들면 챙 모자가 된다 — 실제로 한 번 그렇게 됐다.
            // 링은 반드시 알갱이를 둘러 만들고, 얼굴을 가리지 않게 눈높이 아래에 둔다.
            const float ringY = -0.20f;
            float ringR = Mathf.Sqrt(Mathf.Max(0f, (HelmetD * 0.5f) * (HelmetD * 0.5f) - ringY * ringY));
            for (int i = 0; i < 16; i++)
            {
                float a = i / 16f * Mathf.PI * 2f;
                Prim.FitBall(d, "헬멧 리벳",
                             new Vector3(Mathf.Cos(a) * ringR, ringY, Mathf.Sin(a) * ringR),
                             0.052f, SuitGrey, 0.3f);
            }

            // 정수리 환기구
            Prim.FitPipe(d, "환기구", new Vector3(0f, 0.285f, -0.02f), 0.13f, 0.07f, SuitGrey, 0.3f);

            // 안테나
            Prim.FitPipe(d, "안테나", new Vector3(0.16f, 0.34f, -0.10f), 0.026f, 0.20f, Rig, 0.2f);
            Prim.FitBall(d, "안테나 등", new Vector3(0.16f, 0.45f, -0.10f), 0.055f, new Color(0.95f, 0.30f, 0.28f), 0.8f);

            // 얼굴 (헬멧 안). 아래 장식들은 전부 이 공의 표면(반지름 0.15) 밖으로 나와야 보인다 —
            // 안에 넣으면 얼굴에 파묻혀 사라진다 (입이 그렇게 한 번 사라졌다).
            Prim.FitBall(d, "얼굴", new Vector3(0f, -0.03f, 0.03f), 0.30f, Skin, 1f);

            Color eyeWhite = new Color(0.99f, 0.99f, 1.00f);
            Color pupil = new Color(0.09f, 0.10f, 0.14f);
            for (int i = 0; i < 2; i++)
            {
                float x = i == 0 ? -0.058f : 0.058f;
                var eye = Prim.FitBall(d, "눈", new Vector3(x, 0.005f, 0.132f), 0.086f, eyeWhite, 0.2f);
                eye.transform.localScale = new Vector3(0.075f, 0.098f, 0.070f);
                Prim.FitBall(d, "눈동자", new Vector3(x, -0.004f, 0.157f), 0.052f, pupil, 0.1f);
                Prim.FitBall(d, "눈빛", new Vector3(x + 0.016f, 0.022f, 0.166f), 0.021f, eyeWhite, 0.1f);
                Prim.FitBall(d, "볼", new Vector3(x * 1.85f, -0.058f, 0.115f), 0.062f,
                             new Color(0.98f, 0.62f, 0.60f), 0.9f);
            }

            // 웃는 입 — 짧은 막대 세 개로 살짝 굽은 선을 만든다
            Color lip = new Color(0.60f, 0.29f, 0.30f);
            Prim.FitBox(d, "입", new Vector3(0f, -0.088f, 0.178f), new Vector3(0.040f, 0.016f, 0.014f), lip, 0.6f);
            for (int i = -1; i <= 1; i += 2)
            {
                var corner = Prim.FitBox(d, "입꼬리", new Vector3(i * 0.030f, -0.079f, 0.174f),
                                         new Vector3(0.030f, 0.016f, 0.014f), lip, 0.6f);
                corner.transform.localRotation = Quaternion.Euler(0f, 0f, i * 26f);
            }
        }

        // ── 가슴 · 등짐 ─────────────────────────────────────────
        static void Torso(Doll doll, Color suit, Color accent)
        {
            var d = Prim.DecoRoot(doll.Chest.transform);

            // 생명유지장치 (PLSS)
            Prim.FitBox(d, "등짐", new Vector3(0f, 0.01f, -0.215f), new Vector3(0.34f, 0.30f, 0.16f), Rig, 0.25f);
            Prim.FitPipe(d, "산소통", new Vector3(-0.10f, 0.02f, -0.30f), 0.085f, 0.26f, SuitGrey, 0.2f);
            Prim.FitPipe(d, "산소통", new Vector3(0.10f, 0.02f, -0.30f), 0.085f, 0.26f, SuitGrey, 0.2f);
            Prim.FitBox(d, "등짐 띠", new Vector3(0f, -0.11f, -0.216f), new Vector3(0.35f, 0.05f, 0.17f), accent, 0.3f);

            // 가슴 제어판
            Prim.FitBox(d, "제어판", new Vector3(0f, -0.02f, 0.157f), new Vector3(0.20f, 0.13f, 0.03f), Rig, 0.2f);
            Prim.FitBall(d, "표시등", new Vector3(-0.055f, 0.015f, 0.176f), 0.030f, new Color(0.35f, 0.95f, 0.45f), 0.8f);
            Prim.FitBall(d, "표시등", new Vector3(0.000f, 0.015f, 0.176f), 0.030f, new Color(0.98f, 0.82f, 0.30f), 0.8f);
            Prim.FitBall(d, "표시등", new Vector3(0.055f, 0.015f, 0.176f), 0.030f, new Color(0.95f, 0.36f, 0.34f), 0.8f);

            // 어깨 패드
            Prim.FitBall(d, "어깨", new Vector3(-0.21f, 0.09f, 0f), 0.18f, suit, 0.6f);
            Prim.FitBall(d, "어깨", new Vector3(0.21f, 0.09f, 0f), 0.18f, suit, 0.6f);
        }

        static void Hips(Doll doll, Color accent)
        {
            var d = Prim.DecoRoot(doll.Hips.transform);
            Prim.FitBox(d, "허리띠", new Vector3(0f, 0.055f, 0f), new Vector3(0.355f, 0.06f, 0.275f), accent, 0.3f);
            Prim.FitBox(d, "공구 주머니", new Vector3(0.20f, -0.02f, 0.02f), new Vector3(0.08f, 0.12f, 0.14f), Rig, 0.3f);
        }

        // ── 팔 ──────────────────────────────────────────────────
        static Arm BuildArm(Doll doll, bool left, Color suit, Color accent)
        {
            var a = new Arm();
            a.IsLeft = left;
            float sx = left ? -ShoulderX : ShoulderX;
            string side = left ? "왼" : "오른";

            float upperY = (UpperArmTop + UpperArmBot) * 0.5f;
            float lowerY = (LowerArmTop + LowerArmBot) * 0.5f;

            a.Upper = Part(doll, Prim.Capsule(side + "위팔", new Vector3(sx, upperY, 0f),
                           ArmD, UpperArmTop - UpperArmBot + ArmD, suit, 0.6f), 0.9f);
            a.Lower = Part(doll, Prim.Capsule(side + "아래팔", new Vector3(sx, lowerY, 0f),
                           ArmD * 0.94f, LowerArmTop - LowerArmBot + ArmD, suit, 0.6f), 0.6f);

            var handGo = Prim.Ball(side + "장갑", new Vector3(sx, HandY, 0f), HandD, SuitGrey, 0.8f);
            var handBody = Part(doll, handGo, 0.45f);

            // 팔뚝의 오렌지 띠 — 우주복은 이 띠 하나로 읽힌다
            var ud = Prim.DecoRoot(a.Upper.transform);
            Prim.FitPipe(ud, "팔 띠", new Vector3(0f, -0.055f, 0f), ArmD * 1.06f, 0.045f, accent, 0.3f);
            var ld = Prim.DecoRoot(a.Lower.transform);
            Prim.FitPipe(ld, "손목테", new Vector3(0f, -0.075f, 0f), ArmD * 1.02f, 0.040f, accent, 0.3f);

            a.Shoulder = Join(a.Upper, doll.Chest, new Vector3(0f, UpperArmTop - upperY, 0f));
            JointTools.Limit(a.Shoulder, -150f, 110f, 88f, 88f);
            JointTools.SetDrive(a.Shoulder, 26f, 4f);           // 평소엔 흐물흐물 늘어져 있다

            a.Elbow = Join(a.Lower, a.Upper, new Vector3(0f, LowerArmTop - lowerY, 0f));
            JointTools.Limit(a.Elbow, -145f, 4f, 12f, 12f);
            JointTools.SetDrive(a.Elbow, 18f, 3f);

            var wrist = Join(handBody, a.Lower, new Vector3(0f, HandD * 0.5f, 0f));
            JointTools.Limit(wrist, -50f, 50f, 40f, 40f);
            JointTools.SetDrive(wrist, 14f, 2.5f);

            a.Hand = handGo.AddComponent<Hand>();
            a.Hand.Body = handBody;
            a.Hand.IsLeft = left;

            var anchor = new GameObject(side + "어깨점");
            anchor.transform.SetParent(doll.Chest.transform, false);
            // 가슴은 비균등 스케일이 걸린 큐브다 — 미터 오프셋을 그대로 넣으면 안 된다
            anchor.transform.localPosition =
                Prim.LocalMeters(doll.Chest.transform, new Vector3(sx, ShoulderY - ChestY, 0f));
            a.ShoulderAnchor = anchor.transform;

            return a;
        }

        // ── 다리 ────────────────────────────────────────────────
        static Leg BuildLeg(Doll doll, bool left, Color suit)
        {
            var l = new Leg();
            l.IsLeft = left;
            float lx = left ? -LegX : LegX;
            string side = left ? "왼" : "오른";

            float thighY = (ThighTop + ThighBot) * 0.5f;
            float shinY = (ShinTop + ShinBot) * 0.5f;

            l.Thigh = Part(doll, Prim.Capsule(side + "허벅지", new Vector3(lx, thighY, 0f),
                           ThighD, ThighTop - ThighBot + ThighD * 0.6f, suit, 0.6f), 2.0f);
            l.Shin = Part(doll, Prim.Capsule(side + "정강이", new Vector3(lx, shinY, 0f),
                          ShinD, ShinTop - ShinBot + ShinD * 0.6f, suit, 0.6f), 1.1f);
            l.Foot = Part(doll, Prim.Box(side + "부츠", new Vector3(lx, FootY, 0.035f),
                          new Vector3(FootW, FootH, FootLen), Rig, 0.25f), 0.9f);

            var kd = Prim.DecoRoot(l.Shin.transform);
            Prim.FitPipe(kd, "무릎테", new Vector3(0f, 0.085f, 0f), ShinD * 1.08f, 0.045f, SuitGrey, 0.3f);

            l.Hip = Join(l.Thigh, doll.Hips, new Vector3(0f, ThighTop - thighY, 0f));
            JointTools.Limit(l.Hip, -84f, 46f, 26f, 22f);
            JointTools.SetDrive(l.Hip, 300f, 26f);

            l.Knee = Join(l.Shin, l.Thigh, new Vector3(0f, ShinTop - shinY, 0f));
            JointTools.Limit(l.Knee, -3f, 132f, 6f, 6f);
            JointTools.SetDrive(l.Knee, 240f, 18f);

            var footCol = l.Foot.GetComponent<Collider>();
            if (footCol != null) footCol.material = Prim.FootPhysics;

            l.Ankle = Join(l.Foot, l.Shin, new Vector3(0f, FootH * 0.5f, -0.035f));
            JointTools.Limit(l.Ankle, -32f, 42f, 14f, 14f);
            JointTools.SetDrive(l.Ankle, 70f, 7f);

            return l;
        }

        static Rigidbody Part(Doll doll, GameObject go, float mass)
        {
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.linearDamping = 0.05f;               // 진공이다 — 공기 저항으로 감속하지 않는다
            rb.angularDamping = 0.7f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.maxAngularVelocity = 22f;
            rb.maxDepenetrationVelocity = 2f;      // 끼었다 풀릴 때 로켓처럼 날아가지 않게

            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                col.material = Prim.SuitPhysics;
                doll.Colliders.Add(col);
            }

            go.transform.SetParent(doll.Root, true);
            doll.Bodies.Add(rb);
            return rb;
        }

        static ConfigurableJoint Join(Rigidbody child, Rigidbody parent, Vector3 anchorLocal)
        {
            var j = child.gameObject.AddComponent<ConfigurableJoint>();
            j.connectedBody = parent;
            j.autoConfigureConnectedAnchor = false;
            j.anchor = anchorLocal;
            j.connectedAnchor = parent.transform.InverseTransformPoint(child.transform.TransformPoint(anchorLocal));

            j.xMotion = ConfigurableJointMotion.Locked;
            j.yMotion = ConfigurableJointMotion.Locked;
            j.zMotion = ConfigurableJointMotion.Locked;

            j.axis = Vector3.right;
            j.secondaryAxis = Vector3.up;

            j.enablePreprocessing = false;                       // 랙돌에서 preprocessing 은 폭주의 원인
            j.projectionMode = JointProjectionMode.PositionAndRotation;
            j.projectionDistance = 0.03f;
            j.projectionAngle = 12f;
            j.configuredInWorldSpace = false;
            j.targetRotation = Quaternion.identity;
            return j;
        }
    }
}
