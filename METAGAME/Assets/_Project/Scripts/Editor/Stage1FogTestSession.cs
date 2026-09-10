using System;
using Metagame.Gameplay;
using UnityEditor;
using UnityEngine;
namespace Metagame.EditorTools
{
    /// <summary>Controls the same scene-owned fog used in builds.</summary>
    [InitializeOnLoad]
    public static class Stage1FogTestSession
    {
        public static bool Installed => Stage1FogController.Active != null;
        public static bool ShaderEnabled => Installed && Stage1FogController.Active.ShaderEnabled;
        static Stage1FogTestSession()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Cleanup;
            EditorApplication.quitting += Cleanup;
        }
        public static void Install()
        {
            if (Installed) return;
            var controller = UnityEngine.Object.FindFirstObjectByType<Stage1FogController>();
            if (controller == null) throw new InvalidOperationException("Open the configured Stage1_Astra scene.");
            controller.Initialize();
        }
        public static void Configure(float start, float end, float maximum, Color color, float desaturation = .7f)
        { if (Installed) Stage1FogController.Active.Configure(start,end,maximum,color,desaturation); }
        public static void ConfigureMist(float strength, float baseHeight, float thickness, float speed)
        { if (Installed) Stage1FogController.Active.ConfigureMist(strength,baseHeight,thickness,speed); }
        public static void SetMode(int mode)
        {
            Install(); Stage1FogController.Active.SetMode(mode); SceneView.RepaintAll();
        }
        public static void Cleanup() { if (Installed) Stage1FogController.Active.Release(); }
    }
}
