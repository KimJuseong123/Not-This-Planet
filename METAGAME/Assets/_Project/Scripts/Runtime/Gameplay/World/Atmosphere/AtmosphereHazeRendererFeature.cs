using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace Metagame.Gameplay
{
    /// <summary>
    /// URP RenderGraph-compatible depth haze pass. The pass is always safe to enqueue;
    /// the global enabled flag makes it a no-op when no atmosphere is active.
    /// </summary>
    public sealed class AtmosphereHazeRendererFeature : ScriptableRendererFeature
    {
        [SerializeField] Material passMaterial;
        [SerializeField] bool enableOnMobile;
        [SerializeField] RenderPassEvent injectionPoint = RenderPassEvent.BeforeRenderingPostProcessing;

        AtmosphereHazePass pass;

        public Material PassMaterial
        {
            get { return passMaterial; }
            set { passMaterial = value; Create(); }
        }

        public bool EnableOnMobile
        {
            get { return enableOnMobile; }
            set { enableOnMobile = value; }
        }

        public override void Create()
        {
            pass = new AtmosphereHazePass
            {
                renderPassEvent = injectionPoint
            };
            pass.ConfigureInput(ScriptableRenderPassInput.Depth);
            pass.requiresIntermediateTexture = true;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (GameSession.Instance != null && GameSession.Instance.EndingActive) return;
            if (passMaterial == null || pass == null) return;
            if (!enableOnMobile && Application.isMobilePlatform) return;
            bool hazeEnabled = Shader.GetGlobalFloat(AtmosphereController.HazeEnabledId) >= 0.5f;
            bool groundFogEnabled = Shader.GetGlobalFloat(AtmosphereController.GroundFogEnabledId) >= 0.5f;
            if (!hazeEnabled && !groundFogEnabled) return;
            if (renderingData.cameraData.cameraType == CameraType.Preview ||
                renderingData.cameraData.cameraType == CameraType.Reflection)
                return;

            pass.Setup(passMaterial);
            renderer.EnqueuePass(pass);
        }

        protected override void Dispose(bool disposing)
        {
            pass = null;
        }

        sealed class AtmosphereHazePass : ScriptableRenderPass
        {
            sealed class PassData
            {
                public TextureHandle source;
                public Material material;
            }

            Material material;

            public void Setup(Material value) { material = value; }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (material == null) return;

                var resources = frameData.Get<UniversalResourceData>();
                if (!resources.cameraColor.IsValid() || !resources.activeColorTexture.IsValid()) return;

                var descriptor = renderGraph.GetTextureDesc(resources.cameraColor);
                descriptor.name = "_MetagameAtmosphereHazeSource";
                descriptor.clearBuffer = false;
                var source = renderGraph.CreateTexture(descriptor);
                renderGraph.AddCopyPass(resources.cameraColor, source, "Atmosphere Haze Source");

                // The shader's stencil exclusion needs the camera's actual depth/stencil
                // attachment. A color-only blit cannot honor fog-exempt guide materials.
                using (var builder = renderGraph.AddRasterRenderPass<PassData>("Atmosphere Haze", out var data))
                {
                    data.source = source;
                    data.material = material;
                    builder.UseTexture(source, AccessFlags.Read);
                    builder.UseAllGlobalTextures(true);
                    builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);
                    builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.Read);
                    builder.SetRenderFunc((PassData passData, RasterGraphContext context) =>
                        Blitter.BlitTexture(context.cmd, passData.source, new Vector4(1, 1, 0, 0), passData.material, 0));
                }
            }
        }
    }
}
