using System.Collections;
using Metagame.Gameplay.Parts;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Metagame.Gameplay
{
    /// <summary>Scene presentation and authored carrier placement; gameplay remains in existing systems.</summary>
    public sealed class Stage1ConceptRuntime : MonoBehaviour
    {
        [SerializeField] private RenderPipelineAsset _pipeline;
        [SerializeField] private TractorBeam _carrier;
        private RenderPipelineAsset _previousPipeline;
        private bool _applied;

        public void Configure(RenderPipelineAsset pipeline, TractorBeam carrier)
        {
            _pipeline = pipeline;
            _carrier = carrier;
        }

        private void OnEnable()
        {
            if (_pipeline == null) return;
            _previousPipeline = QualitySettings.renderPipeline;
            QualitySettings.renderPipeline = _pipeline;
            _applied = true;
        }

        private IEnumerator Start()
        {
            // All composition-root Awakes have run. Never replace shared prefabs/materials.
            if (_carrier != null && _carrier.TryGetComponent<TransportVehicle>(out var vehicle))
                vehicle.PlaceOnGround(_carrier.transform.position, 180f);

            yield return null;
            var player = GameSession.Instance != null ? GameSession.Instance.LocalPlayer : null;
            if (player != null) player.DirectToolPickupRange = 1.4f;
            if (player != null && player.Cam != null)
            {
                var camera = player.Cam.Cam;
                camera.clearFlags = CameraClearFlags.Skybox;
                camera.farClipPlane = 400f;
                camera.allowHDR = true;
                camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            }
        }

        private void OnDisable()
        {
            if (_applied) QualitySettings.renderPipeline = _previousPipeline;
            _applied = false;
        }
    }
}
