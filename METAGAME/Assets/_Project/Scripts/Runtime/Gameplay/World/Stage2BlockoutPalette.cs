using UnityEngine;

namespace Metagame.Gameplay.Stage2
{
    /// <summary>Keeps prototype colors explicit on this yard without changing project-wide URP settings.</summary>
    [ExecuteAlways]
    public sealed class Stage2BlockoutPalette : MonoBehaviour
    {
        void OnEnable() => Apply();

        public void Apply()
        {
            var block = new MaterialPropertyBlock();
            foreach (var renderer in GetComponentsInChildren<MeshRenderer>(true))
            {
                var material = renderer.sharedMaterial;
                if (material == null || !material.HasProperty("_BaseColor")) continue;
                renderer.GetPropertyBlock(block);
                block.SetColor("_BaseColor", material.GetColor("_BaseColor"));
                renderer.SetPropertyBlock(block);
                block.Clear();
            }
        }
    }
}
