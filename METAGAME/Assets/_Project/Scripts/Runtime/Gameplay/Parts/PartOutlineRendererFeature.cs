using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Metagame.Gameplay.Parts
{
    /// <summary>
    /// 핵심 파츠에 테두리를 씌우고, 용접으로 묶인 것끼리는 <b>하나의 윤곽</b>으로 보이게
    /// 한다 (S15P21B202-562).
    ///
    /// <para><b>두 패스다.</b> 먼저 파츠를 마스크 텍스처에 <see cref="PartOutlineGroups"/>
    /// 가 매긴 덩어리 번호로 그리고, 다음에 화면 전체를 훑어 <b>이웃 번호가 다른 자리</b>
    /// 에만 선을 올린다.</para>
    ///
    /// <para><b>왜 오브젝트별 외곽선이 아닌가.</b> 부풀린 메시를 뒤집어 그리는 흔한 방법은
    /// 파츠마다 자기 윤곽을 그린다. 그러면 용접해도 두 테두리가 겹칠 뿐 <b>하나가 되지
    /// 않는다</b> — 이 기능이 보여 주려는 것이 바로 그 "하나됨" 이라, 방식 선택이 곧
    /// 요구사항이다.</para>
    ///
    /// <para>구조는 <c>AtmosphereHazeRendererFeature</c> 를 따른다 — 같은 저장소에서 두
    /// 가지 방법을 쓰지 않는다.</para>
    /// </summary>
    public sealed class PartOutlineRendererFeature : ScriptableRendererFeature
    {
        [SerializeField] Material maskMaterial;
        [SerializeField] Material outlineMaterial;
        [SerializeField] RenderPassEvent injectionPoint = RenderPassEvent.BeforeRenderingPostProcessing;

        PartOutlinePass pass;

        public Material MaskMaterial
        {
            get { return maskMaterial; }
            set { maskMaterial = value; Create(); }
        }

        public Material OutlineMaterial
        {
            get { return outlineMaterial; }
            set { outlineMaterial = value; Create(); }
        }

        public override void Create()
        {
            pass = new PartOutlinePass { renderPassEvent = injectionPoint };
            pass.requiresIntermediateTexture = true;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (GameSession.Instance != null && GameSession.Instance.EndingActive) return;
            if (maskMaterial == null || outlineMaterial == null || pass == null) return;
            if (renderingData.cameraData.cameraType == CameraType.Preview ||
                renderingData.cameraData.cameraType == CameraType.Reflection)
                return;

            // 그릴 파츠가 없으면 패스를 아예 걸지 않는다 — 로비처럼 파츠가 없는 장면에서
            // 마스크 텍스처를 잡고 지우는 비용만 내지 않게 한다.
            if (!PartOutlineTargets.Collect()) return;

            pass.Setup(maskMaterial, outlineMaterial);
            renderer.EnqueuePass(pass);
        }

        protected override void Dispose(bool disposing)
        {
            pass = null;
        }

        sealed class PartOutlinePass : ScriptableRenderPass
        {
            static readonly int MaskId = Shader.PropertyToID("_MetagamePartOutlineMask");

            sealed class MaskPassData
            {
                public Material material;
            }

            sealed class OutlinePassData
            {
                public TextureHandle mask;
                public Material material;
            }

            Material mask;
            Material outline;

            public void Setup(Material maskMaterial, Material outlineMaterial)
            {
                mask = maskMaterial;
                outline = outlineMaterial;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (mask == null || outline == null) return;

                var resources = frameData.Get<UniversalResourceData>();
                if (!resources.activeColorTexture.IsValid() || !resources.activeDepthTexture.IsValid()) return;

                // 마스크는 R8 한 장이면 족하다 — 번호만 담는다. 핵심 파츠는 네 개라
                // 255 칸이 남아돈다.
                var descriptor = renderGraph.GetTextureDesc(resources.activeColorTexture);
                descriptor.name = "_MetagamePartOutlineMask";
                descriptor.format = GraphicsFormat.R8_UNorm;
                descriptor.clearBuffer = true;
                descriptor.clearColor = Color.clear;
                descriptor.depthBufferBits = 0;
                var maskTexture = renderGraph.CreateTexture(descriptor);

                // ── 마스크: 파츠를 번호로 그린다 ──────────────────
                //
                // 카메라의 실제 깊이를 함께 물린다. 그래야 벽이나 다른 파츠에 가려진
                // 부분이 마스크에 안 찍힌다 — 안 물리면 테두리가 벽을 뚫고 보여
                // 안내가 아니라 투시가 된다.
                using (var builder = renderGraph.AddRasterRenderPass<MaskPassData>(
                           "Part Outline Mask", out var data))
                {
                    data.material = mask;
                    builder.SetRenderAttachment(maskTexture, 0, AccessFlags.Write);
                    builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.Read);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc((MaskPassData passData, RasterGraphContext context) =>
                        DrawMask(context.cmd, passData.material));
                }

                // ── 테두리: 번호가 바뀌는 자리에만 선 ─────────────
                using (var builder = renderGraph.AddRasterRenderPass<OutlinePassData>(
                           "Part Outline", out var data))
                {
                    data.mask = maskTexture;
                    data.material = outline;
                    builder.UseTexture(maskTexture, AccessFlags.Read);
                    builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc((OutlinePassData passData, RasterGraphContext context) =>
                    {
                        passData.material.SetTexture(MaskId, passData.mask);
                        context.cmd.DrawProcedural(Matrix4x4.identity, passData.material, 0,
                                                   MeshTopology.Triangles, 3, 1);
                    });
                }
            }

            /// <summary>
            /// 파츠를 하나씩 그린다.
            ///
            /// <para><b>렌더러 목록 대신 직접 그리는 이유.</b> 파츠 재질(DoughSurface ·
            /// 블렌더 재질)에는 이 패스가 고를 셰이더 태그가 없다. 태그를 붙이러 다니는
            /// 것보다, <b>네 개짜리 목록</b>을 직접 도는 편이 값도 싸고 무엇이 그려지는지도
            /// 분명하다.</para>
            ///
            /// <para><b>번호는 그리기 직전에 전역으로 넣는다.</b> 재질 하나를 공유하면서
            /// 파츠마다 다른 값을 주는 다른 길은 렌더러마다
            /// <see cref="MaterialPropertyBlock"/> 을 붙이는 것인데, 그러면 그 렌더러가
            /// SRP Batcher 대상에서 빠져 <b>파츠의 본 렌더링 배칭까지 깨진다</b> —
            /// 테두리 하나 그리자고 파츠 전체를 느리게 만드는 셈이다. 여기는 순서대로
            /// 그리므로 전역 하나로 충분하다.</para>
            /// </summary>
            static void DrawMask(RasterCommandBuffer cmd, Material material)
            {
                var targets = PartOutlineTargets.Targets;
                for (var i = 0; i < targets.Count; i++)
                {
                    var target = targets[i];
                    if (target.Renderer == null) continue;

                    cmd.SetGlobalFloat(PartOutlineTargets.OutlineGroupId, target.Group);
                    for (var sub = 0; sub < target.SubMeshCount; sub++)
                        cmd.DrawRenderer(target.Renderer, material, sub, 0);
                }
            }
        }
    }
}
