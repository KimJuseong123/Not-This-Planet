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
        readonly MaterialPropertyBlock properties = new();
        readonly List<(Renderer Source, Renderer Copy)> renderers = new();
        bool hidden;
        public Transform Model { get; private set; }

        public FirstPersonToolVisual(Transform source, Transform camera)
        {
            originals = source.GetComponentsInChildren<Renderer>(true);
            hiddenBefore = new bool[originals.Length];
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

        public void SetVisible(bool visible)
        {
            if (Model != null) Model.gameObject.SetActive(visible);
            if (hidden == visible) return;
            hidden = visible;
            for (int i = 0; i < originals.Length; i++)
            {
                if (originals[i] == null) continue;
                if (visible) hiddenBefore[i] = originals[i].forceRenderingOff;
                originals[i].forceRenderingOff = visible || hiddenBefore[i];
            }
        }

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
