using Metagame.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Metagame.EditorTools
{
    /// <summary>
    /// Sandbox_Physics 씬을 코드로 생성한다.
    /// 충돌 매트릭스와 적층 안정성을 눈으로 확인하기 위한 그레이박스 씬이며,
    /// 씬을 다시 만들어야 하면 Tools > Metagame > Rebuild Physics Sandbox 를 실행한다.
    /// </summary>
    public static class SandboxSceneBuilder
    {
        const string ScenePath = "Assets/_Project/Scenes/Sandbox_Physics.unity";

        [MenuItem("Tools/Metagame/Rebuild Physics Sandbox")]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var cam = Camera.main;
            if (cam != null)
            {
                cam.transform.SetPositionAndRotation(new Vector3(0f, 22f, -62f), Quaternion.Euler(12f, 0f, 0f));
                cam.farClipPlane = 300f;
            }

            Ground();
            CaseCarriedPartPassesThroughPlayers(-34f);
            CaseDownedPlayerDoesNotPush(-14f);
            CaseFivePartStack(14f);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[SandboxSceneBuilder] saved {ScenePath}");
        }

        static void Ground()
        {
            var g = Solid("Ground", PrimitiveType.Cube, new Vector3(0f, -0.5f, 0f),
                new Vector3(160f, 1f, 80f), GameLayers.Ground);
            GameObjectUtility.SetStaticEditorFlags(g, StaticEditorFlags.BatchingStatic);
        }

        // 1. 견인 중인 파츠가 플레이어를 통과해 지면까지 떨어져야 한다.
        static void CaseCarriedPartPassesThroughPlayers(float x)
        {
            Label("Case1_CarriedPart_PassesThrough_Players", x);
            Player("Case1_Player", new Vector3(x, PhysicsScale.PlayerHeight * 0.5f, 0f), GameLayers.Player);
            Player("Case1_PlayerDowned", new Vector3(x + 2f, PhysicsScale.PlayerHeight * 0.5f, 0f), GameLayers.PlayerDowned);
            Part("Case1_CarriedPart", new Vector3(x + 1f, 14f, 0f), GameLayers.PartCarried);
        }

        // 2. 기절한 플레이어와 서 있는 플레이어가 겹쳐도 서로 밀지 않아야 한다.
        static void CaseDownedPlayerDoesNotPush(float x)
        {
            Label("Case2_DownedPlayer_DoesNotPush_Player", x);
            Player("Case2_Player", new Vector3(x, PhysicsScale.PlayerHeight * 0.5f, 0f), GameLayers.Player);
            Player("Case2_Downed", new Vector3(x + 0.1f, PhysicsScale.PlayerHeight * 0.5f, 0.1f), GameLayers.PlayerDowned);
        }

        // 3. 1000kg 파츠 5단이 흔들리거나 미끄러지지 않아야 한다.
        static void CaseFivePartStack(float x)
        {
            Label("Case3_FivePartStack_Stability", x);
            for (var i = 0; i < 5; i++)
            {
                var y = PhysicsScale.PartHeight * (i + 0.5f);
                Part($"Case3_Part_{i}", new Vector3(x, y, 0f), GameLayers.Part);
            }
        }

        static GameObject Player(string name, Vector3 pos, int layer)
        {
            var go = Solid(name, PrimitiveType.Capsule, pos,
                new Vector3(PhysicsScale.PlayerRadius * 2f, PhysicsScale.PlayerHeight * 0.5f, PhysicsScale.PlayerRadius * 2f),
                layer);
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = PhysicsScale.PlayerMass;
            rb.constraints = RigidbodyConstraints.FreezeRotation;
            return go;
        }

        static GameObject Part(string name, Vector3 pos, int layer)
        {
            var go = Solid(name, PrimitiveType.Cube, pos,
                new Vector3(PhysicsScale.PartWidth, PhysicsScale.PartHeight, PhysicsScale.PartWidth), layer);
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = PhysicsScale.PartMass;
            rb.maxAngularVelocity = PhysicsScale.PartMaxAngularSpeed;
            return go;
        }

        static GameObject Solid(string name, PrimitiveType type, Vector3 pos, Vector3 scale, int layer)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.layer = layer;
            go.transform.position = pos;
            go.transform.localScale = scale;
            return go;
        }

        static void Label(string name, float x) =>
            new GameObject(name).transform.position = new Vector3(x, 0f, 0f);
    }
}
