using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace Metagame.Gameplay
{
    /// <summary>Scene-scoped fog pass shared by the player build and editor preview.</summary>
    public sealed class Stage1FogRendererFeature : ScriptableRendererFeature
    {

        private FogPass _pass;
        public override void Create()
        {
            _pass = new FogPass { renderPassEvent = RenderPassEvent.BeforeRenderingTransparents };
            _pass.ConfigureInput(ScriptableRenderPassInput.Depth);
            _pass.requiresIntermediateTexture = true;
        }
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData data)
        {
            var owner = Stage1FogController.Active;
            if (owner == null || !owner.ShaderEnabled || owner.Material == null) return;
            if (data.cameraData.cameraType != CameraType.Game && data.cameraData.cameraType != CameraType.SceneView) return;
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene() != owner.gameObject.scene) return;
            _pass.Material = owner.Material;
            renderer.EnqueuePass(_pass);
        }
        private sealed class FogPass : ScriptableRenderPass
        {
            public Material Material;
            private sealed class PassData { public TextureHandle Source; public Material Material; }
            public override void RecordRenderGraph(RenderGraph graph, ContextContainer context)
            {
                var resources = context.Get<UniversalResourceData>();
                if (resources.isActiveTargetBackBuffer) return;
                var desc = graph.GetTextureDesc(resources.activeColorTexture);
                desc.name = "Stage1 Fog Comparison Source";
                desc.clearBuffer = false;
                var source = graph.CreateTexture(desc);
                graph.AddCopyPass(resources.activeColorTexture, source, "Stage1 Fog Copy");
                using (var builder = graph.AddRasterRenderPass<PassData>("Stage1 Bounded Depth Fog", out var data))
                {
                    data.Source = source;
                    data.Material = Material;
                    builder.UseTexture(source, AccessFlags.Read);
                    builder.UseTexture(resources.cameraDepthTexture, AccessFlags.Read);
                    builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);
                    builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.Read);
                    builder.SetRenderFunc((PassData pass, RasterGraphContext ctx) =>
                        Blitter.BlitTexture(ctx.cmd, pass.Source, new Vector4(1, 1, 0, 0), pass.Material, 0));
                }
            }
        }
    }
}
