using System.Collections.Generic;
using UnityEngine;

namespace SpaceFlop
{
    /// <summary>지나가면 부활 지점이 여기로 옮겨진다.</summary>
    public class Checkpoint : MonoBehaviour
    {
        public static readonly List<Checkpoint> All = new List<Checkpoint>();

        public int Index;
        public string Title = "";
        public Vector3 SpawnFoot;
        public bool Reached { get; private set; }

        Renderer glow;

        void OnEnable() { All.Add(this); }
        void OnDisable() { All.Remove(this); }

        public static Checkpoint Create(Transform parent, int index, string title, Vector3 footPos)
        {
            var go = new GameObject("표지 " + index + " " + title);
            go.transform.SetParent(parent, false);
            go.transform.position = footPos;

            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(3.4f, 3.0f, 5.0f);
            box.center = new Vector3(0f, 1.5f, 0f);

            var cp = go.AddComponent<Checkpoint>();
            cp.Index = index;
            cp.Title = title;
            cp.SpawnFoot = footPos;

            // 착륙 표지등 — 도달하면 초록으로 바뀐다
            Prim.Decor(Prim.Pipe("표지 기둥", footPos + new Vector3(0f, 0.55f, 2.0f), 0.10f, 1.1f,
                                 new Color(0.40f, 0.42f, 0.46f), 0.2f, go.transform));
            var lamp = Prim.Decor(Prim.Ball("표지등", footPos + new Vector3(0f, 1.20f, 2.0f), 0.34f,
                                            Color.white, 0.5f, go.transform));
            lamp.GetComponent<Renderer>().sharedMaterial = Prim.Ghost(new Color(0.98f, 0.42f, 0.30f, 0.85f), 1.8f, 1f);
            cp.glow = lamp.GetComponent<Renderer>();
            return cp;
        }

        public void MarkReached()
        {
            if (Reached) return;
            Reached = true;
            if (glow != null) glow.sharedMaterial = Prim.Ghost(new Color(0.30f, 0.98f, 0.52f, 0.90f), 1.8f, 1f);
        }

        void OnTriggerEnter(Collider other)
        {
            var rig = other.GetComponentInParent<PlayerRig>();
            if (rig == null || GameSession.Instance == null) return;
            GameSession.Instance.ReachCheckpoint(this);
        }
    }

    /// <summary>골 — 구조 캡슐. 옆문 앞에 닿으면 판이 끝난다.</summary>
    public class Goal : MonoBehaviour
    {
        public static Goal Instance;

        /// <summary>셀프테스트·HUD 가 쓰는 "여기로 들어가면 된다" 지점.</summary>
        public Vector3 Entrance { get; private set; }

        Material ringMat;
        float t;

        public static Goal Create(Transform parent, Vector3 footPos, float yaw = 120f)
        {
            var go = new GameObject("구조 캡슐");
            go.transform.SetParent(parent, false);
            go.transform.position = footPos;

            var goal = go.AddComponent<Goal>();
            Instance = goal;

            var ship = ShipModel.Spawn(go.transform, footPos, yaw, "구조 캡슐 (모델)");
            if (ship == null) { LegacyRocket(go.transform, footPos, goal); return goal; }

            // 옆문 앞을 골 판정으로 삼는다. 문 자체는 지면에서 2.1m — 인형 키(1.33m)보다 높아서
            // "문에 닿아라" 로 만들면 마지막에 실패 지점이 하나 더 생긴다. 문 앞에 서면 통과다.
            Vector3 dir = ShipModel.HatchDir(yaw);
            Vector3 hatch = ShipModel.HatchAt(footPos, yaw);
            goal.Entrance = footPos + dir * 2.0f;

            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = goal.Entrance - footPos + Vector3.up * 1.1f;
            box.size = new Vector3(2.6f, 2.4f, 2.6f);

            // 발판 — 문까지 올라가는 계단 두 칸 (닿기만 해도 되지만 "탈 수 있다" 로 읽혀야 한다)
            for (int i = 0; i < 2; i++)
            {
                Vector3 c = footPos + dir * (2.05f - i * 0.62f) + Vector3.up * (0.18f + i * 0.36f);
                var step = Prim.Box("탑승 발판", c, new Vector3(1.3f, 0.16f, 1.3f),
                                    new Color(0.52f, 0.55f, 0.60f), 0.2f, go.transform);
                step.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                step.GetComponent<Collider>().material = Prim.GroundPhysics;
            }

            // 문에 어른거리는 초록 빛
            // 문짝(0.96m)보다 조금 작게 — 덮어버리면 놋쇠 손잡이와 주황 문이 안 보인다
            var ring = Prim.GhostBox("해치 빛", hatch + dir * 0.09f,
                                     Vector3.one,
                                     new Color(0.24f, 0.96f, 0.50f, 0.50f), go.transform, 1.6f, 1f);
            ring.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
            ring.transform.localScale = new Vector3(0.82f, 0.82f, 0.06f);
            goal.ringMat = ring.GetComponent<Renderer>().sharedMaterial;

            // 착륙 표시등
            for (int i = 0; i < 3; i++)
            {
                Vector3 c = footPos + Quaternion.Euler(0f, yaw + 40f + i * 140f, 0f) * new Vector3(2.6f, 0.12f, 0f);
                var lamp = Prim.Decor(Prim.Ball("착륙등", c, 0.26f, Color.white, 0.5f, go.transform));
                lamp.GetComponent<Renderer>().sharedMaterial =
                    Prim.Ghost(new Color(0.30f, 0.98f, 0.52f, 0.85f), 1.8f, 1f);
            }

            return goal;
        }

        /// <summary>블렌더 모델이 없을 때의 프리미티브 로켓 (예전 룩).</summary>
        static void LegacyRocket(Transform parent, Vector3 footPos, Goal goal)
        {
            goal.Entrance = footPos;

            var box = parent.gameObject.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(2.4f, 2.6f, 2.4f);
            box.center = new Vector3(0f, 1.3f, 0f);

            Color hull = new Color(0.88f, 0.90f, 0.94f);
            Color trim = new Color(0.94f, 0.36f, 0.26f);
            Prim.Decor(Prim.Pipe("로켓 동체", footPos + new Vector3(0f, 3.6f, 0f), 2.6f, 6.4f, hull, 0.2f, parent));
            Prim.Decor(Prim.Ball("노즈콘", footPos + new Vector3(0f, 7.2f, 0f), 2.6f, trim, 0.4f, parent));
            Prim.Decor(Prim.Pipe("띠", footPos + new Vector3(0f, 5.3f, 0f), 2.66f, 0.5f, trim, 0.2f, parent));

            var ring = Prim.GhostBox("해치 빛", footPos + new Vector3(-1.30f, 1.35f, 0f),
                                     new Vector3(0.10f, 1.5f, 1.5f),
                                     new Color(0.24f, 0.96f, 0.50f, 0.55f), parent, 1.4f, 1f);
            goal.ringMat = ring.GetComponent<Renderer>().sharedMaterial;
        }

        void Update()
        {
            if (ringMat == null) return;
            t += Time.deltaTime;
            float a = 0.52f + Mathf.Sin(t * 2.4f) * 0.14f;
            ringMat.SetColor("_Color", new Color(0.24f, 0.96f, 0.50f, a));
        }

        void OnTriggerEnter(Collider other)
        {
            var rig = other.GetComponentInParent<PlayerRig>();
            if (rig == null || GameSession.Instance == null) return;
            GameSession.Instance.Clear();
        }
    }

    /// <summary>
    /// 배터리 셀을 꽂는 자리. 이 프로토타입의 "물건 나르기" 를 퍼즐로 쓰는 곳이다 —
    /// 셀을 10m 쯤 들고 와서 여기 근처에 놓기만 하면 빨려 들어가 꽂힌다 (관대하게).
    /// </summary>
    public class PowerSocket : MonoBehaviour
    {
        public static PowerSocket Instance;

        public SlidingDoor Door;
        public Rigidbody Cell;                 // 꽂아야 하는 배터리 셀
        public Vector3 SlotPosition;
        public bool Powered { get; private set; }
        /// <summary>셀이 이 거리 안에 들어오면 꽂힌다. 조준을 요구하지 않는다.</summary>
        public float SnapRange = 1.0f;

        Material ringMat;
        Renderer[] lamps;

        public static PowerSocket Create(Transform parent, Vector3 basePos, SlidingDoor door)
        {
            var go = new GameObject("전원 소켓");
            go.transform.SetParent(parent, false);
            go.transform.position = basePos;

            var pedestal = Prim.Box("소켓 받침", basePos + new Vector3(0f, 0.35f, 0f),
                                    new Vector3(0.9f, 0.7f, 0.9f), new Color(0.36f, 0.38f, 0.44f), 0.15f, go.transform);
            pedestal.GetComponent<Collider>().material = Prim.GroundPhysics;

            var s = go.AddComponent<PowerSocket>();
            s.Door = door;
            s.SlotPosition = basePos + new Vector3(0f, 0.92f, 0f);

            // 꽂는 구멍
            Prim.Decor(Prim.Pipe("소켓 구멍", s.SlotPosition + new Vector3(0f, -0.06f, 0f), 0.42f, 0.14f,
                                 new Color(0.16f, 0.17f, 0.20f), 0.1f, go.transform));

            var ring = Prim.Decor(Prim.Pipe("소켓 테", s.SlotPosition + new Vector3(0f, -0.02f, 0f), 0.56f, 0.06f,
                                            Color.white, 0.2f, go.transform));
            ring.GetComponent<Renderer>().sharedMaterial = Prim.Ghost(new Color(0.98f, 0.40f, 0.30f, 0.8f), 2.0f, 1f);
            s.ringMat = ring.GetComponent<Renderer>().sharedMaterial;

            var l = new List<Renderer>();
            for (int i = 0; i < 3; i++)
            {
                var lamp = Prim.Decor(Prim.Ball("소켓 등", basePos + new Vector3(-0.24f + i * 0.24f, 0.58f, 0.47f),
                                                0.11f, Color.white, 0.5f, go.transform));
                lamp.GetComponent<Renderer>().sharedMaterial = Prim.Ghost(new Color(0.95f, 0.32f, 0.28f, 0.9f), 1.6f, 0f);
                l.Add(lamp.GetComponent<Renderer>());
            }
            s.lamps = l.ToArray();

            Instance = s;
            return s;
        }

        /// <summary>배터리 셀을 만들어 소켓과 짝지어 둔다.</summary>
        public Rigidbody CreateCell(Transform parent, Vector3 center)
        {
            var go = Prim.Box("배터리 셀", center, new Vector3(0.40f, 0.62f, 0.40f),
                              new Color(0.94f, 0.62f, 0.16f), 0.35f, parent);
            go.GetComponent<Collider>().material = Prim.GroundPhysics;

            var d = Prim.DecoRoot(go.transform);
            Prim.FitBox(d, "셀 띠", new Vector3(0f, 0.10f, 0f), new Vector3(0.43f, 0.09f, 0.43f),
                        new Color(0.20f, 0.21f, 0.25f), 0.2f);
            Prim.FitBox(d, "셀 띠", new Vector3(0f, -0.10f, 0f), new Vector3(0.43f, 0.09f, 0.43f),
                        new Color(0.20f, 0.21f, 0.25f), 0.2f);
            var top = Prim.FitPipe(d, "셀 단자", new Vector3(0f, 0.34f, 0f), 0.34f, 0.10f, Color.white, 0.3f);
            top.GetComponent<Renderer>().sharedMaterial = Prim.Ghost(new Color(0.45f, 0.95f, 1.00f, 0.85f), 1.8f, 1f);

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 14f;
            rb.linearDamping = 0.08f;
            rb.angularDamping = 0.5f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.maxDepenetrationVelocity = 2f;

            Grabbable.Mark(go, "배터리 셀", false, 1f, 1f);      // 우선순위 1 — 자석 손이 이걸 먼저 고른다
            Cell = rb;
            return rb;
        }

        void FixedUpdate()
        {
            if (Powered || Cell == null) return;

            var g = Cell.GetComponent<Grabbable>();
            if (g != null && g.HandCount > 0) return;            // 손에 쥔 채로는 안 꽂힌다

            if (Vector3.Distance(Cell.position, SlotPosition) > SnapRange) return;

            Insert();
        }

        /// <summary>셀프테스트도 쓰는 진입점 — 셀을 소켓에 꽂는다.</summary>
        public void Insert()
        {
            if (Powered || Cell == null) return;

            Cell.linearVelocity = Vector3.zero;
            Cell.angularVelocity = Vector3.zero;
            Cell.isKinematic = true;
            Cell.position = SlotPosition;
            Cell.rotation = Quaternion.identity;
            Cell.transform.SetPositionAndRotation(SlotPosition, Quaternion.identity);

            var g = Cell.GetComponent<Grabbable>();
            if (g != null) g.CanGrab = false;                    // 꽂힌 셀은 다시 뽑을 수 없다

            Powered = true;
            if (Door != null) Door.Target01 = 1f;

            Color on = new Color(0.32f, 0.98f, 0.52f, 0.9f);
            if (ringMat != null) ringMat.SetColor("_Color", on);
            for (int i = 0; lamps != null && i < lamps.Length; i++)
                if (lamps[i] != null) lamps[i].sharedMaterial = Prim.Ghost(on, 1.6f, 1f);
        }
    }

    /// <summary>전원이 들어오면 열리는 에어록 해치. 열림 정도만큼 위로 올라간다.</summary>
    public class SlidingDoor : MonoBehaviour
    {
        public static SlidingDoor Instance;

        public Vector3 ClosedPos;
        public Vector3 OpenPos;
        public float Target01;
        public float Current01 { get; private set; }
        public float Speed = 0.55f;

        Rigidbody body;

        public bool IsOpen { get { return Current01 > 0.95f; } }

        public static SlidingDoor Create(Transform parent, Vector3 center, Vector3 size, Vector3 openOffset)
        {
            // 해치는 벽보다 어둡게 — 벽과 같은 색이면 "여기가 문이다" 가 안 읽힌다
            var go = Prim.Box("에어록 해치", center, size, new Color(0.34f, 0.36f, 0.42f), 0.2f, parent);
            go.GetComponent<Collider>().material = Prim.GroundPhysics;

            // 줄무늬는 사람이 걸어오는 쪽(±X) 면에 붙인다. 두께 방향(±Z)에 붙이면 옆에서만 보인다.
            var d = Prim.DecoRoot(go.transform);
            Color warn = new Color(0.95f, 0.66f, 0.14f);
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sy = -1; sy <= 1; sy += 2)
                    Prim.FitBox(d, "해치 줄무늬",
                                new Vector3(sx * size.x * 0.52f, sy * size.y * 0.30f, 0f),
                                new Vector3(0.06f, 0.22f, size.z * 0.86f), warn, 0.2f);
            Prim.FitBox(d, "해치 손잡이", new Vector3(-size.x * 0.55f, 0f, 0f),
                        new Vector3(0.10f, 0.14f, 1.1f), new Color(0.72f, 0.76f, 0.82f), 0.2f);

            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            var door = go.AddComponent<SlidingDoor>();
            door.body = rb;
            door.ClosedPos = center;
            door.OpenPos = center + openOffset;
            Instance = door;
            return door;
        }

        void FixedUpdate()
        {
            Current01 = Mathf.MoveTowards(Current01, Mathf.Clamp01(Target01), Speed * Time.fixedDeltaTime);
            body.MovePosition(Vector3.Lerp(ClosedPos, OpenPos, Current01));
        }
    }

    /// <summary>두 점 사이를 오가는 자기부상 화물판. 위에 탄 인형은 마찰로 같이 간다.</summary>
    public class MovingPlatform : MonoBehaviour
    {
        public static readonly List<MovingPlatform> All = new List<MovingPlatform>();

        public Vector3 A, B;
        public float Period = 7f;
        public float Phase;

        Rigidbody body;

        void OnEnable() { All.Add(this); }
        void OnDisable() { All.Remove(this); }

        public static MovingPlatform Create(Transform parent, Vector3 a, Vector3 b, Vector3 size,
                                            Color color, float period = 7f, float phase = 0f)
        {
            var go = Prim.Box("화물판", a, size, color, 0.2f, parent);
            go.GetComponent<Collider>().material = Prim.GroundPhysics;

            var d = Prim.DecoRoot(go.transform);
            Prim.FitBox(d, "부상 코일", new Vector3(0f, -size.y * 0.55f, 0f),
                        new Vector3(size.x * 0.8f, 0.10f, size.z * 0.8f), Color.white, 0.3f)
                .GetComponent<Renderer>().sharedMaterial = Prim.Ghost(new Color(0.40f, 0.80f, 1.00f, 0.55f), 1.8f, 1f);

            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            var mp = go.AddComponent<MovingPlatform>();
            mp.body = rb;
            mp.A = a; mp.B = b; mp.Period = period; mp.Phase = phase;
            return mp;
        }

        void FixedUpdate()
        {
            float t = (Mathf.Sin((Time.time / Period + Phase) * Mathf.PI * 2f) + 1f) * 0.5f;
            body.MovePosition(Vector3.Lerp(A, B, t));
        }
    }

    /// <summary>제자리에서 도는 통신 안테나 — 맞으면 저중력이라 멀리 날아간다.</summary>
    public class Spinner : MonoBehaviour
    {
        public float DegPerSecond = 60f;
        Rigidbody body;
        float angle;

        public static Spinner Create(Transform parent, Vector3 center, Vector3 size, Color color, float dps)
        {
            var go = Prim.Box("회전 안테나", center, size, color, 0.2f, parent);
            go.GetComponent<Collider>().material = Prim.GroundPhysics;

            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            var s = go.AddComponent<Spinner>();
            s.body = rb;
            s.DegPerSecond = dps;
            return s;
        }

        void FixedUpdate()
        {
            angle += DegPerSecond * Time.fixedDeltaTime;
            body.MoveRotation(Quaternion.Euler(0f, angle, 0f));
        }
    }
}
