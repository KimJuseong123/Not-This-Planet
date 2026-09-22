using System.Collections.Generic;
using UnityEngine;
using Unity.Profiling;

namespace Metagame.Gameplay.Parts
{
    /// <summary>
    /// 이번 프레임에 테두리를 그릴 파츠와 그 <b>덩어리 번호</b>를 모은다
    /// (S15P21B202-562).
    ///
    /// <para>렌더러 피처가 <see cref="Collect"/> 를 부르고, 마스크 패스가
    /// <see cref="Targets"/> 를 돈다. 번호는 <see cref="Target.Group"/> 에 실어 두고
    /// 그리기 직전에 전역으로 넣는다 — <see cref="MaterialPropertyBlock"/> 을 렌더러에
    /// 붙이면 그 렌더러가 SRP Batcher 대상에서 빠져 <b>파츠의 본 렌더링 배칭이
    /// 깨진다.</b></para>
    ///
    /// <para><b>버퍼를 재사용한다.</b> 매 프레임 도는 경로라 여기서 새로 할당하면 그대로
    /// GC 압력이 된다.</para>
    /// </summary>
    public static class PartOutlineTargets
    {
        /// <summary>마스크 셰이더가 읽는 번호.</summary>
        public static readonly int OutlineGroupId = Shader.PropertyToID("_MetagamePartOutlineGroup");

        /// <summary>번호를 0~1 로 정규화할 때 나누는 값. R8 한 칸이 1/255 다.</summary>
        public const float GroupScale = 255f;

        public readonly struct Target
        {
            public readonly Renderer Renderer;
            public readonly int SubMeshCount;

            /// <summary>마스크에 적을 번호 0~1. 그리기 직전에 전역으로 넣는다.</summary>
            public readonly float Group;

            public Target(Renderer renderer, int subMeshCount, float group)
            {
                Renderer = renderer;
                SubMeshCount = subMeshCount;
                Group = group;
            }
        }

        static readonly List<Target> TargetBuffer = new();
        static readonly List<SpaceshipPart> PartBuffer = new();
        static readonly Dictionary<SpaceshipPart, int> GroupBuffer = new();
        static readonly List<Renderer> RendererBuffer = new();
        static readonly List<SpaceshipPart> ActiveParts = new();
        static readonly PartOutlineGroups.Workspace GroupWorkspace = new();
        static readonly ProfilerMarker CollectMarker = new("Parts.Outline.Collect");
        static bool registrySeeded;
        static readonly List<SpaceshipPart> PreviousParts = new();
        static readonly List<(SpaceshipPart Moving, SpaceshipPart Target, bool Joined)> PreviousLinks = new();
        static int foundGroups, mergedGroups;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetRegistry()
        {
            ActiveParts.Clear(); TargetBuffer.Clear(); PartBuffer.Clear(); GroupBuffer.Clear();
            RendererBuffer.Clear(); PreviousParts.Clear(); PreviousLinks.Clear(); GroupWorkspace.Clear();
            registrySeeded = false; collectedFrame = -1;
            reportedParts = reportedMerged = -1;
        }

        internal static void Register(SpaceshipPart part)
        {
            if (!ActiveParts.Contains(part)) ActiveParts.Add(part);
            collectedFrame = -1;
        }

        internal static void Unregister(SpaceshipPart part)
        {
            ActiveParts.Remove(part);
            collectedFrame = -1;
        }

        public static IReadOnlyList<Target> Targets => TargetBuffer;

        static int collectedFrame = -1;
        static bool collectedResult;

        /// <summary>
        /// 그릴 것을 모은다. 하나도 없으면 거짓을 돌려주고, 피처는 패스를 걸지 않는다.
        ///
        /// <para><b>프레임당 한 번만 센다.</b> 부르는 곳이 <c>AddRenderPasses</c> 라
        /// <b>카메라마다</b> 불린다(게임 뷰 · 씬 뷰 · 반사 …). 그때마다 씬을 훑으면
        /// 파츠가 네 개여도 비용은 씬 크기와 카메라 수에 비례해 붙는다.</para>
        /// </summary>
        public static bool Collect()
        {
            if (collectedFrame == Time.frameCount) return collectedResult;
            collectedFrame = Time.frameCount;
            using (CollectMarker.Auto()) collectedResult = CollectNow();
            return collectedResult;
        }

        static bool CollectNow()
        {
            TargetBuffer.Clear();
            PartBuffer.Clear();

            // EditMode does not guarantee OnEnable for ordinary MonoBehaviours. Runtime needs
            // only one seed, including when Enter Play Mode disables scene/domain reload.
            if (!Application.isPlaying || !registrySeeded)
            {
                ActiveParts.Clear();
                ActiveParts.AddRange(Object.FindObjectsByType<SpaceshipPart>(
                    FindObjectsInactive.Exclude, FindObjectsSortMode.None));
                registrySeeded = Application.isPlaying;
            }
            for (var i = 0; i < ActiveParts.Count; i++)
            {
                var part = ActiveParts[i];
                if (part == null || !part.isActiveAndEnabled) continue;
                if (PartSpec.Find(part.name) == null) continue;
                PartBuffer.Add(part);
            }
            if (PartBuffer.Count == 0)
            {
                GroupBuffer.Clear(); PreviousParts.Clear(); PreviousLinks.Clear(); GroupWorkspace.Clear();
                return false;
            }

            if (GroupsChanged())
            {
                foundGroups = PartOutlineGroups.Assign(PartBuffer, PartAssemblyLink.All, GroupBuffer, GroupWorkspace);
                mergedGroups = PartOutlineGroups.KeepMergedOnly(GroupBuffer, GroupWorkspace);
            }

            // 혼자인 파츠에는 테두리를 두르지 않는다 (S15P21B202-562). 테두리는
            // "붙었다" 를 보여 주는 표시라, 아직 혼자인 것까지 두르면 뜻이 흐려진다.
            Report(foundGroups, mergedGroups);
            if (mergedGroups == 0) return false;

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

                    TargetBuffer.Add(new Target(renderer, SubMeshCountOf(renderer),
                                                group / GroupScale));
                }
            }

            return TargetBuffer.Count > 0;
        }

        static bool GroupsChanged()
        {
            bool changed = PreviousParts.Count != PartBuffer.Count;
            if (!changed)
                for (int i = 0; i < PartBuffer.Count; i++)
                    if (PreviousParts[i] != PartBuffer[i]) { changed = true; break; }
            PreviousParts.Clear(); PreviousParts.AddRange(PartBuffer);
            var links = PartAssemblyLink.All;
            changed |= PreviousLinks.Count != links.Count;
            for (int i = 0; i < links.Count; i++)
            {
                var link = links[i];
                var state = (link != null ? link.MovingPart : null,
                    link != null ? link.TargetPart : null, PartOutlineGroups.IsWeldJoined(link));
                if (i >= PreviousLinks.Count) { PreviousLinks.Add(state); changed = true; }
                else if (!PreviousLinks[i].Equals(state)) { PreviousLinks[i] = state; changed = true; }
            }
            if (PreviousLinks.Count > links.Count)
                PreviousLinks.RemoveRange(links.Count, PreviousLinks.Count - links.Count);
            return changed;
        }

        static int reportedParts = -1;
        static int reportedMerged = -1;

        /// <summary>
        /// 무엇을 찾았고 무엇을 그리기로 했는지 <b>값이 바뀔 때만</b> 한 줄 남긴다
        /// (S15P21B202-562).
        ///
        /// <para><b>왜 로그가 필요한가.</b> 테두리가 안 보일 때 원인이 세 갈래다 —
        /// 파츠를 못 찾았나, 용접 판정이 안 섰나, 그리기가 안 됐나. 화면만 보고는
        /// 가릴 수 없고 빌드 한 번이 길다. 이 한 줄이면 앞의 둘은 바로 갈린다.</para>
        ///
        /// <para>매 프레임 도는 경로라 값이 바뀔 때만 찍는다.</para>
        /// </summary>
        static void Report(int foundGroups, int mergedGroups)
        {
            if (PartBuffer.Count == reportedParts && mergedGroups == reportedMerged) return;

            reportedParts = PartBuffer.Count;
            reportedMerged = mergedGroups;

            var welded = 0;
            for (var i = 0; i < PartAssemblyLink.All.Count; i++)
                if (PartOutlineGroups.IsWeldJoined(PartAssemblyLink.All[i])) welded++;

            Debug.Log($"[PartOutline] 핵심 파츠 {PartBuffer.Count}개 · 접합 {PartAssemblyLink.All.Count}개"
                      + $"(용접으로 이어진 것 {welded}개) · 덩어리 {foundGroups}개 → 테두리 {mergedGroups}개");
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
