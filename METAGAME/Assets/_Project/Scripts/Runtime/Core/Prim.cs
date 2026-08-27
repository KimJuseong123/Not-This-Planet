using System.Collections.Generic;
using UnityEngine;

namespace Metagame.Core
{
    /// <summary>
    /// 런타임에 장난감 룩 오브젝트를 찍어내는 공장.
    /// 에셋 없이 프리미티브 + 커스텀 셰이더만 쓴다 (프리팹 관리 비용 0).
    ///
    /// 스케일 함정 하나: 프리미티브는 localScale 로 크기를 만든다. 그 자식에 장식을 달면
    /// 부모의 스케일이 그대로 곱해지고, 부모가 비균등 스케일이면 회전한 자식은 찌그러진다.
    /// 그래서 장식은 반드시 <see cref="DecoRoot"/> 아래에 단다 — 부모 스케일을 정확히
    /// 되돌리는 노드라, 그 아래는 그냥 "미터" 로 생각하면 된다.
    /// </summary>
    public static class Prim
    {
        static Shader doughShader;
        static Shader ghostShader;
        static Shader flatShader;
        static readonly Dictionary<string, Material> doughCache = new Dictionary<string, Material>();

        public static Shader DoughShader
        {
            get { if (doughShader == null) doughShader = Find("DoughSurface", "SpaceFlop/Dough"); return doughShader; }
        }

        public static Shader GhostShader
        {
            get { if (ghostShader == null) ghostShader = Find("GhostSurface", "SpaceFlop/Ghost"); return ghostShader; }
        }

        /// <summary>빛을 받지 않는 순수 발광 — 별·행성 테두리·표시등에 쓴다.</summary>
        public static Shader FlatShader
        {
            get { if (flatShader == null) flatShader = Find("FlatUnlit", "SpaceFlop/Flat"); return flatShader; }
        }

        static Shader Find(string resourceName, string shaderName)
        {
            var s = Resources.Load<Shader>(resourceName);      // 빌드된 플레이어에서도 확실히 잡힌다
            if (s != null) return s;
            s = Shader.Find(shaderName);
            if (s != null) return s;
            s = Shader.Find("Standard");
            return s != null ? s : Shader.Find("Diffuse");
        }

        /// <summary>같은 색/말랑함이면 머티리얼을 공유한다.</summary>
        public static Material Dough(Color c, float softness)
        {
            string key = string.Format("{0:0.00}_{1:0.00}_{2:0.00}_{3:0.00}", c.r, c.g, c.b, softness);
            Material m;
            if (doughCache.TryGetValue(key, out m) && m != null) return m;

            m = new Material(DoughShader);
            m.name = "Dough_" + key;
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_Softness")) m.SetFloat("_Softness", softness);
            if (m.HasProperty("_Gloss")) m.SetFloat("_Gloss", Mathf.Lerp(0.55f, 0.30f, softness));
            if (m.HasProperty("_Mottle")) m.SetFloat("_Mottle", Mathf.Lerp(0.10f, 0.20f, softness));
            // 진공에는 하늘빛이 없다 — 앰비언트를 낮게 두어야 그림자가 새까맣게 떨어진다
            if (m.HasProperty("_Ambient")) m.SetFloat("_Ambient", 0.22f);
            doughCache[key] = m;
            return m;
        }

        public static Material Ghost(Color c, float rimBoost = 1.6f, float pulse = 0f)
        {
            var m = new Material(GhostShader);
            m.name = "Ghost";
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_RimBoost")) m.SetFloat("_RimBoost", rimBoost);
            if (m.HasProperty("_Pulse")) m.SetFloat("_Pulse", pulse);
            return m;
        }

        public static Material Flat(Color c)
        {
            var m = new Material(FlatShader);
            m.name = "Flat";
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            return m;
        }

        // ── 프리미티브 ──────────────────────────────────────────
        public static GameObject Box(string name, Vector3 pos, Vector3 size, Color color,
                                     float softness = 0.35f, Transform parent = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            if (parent != null) go.transform.SetParent(parent, true);
            go.transform.position = pos;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = Dough(color, softness);
            return go;
        }

        public static GameObject Ball(string name, Vector3 pos, float diameter, Color color,
                                      float softness = 0.9f, Transform parent = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            if (parent != null) go.transform.SetParent(parent, true);
            go.transform.position = pos;
            go.transform.localScale = Vector3.one * diameter;
            go.GetComponent<Renderer>().sharedMaterial = Dough(color, softness);
            return go;
        }

        /// <summary>세로 캡슐. height 는 전체 길이(양 끝 반구 포함).</summary>
        public static GameObject Capsule(string name, Vector3 pos, float diameter, float height, Color color,
                                         float softness = 0.9f, Transform parent = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = name;
            if (parent != null) go.transform.SetParent(parent, true);
            go.transform.position = pos;
            go.transform.localScale = new Vector3(diameter, Mathf.Max(height * 0.5f, diameter * 0.5f), diameter);
            go.GetComponent<Renderer>().sharedMaterial = Dough(color, softness);
            return go;
        }

        public static GameObject Pipe(string name, Vector3 pos, float diameter, float height, Color color,
                                      float softness = 0.2f, Transform parent = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            if (parent != null) go.transform.SetParent(parent, true);
            go.transform.position = pos;
            go.transform.localScale = new Vector3(diameter, height * 0.5f, diameter);
            go.GetComponent<Renderer>().sharedMaterial = Dough(color, softness);
            return go;
        }

        /// <summary>표시 전용 — 물리에 관여하지 않게 콜라이더를 뗀다.</summary>
        public static GameObject Decor(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            // Destroy 는 프레임 끝에 처리된다 — 그 사이에 눈알이 몸통과 충돌하면 인형이 폭발한다
            if (col != null) { col.enabled = false; Object.Destroy(col); }
            return go;
        }

        public static GameObject GhostBox(string name, Vector3 pos, Vector3 size, Color color,
                                          Transform parent = null, float rimBoost = 1.6f, float pulse = 1f)
        {
            var go = Decor(Box(name, pos, size, Color.white, 0.3f, parent));
            go.GetComponent<Renderer>().sharedMaterial = Ghost(color, rimBoost, pulse);
            return go;
        }

        // ── 장식 붙이기 ─────────────────────────────────────────
        /// <summary>
        /// 부모의 스케일을 정확히 되돌리는 빈 노드. 이 아래의 자식은 "미터" 단위로 놓으면 되고,
        /// 회전시켜도 찌그러지지 않는다 (부모까지 합친 월드 스케일이 1 이 되기 때문).
        /// </summary>
        public static Transform DecoRoot(Transform part)
        {
            var go = new GameObject("장식");
            go.transform.SetParent(part, false);
            Vector3 s = part.localScale;
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = new Vector3(Safe(s.x), Safe(s.y), Safe(s.z));
            return go.transform;
        }

        static float Safe(float v) { return Mathf.Abs(v) < 1e-5f ? 1f : 1f / v; }

        public static GameObject FitBox(Transform deco, string name, Vector3 offset, Vector3 size,
                                        Color color, float softness = 0.3f)
        {
            var go = Decor(Box(name, Vector3.zero, Vector3.one, color, softness));
            Place(go.transform, deco, offset, size);
            return go;
        }

        public static GameObject FitBall(Transform deco, string name, Vector3 offset, float diameter,
                                         Color color, float softness = 0.8f)
        {
            var go = Decor(Ball(name, Vector3.zero, 1f, color, softness));
            Place(go.transform, deco, offset, Vector3.one * diameter);
            return go;
        }

        public static GameObject FitPipe(Transform deco, string name, Vector3 offset, float diameter,
                                         float height, Color color, float softness = 0.2f)
        {
            var go = Decor(Pipe(name, Vector3.zero, 1f, 2f, color, softness));
            Place(go.transform, deco, offset, new Vector3(diameter, height * 0.5f, diameter));
            return go;
        }

        static void Place(Transform t, Transform deco, Vector3 offset, Vector3 scale)
        {
            t.SetParent(deco, false);
            t.localPosition = offset;
            t.localRotation = Quaternion.identity;
            t.localScale = scale;
        }

        /// <summary>부모의 스케일을 감안해 "미터" 오프셋을 localPosition 으로 바꾼다.</summary>
        public static Vector3 LocalMeters(Transform parent, Vector3 meters)
        {
            Vector3 s = parent.localScale;
            return new Vector3(meters.x * Safe(s.x), meters.y * Safe(s.y), meters.z * Safe(s.z));
        }

        // ── 물리 재질 ───────────────────────────────────────────
        /// <summary>정적 지형(레골리스·금속 갑판)용 — 미끄럽지 않게.</summary>
        public static PhysicsMaterial GroundPhysics
        {
            get
            {
                if (groundPhys == null)
                {
                    groundPhys = new PhysicsMaterial("표면");
                    groundPhys.dynamicFriction = 0.85f;
                    groundPhys.staticFriction = 0.95f;
                    groundPhys.bounciness = 0f;
                    groundPhys.frictionCombine = PhysicsMaterialCombine.Maximum;
                }
                return groundPhys;
            }
        }
        static PhysicsMaterial groundPhys;

        /// <summary>우주복 겉감 — 잘 미끄러지지 않되 바닥에 들러붙지도 않게.</summary>
        public static PhysicsMaterial SuitPhysics
        {
            get
            {
                if (suitPhys == null)
                {
                    suitPhys = new PhysicsMaterial("우주복");
                    suitPhys.dynamicFriction = 0.55f;
                    suitPhys.staticFriction = 0.65f;
                    suitPhys.bounciness = 0.02f;
                    suitPhys.frictionCombine = PhysicsMaterialCombine.Average;
                }
                return suitPhys;
            }
        }
        static PhysicsMaterial suitPhys;

        /// <summary>부츠 밑창 — 몸을 미는 것은 스프링이지 발이 아니다. 마찰은 낮게 둔다.</summary>
        public static PhysicsMaterial FootPhysics
        {
            get
            {
                if (footPhys == null)
                {
                    footPhys = new PhysicsMaterial("부츠");
                    footPhys.dynamicFriction = 0.30f;
                    footPhys.staticFriction = 0.35f;
                    footPhys.bounciness = 0f;
                    footPhys.frictionCombine = PhysicsMaterialCombine.Minimum;
                }
                return footPhys;
            }
        }
        static PhysicsMaterial footPhys;
    }
}
