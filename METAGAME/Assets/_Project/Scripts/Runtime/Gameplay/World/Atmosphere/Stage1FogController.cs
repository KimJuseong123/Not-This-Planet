using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Metagame.Gameplay
{
    /// <summary>Scene-owned fog; no pipeline cloning, editor APIs or gameplay changes.</summary>
    public sealed class Stage1FogController : MonoBehaviour
    {
        [SerializeField] private Shader fogShader;
        [SerializeField] private Renderer[] skyBeacons = Array.Empty<Renderer>();
        public static Stage1FogController Active { get; private set; }
        public Material Material { get; private set; }
        public bool ShaderEnabled { get; private set; }
        private readonly List<Material[]> originals = new List<Material[]>();
        private readonly List<Material[]> copies = new List<Material[]>();
        private bool previousFog;
        private int mode = 2;

        private void OnEnable() { if (Application.isPlaying) SetMode(2); }
        private void LateUpdate()
        {
            // Bootstrap may apply basic atmosphere after OnEnable. Do not double-fog.
            if (Material != null && SceneManager.GetActiveScene() == gameObject.scene)
                RenderSettings.fog = mode == 1;
        }
        public void Initialize()
        {
            if (Material != null || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
            if (Active != null && Active != this) throw new InvalidOperationException("Only one scene fog may be active.");
            if (fogShader == null || !fogShader.isSupported) throw new InvalidOperationException("Stage1 fog shader missing or unsupported.");
            previousFog = RenderSettings.fog;
            Material = new Material(fogShader) { hideFlags = HideFlags.DontSave };
            Active = this;
            foreach (var beacon in skyBeacons)
            {
                var source = beacon != null ? beacon.sharedMaterials : Array.Empty<Material>();
                var cloned = new Material[source.Length];
                for (int i = 0; i < source.Length; i++)
                    if (source[i] != null) cloned[i] = new Material(source[i]) { hideFlags = HideFlags.DontSave };
                originals.Add(source); copies.Add(cloned);
                if (beacon != null) beacon.sharedMaterials = cloned;
            }
            Configure(5, 55, .65f, new Color(.27f,.32f,.43f), .7f);
            ConfigureMist(.35f, 0, 5, .08f);
        }
        public void Configure(float start, float end, float maximum, Color color, float desaturation)
        {
            if (Material == null) return;
            Material.SetVector("_FogRange", new Vector4(Mathf.Max(0,start), Mathf.Max(start+1,end), Mathf.Clamp01(maximum), 1.2f));
            Material.SetColor("_FogTint", color.linear);
            Material.SetFloat("_DistanceDesaturation", Mathf.Clamp01(desaturation));
        }
        public void ConfigureMist(float strength, float baseHeight, float thickness, float speed)
        {
            if (Material == null) return;
            Material.SetVector("_GroundMist", new Vector4(Mathf.Clamp(strength,0,.7f),baseHeight,Mathf.Max(.1f,thickness),Mathf.Clamp(speed,0,.5f)));
            Material.SetColor("_MistTint", new Color(.46f,.54f,.65f).linear);
        }
        public void SetMode(int value)
        {
            Initialize();
            if (Material == null) return;
            mode = value;
            ShaderEnabled = mode == 2;
            RenderSettings.fog = mode == 1;
            for (int b=0; b<copies.Count; b++)
                for (int i=0; i<copies[b].Length; i++)
                    if (copies[b][i] != null)
                        copies[b][i].renderQueue = ShaderEnabled ? (int)RenderQueue.Transparent : originals[b][i].renderQueue;
        }
        private void OnDisable() { Release(); }
        public void Release()
        {
            if (Material == null) return;
            ShaderEnabled = false;
            if (Active == this) Active = null;
            if (SceneManager.GetActiveScene() == gameObject.scene) RenderSettings.fog = previousFog;
            for (int b=0; b<copies.Count; b++)
            {
                if (skyBeacons[b] != null) skyBeacons[b].sharedMaterials = originals[b];
                foreach (var material in copies[b]) if (material != null) DisposeObject(material);
            }
            copies.Clear(); originals.Clear();
            DisposeObject(Material); Material = null;
        }
        private static void DisposeObject(UnityEngine.Object value)
        {
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }
    }
}
