using UnityEngine;
using Metagame.Core;

namespace Metagame.Gameplay
{
    /// <summary>공유 메시/재질을 보존하고, 장식은 기존 몸 아래에 두어 숨김·탑승 정책을 따른다.</summary>
    public sealed class PlayerAppearance : MonoBehaviour
    {
        Doll doll;
        Renderer[] surfaces;
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
                    foreach (string property in new[] { "_BaseColor", "_Color" })
                        if (material.HasProperty(property))
                            block.SetColor(property, material.GetColor(property) * tint);
                    renderer.SetPropertyBlock(block, i);
                }
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
            go.GetComponent<Renderer>().sharedMaterial = Prim.Dough(color, 0.2f);
            return go.transform;
        }

        static void Clear(ref Transform root)
        {
            if (root == null) return;
            root.gameObject.SetActive(false);
            if (Application.isPlaying) Destroy(root.gameObject); else DestroyImmediate(root.gameObject);
            root = null;
        }
    }
}
