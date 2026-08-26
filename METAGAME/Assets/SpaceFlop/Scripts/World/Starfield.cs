using UnityEngine;

namespace SpaceFlop
{
    /// <summary>
    /// 하늘 대신 놓는 것들 — 별밭·행성·해.
    /// 카메라를 따라다니되 회전은 하지 않는다 (스카이박스와 같은 원리).
    /// 그래서 140m 짜리 코스를 걸어도 별자리가 흐르지 않는다.
    ///
    /// 별 900개를 GameObject 로 만들면 드로우콜이 900개다 —
    /// 정점 색을 쓰는 메시 하나로 찍는다.
    /// </summary>
    public class Starfield : MonoBehaviour
    {
        public const float Radius = 300f;
        public const int StarCount = 1500;
        public const int BandCount = 900;      // 은하수 띠

        public Transform Planet { get; private set; }
        public Transform SunDisc { get; private set; }
        public int Stars { get; private set; }

        Transform follow;

        public static Starfield Create(Transform parent, Vector3 sunDirection)
        {
            var go = new GameObject("~우주 배경");
            go.transform.SetParent(parent, false);
            var sf = go.AddComponent<Starfield>();

            sf.BuildStars(go.transform);
            sf.BuildPlanet(go.transform);
            sf.BuildSun(go.transform, sunDirection);
            return sf;
        }

        void LateUpdate()
        {
            if (follow == null)
            {
                var cam = Camera.main;
                if (cam == null) return;
                follow = cam.transform;
            }
            transform.position = follow.position;      // 회전은 따라가지 않는다
        }

        // ── 별밭 ────────────────────────────────────────────────
        void BuildStars(Transform parent)
        {
            var rnd = new System.Random(20260825);
            int total = StarCount + BandCount;
            Stars = total;

            var verts = new Vector3[total * 4];
            var cols = new Color[total * 4];
            var tris = new int[total * 6];

            // 은하수 띠의 축 — 이 축에 수직인 대원 근처에 별을 몰아 넣는다
            Vector3 bandAxis = new Vector3(0.32f, 0.86f, -0.40f).normalized;
            Vector3 bandU = Vector3.Normalize(Vector3.Cross(bandAxis, Vector3.right));
            Vector3 bandV = Vector3.Cross(bandAxis, bandU);

            for (int i = 0; i < total; i++)
            {
                bool inBand = i >= StarCount;
                Vector3 dir;

                if (inBand)
                {
                    float a = (float)rnd.NextDouble() * Mathf.PI * 2f;
                    float spread = ((float)rnd.NextDouble() - 0.5f) * 0.22f;   // 띠의 두께
                    dir = (bandU * Mathf.Cos(a) + bandV * Mathf.Sin(a) + bandAxis * spread).normalized;
                }
                else
                {
                    float z = (float)rnd.NextDouble() * 2f - 1f;
                    float a = (float)rnd.NextDouble() * Mathf.PI * 2f;
                    float r = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));
                    dir = new Vector3(r * Mathf.Cos(a), z, r * Mathf.Sin(a));
                }

                // 밝기: 대부분 흐리고 몇 개만 밝다 (거듭제곱으로 치우치게)
                float b = Mathf.Pow((float)rnd.NextDouble(), inBand ? 3.2f : 2.4f);
                float bright = inBand ? 0.10f + b * 0.34f : 0.16f + b * 0.84f;
                float size = (inBand ? 0.10f : 0.13f) + b * (inBand ? 0.10f : 0.42f);

                // 색온도 — 푸른 별, 흰 별, 붉은 별
                float t = (float)rnd.NextDouble();
                Color tint = t < 0.22f ? new Color(0.72f, 0.82f, 1.00f)
                           : t > 0.86f ? new Color(1.00f, 0.80f, 0.66f)
                                       : new Color(0.96f, 0.97f, 1.00f);
                Color c = tint * bright;
                c.a = 1f;

                Vector3 pos = dir * Radius;
                Vector3 right = Vector3.Normalize(Vector3.Cross(dir, Mathf.Abs(dir.y) > 0.95f ? Vector3.right : Vector3.up));
                Vector3 up = Vector3.Cross(right, dir);

                int v = i * 4;
                verts[v + 0] = pos - right * size - up * size;
                verts[v + 1] = pos - right * size + up * size;
                verts[v + 2] = pos + right * size + up * size;
                verts[v + 3] = pos + right * size - up * size;
                cols[v + 0] = cols[v + 1] = cols[v + 2] = cols[v + 3] = c;

                int f = i * 6;
                tris[f + 0] = v + 0; tris[f + 1] = v + 1; tris[f + 2] = v + 2;
                tris[f + 3] = v + 0; tris[f + 4] = v + 2; tris[f + 5] = v + 3;
            }

            var mesh = new Mesh();
            mesh.name = "별밭";
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = verts;
            mesh.colors = cols;
            mesh.triangles = tris;
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * (Radius * 4f));   // 화면 밖 판정에서 잘리지 않게

            var go = new GameObject("별밭");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Prim.Flat(Color.white);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        // ── 고향 행성 ───────────────────────────────────────────
        void BuildPlanet(Transform parent)
        {
            // 화면 위쪽·왼쪽에 걸리게 둔다. 코스를 보는 앵글(+X 쪽)에서 가운데를 가리지 않는다.
            Vector3 dir = new Vector3(-0.46f, 0.60f, 0.66f).normalized;
            Vector3 pos = dir * (Radius * 0.82f);
            const float D = 78f;

            // 해를 받아 초승달처럼 보인다 — 그래서 그냥 Dough 로 칠한다
            var planet = Prim.Decor(Prim.Ball("고향 행성", pos, D, new Color(0.20f, 0.40f, 0.72f), 0.85f, parent));
            NoShadow(planet);

            // 구름 띠 — 행성과 같은 중심의 납작한 타원체를 겹친다.
            // 표면 위로 삐져나온 테두리만 보이므로 언제나 "띠" 로 읽힌다
            // (덩어리를 표면에 붙이면 각도에 따라 공이 매달린 것처럼 보인다).
            Band(parent, pos, new Vector3(D * 1.05f, D * 0.34f, D * 1.05f),
                 Quaternion.Euler(14f, 0f, 8f), new Color(0.88f, 0.91f, 0.95f));
            Band(parent, pos, new Vector3(D * 1.03f, D * 0.16f, D * 1.03f),
                 Quaternion.Euler(-26f, 30f, 0f), new Color(0.46f, 0.66f, 0.48f));
            Band(parent, pos, new Vector3(D * 1.02f, D * 0.10f, D * 1.02f),
                 Quaternion.Euler(52f, -18f, 0f), new Color(0.86f, 0.89f, 0.94f));

            // 대기 테두리
            var halo = Prim.Decor(Prim.Ball("대기", pos, D * 1.16f, Color.white, 0.5f, parent));
            halo.GetComponent<Renderer>().sharedMaterial =
                Prim.Ghost(new Color(0.45f, 0.70f, 1.00f, 0.16f), 2.6f, 0f);
            NoShadow(halo);

            Planet = planet.transform;
        }

        void Band(Transform parent, Vector3 pos, Vector3 scale, Quaternion rot, Color c)
        {
            var go = Prim.Decor(Prim.Ball("구름 띠", pos, 1f, c, 0.9f, parent));
            go.transform.localScale = scale;
            go.transform.rotation = rot;
            NoShadow(go);
        }

        static void NoShadow(GameObject go)
        {
            var mr = go.GetComponent<Renderer>();
            if (mr == null) return;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        // ── 해 ──────────────────────────────────────────────────
        void BuildSun(Transform parent, Vector3 sunDirection)
        {
            Vector3 pos = sunDirection.normalized * (Radius * 0.9f);

            var disc = Prim.Decor(Prim.Ball("해", pos, 26f, Color.white, 0.5f, parent));
            disc.GetComponent<Renderer>().sharedMaterial = Prim.Ghost(new Color(1f, 0.98f, 0.90f, 1f), 0f, 0f);

            var glow = Prim.Decor(Prim.Ball("해 무리", pos, 62f, Color.white, 0.5f, parent));
            glow.GetComponent<Renderer>().sharedMaterial = Prim.Ghost(new Color(1f, 0.94f, 0.74f, 0.14f), 1.8f, 0f);

            SunDisc = disc.transform;
        }
    }
}
