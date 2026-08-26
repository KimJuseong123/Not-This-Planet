using UnityEngine;

namespace SpaceFlop
{
    /// <summary>
    /// 블렌더로 만든 캡슐 우주선(<c>SF_Spaceship_Capsule</c>)을 세계에 놓는다.
    ///
    /// 아래 로컬 좌표들은 임포트된 FBX 에서 직접 잰 값이다 (Editor/ArtInspect 로 찍었다).
    /// <b>README 와 한 군데가 다르다</b> — 큰 창(Porthole)은 +Z 가 아니라 <b>−Z</b> 를 본다.
    /// 캐릭터 본의 좌우가 뒤집힌 것과 같은 축 변환 때문이다. 문서가 아니라 좌표를 믿는다.
    ///
    /// 메시가 91개라 콜라이더는 메시가 아니라 캡슐 하나 + 착륙발 4개로 근사한다
    /// (비볼록 MeshCollider 는 이 게임의 잡기·밀기 물리에서 못 쓴다).
    /// </summary>
    public static class ShipModel
    {
        public const string ResourceKey = "SF_Spaceship_Capsule";

        public const float Height = 4.57f;
        /// <summary>옆문 한가운데 (로컬). 지면에서 2.1m — 우리 인형 키의 1.6배다.</summary>
        public static readonly Vector3 SideHatch = new Vector3(1.36f, 2.10f, 0.06f);
        /// <summary>옆문이 바라보는 방향 (로컬).</summary>
        public static readonly Vector3 HatchFacing = Vector3.right;
        public static readonly Vector3 Porthole = new Vector3(0f, 2.22f, -1.31f);

        static GameObject prefab;
        static bool searched;

        public static GameObject Prefab
        {
            get
            {
                if (!searched)
                {
                    searched = true;
                    prefab = Resources.Load<GameObject>(ResourceKey);
                    if (prefab == null)
                        Debug.LogWarning("[ShipModel] Resources/" + ResourceKey
                                         + " 없음 — 프리미티브 우주선으로 돈다.");
                }
                return prefab;
            }
        }

        public static bool Available { get { return Prefab != null; } }

        /// <summary>
        /// 착륙한 우주선 한 대. <paramref name="footPos"/> 는 착륙발이 닿는 지면,
        /// <paramref name="yaw"/> 는 Y축 회전(도).
        /// </summary>
        public static GameObject Spawn(Transform parent, Vector3 footPos, float yaw, string name)
        {
            if (Prefab == null) return null;

            var go = Object.Instantiate(Prefab);
            go.name = name;
            go.transform.SetParent(parent, true);
            go.transform.SetPositionAndRotation(footPos, Quaternion.Euler(0f, yaw, 0f));

            // 동체 — 캡슐 하나로 감싼다
            var body = go.AddComponent<CapsuleCollider>();
            body.center = new Vector3(0f, 2.0f, 0f);
            body.radius = 1.42f;
            body.height = 3.35f;
            body.direction = 1;                       // Y축
            body.material = Prim.GroundPhysics;

            // 착륙발 4개 — 밟고 올라설 수 있어야 한다
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    var foot = go.AddComponent<BoxCollider>();
                    foot.center = new Vector3(sx * 1.25f, 0.09f, sz * 0.97f);
                    foot.size = new Vector3(0.50f, 0.18f, 0.40f);
                    foot.material = Prim.GroundPhysics;
                }

            return go;
        }

        /// <summary>옆문의 월드 위치.</summary>
        public static Vector3 HatchAt(Vector3 footPos, float yaw)
        {
            return footPos + Quaternion.Euler(0f, yaw, 0f) * SideHatch;
        }

        /// <summary>옆문이 바라보는 월드 방향 (수평).</summary>
        public static Vector3 HatchDir(float yaw)
        {
            Vector3 d = Quaternion.Euler(0f, yaw, 0f) * HatchFacing;
            d.y = 0f;
            return d.sqrMagnitude > 1e-4f ? d.normalized : Vector3.right;
        }
    }
}
