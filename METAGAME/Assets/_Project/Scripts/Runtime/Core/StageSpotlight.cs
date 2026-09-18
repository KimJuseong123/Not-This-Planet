using UnityEngine;

namespace Metagame.Core
{
    /// <summary>
    /// 무대 조명 한 벌 — 빛기둥 · 바닥 웅덩이 · 위쪽 광원 원반 (S15P21B202-631 · 639).
    ///
    /// <para><b>Light 컴포넌트가 아니라 그림이다.</b> Unity 의 광원은 표면을 밝힐 뿐,
    /// 공기 중의 빛줄기는 URP 기본으로 렌더되지 않는다(볼류메트릭이 필요하다).
    /// 무대 조명처럼 보이려면 직접 그려야 한다. 셋 다 가산 합성이라 그 씬에서 광원이
    /// 표면에 기여하든 말든 항상 보인다 — 커마에서는 광원이 전혀 안 먹었고 엔딩에서는
    /// 먹는데, 그림은 양쪽에서 똑같이 나온다.</para>
    ///
    /// <para><b>축이 맞아야 한다.</b> 광원을 앞옆으로 비스듬히 두면 빛기둥이 한쪽으로
    /// 치우쳐 보인다 — 631 에서 정면은 왼쪽으로, 옆면은 뒤로 쏠렸다. 광원은 캐릭터의
    /// <b>정수리 바로 위</b>, 웅덩이는 <b>광원 바로 아래</b>에 둔다. <see cref="Root"/>
    /// 를 캐릭터 발밑에 놓으면 그 관계가 저절로 지켜진다.</para>
    /// </summary>
    public sealed class StageSpotlight
    {
        /// <summary>커마에서 쓰는 높이. 키 1.147m 인 캐릭터의 정수리보다 0.83m 위다.</summary>
        public const float DefaultApexHeight = 1.98f;
        public const float DefaultTopRadius = 0.085f;
        public const float DefaultBottomRadius = 0.78f;

        // 셋의 기본 세기. Intensity 가 여기에 곱해진다.
        const float BeamStrength = 0.30f;
        const float PoolStrength = 0.85f;
        const float LampStrength = 1f;

        // 원반은 모양이 하나뿐이라 돌려 쓴다. 플레이 모드를 나가면 파괴되므로 null 검사로
        // 다시 만든다 — 그래서 인스턴스가 이것을 파괴하면 안 된다.
        static Mesh sharedDisc;

        readonly Material _beamMaterial, _poolMaterial, _lampMaterial;
        readonly Mesh _coneMesh;
        float _intensity = 1f;

        public Transform Root { get; private set; }
        public GameObject Beam { get; private set; }
        public GameObject Pool { get; private set; }
        public GameObject Lamp { get; private set; }

        /// <summary>빛이 나오는 자리 — 루트(발밑) 기준 높이.</summary>
        public float ApexHeight { get; private set; }

        StageSpotlight(Shader shader, string name, Mesh cone)
        {
            _beamMaterial = new Material(shader) { name = name + " Beam" };
            _poolMaterial = new Material(shader) { name = name + " Pool" };
            _lampMaterial = new Material(shader) { name = name + " Lamp" };
            _coneMesh = cone;
        }

        /// <summary>
        /// 조명 한 벌을 만든다. 셰이더를 못 찾으면 <c>null</c> 이다.
        /// </summary>
        /// <param name="name">자식 이름 앞에 붙는다 — "<c>{name} Beam</c>" 처럼.</param>
        /// <param name="parent">루트를 붙일 곳. <b>루트의 로컬 원점이 캐릭터 발밑</b>이다.</param>
        /// <param name="layer">그 카메라가 보는 레이어. 엔딩 무대는 31 이라 0 이면 안 보인다.</param>
        public static StageSpotlight Create(string name, Transform parent,
            float apexHeight = DefaultApexHeight,
            float topRadius = DefaultTopRadius,
            float bottomRadius = DefaultBottomRadius,
            float poolWidth = 1.9f, float poolDepth = 1.35f,
            int layer = 0)
        {
            var shader = Resources.Load<Shader>("SpotlightGlow");
            if (shader == null)
            {
                Debug.LogError("[StageSpotlight] SpotlightGlow 셰이더를 찾지 못했습니다.");
                return null;
            }
            if (sharedDisc == null) sharedDisc = BuildDisc();

            var spot = new StageSpotlight(shader, name, BuildCone(topRadius, bottomRadius, apexHeight))
            { ApexHeight = apexHeight };

            spot.Root = new GameObject(name) { layer = layer }.transform;
            if (parent != null) spot.Root.SetParent(parent, false);

            // 원뿔은 꼭짓점이 원점이고 아래로 뻗는다. 꼭짓점을 광원 자리에 둔다.
            spot.Beam = spot.Add(name + " Beam", spot._coneMesh, spot._beamMaterial, 0f, BeamStrength, layer);
            spot.Beam.transform.localPosition = new Vector3(0f, apexHeight, 0f);

            // 웅덩이는 광원 바로 아래. 0.01m 띄워야 바닥면과 z 싸움을 하지 않는다.
            spot.Pool = spot.Add(name + " Pool", sharedDisc, spot._poolMaterial, 1f, PoolStrength, layer);
            spot.Pool.transform.localPosition = new Vector3(0f, 0.01f, 0f);
            spot.Pool.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            spot.Pool.transform.localScale =
                new Vector3(bottomRadius * poolWidth, bottomRadius * poolDepth, 1f);

            // 광원 원반은 카메라 쪽으로 돌려 어느 각도에서나 동그랗게 보인다.
            spot.Lamp = spot.Add(name + " Lamp", sharedDisc, spot._lampMaterial, 1f, LampStrength, layer);
            spot.Lamp.transform.localPosition = new Vector3(0f, apexHeight, 0f);
            spot.Lamp.transform.localScale = Vector3.one * (topRadius * 4.2f);

            return spot;
        }

        GameObject Add(string name, Mesh mesh, Material material,
            float radialMix, float strength, int layer)
        {
            var go = new GameObject(name) { layer = layer };
            go.transform.SetParent(Root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            material.SetFloat("_RadialMix", radialMix);
            material.SetFloat("_Strength", strength);
            renderer.sharedMaterial = material;
            return go;
        }

        /// <summary>
        /// 세기를 여닫는다. <b>오브젝트를 껐다 켜는 대신 이것을 쓴다</b> — 켜지고 꺼지는
        /// 것 자체가 연출이라 한 프레임에 튀면 안 된다.
        ///
        /// <para><b>1 을 넘겨도 된다.</b> "뿅" 하고 켜질 때 한 번 넘겼다 가라앉는 데
        /// 쓴다(엔딩은 1.28 까지 간다). 가산 합성이라 넘겨도 깨지지 않는다.</para>
        /// </summary>
        public float Intensity
        {
            get => _intensity;
            set
            {
                _intensity = Mathf.Clamp(value, 0f, 2f);
                if (_beamMaterial != null) _beamMaterial.SetFloat("_Strength", BeamStrength * _intensity);
                if (_poolMaterial != null) _poolMaterial.SetFloat("_Strength", PoolStrength * _intensity);
                if (_lampMaterial != null) _lampMaterial.SetFloat("_Strength", LampStrength * _intensity);
                // 다 꺼졌으면 그리지도 않는다. 세기 0 이면 가산 합성이라 화소는 안 바뀌지만
                // 인원수만큼 있어 드로우콜은 그대로 나간다.
                bool on = _intensity > 0.001f;
                if (Root != null && Root.gameObject.activeSelf != on) Root.gameObject.SetActive(on);
            }
        }

        /// <summary>광원 원반이 카메라를 마주보게 한다. 매 프레임 부른다.</summary>
        public void FaceCamera(Quaternion cameraRotation)
        {
            if (Lamp != null) Lamp.transform.rotation = cameraRotation;
        }

        public void Destroy()
        {
            Kill(_beamMaterial); Kill(_poolMaterial); Kill(_lampMaterial);
            Kill(_coneMesh);                    // sharedDisc 는 돌려 쓰므로 건드리지 않는다
            if (Root != null) Kill(Root.gameObject);
            Root = null; Beam = null; Pool = null; Lamp = null;
        }

        static void Kill(Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Object.Destroy(target);
            else Object.DestroyImmediate(target);
        }

        /// <summary>
        /// 끝이 열린 원뿔. 꼭짓점이 원점이고 아래(-Y)로 뻗는다.
        /// uv.y 는 위에서 0, 아래에서 1 이다(셰이더가 그렇게 읽는다).
        /// </summary>
        static Mesh BuildCone(float topRadius, float bottomRadius, float height)
        {
            const int segments = 40;
            var vertices = new Vector3[(segments + 1) * 2];
            var uv = new Vector2[vertices.Length];
            var colors = new Color[vertices.Length];
            var triangles = new int[segments * 6];
            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                float angle = t * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                vertices[i * 2] = dir * topRadius;
                vertices[i * 2 + 1] = dir * bottomRadius + Vector3.down * height;
                uv[i * 2] = new Vector2(t, 0f);
                uv[i * 2 + 1] = new Vector2(t, 1f);
                colors[i * 2] = Color.white;
                colors[i * 2 + 1] = new Color(1f, 1f, 1f, 0.55f);
            }
            for (int i = 0; i < segments; i++)
            {
                int v = i * 2, q = i * 6;
                triangles[q] = v; triangles[q + 1] = v + 1; triangles[q + 2] = v + 2;
                triangles[q + 3] = v + 2; triangles[q + 4] = v + 1; triangles[q + 5] = v + 3;
            }
            var mesh = new Mesh { name = "Spotlight Cone" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uv);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            return mesh;
        }

        /// <summary>가운데가 밝은 반지름 1 원반. 셰이더가 중심에서 방사로 옅어지게 한다.</summary>
        static Mesh BuildDisc()
        {
            const int segments = 48;
            var vertices = new Vector3[segments + 2];
            var uv = new Vector2[vertices.Length];
            var colors = new Color[vertices.Length];
            var triangles = new int[segments * 3];
            vertices[0] = Vector3.zero;
            uv[0] = new Vector2(0.5f, 0.5f);
            colors[0] = Color.white;
            for (int i = 0; i <= segments; i++)
            {
                float angle = i / (float)segments * Mathf.PI * 2f;
                var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                vertices[i + 1] = new Vector3(dir.x, dir.y, 0f);
                uv[i + 1] = new Vector2(0.5f + dir.x * 0.5f, 0.5f + dir.y * 0.5f);
                colors[i + 1] = new Color(1f, 1f, 1f, 0f);
            }
            for (int i = 0; i < segments; i++)
            {
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = i + 2;
            }
            var mesh = new Mesh { name = "Spotlight Disc" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uv);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            return mesh;
        }
    }
}
