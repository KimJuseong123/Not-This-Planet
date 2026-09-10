using UnityEngine;
using UnityEngine.Rendering;

namespace Metagame.Gameplay.Parts
{
    /// <summary>Local presentation of field state; no gameplay colliders.</summary>
    [DisallowMultipleComponent]
    public sealed class LowGravityFieldVisual : MonoBehaviour
    {
        LowGravityField field;
        Transform beam;
        Mesh mesh;
        Material material;
        MeshRenderer beamRenderer;
        float visibility;
        static readonly int VisibilityId = Shader.PropertyToID("_Visibility");

        public void Initialize(LowGravityField owner)
        {
            if (beam != null) return;
            field = owner;
            var shader = Resources.Load<Shader>("TractorFieldBeam");
            if (shader == null) return;
            material = new Material(shader) { name = "Tractor Field Cyan Glow" };
            var go = new GameObject("Tractor Field Beam");
            beam = go.transform;
            beam.SetParent(transform, false);
            mesh = BuildMesh(owner);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            beamRenderer = go.AddComponent<MeshRenderer>();
            beamRenderer.sharedMaterial = material;
            beamRenderer.shadowCastingMode = ShadowCastingMode.Off;
            beamRenderer.receiveShadows = false;
            beamRenderer.enabled = false;
        }

        void LateUpdate()
        {
            if (field == null || beam == null) return;
            visibility = Mathf.MoveTowards(visibility,
                field.isActiveAndEnabled && field.IsActive ? 1f : 0f, Time.deltaTime * 4f);
            beamRenderer.enabled = visibility > 0f;
            material.SetFloat(VisibilityId, visibility);
            // Gameplay uses world down even when the ship rolls. Cancel root scale too.
            beam.SetPositionAndRotation(field.Origin.position, Quaternion.identity);
            var scale = transform.lossyScale;
            beam.localScale = new Vector3(1f / Mathf.Max(Mathf.Abs(scale.x), 0.001f),
                1f / Mathf.Max(Mathf.Abs(scale.y), 0.001f),
                1f / Mathf.Max(Mathf.Abs(scale.z), 0.001f));
        }

        static Mesh BuildMesh(LowGravityField owner)
        {
            const int segments = 64;
            const int shells = 3;
            var vertices = new Vector3[(segments + 1) * 2 * shells];
            var uv = new Vector2[vertices.Length];
            var colors = new Color[vertices.Length];
            var triangles = new int[segments * 6 * shells];
            for (int shell = 0; shell < shells; shell++)
            {
                float width = shell == 0 ? 1f : shell == 1 ? 0.62f : 0.17f;
                var tint = shell == 2 ? new Color(0.25f, 0.95f, 1f, 0.30f)
                    : new Color(0.12f, 0.65f, 1f, shell == 0 ? 0.13f : 0.10f);
                int offset = shell * (segments + 1) * 2;
                for (int i = 0; i <= segments; i++)
                {
                    float u = (float)i / segments;
                    var direction = new Vector3(Mathf.Cos(u * Mathf.PI * 2f), 0f,
                        Mathf.Sin(u * Mathf.PI * 2f));
                    int v = offset + i * 2;
                    vertices[v] = direction * owner.RadiusAt(0f) * width;
                    vertices[v + 1] = direction * owner.GroundRadius * width + Vector3.down * owner.Depth;
                    uv[v] = new Vector2(u, 0f);
                    uv[v + 1] = new Vector2(u, 1f);
                    colors[v] = colors[v + 1] = tint;
                    if (i == segments) continue;
                    int t = (shell * segments + i) * 6;
                    triangles[t] = v; triangles[t + 1] = v + 1; triangles[t + 2] = v + 2;
                    triangles[t + 3] = v + 2; triangles[t + 4] = v + 1; triangles[t + 5] = v + 3;
                }
            }
            var result = new Mesh { name = "Tractor Field Layered Cone" };
            result.vertices = vertices;
            result.uv = uv;
            result.colors = colors;
            result.triangles = triangles;
            result.RecalculateNormals();
            result.RecalculateBounds();
            return result;
        }

        void OnDisable()
        {
            visibility = 0f;
            if (beamRenderer != null) beamRenderer.enabled = false;
        }

        void OnDestroy()
        {
            if (Application.isPlaying)
            {
                if (mesh != null) Destroy(mesh);
                if (material != null) Destroy(material);
            }
            else
            {
                if (mesh != null) DestroyImmediate(mesh);
                if (material != null) DestroyImmediate(material);
            }
        }
    }
}
