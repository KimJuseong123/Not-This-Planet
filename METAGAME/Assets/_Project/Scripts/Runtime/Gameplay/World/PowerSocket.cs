using System.Collections.Generic;
using UnityEngine;
using Metagame.Core;

namespace Metagame.Gameplay
{
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
}
