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
        static readonly int PhaseId = Shader.PropertyToID("_StormPhase");
        static readonly int OpacityId = Shader.PropertyToID("_StormOpacity");
        MaterialPropertyBlock properties;
        Vector3[] debrisPositions;
        Quaternion[] debrisRotations;
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

        public void Evaluate(double time, float opacity = 1f)
        {
            Initialize();
            float phase = PhaseAt(time);
            properties.SetFloat(PhaseId, phase);
            properties.SetFloat(OpacityId, Mathf.Clamp01(opacity));
            for (int i = 0; i < animatedRenderers.Length; i++)
                if (animatedRenderers[i] != null) animatedRenderers[i].SetPropertyBlock(properties);
            for (int i = 0; i < debris.Length; i++)
            {
                if (debris[i] == null) continue;
                float angle = (phase + i * .173f) * Mathf.PI * 2f;
                debris[i].localPosition = debrisPositions[i] + new Vector3(
                    .18f * Mathf.Cos(angle), .20f * Mathf.Sin(angle), .13f * Mathf.Sin(angle * 2f));
                debris[i].localRotation = debrisRotations[i] * Quaternion.Euler(phase * 360f, phase * 720f, 0f);
            }
        }

        void Update()
        {
            if (previewAutonomously) Evaluate(Time.timeAsDouble);
        }
    }
}
