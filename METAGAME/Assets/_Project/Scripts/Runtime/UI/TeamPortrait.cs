using System;
using Metagame.Gameplay;
using UnityEngine;
using UnityEngine.Rendering;

namespace Metagame.UI
{
    /// <summary>
    /// 실제 머리와 슈트 메시를 원형 이미지에 한 번 그린다. 카메라·게임 오브젝트를
    /// 만들거나 원본 렌더러의 활성 상태, 레이어, 재질을 변경하지 않는다.
    /// </summary>
    public sealed class TeamPortrait : IDisposable
    {
        const int Resolution = 128;
        RenderTexture _texture;
        Material _material;
        Mesh _baked;
        readonly MaterialPropertyBlock _properties = new();
        PlayerRig _rig;
        int _appearance = -1;
        public Texture Texture => _texture;

        public void Refresh(PlayerRig rig, int appearance)
        {
            if (!ReferenceEquals(_rig, rig)) Dispose();
            if (_rig == rig && _appearance == appearance && _texture != null && _texture.IsCreated()) return;
            if (rig == null || rig.Doll?.Head == null || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                return;
            var doll = rig.Doll;
            var applied = doll.Root.GetComponent<PlayerAppearance>();
            // SyncVar 훅이 외형을 적용한 뒤 캡처한다. 스폰/장식 교체 중의 임시 모습을 캐시하지 않는다.
            if (applied != null && applied.Applied != appearance) return;
            if (_material == null)
            {
                var shader = Resources.Load<Shader>("TeamPortrait");
                if (shader == null || !shader.isSupported) return;
                _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            if (_texture == null)
            {
                _texture = new RenderTexture(Resolution, Resolution, 24, RenderTextureFormat.ARGB32)
                {
                    name = "Team portrait", hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp
                };
            }
            if (!_texture.IsCreated() && !_texture.Create()) return;

            var head = doll.Head.transform;
            var center = head.position + head.up * .24f;
            var cameraPose = Matrix4x4.TRS(center + head.forward * 2f,
                head.rotation * Quaternion.Euler(0f, 180f, 0f), Vector3.one);
            var view = Matrix4x4.Scale(new Vector3(1, 1, -1)) * cameraPose.inverse;
            var projection = GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(-.58f, .58f, -.58f, .58f, .1f, 4f), true);
            var vp = projection * view;
            int drawn = 0;
            var previousTarget = RenderTexture.active;
            try
            {
                using (var commands = new CommandBuffer { name = "Team portrait snapshot" })
                {
                    commands.SetRenderTarget(_texture);
                    commands.ClearRenderTarget(true, true, Color.clear);
                    // 모델이 있는 경우 숨겨 둔 물리 프리미티브는 제외한다.
                    var source = doll.Suit != null ? doll.Suit.transform : head;
                    drawn += DrawRenderers(commands, source, vp, head.worldToLocalMatrix);
                    if (doll.Suit != null && applied != null && applied.HelmetVisual != null)
                    {
                        // 헬멧 장식은 슈트가 아닌 물리 머리 아래에 붙는다.
                        drawn += DrawRenderers(commands, applied.HelmetVisual, vp, head.worldToLocalMatrix);
                    }
                    Graphics.ExecuteCommandBuffer(commands);
                }
            }
            finally { RenderTexture.active = previousTarget; }
            if (drawn > 0) { _rig = rig; _appearance = appearance; }
        }

        int DrawRenderers(CommandBuffer commands, Transform root, Matrix4x4 vp, Matrix4x4 headFrame)
        {
            int drawn = 0;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is ParticleSystemRenderer || renderer is LineRenderer) continue;
                Mesh mesh;
                if (renderer is SkinnedMeshRenderer skin)
                {
                    if (skin.sharedMesh == null) continue;
                    _baked ??= new Mesh { name = "Team portrait bake", hideFlags = HideFlags.HideAndDontSave };
                    skin.BakeMesh(_baked);
                    mesh = _baked;
                }
                else
                {
                    var filter = renderer.GetComponent<MeshFilter>();
                    mesh = filter != null ? filter.sharedMesh : null;
                }
                if (mesh == null) continue;
                var model = renderer.localToWorldMatrix;
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < mesh.subMeshCount && i < materials.Length; i++)
                {
                    var original = materials[i];
                    if (original == null) continue;
                    _properties.Clear();
                    // 외형 시스템이 설정한 플레이어별 MaterialPropertyBlock도 보존해 읽는다.
                    renderer.GetPropertyBlock(_properties, i);
                    Color tint = original.HasProperty("_WhiteTint")
                        ? original.GetColor("_WhiteTint") : Color.white;
                    if (_properties.HasColor("_WhiteTint")) tint = _properties.GetColor("_WhiteTint");
                    Color baseColor = original.HasProperty("_BaseColor") ? original.GetColor("_BaseColor")
                        : original.HasProperty("_Color") ? original.GetColor("_Color") : Color.white;
                    if (_properties.HasColor("_BaseColor")) baseColor = _properties.GetColor("_BaseColor");
                    else if (_properties.HasColor("_Color")) baseColor = _properties.GetColor("_Color");
                    string textureProperty = original.HasProperty("_BaseMap") ? "_BaseMap"
                        : original.HasProperty("_MainTex") ? "_MainTex" : null;
                    var texture = textureProperty != null ? original.GetTexture(textureProperty) : null;
                    var scale = textureProperty != null ? original.GetTextureScale(textureProperty) : Vector2.one;
                    var offset = textureProperty != null ? original.GetTextureOffset(textureProperty) : Vector2.zero;
                    _properties.Clear();
                    _properties.SetTexture("_MainTex", texture != null ? texture : Texture2D.whiteTexture);
                    _properties.SetVector("_MainTex_ST", new Vector4(scale.x, scale.y, offset.x, offset.y));
                    _properties.SetColor("_Tint", tint);
                    _properties.SetColor("_Color", baseColor);
                    _properties.SetMatrix("_PortraitMvp", vp * model);
                    _properties.SetMatrix("_PortraitNormal", headFrame * model);
                    commands.DrawMesh(mesh, Matrix4x4.identity, _material, i, 0, _properties);
                    drawn++;
                }
                // BakeMesh는 같은 임시 메시를 다시 쓰므로 다음 렌더러 전에 명령을 실행한다.
                Graphics.ExecuteCommandBuffer(commands);
                commands.Clear();
                commands.SetRenderTarget(_texture);
            }
            return drawn;
        }

        public void Dispose()
        {
            if (_texture != null) _texture.Release();
            Release(_texture); Release(_material); Release(_baked);
            _texture = null; _material = null; _baked = null; _rig = null; _appearance = -1;
        }

        static void Release(UnityEngine.Object resource)
        {
            if (resource == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(resource);
            else UnityEngine.Object.DestroyImmediate(resource);
        }
    }
}
