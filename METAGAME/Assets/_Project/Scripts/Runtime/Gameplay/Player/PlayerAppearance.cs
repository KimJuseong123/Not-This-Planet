using UnityEngine;
using Metagame.Core;
using System.Collections.Generic;

namespace Metagame.Gameplay
{
    /// <summary>공유 메시/재질을 보존하고, 장식은 기존 몸 아래에 두어 숨김·탑승 정책을 따른다.</summary>
    public sealed class PlayerAppearance : MonoBehaviour
    {
        Doll doll;
        Renderer[] surfaces;
        readonly List<Material> ownedMaterials = new List<Material>();
        Transform helmetRoot, badgeRoot;
        int applied = -1;
        public int Applied => applied;

        public static void Apply(Doll target, int packed)
        {
            if (target == null || target.Root == null) return;
            var appearance = target.Root.GetComponent<PlayerAppearance>();
            if (appearance == null)
            {
                appearance = target.Root.gameObject.AddComponent<PlayerAppearance>();
                appearance.doll = target;
                appearance.surfaces = target.Suit != null
                    ? target.Suit.GetComponentsInChildren<Renderer>(true)
                    : target.Root.GetComponentsInChildren<Renderer>(true);
                if (target.Suit != null) appearance.UseWhiteOnlyTintMaterials();
            }
            appearance.Set(CustomizationData.Decode(packed));
        }

        public static void Tint(Renderer[] renderers, Color tint)
        {
            var block = new MaterialPropertyBlock();
            foreach (var renderer in renderers)
            {
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    var material = materials[i];
                    if (material == null) continue;
                    block.Clear();
                    renderer.GetPropertyBlock(block, i);
                    if (material.HasProperty("_WhiteTint"))
                    {
                        block.SetColor("_WhiteTint", tint);
                    }
                    else foreach (string property in new[] { "_BaseColor", "_Color" })
                        if (material.HasProperty(property))
                        {
                            Color original = material.GetColor(property);
                            float brightest = Mathf.Max(original.r, Mathf.Max(original.g, original.b));
                            float darkest = Mathf.Min(original.r, Mathf.Min(original.g, original.b));
                            bool brightNeutral = darkest >= 0.6f && brightest - darkest <= 0.18f;
                            block.SetColor(property, brightNeutral ? original * tint : original);
                        }
                    renderer.SetPropertyBlock(block, i);
                }
            }
        }

        void UseWhiteOnlyTintMaterials()
        {
            var shader = Resources.Load<Shader>("CharacterSuitTint");
            if (shader == null)
            {
                Debug.LogWarning("[PlayerAppearance] CharacterSuitTint 셰이더가 없어 전체 재질 색상 방식으로 돌아갑니다.");
                return;
            }

            foreach (var surface in surfaces)
            {
                var sourceMaterials = surface.sharedMaterials;
                var replacements = new Material[sourceMaterials.Length];
                for (int i = 0; i < sourceMaterials.Length; i++)
                {
                    var source = sourceMaterials[i];
                    if (source == null) continue;

                    var replacement = new Material(shader) { name = source.name + " (White Tint)" };
                    Texture albedo = source.HasProperty("_BaseMap")
                        ? source.GetTexture("_BaseMap")
                        : source.mainTexture;
                    replacement.SetTexture("_BaseMap", albedo);
                    replacement.SetTextureScale("_BaseMap", source.mainTextureScale);
                    replacement.SetTextureOffset("_BaseMap", source.mainTextureOffset);
                    if (source.HasProperty("_BumpMap"))
                        replacement.SetTexture("_BumpMap", source.GetTexture("_BumpMap"));
                    if (source.HasProperty("_BumpScale"))
                        replacement.SetFloat("_BumpScale", source.GetFloat("_BumpScale"));
                    if (source.HasProperty("_Smoothness"))
                        replacement.SetFloat("_Smoothness", source.GetFloat("_Smoothness"));
                    else if (source.HasProperty("_Glossiness"))
                        replacement.SetFloat("_Smoothness", source.GetFloat("_Glossiness"));

                    // 사람은 안개를 받지 않는다 (S15P21B202-540).
                    //
                    // <b>여기서도 걸어야 한다.</b> RagdollBuilder 가 다 만든 몸을 한 번
                    // 훑어 예외로 돌려놓는데, 이 함수는 그 뒤에 불리면서 재질을 통째로
                    // 새로 만들어 덮는다(SpaceBootstrap · NetPlayer 가 Apply 를 나중에
                    // 부른다). 그래서 여기가 빠지면 <b>모델을 쓰는 아바타만</b> 다시
                    // 회색이 된다 — 정작 사람들이 보는 그 모습이다.
                    //
                    // 이 재질은 사람마다 새로 만든 것이고 ownedMaterials 가 수명을
                    // 들고 있다. 그래서 쌍둥이를 만들지 않고 값만 직접 넣는다.
                    if (replacement.HasProperty(Prim.FogStrengthProperty))
                        replacement.SetFloat(Prim.FogStrengthProperty, 0f);
                    if (replacement.HasProperty(Prim.FogExemptMaskProperty))
                        replacement.SetFloat(Prim.FogExemptMaskProperty, Prim.FogExemptMaskValue);

                    replacements[i] = replacement;
                    ownedMaterials.Add(replacement);
                }
                surface.sharedMaterials = replacements;
            }
        }

        void Set(CustomizationData data)
        {
            if (applied == data.Packed) return;
            applied = data.Packed;
            Tint(surfaces, CustomizationData.TintColor(data.Tint));
            Clear(ref helmetRoot);
            Clear(ref badgeRoot);
            Color accent = new Color(0.28f, 0.85f, 0.95f);
            if (data.Helmet != 0)
            {
                helmetRoot = Prim.DecoRoot(doll.Head.transform);
                helmetRoot.gameObject.layer = doll.Head.gameObject.layer;
                helmetRoot.name = "Customizer Helmet";
                if (data.Helmet == 1)
                {
                    Piece(helmetRoot, PrimitiveType.Cube, new Vector3(0.25f, 0.25f, 0), new Vector3(0.025f, 0.22f, 0.025f), Color.white);
                    Piece(helmetRoot, PrimitiveType.Sphere, new Vector3(0.25f, 0.37f, 0), Vector3.one * 0.065f, accent);
                }
                else
                    for (int side = -1; side <= 1; side += 2)
                        Piece(helmetRoot, PrimitiveType.Sphere, new Vector3(side * 0.31f, 0, 0), new Vector3(0.09f, 0.19f, 0.19f), accent);
            }
            if (data.Badge != 0)
            {
                badgeRoot = Prim.DecoRoot(doll.Chest.transform);
                badgeRoot.gameObject.layer = doll.Chest.gameObject.layer;
                badgeRoot.name = "Customizer Badge";
                int count = data.Badge;
                for (int i = 0; i < count; i++)
                {
                    var piece = Piece(badgeRoot, PrimitiveType.Cube,
                        new Vector3(-0.09f + i * 0.07f, 0, 0.24f), new Vector3(0.055f, 0.055f, 0.025f), accent);
                    piece.localRotation = Quaternion.Euler(0, 0, 45);
                }
            }
            // 선택 동기화가 1인칭/탑승 숨김보다 늦게 와도 새 장식이 튀어나오지 않는다.
            if (!doll.Visible)
            {
                bool keepShadow = false;
                foreach (var surface in surfaces)
                    if (surface.enabled && surface.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly)
                        keepShadow = true;
                doll.SetVisible(false, keepShadow);
            }
        }

        static Transform Piece(Transform parent, PrimitiveType type, Vector3 position, Vector3 scale, Color color)
        {
            var go = GameObject.CreatePrimitive(type);
            var collider = go.GetComponent<Collider>();
            collider.enabled = false;
            if (Application.isPlaying) Destroy(collider); else DestroyImmediate(collider);
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            // 커스터마이저 장식도 몸과 같이 안개에서 뺀다 (S15P21B202-540).
            // 몸만 또렷하고 헬멧 뿔·배지만 회색이면 그것대로 눈에 띈다.
            go.GetComponent<Renderer>().sharedMaterial = Prim.Dough(color, 0.2f, fogExempt: true);
            return go.transform;
        }

        static void Clear(ref Transform root)
        {
            if (root == null) return;
            root.gameObject.SetActive(false);
            if (Application.isPlaying) Destroy(root.gameObject); else DestroyImmediate(root.gameObject);
            root = null;
        }

        void OnDestroy()
        {
            foreach (var material in ownedMaterials)
                if (material != null)
                    if (Application.isPlaying) Destroy(material); else DestroyImmediate(material);
            ownedMaterials.Clear();
        }
    }
}
