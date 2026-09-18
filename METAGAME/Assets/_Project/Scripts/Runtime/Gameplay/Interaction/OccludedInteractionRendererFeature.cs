using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Metagame.Gameplay.Interaction
{
    /// <summary>가려진 상호작용 물체 윤곽을 로컬 플레이어 카메라에만 표시한다.</summary>
    public sealed class OccludedInteractionRendererFeature : ScriptableRendererFeature
    {
        [SerializeField] Color _outlineColor = new Color(1f, 0.78f, 0.2f, 0.95f);
        [SerializeField, Range(1f, 6f)] float _thickness = 2f;
        Material _mask;
        Material _outline;
        OutlinePass _pass;

        public override void Create()
        {
            Release();
            var maskShader = Resources.Load<Shader>("OccludedInteractionMask");
            var outlineShader = Resources.Load<Shader>("OccludedInteractionOutline");
            if (maskShader == null || outlineShader == null) return;
            _mask = CoreUtils.CreateEngineMaterial(maskShader);
            _outline = CoreUtils.CreateEngineMaterial(outlineShader);
            _pass = new OutlinePass(_mask, _outline);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var session = GameSession.Instance;
            var camera = renderingData.cameraData.camera;
            if (!Application.isPlaying || session == null || session.EndingActive ||
                session.LocalPlayer == null || session.LocalPlayer.Cam == null ||
                camera != session.LocalPlayer.Cam.Cam ||
                renderingData.cameraData.renderType != CameraRenderType.Base ||
                _pass == null || !_pass.Collect(camera)) return;
            _outline.SetColor("_OutlineColor", _outlineColor);
            _outline.SetFloat("_OutlineThickness", _thickness);
            renderer.EnqueuePass(_pass);
        }

        protected override void Dispose(bool disposing) => Release();
        void Release()
        {
            CoreUtils.Destroy(_mask);
            CoreUtils.Destroy(_outline);
            _mask = null;
            _outline = null;
            _pass = null;
        }

        sealed class OutlinePass : ScriptableRenderPass
        {
            static readonly int MaskId = Shader.PropertyToID("_InteractionMask");
            static readonly int DepthId = Shader.PropertyToID("_InteractionSceneDepth");
            static readonly int GroupId = Shader.PropertyToID("_InteractionGroup");
            readonly Material _mask;
            readonly Material _outline;
            readonly OccludedInteractionTargets _targets = new();

            sealed class MaskData
            {
                public Material Material;
                public System.Collections.Generic.IReadOnlyList<OccludedInteractionTargets.Target> Targets;
                public int Start;
                public int End;
            }
            sealed class OutlineData
            {
                public Material Material;
                public TextureHandle Mask;
                public TextureHandle Depth;
                public Rect Scissor;
                public readonly MaterialPropertyBlock Properties = new();
            }

            public OutlinePass(Material mask, Material outline)
            {
                _mask = mask;
                _outline = outline;
                renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
                requiresIntermediateTexture = true;
                ConfigureInput(ScriptableRenderPassInput.Depth);
            }
            public bool Collect(Camera camera) => _targets.Collect(camera);

            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                if (!resources.cameraDepthTexture.IsValid() || !resources.activeColorTexture.IsValid()) return;
                var camera = frameData.Get<UniversalCameraData>().camera;
                var targets = _targets.Targets;
                // 대상별 마스크를 사용해야 파츠 뒤에 숨은 도구도 사라지지 않는다.
                for (var start = 0; start < targets.Count;)
                {
                    var end = start + 1;
                    var bounds = targets[start].Renderer.bounds;
                    while (end < targets.Count && targets[end].Group == targets[start].Group)
                    {
                        bounds.Encapsulate(targets[end].Renderer.bounds);
                        end++;
                    }
                    var descriptor = graph.GetTextureDesc(resources.activeColorTexture);
                    descriptor.name = "Interaction silhouette and eye depth";
                    descriptor.format = GraphicsFormat.R32G32_SFloat;
                    descriptor.depthBufferBits = DepthBits.None;
                    descriptor.msaaSamples = MSAASamples.None;
                    descriptor.bindTextureMS = false;
                    descriptor.clearBuffer = true;
                    descriptor.clearColor = Color.clear;
                    descriptor.filterMode = FilterMode.Point;
                    var mask = graph.CreateTexture(descriptor);
                    descriptor.name = "Interaction private depth";
                    descriptor.colorFormat = GraphicsFormat.None;
                    descriptor.depthBufferBits = DepthBits.Depth32;
                    var depth = graph.CreateTexture(descriptor);
                    using (var builder = graph.AddRasterRenderPass<MaskData>("Interaction silhouette", out var data))
                    {
                        data.Material = _mask;
                        data.Targets = targets;
                        data.Start = start;
                        data.End = end;
                        builder.SetRenderAttachment(mask, 0);
                        builder.SetRenderAttachmentDepth(depth, AccessFlags.Write);
                        builder.AllowGlobalStateModification(true);
                        builder.SetRenderFunc((MaskData pass, RasterGraphContext context) =>
                        {
                            for (var i = pass.Start; i < pass.End; i++)
                            {
                                var target = pass.Targets[i];
                                if (target.Renderer == null) continue;
                                context.cmd.SetGlobalFloat(GroupId, target.Group);
                                for (var sub = 0; sub < target.SubMeshCount; sub++)
                                    context.cmd.DrawRenderer(target.Renderer, pass.Material, sub, 0);
                            }
                        });
                    }
                    using (var builder = graph.AddRasterRenderPass<OutlineData>("Occluded interaction outline", out var data))
                    {
                        data.Material = _outline;
                        data.Mask = mask;
                        data.Depth = resources.cameraDepthTexture;
                        data.Scissor = ScreenRect(camera, bounds, descriptor.width, descriptor.height);
                        builder.UseTexture(mask);
                        builder.UseTexture(data.Depth);
                        builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);
                        builder.SetRenderFunc((OutlineData pass, RasterGraphContext context) =>
                        {
                            pass.Properties.SetTexture(MaskId, pass.Mask);
                            pass.Properties.SetTexture(DepthId, pass.Depth);
                            context.cmd.EnableScissorRect(pass.Scissor);
                            context.cmd.DrawProcedural(Matrix4x4.identity, pass.Material, 0, MeshTopology.Triangles, 3, 1, pass.Properties);
                            context.cmd.DisableScissorRect();
                        });
                    }
                    start = end;
                }
            }

            // 전체화면 합성 셰이더를 실행하되 대상의 투영 사각형 안에서만 픽셀을 처리한다.
            static Rect ScreenRect(Camera camera, Bounds bounds, int width, int height)
            {
                var min = Vector2.one;
                var max = Vector2.zero;
                for (var i = 0; i < 8; i++)
                {
                    var offset = Vector3.Scale(bounds.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var point = camera.WorldToViewportPoint(bounds.center + offset);
                    if (point.z <= camera.nearClipPlane) return new Rect(0, 0, width, height);
                    min = Vector2.Min(min, point);
                    max = Vector2.Max(max, point);
                }
                return Rect.MinMaxRect(
                    Mathf.Max(0, Mathf.Floor(min.x * width) - 2),
                    Mathf.Max(0, Mathf.Floor(min.y * height) - 2),
                    Mathf.Min(width, Mathf.Ceil(max.x * width) + 2),
                    Mathf.Min(height, Mathf.Ceil(max.y * height) + 2));
            }
        }
    }
}