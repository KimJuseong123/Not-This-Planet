using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Metagame.Gameplay
{
    /// <summary>Local presentation only; the existing storm clock owns travel and physics.</summary>
    public sealed class MagneticStormVisual : MonoBehaviour
    {
        public const string ResourcePath = "Stage1MagneticStorm/Stage1MagneticStorm";
        public const double LoopSeconds = 6d;
        [SerializeField] Renderer[] animatedRenderers;
        [SerializeField] Transform[] debris;
        [SerializeField] bool previewAutonomously = true;
        [Tooltip("연기 불투명도. 0이면 투명, 1이면 원래 연기입니다. 자기장 선은 유지됩니다.")]
        [SerializeField, Range(0f, 1f)] float smokeOpacity = .38f;
        [Header("Directional cloud billowing")]
        [InspectorName("Travel Direction Billow")]
        [Tooltip("실제 이동 벡터 방향 쪽 연기의 뭉게짐. 모델의 정면이나 가로축이 아니라 진행 방향을 따릅니다.")]
        [SerializeField, Range(0f, 3f)] float frontBillowStrength = 1.4f;
        [InspectorName("Opposite Direction Billow")]
        [Tooltip("실제 이동 벡터의 반대쪽 연기의 뭉게짐. 진행 방향 쪽과 독립적으로 조절합니다.")]
        [SerializeField, Range(0f, 3f)] float rearBillowStrength = 1.8f;
        static readonly int SmokeOpacityId = Shader.PropertyToID("_SmokeOpacity");
        static readonly int FrontBillowId = Shader.PropertyToID("_FrontBillowStrength");
        static readonly int RearBillowId = Shader.PropertyToID("_RearBillowStrength");
        static readonly int WakeAgesId = Shader.PropertyToID("_WakeAges");
        static readonly int TravelAxisId = Shader.PropertyToID("_TravelAxisUV");
        static readonly int TravelOffsetId = Shader.PropertyToID("_TravelOffsetUV");
        static readonly int UpOffsetId = Shader.PropertyToID("_UpOffsetUV");
        static readonly int PhaseId = Shader.PropertyToID("_StormPhase");
        static readonly int OpacityId = Shader.PropertyToID("_StormOpacity");
        MaterialPropertyBlock properties;
        Vector3[] debrisPositions;
        Quaternion[] debrisRotations;
        readonly MeshRenderer[] wakeRenderers = new MeshRenderer[2];
        MeshRenderer smokeRenderer;
        readonly List<DepthRequest> depthRequests = new List<DepthRequest>();

        struct DepthRequest
        {
            public UniversalAdditionalCameraData data;
            public CameraOverrideOption previous;
        }

        void OnEnable() { RenderPipelineManager.beginCameraRendering += RequireDepth; }
        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= RequireDepth;
            for (int i = 0; i < depthRequests.Count; i++)
            {
                var request = depthRequests[i];
                if (request.data != null && request.data.requiresDepthOption == CameraOverrideOption.On)
                    request.data.requiresDepthOption = request.previous;
            }
            depthRequests.Clear();
            HideWakes();
        }

        void RequireDepth(ScriptableRenderContext context, Camera camera)
        {
            if (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView) return;
            var data = camera.GetUniversalAdditionalCameraData();
            if (data.requiresDepthOption == CameraOverrideOption.On) return;
            // Request scene depth only while a storm is visible, including lower PC quality tiers.
            depthRequests.Add(new DepthRequest { data = data, previous = data.requiresDepthOption });
            data.requiresDepthOption = CameraOverrideOption.On;
        }

        public static float PhaseAt(double time)
        {
            if (double.IsNaN(time) || double.IsInfinity(time)) return 0f;
            return (float)((time % LoopSeconds + LoopSeconds) % LoopSeconds / LoopSeconds);
        }

        void Awake() { Initialize(); }
        void Initialize()
        {
            if (properties != null) return;
            properties = new MaterialPropertyBlock();
            if (animatedRenderers == null) animatedRenderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < animatedRenderers.Length; i++)
            {
                var renderer = animatedRenderers[i] as MeshRenderer;
                if (renderer != null && renderer.sharedMaterial != null
                    && renderer.sharedMaterial.HasProperty(WakeAgesId))
                { smokeRenderer = renderer; break; }
            }
            if (debris == null) debris = new Transform[0];
            debrisPositions = new Vector3[debris.Length];
            debrisRotations = new Quaternion[debris.Length];
            for (int i = 0; i < debris.Length; i++)
            {
                if (debris[i] == null) continue;
                debrisPositions[i] = debris[i].localPosition;
                debrisRotations[i] = debris[i].localRotation;
            }
        }

        public void SetExternallyDriven() { previewAutonomously = false; }

        public void SetExtents(Vector3 halfExtents)
        {
            transform.localScale = new Vector3(halfExtents.x / 12f, halfExtents.y / 2.5f, halfExtents.z / 5f);
        }

        public void Evaluate(double time, float opacity = 1f, Vector3? travelDirection = null)
        {
            Initialize();
            float phase = PhaseAt(time);
            properties.SetFloat(PhaseId, phase);
            properties.SetFloat(OpacityId, Mathf.Clamp01(opacity));
            properties.SetFloat(SmokeOpacityId, Mathf.Clamp01(smokeOpacity));
            properties.SetFloat(FrontBillowId, Mathf.Clamp(frontBillowStrength, 0f, 3f));
            properties.SetFloat(RearBillowId, Mathf.Clamp(rearBillowStrength, 0f, 3f));
            properties.SetVector(WakeAgesId, new Vector4(0, 0, 1, 0));
            if (smokeRenderer != null) SetTravelFrame(smokeRenderer.transform, travelDirection ?? transform.forward);
            for (int i = 0; i < animatedRenderers.Length; i++)
                if (animatedRenderers[i] != null) animatedRenderers[i].SetPropertyBlock(properties);
            for (int i = 0; i < debris.Length; i++)
            {
                if (debris[i] == null) continue;
                float angle = (phase + i * .173f) * Mathf.PI * 2f;
                debris[i].localPosition = debrisPositions[i] + new Vector3(
                    .36f * Mathf.Cos(angle), .42f * Mathf.Sin(angle), .25f * Mathf.Sin(angle * 2f));
                debris[i].localRotation = debrisRotations[i] * Quaternion.Euler(phase * 360f, phase * 720f, 0f);
            }
        }

        public void SetBodyVisible(bool visible)
        {
            Initialize();
            for (int i = 0; i < animatedRenderers.Length; i++)
                if (animatedRenderers[i] != null) animatedRenderers[i].enabled = visible;
        }

        public void HideWakes()
        {
            for (int i = 0; i < wakeRenderers.Length; i++)
                if (wakeRenderers[i] != null) wakeRenderers[i].enabled = false;
        }

        public void EvaluateWake(int index, StormWake wake, double time, float opacity)
        {
            Initialize();
            if (index < 0 || index >= wakeRenderers.Length) return;
            if (!wake.Pose.IsActive || opacity <= 0f || smokeRenderer == null)
            {
                if (wakeRenderers[index] != null) wakeRenderers[index].enabled = false;
                return;
            }
            if (wakeRenderers[index] == null)
            {
                var go = new GameObject("Magnetic Storm Wake " + index);
                go.layer = smokeRenderer.gameObject.layer;
                // A sibling avoids shearing an older wake when the next sweep rotates
                // beneath the main volume's non-uniform scale.
                go.transform.SetParent(transform.parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = smokeRenderer.GetComponent<MeshFilter>().sharedMesh;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = smokeRenderer.sharedMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                wakeRenderers[index] = renderer;
            }
            var target = wakeRenderers[index];
            // Reconstruct the world pose each tick; no stored frame history.
            target.transform.SetPositionAndRotation(wake.Pose.Center, wake.Pose.Rotation);
            var parentScale = target.transform.parent != null ? target.transform.parent.lossyScale : Vector3.one;
            target.transform.localScale = new Vector3(
                wake.Pose.HalfExtents.x / (12f * Mathf.Max(.0001f, Mathf.Abs(parentScale.x))),
                wake.Pose.HalfExtents.y / (2.5f * Mathf.Max(.0001f, Mathf.Abs(parentScale.y))),
                wake.Pose.HalfExtents.z / (5f * Mathf.Max(.0001f, Mathf.Abs(parentScale.z))));
            properties.SetFloat(PhaseId, PhaseAt(time));
            properties.SetFloat(OpacityId, Mathf.Clamp01(opacity));
            properties.SetFloat(SmokeOpacityId, Mathf.Clamp01(smokeOpacity) * Mathf.Clamp01(opacity));
            properties.SetFloat(FrontBillowId, Mathf.Clamp(frontBillowStrength, 0f, 3f));
            properties.SetFloat(RearBillowId, Mathf.Clamp(rearBillowStrength, 0f, 3f));
            properties.SetVector(WakeAgesId, new Vector4(wake.HeadAge, wake.TailAge, wake.Lifetime, 1));
            SetTravelFrame(target.transform, wake.Pose.Direction);
            target.SetPropertyBlock(properties);
            target.enabled = true;
        }

        void SetTravelFrame(Transform volume, Vector3 direction)
        {
            direction = direction.sqrMagnitude > .0001f ? direction.normalized : transform.forward;
            // Dotting this covector with (uv-.5)*2 gives position along actual travel.
            // Use each renderer's matrix: imported FBX rotation and nonuniform scale matter.
            Vector3 projection = new Vector3(
                Vector3.Dot(volume.TransformVector(Vector3.right * 12f), direction),
                Vector3.Dot(volume.TransformVector(Vector3.up * 2.5f), direction),
                Vector3.Dot(volume.TransformVector(Vector3.forward * 5f), direction));
            float reach = Mathf.Max(.0001f, Mathf.Abs(projection.x) + Mathf.Abs(projection.y) + Mathf.Abs(projection.z));
            properties.SetVector(TravelAxisId, projection / reach);
            var worldToLocal = volume.worldToLocalMatrix;
            var uvScale = new Vector3(1f / 24f, 1f / 5f, 1f / 10f);
            // UV offset per world metre, so a broad mesh cannot amplify sideways motion.
            properties.SetVector(TravelOffsetId, Vector3.Scale(worldToLocal.MultiplyVector(direction), uvScale));
            properties.SetVector(UpOffsetId, Vector3.Scale(worldToLocal.MultiplyVector(Vector3.up), uvScale));
        }

        void OnDestroy()
        {
            for (int i = 0; i < wakeRenderers.Length; i++)
            {
                if (wakeRenderers[i] == null) continue;
                if (Application.isPlaying) Destroy(wakeRenderers[i].gameObject);
                else DestroyImmediate(wakeRenderers[i].gameObject);
            }
        }

        void Update()
        {
            if (previewAutonomously) Evaluate(Time.timeAsDouble);
        }
    }
}
