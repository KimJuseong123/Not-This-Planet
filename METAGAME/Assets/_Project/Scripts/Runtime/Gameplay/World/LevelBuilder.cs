using System.Collections.Generic;
using UnityEngine;
using Metagame.Core;

namespace Metagame.Gameplay
{
    /// <summary>
    /// 소행성 표면의 코스를 통째로 코드로 세운다. +X 방향으로 나아가는 한 줄이다.
    ///
    ///   0 착륙장   저중력을 몸으로 재고, 화물을 들고 던져 본다
    ///   1 크레이터 둥실 뛰어 크레이터 두 개를 건넌다
    ///   2 절벽     뛰어서 핸드레일을 잡고 기어오른다 (점프만으로는 못 넘는 높이)
    ///   3 케이블   20m 짜리 균열 위의 계류 케이블 두 줄
    ///   4 화물 널  기울어지는 널빤지
    ///   5 화물판   오가는 자기부상 판을 타고 건넌다
    ///   6 에어록   배터리 셀을 들고 와 소켓에 꽂으면 해치가 열린다
    ///   7 골       회전 안테나를 피해 구조 로켓으로
    ///
    /// 간격 설계의 기준은 하나다 — 저중력 점프의 사거리(약 11m).
    /// 케이블 균열(20m)과 화물판 구간(14m)은 그보다 넓어야 퍼즐이 성립한다.
    /// 셀프테스트가 이 부등식을 지킨다.
    /// </summary>
    public class LevelBuilder : MonoBehaviour
    {
        public static readonly Color Regolith = new Color(0.42f, 0.41f, 0.40f);
        public static readonly Color RegolithLit = new Color(0.53f, 0.52f, 0.50f);
        public static readonly Color Rock = new Color(0.33f, 0.32f, 0.32f);
        public static readonly Color Metal = new Color(0.68f, 0.72f, 0.78f);
        public static readonly Color Deck = new Color(0.56f, 0.60f, 0.66f);
        public static readonly Color Accent = new Color(0.95f, 0.55f, 0.16f);
        public static readonly Color Cable = new Color(0.80f, 0.78f, 0.72f);

        public Transform Terrain { get; private set; }
        public Transform Props { get; private set; }
        public readonly List<Rigidbody> Cargo = new List<Rigidbody>();
        public readonly List<Rigidbody> CableA = new List<Rigidbody>();
        public readonly List<Rigidbody> CableB = new List<Rigidbody>();
        public SlidingDoor Hatch { get; private set; }
        public PowerSocket Socket { get; private set; }
        public Rigidbody BatteryCell { get; private set; }
        public MovingPlatform Platform { get; private set; }
        public Rigidbody Seesaw { get; private set; }
        public Goal Rocket { get; private set; }
        /// <summary>출발 지점에 놓인 캡슐 (모델이 없으면 null).</summary>
        public GameObject Lander { get; private set; }

        AuthoredMapSettings authoredMap;
        void Awake() => authoredMap = FindAnyObjectByType<AuthoredMapSettings>();
        public Vector3 SpawnFoot => authoredMap != null && authoredMap.playerSpawn != null
            ? authoredMap.playerSpawn.position : Vector3.zero;

        /// <summary>저중력 점프의 최고 높이 — 코스 설계가 전부 이 값에 매여 있다.</summary>
        public static float JumpHeight
        {
            get { return Tuning.JumpSpeed * Tuning.JumpSpeed / (2f * Mathf.Abs(Tuning.Gravity)); }
        }

        /// <summary>달리며 뛰었을 때의 사거리. 이보다 넓은 구덩이는 다른 수단이 있어야 한다.</summary>
        public static float JumpRange
        {
            get { return Tuning.RunSpeed * (2f * Tuning.JumpSpeed / Mathf.Abs(Tuning.Gravity)); }
        }

        public void Build()
        {
            Random.InitState(20260825);       // 돌 배치가 매번 같아야 스크린샷 비교가 된다
            Terrain = new GameObject("~지형").transform;
            Terrain.SetParent(transform, false);
            Props = new GameObject("~소품").transform;
            Props.SetParent(transform, false);

            Abyss();
            Stage0Landing();
            Stage1Craters();
            Stage2Cliff();
            Stage3Cables();
            Stage4Plank();
            Stage5CargoPad();
            Stage6Airlock();
            Stage7Rocket();
            Checkpoints();
        }

        /// <summary>
        /// 구덩이 저 아래의 바닥. 콜라이더가 없어 실제로는 통과한다 —
        /// 진공에는 안개가 없어서, 이게 없으면 모든 구덩이가 그냥 "검은 화면" 으로 보인다.
        /// </summary>
        void Abyss()
        {
            Prim.Decor(Prim.Box("균열 바닥", new Vector3(64f, -38f, 0f), new Vector3(180f, 4f, 64f),
                                new Color(0.13f, 0.13f, 0.15f), 0.2f, Terrain));
            for (int i = 0; i < 9; i++)
            {
                float x = 4f + i * 17f;
                Prim.Decor(Prim.Ball("바닥 바위", new Vector3(x, -35.6f, Random.Range(-16f, 16f)),
                                     Random.Range(4f, 9f), new Color(0.17f, 0.17f, 0.19f), 0.4f, Terrain));
            }
        }

        // ── 도구 ────────────────────────────────────────────────
        GameObject Slab(string name, float xMin, float xMax, float top, float zHalf,
                        Color color, float depth = 9f)
        {
            float cx = (xMin + xMax) * 0.5f;
            var go = Prim.Box(name, new Vector3(cx, top - depth * 0.5f, 0f),
                              new Vector3(xMax - xMin, depth, zHalf * 2f), color, 0.25f, Terrain);
            go.GetComponent<Collider>().material = Prim.GroundPhysics;
            return go;
        }

        GameObject Block(string name, Vector3 center, Vector3 size, Color color)
        {
            var go = Prim.Box(name, center, size, color, 0.25f, Terrain);
            go.GetComponent<Collider>().material = Prim.GroundPhysics;
            return go;
        }

        Rigidbody Crate(string name, Vector3 center, Vector3 size, float mass, Color color)
        {
            var go = Prim.Box(name, center, size, color, 0.35f, Props);
            go.GetComponent<Collider>().material = Prim.GroundPhysics;

            var d = Prim.DecoRoot(go.transform);
            Prim.FitBox(d, "화물 띠", new Vector3(0f, 0f, size.z * 0.51f),
                        new Vector3(size.x * 1.01f, size.y * 0.16f, 0.02f), Accent, 0.2f);

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.linearDamping = 0.05f;              // 진공 — 저절로 멈추지 않는다
            rb.angularDamping = 0.25f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.maxDepenetrationVelocity = 2f;

            Grabbable.Mark(go, name);
            Cargo.Add(rb);
            return rb;
        }

        Rigidbody Tank(string name, Vector3 center, float diameter, float mass, Color color)
        {
            var go = Prim.Ball(name, center, diameter, color, 0.55f, Props);
            go.GetComponent<Collider>().material = Prim.GroundPhysics;

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.linearDamping = 0.04f;
            rb.angularDamping = 0.12f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            Grabbable.Mark(go, name);
            Cargo.Add(rb);
            return rb;
        }

        /// <summary>주워 던질 수 있는 돌. 굴러다니는 것이 있어야 저중력이 눈에 보인다.</summary>
        void Boulder(Vector3 center, float size, bool loose)
        {
            var go = Prim.Ball(loose ? "돌덩이" : "바위", center, size, Rock, 0.5f, loose ? Props : Terrain);
            go.transform.localScale = new Vector3(size, size * Random.Range(0.62f, 0.9f), size * Random.Range(0.8f, 1.1f));
            go.GetComponent<Collider>().material = Prim.GroundPhysics;

            if (!loose) return;

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = size * size * size * 30f;
            rb.linearDamping = 0.05f;
            rb.angularDamping = 0.2f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            Grabbable.Mark(go, "돌덩이");
            Cargo.Add(rb);
        }

        /// <summary>크레이터 자국 — 테두리 돌을 둥글게 깐다.</summary>
        void Crater(Vector3 center, float radius, int rocks = 11)
        {
            for (int i = 0; i < rocks; i++)
            {
                float a = i / (float)rocks * Mathf.PI * 2f;
                Vector3 p = center + new Vector3(Mathf.Cos(a) * radius, 0.06f, Mathf.Sin(a) * radius);
                var go = Prim.Decor(Prim.Ball("크레이터 테", p, radius * 0.30f, RegolithLit, 0.4f, Terrain));
                go.transform.localScale = new Vector3(radius * 0.34f, radius * 0.12f, radius * 0.30f);
            }
            var floor = Prim.Decor(Prim.Ball("크레이터 바닥", center + Vector3.up * 0.02f, radius * 1.7f,
                                             new Color(0.35f, 0.33f, 0.31f), 0.4f, Terrain));
            floor.transform.localScale = new Vector3(radius * 1.8f, 0.06f, radius * 1.8f);
        }

        void Sign(Vector3 footPos, string text, float yaw = -90f)
        {
            var post = Prim.Decor(Prim.Pipe("표지 기둥", footPos + Vector3.up * 0.55f, 0.07f, 1.1f,
                                            new Color(0.44f, 0.46f, 0.52f), 0.2f, Props));
            var board = Prim.Decor(Prim.Box("표지 " + text, footPos + Vector3.up * 1.25f,
                                            new Vector3(1.3f, 0.5f, 0.06f),
                                            new Color(0.20f, 0.24f, 0.30f), 0.2f, Props));
            board.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            post.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            var d = Prim.DecoRoot(board.transform);
            Prim.FitBox(d, "표지 테", new Vector3(0f, 0f, -0.04f), new Vector3(1.38f, 0.58f, 0.02f), Accent, 0.2f);

            var label = board.AddComponent<WorldLabel>();
            label.Text = text;
        }

        // ── 0. 착륙장 ───────────────────────────────────────────
        void Stage0Landing()
        {
            Slab("착륙장", -12f, 14f, 0f, 8f, Regolith);

            // 타고 온 착륙선 — 여기가 출발점이라는 표시. 골의 구조선과 같은 형식의 캡슐이다.
            Vector3 landerFoot = new Vector3(-7.0f, 0f, 3.4f);
            Lander = ShipModel.Spawn(Terrain, landerFoot, -35f, "타고 온 캡슐");
            if (Lander == null)
            {
                Prim.Decor(Prim.Pipe("착륙선 동체", landerFoot + new Vector3(0f, 1.5f, 0f), 2.4f, 2.2f, Metal, 0.2f, Terrain));
                Prim.Decor(Prim.Ball("착륙선 덮개", landerFoot + new Vector3(0f, 2.7f, 0f), 2.3f, Metal, 0.5f, Terrain));
            }
            var lamp = Prim.Decor(Prim.Ball("착륙 유도등", landerFoot + new Vector3(1.9f, 0.14f, -1.6f),
                                            0.30f, Color.white, 0.5f, Terrain));
            lamp.GetComponent<Renderer>().sharedMaterial = Prim.Ghost(new Color(0.40f, 0.90f, 1.00f, 0.85f), 1.8f, 1f);

            Crater(new Vector3(2.5f, 0f, -5.2f), 2.6f);
            Crater(new Vector3(-2.0f, 0f, 5.8f), 1.9f, 9);

            Sign(new Vector3(3.2f, 0f, -2.6f), "WASD 걷기 · Space 점프 — 여기선 높이 뜬다", 200f);
            Sign(new Vector3(7.0f, 0f, 2.8f), "마우스 좌/우 = 왼팔/오른팔 뻗기", 160f);
            Sign(new Vector3(11.2f, 0f, -2.4f), "도구는 양손으로 든다. Q 를 꾹 누르면 충전해서 던진다", 205f);

            Crate("화물 상자", new Vector3(7.6f, 0.42f, 1.7f), new Vector3(0.8f, 0.8f, 0.8f), 18f, Deck);
            Crate("화물 상자", new Vector3(9.0f, 0.42f, -1.5f), new Vector3(0.8f, 0.8f, 0.8f), 18f, Deck);
            Crate("무거운 궤짝", new Vector3(5.6f, 0.62f, -3.6f), new Vector3(1.2f, 1.2f, 1.2f), 55f, Accent);

            Tank("산소통", new Vector3(3.8f, 0.55f, 4.2f), 1.0f, 9f, new Color(0.86f, 0.90f, 0.94f));
            Tank("산소통", new Vector3(2.4f, 0.55f, 2.9f), 1.0f, 9f, new Color(0.86f, 0.90f, 0.94f));

            Boulder(new Vector3(-3.5f, 0.30f, -6.4f), 1.4f, false);
            Boulder(new Vector3(11.5f, 0.24f, 5.6f), 1.1f, false);
            Boulder(new Vector3(6.2f, 0.22f, 5.0f), 0.55f, true);
            Boulder(new Vector3(0.8f, 0.20f, -3.4f), 0.48f, true);
        }

        // ── 1. 크레이터 징검다리 ────────────────────────────────
        void Stage1Craters()
        {
            Block("크레이터 봉우리", new Vector3(20.5f, 0.4f, 0f), new Vector3(3.0f, 0.8f, 4.6f), Rock);
            Block("크레이터 봉우리", new Vector3(27.5f, 0.7f, 0f), new Vector3(3.0f, 1.4f, 4.6f), Rock);
            Slab("고원", 33f, 41f, 1.6f, 7f, Regolith);

            Boulder(new Vector3(20.5f, 1.05f, 1.8f), 0.6f, true);
            Sign(new Vector3(12.2f, 0f, 1.8f), "달리기 Shift — 저중력에서는 아주 멀리 난다", 200f);
            Sign(new Vector3(35.0f, 1.6f, 2.9f), "이 절벽은 뛰어서는 못 넘는다", 165f);
        }

        // ── 2. 절벽 · 핸드레일 ──────────────────────────────────
        void Stage2Cliff()
        {
            Slab("절벽 위", 41f, 58f, 4.7f, 7f, Regolith, 12f);

            // 절벽 앞면에 튀어나온 핸드레일 — 뛰어올라 이걸 잡는다
            var bar = Prim.Pipe("핸드레일", new Vector3(40.83f, 4.45f, 0f), 0.30f, 11.4f, Metal, 0.15f, Terrain);
            bar.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            bar.GetComponent<Collider>().material = Prim.GroundPhysics;
            Grabbable.Mark(bar, "핸드레일", true, 1f, 0.6f);

            Prim.Decor(Prim.Box("레일 받침", new Vector3(40.94f, 4.05f, -3.4f), new Vector3(0.32f, 0.85f, 0.20f), Metal, 0.15f, Terrain));
            Prim.Decor(Prim.Box("레일 받침", new Vector3(40.94f, 4.05f, 3.4f), new Vector3(0.32f, 0.85f, 0.20f), Metal, 0.15f, Terrain));

            // 절벽 면의 경고 등
            for (int i = -1; i <= 1; i += 2)
            {
                var l = Prim.Decor(Prim.Ball("경고등", new Vector3(40.9f, 3.2f, i * 2.2f), 0.24f, Color.white, 0.5f, Terrain));
                l.GetComponent<Renderer>().sharedMaterial = Prim.Ghost(new Color(0.98f, 0.62f, 0.20f, 0.85f), 1.8f, 1f);
            }

            Sign(new Vector3(37.6f, 1.6f, -2.7f), "Ctrl 로 팔을 위로 · 뛰면서 레일을 잡아라", 200f);
            Sign(new Vector3(44.0f, 4.7f, 3.0f), "잡은 채로 W = 기어오르기", 160f);

            Boulder(new Vector3(48f, 5.0f, -4.6f), 1.6f, false);
            Boulder(new Vector3(53f, 4.95f, 4.2f), 1.2f, false);
            Crater(new Vector3(50f, 4.7f, 0f), 3.0f);
        }

        // ── 3. 계류 케이블 ──────────────────────────────────────
        void Stage3Cables()
        {
            // 균열: x 58 ~ 78 (20m). 달려 뛰어도 11m 밖에 못 간다.
            Prim.Decor(Prim.Box("들보", new Vector3(68f, 10.5f, 0f), new Vector3(23f, 0.45f, 0.55f), Metal, 0.2f, Terrain));
            Prim.Decor(Prim.Pipe("들보 기둥", new Vector3(57.2f, 7.6f, 0f), 0.45f, 6.0f, Metal, 0.2f, Terrain));
            Prim.Decor(Prim.Pipe("들보 기둥", new Vector3(78.8f, 7.6f, 0f), 0.45f, 6.0f, Metal, 0.2f, Terrain));

            CableA.AddRange(RopeBuilder.Build(Terrain, new Vector3(63f, 10.25f, 0f), 9, 0.52f, 0.19f,
                                              Cable, "케이블A", 1.0f));
            CableB.AddRange(RopeBuilder.Build(Terrain, new Vector3(71f, 10.25f, 0f), 9, 0.52f, 0.19f,
                                              Cable, "케이블B", 1.0f));

            Block("쉼터 바위", new Vector3(76.4f, 4.1f, 0f), new Vector3(2.6f, 0.6f, 4.4f), Rock);
            Slab("건너편", 78f, 92f, 4.7f, 7f, Regolith, 12f);

            Sign(new Vector3(55.4f, 4.7f, -2.8f), "뛰어서 케이블을 잡아라 — 놓쳐도 천천히 떨어진다", 200f);
        }

        // ── 4. 화물 널빤지 (시소) ───────────────────────────────
        void Stage4Plank()
        {
            Block("널 받침", new Vector3(85f, 4.86f, 0f), new Vector3(0.5f, 0.32f, 2.2f), Metal);

            var plankGo = Prim.Box("화물 널빤지", new Vector3(85f, 5.16f, 0f),
                                   new Vector3(6.2f, 0.28f, 2.5f), Deck, 0.25f, Props);
            plankGo.GetComponent<Collider>().material = Prim.GroundPhysics;

            var d = Prim.DecoRoot(plankGo.transform);
            Prim.FitBox(d, "널 줄무늬", new Vector3(-2.4f, 0.16f, 0f), new Vector3(0.5f, 0.03f, 2.4f), Accent, 0.2f);
            Prim.FitBox(d, "널 줄무늬", new Vector3(2.4f, 0.16f, 0f), new Vector3(0.5f, 0.03f, 2.4f), Accent, 0.2f);

            var rb = plankGo.AddComponent<Rigidbody>();
            rb.mass = 26f;                          // 인형(36kg)보다 가벼워야 밟는 쪽으로 기운다
            rb.linearDamping = 0.15f;
            rb.angularDamping = 0.4f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            var hinge = plankGo.AddComponent<HingeJoint>();
            hinge.autoConfigureConnectedAnchor = false;
            hinge.connectedBody = null;
            hinge.anchor = Vector3.zero;
            hinge.connectedAnchor = new Vector3(85f, 5.16f, 0f);
            hinge.axis = new Vector3(0f, 0f, 1f);
            hinge.useLimits = true;
            var lim = hinge.limits; lim.min = -20f; lim.max = 20f; hinge.limits = lim;

            Grabbable.Mark(plankGo, "화물 널빤지");
            Seesaw = rb;

            Sign(new Vector3(81.5f, 4.7f, 2.9f), "널빤지는 밟는 쪽으로 기운다", 160f);
        }

        // ── 5. 자기부상 화물판 ──────────────────────────────────
        void Stage5CargoPad()
        {
            Platform = MovingPlatform.Create(Terrain,
                new Vector3(95.5f, 4.45f, 0f), new Vector3(102.5f, 4.45f, 0f),
                new Vector3(4.2f, 0.5f, 4.4f), Deck, 9f, 0f);

            Slab("에어록 앞", 106f, 120.2f, 4.7f, 7f, Regolith, 12f);
            Sign(new Vector3(93.2f, 4.7f, -2.9f), "화물판이 올 때까지 기다린다", 200f);
        }

        // ── 6. 에어록 · 배터리 셀 ───────────────────────────────
        void Stage6Airlock()
        {
            Slab("에어록 너머", 120.2f, 142f, 4.7f, 7f, Regolith, 12f);   // 맞대기만 한다 (겹치면 z-파이팅)

            Block("에어록 벽", new Vector3(120.5f, 6.9f, -4.0f), new Vector3(0.8f, 4.8f, 4.0f), Metal);
            Block("에어록 벽", new Vector3(120.5f, 6.9f, 4.0f), new Vector3(0.8f, 4.8f, 4.0f), Metal);
            Block("에어록 상인방", new Vector3(120.5f, 8.6f, 0f), new Vector3(0.8f, 1.4f, 4.0f), Metal);

            Hatch = SlidingDoor.Create(Terrain, new Vector3(120.5f, 6.1f, 0f),
                                       new Vector3(0.7f, 3.4f, 3.9f), new Vector3(0f, -3.6f, 0f));

            Socket = PowerSocket.Create(Props, new Vector3(116.5f, 4.7f, 2.6f), Hatch);
            BatteryCell = Socket.CreateCell(Props, new Vector3(109.4f, 5.15f, -1.9f));

            Sign(new Vector3(111.5f, 4.7f, 1.2f), "배터리 셀을 들고 와 소켓에 꽂아라", 200f);
            Sign(new Vector3(117.6f, 4.7f, -2.6f), "가까이 가져가면 알아서 빨려 들어간다", 200f);

            Boulder(new Vector3(113f, 4.95f, -5.0f), 1.3f, false);
        }

        // ── 7. 골 ───────────────────────────────────────────────
        void Stage7Rocket()
        {
            Spinner.Create(Terrain, new Vector3(128f, 5.15f, 0f), new Vector3(8.0f, 0.4f, 0.5f), Accent, 45f);
            Prim.Decor(Prim.Pipe("안테나 기둥", new Vector3(128f, 4.95f, 0f), 0.5f, 0.5f, Metal, 0.2f, Terrain));

            Crater(new Vector3(132f, 4.7f, 3.6f), 2.2f);
            Rocket = Goal.Create(Terrain, new Vector3(136.5f, 4.7f, 0.6f), 120f);

            Sign(new Vector3(124.5f, 4.7f, 2.8f), "회전 안테나에 맞으면 멀리 날아간다", 160f);
        }

        // ── 체크포인트 ──────────────────────────────────────────
        void Checkpoints()
        {
            var cp = new GameObject("~체크포인트").transform;
            cp.SetParent(transform, false);

            Checkpoint.Create(cp, 0, "착륙장", new Vector3(0f, 0f, 0f));
            Checkpoint.Create(cp, 1, "고원", new Vector3(35f, 1.6f, 0f));
            Checkpoint.Create(cp, 2, "절벽 위", new Vector3(43.5f, 4.7f, 0f));
            Checkpoint.Create(cp, 3, "균열 건너", new Vector3(80f, 4.7f, 0f));
            Checkpoint.Create(cp, 4, "화물판 앞", new Vector3(90f, 4.7f, 0f));
            Checkpoint.Create(cp, 5, "에어록 앞", new Vector3(108.5f, 4.7f, 0f));
            Checkpoint.Create(cp, 6, "에어록 통과", new Vector3(123f, 4.7f, 0f));
        }
    }
}
