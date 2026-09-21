using System.Collections.Generic;
using UnityEngine;
using Metagame.Core;

namespace Metagame.Gameplay
{
    /// <summary>
    /// 먼지와 분사 연기. 진공이라 <b>퍼지지 않는다</b> — 튀어 오른 레골리스는
    /// 공기 저항 없이 그대로 포물선을 그리고 떨어진다. 이 한 가지 차이가
    /// "여기는 지구가 아니다" 를 가장 싸게 보여준다.
    ///
    /// 물리 엔진을 쓰지 않고 직접 적분한다 (알갱이 수백 개에 강체를 붙일 이유가 없다).
    /// </summary>
    public class Puff : MonoBehaviour
    {
        public static bool Enabled = true;
        const int Capacity = 220;

        struct Grain
        {
            public Transform T;
            public Vector3 Vel;
            public float Life, MaxLife, Size;
            public bool Falls;          // true = 중력을 받는다 (먼지), false = 그 자리서 사라진다 (분사)
        }

        static Puff instance;
        readonly List<Grain> live = new List<Grain>();
        readonly Stack<Transform> pool = new Stack<Transform>();
        Material dustMat, jetMat;
        readonly Material[] batMats = new Material[3];
        Transform holder;

        static Puff Instance
        {
            get
            {
                if (instance != null) return instance;
                if (!Enabled) return null;

                // <b>에디터에서는 만들지 않는다.</b> EditMode 검증에서는 Awake 가 돌지
                // 않아 <c>holder</c> 와 재질이 비고, <c>Update</c> 도 돌지 않아 알갱이가
                // 씬에 영원히 남는다. 운석 착탄이 먼지를 뿜게 되면서(2026-09-18) 먼지를
                // 끄지 않은 검증들까지 이 길을 지나게 됐다 — <see cref="Enabled"/> 를
                // 검증마다 끄게 하는 대신 여기서 한 번 막는다.
                if (!Application.isPlaying) return null;

                var go = new GameObject("~먼지");
                instance = go.AddComponent<Puff>();
                return instance;
            }
        }

        void Awake()
        {
            instance = this;
            holder = transform;
            dustMat = Prim.Ghost(new Color(0.82f, 0.78f, 0.70f, 0.55f), 0.8f, 0f);
            jetMat = Prim.Ghost(new Color(0.70f, 0.88f, 1.00f, 0.45f), 1.4f, 0f);
        }

        /// <summary>발밑에서 레골리스가 튀어 오른다. strength 0~1.</summary>
        public static void Regolith(Vector3 foot, float strength)
        {
            var p = Instance;
            if (p == null) return;

            int n = Mathf.Clamp(Mathf.RoundToInt(5f + strength * 12f), 4, 18);
            for (int i = 0; i < n; i++)
            {
                float a = (i / (float)n) * Mathf.PI * 2f + Random.Range(-0.25f, 0.25f);
                Vector3 dir = new Vector3(Mathf.Cos(a), Random.Range(0.55f, 1.15f), Mathf.Sin(a));
                p.Spawn(foot + new Vector3(dir.x, 0.03f, dir.z) * 0.10f,
                        dir.normalized * Random.Range(0.8f, 2.4f) * (0.5f + strength),
                        Random.Range(0.045f, 0.10f), Random.Range(0.9f, 1.8f), true, p.dustMat);
            }
        }

        /// <summary>
        /// 착탄 충격파 — 먼지 고리가 <paramref name="radius"/> 까지 <b>퍼져 나간다</b>
        /// (2026-09-18).
        ///
        /// <see cref="Regolith"/> 는 발밑에서 위로 튀지만 이것은 <b>바깥으로 달린다.</b>
        /// 충격파의 범위가 눈에 보여야 "여기까지는 맞는다" 를 배울 수 있다 — 낙하 예고
        /// 원은 강한 충격 범위만 그리므로(§17.2) 바깥 띠는 표시가 없다.
        ///
        /// 알갱이가 <b>수명 안에 반경만큼</b> 가도록 속도를 맞춘다. 진공이라 감속이
        /// 없으므로 속도 × 수명이 곧 거리다.
        /// </summary>
        public static void Shockwave(Vector3 center, float radius)
        {
            var p = Instance;
            if (p == null || radius <= 0f) return;

            const float Life = 0.45f;
            float speed = radius / Life;
            int n = Mathf.Clamp(Mathf.RoundToInt(radius * 7f), 12, 40);

            for (int i = 0; i < n; i++)
            {
                float a = (i / (float)n) * Mathf.PI * 2f + Random.Range(-0.12f, 0.12f);
                Vector3 outward = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));

                // 살짝만 위로 — 고리가 지면을 훑어야 반경으로 읽힌다.
                Vector3 vel = outward * (speed * Random.Range(0.8f, 1.1f))
                            + Vector3.up * Random.Range(0.4f, 1.3f);

                p.Spawn(center + outward * 0.25f + Vector3.up * 0.05f,
                        vel,
                        Random.Range(0.10f, 0.20f), Life * Random.Range(0.85f, 1.15f),
                        true, p.dustMat);
            }
        }

        /// <summary>등짐 분사기의 짧은 김.</summary>
        public static void Thruster(Vector3 pos, Vector3 dir)
        {
            var p = Instance;
            if (p == null) return;

            for (int i = 0; i < 3; i++)
                p.Spawn(pos + Random.insideUnitSphere * 0.05f,
                        dir.normalized * Random.Range(1.2f, 2.2f) + Random.insideUnitSphere * 0.4f,
                        Random.Range(0.05f, 0.09f), Random.Range(0.18f, 0.30f), false, p.jetMat);
        }

        /// <summary>Confirmed meteor contact; bounded particles reuse the existing pool.</summary>
        public static void BatImpact(Vector3 point, byte grade)
        {
            if (!Enabled || grade < 1 || grade > 3) return;
            var p = Instance;
            if (p == null) return;
            int index = grade - 1;
            if (p.batMats[index] == null)
                p.batMats[index] = Prim.Ghost(BatSwingLook.ImpactColor(grade), 3f, 0f);
            int count = grade == 3 ? 32 : grade == 2 ? 22 : 14;
            // Deterministic directions avoid consuming gameplay's shared Random state.
            for (int i = 0; i < count; i++)
            {
                float y = 1f - 2f * (i + 0.5f) / count;
                float radius = Mathf.Sqrt(1f - y * y);
                float angle = i * 2.399963f;
                var direction = new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);
                p.Spawn(point, direction * (3f + grade * 1.5f),
                    0.12f + grade * 0.035f, 0.22f + grade * 0.07f, false, p.batMats[index]);
            }
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
            if (dustMat != null) Destroy(dustMat);
            if (jetMat != null) Destroy(jetMat);
            foreach (var material in batMats)
                if (material != null) Destroy(material);
        }

        void Spawn(Vector3 pos, Vector3 vel, float size, float life, bool falls, Material mat)
        {
            if (live.Count >= Capacity) return;

            Transform t;
            if (pool.Count > 0) { t = pool.Pop(); t.gameObject.SetActive(true); }
            else
            {
                var go = Prim.Decor(Prim.Ball("알갱이", Vector3.zero, 1f, Color.white, 0.5f));
                go.transform.SetParent(holder, false);
                t = go.transform;
            }

            t.GetComponent<Renderer>().sharedMaterial = mat;
            t.position = pos;
            t.localScale = Vector3.one * size;

            live.Add(new Grain { T = t, Vel = vel, Life = life, MaxLife = life, Size = size, Falls = falls });
        }

        void Update()
        {
            float dt = Time.deltaTime;
            for (int i = live.Count - 1; i >= 0; i--)
            {
                var g = live[i];
                g.Life -= dt;
                if (g.Life <= 0f || g.T == null)
                {
                    if (g.T != null) { g.T.gameObject.SetActive(false); pool.Push(g.T); }
                    live.RemoveAt(i);
                    continue;
                }

                if (g.Falls) g.Vel += Physics.gravity * dt;      // 진공 — 감속은 오직 중력뿐
                g.T.position += g.Vel * dt;

                float k = g.Life / g.MaxLife;
                g.T.localScale = Vector3.one * (g.Size * (g.Falls ? Mathf.Lerp(0.35f, 1f, k) : k));
                live[i] = g;
            }
        }
    }
}
