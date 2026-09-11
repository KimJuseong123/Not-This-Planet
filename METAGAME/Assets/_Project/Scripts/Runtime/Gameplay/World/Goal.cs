using UnityEngine;
using Metagame.Core;

namespace Metagame.Gameplay
{
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
}
