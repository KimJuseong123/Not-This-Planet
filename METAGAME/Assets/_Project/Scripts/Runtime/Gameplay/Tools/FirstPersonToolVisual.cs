using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Metagame.Gameplay
{
    // 표시 전용 복사본. 원본을 Instantiate하지 않아 Awake/물리/도구 로직이 복제되지 않는다.
    public sealed class FirstPersonToolVisual : IDisposable
    {
        readonly Dictionary<Transform, Transform> nodes = new();
        readonly List<(Transform Source, Transform Copy)> animatedNodes = new();
        readonly Renderer[] originals;
        readonly bool[] hiddenBefore;

        /// <summary>
        /// 감추기 전의 그림자 모드. 놓을 때 이 값으로 되돌린다.
        ///
        /// <see cref="hiddenBefore"/> 와 짝이다 — 그쪽은 "원래 통째로 꺼져 있던 렌더러는
        /// 그대로 둔다" 를 지키고, 이쪽은 "원래 그림자가 없던 렌더러에 없던 그림자를
        /// 만들지 않는다" 를 지킨다.
        /// </summary>
        readonly ShadowCastingMode[] shadowBefore;
        readonly MaterialPropertyBlock properties = new();
        readonly List<(Renderer Source, Renderer Copy)> renderers = new();
        bool hidden;
        public Transform Model { get; private set; }

        public FirstPersonToolVisual(Transform source, Transform camera)
        {
            originals = source.GetComponentsInChildren<Renderer>(true);
            hiddenBefore = new bool[originals.Length];
            shadowBefore = new ShadowCastingMode[originals.Length];
            Model = CopyTree(source, camera, true);
            Model.name = source.name + " (1P visual)";
            foreach (var pair in nodes)
            {
                var src = pair.Key;
                var dst = pair.Value.gameObject;
                var filter = src.GetComponent<MeshFilter>();
                Renderer renderer = null;
                var meshRenderer = src.GetComponent<MeshRenderer>();
                var skin = src.GetComponent<SkinnedMeshRenderer>();
                if (filter != null && meshRenderer != null)
                {
                    dst.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                    renderer = dst.AddComponent<MeshRenderer>();
                    CopyRenderer(meshRenderer, renderer);
                }
                else if (skin != null)
                {
                    var copy = dst.AddComponent<SkinnedMeshRenderer>();
                    copy.sharedMesh = skin.sharedMesh;
                    copy.localBounds = skin.localBounds;
                    copy.rootBone = skin.rootBone != null && nodes.TryGetValue(skin.rootBone, out var root) ? root : null;
                    var bones = skin.bones;
                    for (int i = 0; i < bones.Length; i++)
                        bones[i] = bones[i] != null && nodes.TryGetValue(bones[i], out var bone) ? bone : null;
                    copy.bones = bones;
                    renderer = copy;
                    CopyRenderer(skin, copy);
                }
            }
            Model.gameObject.SetActive(false);
        }

        Transform CopyTree(Transform source, Transform parent, bool root)
        {
            var copy = new GameObject(source.name).transform;
            copy.SetParent(parent, false);
            copy.gameObject.layer = parent.gameObject.layer;
            copy.SetLocalPositionAndRotation(source.localPosition, source.localRotation);
            copy.localScale = source.localScale;
            copy.gameObject.SetActive(source.gameObject.activeSelf);
            nodes.Add(source, copy);
            if (!root) animatedNodes.Add((source, copy));
            foreach (Transform child in source) CopyTree(child, copy, false);
            return copy;
        }

        void CopyRenderer(Renderer source, Renderer copy)
        {
            copy.sharedMaterials = source.sharedMaterials;
            copy.enabled = source.enabled;
            copy.shadowCastingMode = ShadowCastingMode.Off;
            copy.receiveShadows = false;
            renderers.Add((source, copy));
        }

        /// <summary>
        /// 1인칭 사본을 켜고, 그동안 진짜 도구를 카메라에서 감춘다.
        ///
        /// <para><b>감추되 그림자는 남긴다</b>(S15P21B202-489). 예전에는
        /// <c>forceRenderingOff</c> 로 감췄는데 그 플래그는 <b>그림자 패스도 끈다</b>.
        /// 사본은 그림자를 만들지 않으므로(<see cref="CopyRenderer"/>) 결과적으로
        /// <b>들고 있는 동안 방망이·총의 그림자가 어디에도 없었다.</b></para>
        ///
        /// <para>실측(2026-09-14): 들면 원본이 <c>forceRenderingOff False → True</c> 가
        /// 되고 <c>shadowCastingMode</c> 는 <c>On</c> 인 채로 남는다 — <b>모드만 봐서는
        /// 그림자가 나오는지 알 수 없다.</b> 두 값은 서로를 바꾸지 않는다.</para>
        ///
        /// <para><see cref="Player.Doll.SetVisible"/> 이 1인칭에서 몸을 감출 때 쓰는
        /// 방법과 같게 맞췄다 — 거기 주석이 이유를 적고 있다: <b>"발밑을 읽는 단서가 제
        /// 그림자이므로, 메시만 빼고 그림자는 그대로 두는 것이 옳다"</b>(기획 §10.1).
        /// 몸에 대해 내린 그 판단이 손에 든 물건에는 적용되지 않고 있었다.</para>
        ///
        /// <para><b><c>forceRenderingOff</c> 를 없애지는 않았다.</b>
        /// <see cref="hiddenBefore"/> 가 "원래 꺼져 있던 렌더러는 그대로 둔다" 를
        /// 지키고 있고 그 뜻은 유지해야 한다 — 그림자 모드만으로는 그것을 표현할 수
        /// 없다(꺼져 있던 렌더러가 갑자기 그림자를 만들면 안 된다).</para>
        /// </summary>
        public void SetVisible(bool visible)
        {
            if (Model != null) Model.gameObject.SetActive(visible);
            if (hidden == visible) return;
            hidden = visible;
            for (int i = 0; i < originals.Length; i++)
            {
                if (originals[i] == null) continue;

                if (visible)
                {
                    hiddenBefore[i] = originals[i].forceRenderingOff;
                    shadowBefore[i] = originals[i].shadowCastingMode;
                }

                // 원래 통째로 꺼져 있던 것은 계속 꺼 둔다. 그 경우는 아래 모드가
                // 무엇이든 아무것도 그리지 않는다.
                originals[i].forceRenderingOff = hiddenBefore[i];

                // 메시는 빼고 그림자만 남긴다. 원래 그림자가 없던 렌더러에 없던
                // 그림자를 만들지 않도록, 감추기 전 모드를 저장해 두었다가 되돌린다.
                originals[i].shadowCastingMode = visible
                    ? ShadowsOnlyFrom(shadowBefore[i])
                    : shadowBefore[i];
            }
        }

        /// <summary>
        /// 감출 때 쓸 그림자 모드.
        ///
        /// 원래 그림자를 만들지 않던 렌더러(<see cref="ShadowCastingMode.Off"/>)는 그대로
        /// 둔다 — 감춘다고 없던 그림자가 생기면 그것도 틀린 그림이다. 나머지는
        /// <see cref="ShadowCastingMode.ShadowsOnly"/> 로 바꿔 메시만 뺀다.
        /// </summary>
        static ShadowCastingMode ShadowsOnlyFrom(ShadowCastingMode before) =>
            before == ShadowCastingMode.Off
                ? ShadowCastingMode.Off
                : ShadowCastingMode.ShadowsOnly;

        public void SyncAnimation()
        {
            foreach (var pair in animatedNodes)
            {
                if (pair.Source == null || pair.Copy == null) continue;
                pair.Copy.SetLocalPositionAndRotation(pair.Source.localPosition, pair.Source.localRotation);
                pair.Copy.localScale = pair.Source.localScale;
                pair.Copy.gameObject.SetActive(pair.Source.gameObject.activeSelf);
            }
            foreach (var pair in renderers)
            {
                if (pair.Source == null || pair.Copy == null) continue;
                pair.Copy.enabled = pair.Source.enabled;
                pair.Source.GetPropertyBlock(properties);
                pair.Copy.SetPropertyBlock(properties);
            }
        }

        public void SetPose(Vector3 position, Quaternion rotation, Vector3 scale,
                            Vector3 localGrip, Vector3? handPosition)
        {
            if (Model == null) return;
            Model.SetLocalPositionAndRotation(position, rotation);
            Model.localScale = scale;
            if (handPosition.HasValue)
                Model.position += handPosition.Value - Model.TransformPoint(localGrip);
        }

        public void Dispose()
        {
            SetVisible(false);
            if (Model != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(Model.gameObject);
                else UnityEngine.Object.DestroyImmediate(Model.gameObject);
            }
            Model = null;
        }
    }
}
