using System.Collections.Generic;
using UnityEngine;

namespace Metagame.Gameplay.Parts
{
    /// <summary>
    /// 이번 프레임에 테두리를 그릴 파츠와 그 <b>덩어리 번호</b>를 모은다
    /// (S15P21B202-562).
    ///
    /// <para>렌더러 피처가 매 프레임 <see cref="Collect"/> 를 부르고, 마스크 패스가
    /// <see cref="Targets"/> 를 돈다. 번호는 <see cref="MaterialPropertyBlock"/> 으로
    /// 각 렌더러에 실어 둔다 — 재질 하나를 공유하면서 파츠마다 다른 값을 주는 유일한
    /// 방법이다.</para>
    ///
    /// <para><b>버퍼를 재사용한다.</b> 매 프레임 도는 경로라 여기서 새로 할당하면 그대로
    /// GC 압력이 된다.</para>
    /// </summary>
    public static class PartOutlineTargets
    {
        /// <summary>마스크 셰이더가 읽는 번호.</summary>
        public static readonly int OutlineGroupId = Shader.PropertyToID("_OutlineGroup");

        /// <summary>번호를 0~1 로 정규화할 때 나누는 값. R8 한 칸이 1/255 다.</summary>
        public const float GroupScale = 255f;

        public readonly struct Target
        {
            public readonly Renderer Renderer;
            public readonly int SubMeshCount;

            public Target(Renderer renderer, int subMeshCount)
            {
                Renderer = renderer;
                SubMeshCount = subMeshCount;
            }
        }

        static readonly List<Target> TargetBuffer = new();
        static readonly List<SpaceshipPart> PartBuffer = new();
        static readonly Dictionary<SpaceshipPart, int> GroupBuffer = new();
        static readonly List<Renderer> RendererBuffer = new();
        static MaterialPropertyBlock block;

        public static IReadOnlyList<Target> Targets => TargetBuffer;

        /// <summary>
        /// 그릴 것을 모은다. 하나도 없으면 거짓을 돌려주고, 피처는 패스를 걸지 않는다.
        ///
        /// <para><b>핵심 파츠만 본다.</b> <see cref="PartSpec.All"/> 에 이름이 있는 것만
        /// 고른다 — 돌덩이·화물까지 테두리를 두르면 "핵심을 구분한다" 가 무너진다.</para>
        /// </summary>
        public static bool Collect()
        {
            TargetBuffer.Clear();
            PartBuffer.Clear();

            var parts = Object.FindObjectsByType<SpaceshipPart>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (var i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                if (part == null || !part.isActiveAndEnabled) continue;
                if (PartSpec.Find(part.name) == null) continue;
                PartBuffer.Add(part);
            }
            if (PartBuffer.Count == 0) return false;

            PartOutlineGroups.Assign(PartBuffer, PartAssemblyLink.All, GroupBuffer);
            block ??= new MaterialPropertyBlock();

            for (var i = 0; i < PartBuffer.Count; i++)
            {
                var part = PartBuffer[i];
                if (!GroupBuffer.TryGetValue(part, out var group)
                    || group == PartOutlineGroups.NoGroup) continue;

                RendererBuffer.Clear();
                part.GetComponentsInChildren(false, RendererBuffer);
                for (var r = 0; r < RendererBuffer.Count; r++)
                {
                    var renderer = RendererBuffer[r];
                    if (renderer == null || !renderer.enabled) continue;

                    // 이미 붙어 있는 값을 지우지 않는다 — 다른 기능이 같은 렌더러에
                    // 프로퍼티 블록을 쓰고 있을 수 있다.
                    renderer.GetPropertyBlock(block);
                    block.SetFloat(OutlineGroupId, group / GroupScale);
                    renderer.SetPropertyBlock(block);

                    TargetBuffer.Add(new Target(renderer, SubMeshCountOf(renderer)));
                }
            }

            return TargetBuffer.Count > 0;
        }

        /// <summary>
        /// 서브메시 수. <b>재질 슬롯 수로 세지 않는다</b> — 슬롯이 메시보다 많으면
        /// 없는 서브메시를 그리게 되고, 적으면 일부가 빠진다.
        /// </summary>
        static int SubMeshCountOf(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skinned)
                return skinned.sharedMesh != null ? skinned.sharedMesh.subMeshCount : 1;

            if (renderer.TryGetComponent<MeshFilter>(out var filter)
                && filter.sharedMesh != null)
                return filter.sharedMesh.subMeshCount;

            return 1;
        }
    }
}
