using System.Collections.Generic;
using UnityEngine;

namespace SpaceFlop
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
        Transform holder;

        static Puff Instance
        {
            get
            {
                if (instance != null) return instance;
                if (!Enabled) return null;
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
