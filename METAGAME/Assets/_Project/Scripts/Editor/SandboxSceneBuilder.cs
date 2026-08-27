using Metagame.Core;
using Metagame.Gameplay;
using Metagame.Gameplay.Parts;
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

        /// <summary>운반 중 파츠의 가정 이동 속도. 견인 구현 티켓에서 실측 후 조정한다.</summary>
        const float CarrySpeed = 4f;

        // 실제 플레이어는 조인트로 엮인 액티브 래그돌이다. 이 씬에서는 충돌 규칙만 보면 되므로
        // 같은 높이·무게의 캡슐로 대신한다. 높이는 Tuning 주석의 "귀 끝까지 1.33m" 를 따른다.
        const float StandInHeight = 1.33f;
        const float StandInRadius = 0.32f;

        [MenuItem("Tools/Metagame/Rebuild Physics Sandbox")]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var cam = Camera.main;
            if (cam != null)
            {
                cam.transform.SetPositionAndRotation(new Vector3(-14f, 14f, -52f), Quaternion.Euler(8f, 0f, 0f));
                cam.farClipPlane = 300f;
            }

            Ground();
            CaseCarriedPartNudgesPlayer(-46f);
            CasePlacedPartCrushesPlayer(-30f);
            CaseDownedPlayerIsProtected(-14f);
            CaseFivePartStack(14f);
            CaseClampedPartHoldsFirm(32f);

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

        // 1. 견인 중인 파츠(10kg)가 이동하다 플레이어를 치면 툭 미는 정도로 끝나야 한다.
        //    떨어뜨리기만 해서는 비교가 되지 않는다. 실제 상황은 빔으로 옮기는 중의 충돌이므로
        //    SandboxLauncher 로 운반 속도를 주고 부딪히게 한다.
        static void CaseCarriedPartNudgesPlayer(float x)
        {
            Label("Case1_CarriedPart_Nudges_Player", x);
            Player("Case1_Player", new Vector3(x, StandInHeight * 0.5f, 0f), GameLayers.Player);
            var p = Part("Case1_CarriedPart", new Vector3(x - 2.6f, PhysicsScale.PartHeight * 0.5f, 0f),
                GameLayers.PartCarried, PhysicsScale.CarriedPartMass);
            p.AddComponent<SandboxLauncher>().LaunchVelocity = new Vector3(CarrySpeed, 0f, 0f);
        }

        // 2. 같은 속도로 움직이는 1000kg 파츠는 비교할 수 없이 강하게 밀어내야 한다.
        //    이쪽이 기절 판정 대상이다.
        static void CasePlacedPartCrushesPlayer(float x)
        {
            Label("Case2_PlacedPart_Crushes_Player", x);
            Player("Case2_Player", new Vector3(x, StandInHeight * 0.5f, 0f), GameLayers.Player);
            var p = Part("Case2_PlacedPart", new Vector3(x - 2.6f, PhysicsScale.PartHeight * 0.5f, 0f),
                GameLayers.Part);
            p.AddComponent<SandboxLauncher>().LaunchVelocity = new Vector3(CarrySpeed, 0f, 0f);
        }

        // 3. 기절한 플레이어는 다른 플레이어와도, 견인 중인 파츠와도 겹칠 수 있어야 한다.
        static void CaseDownedPlayerIsProtected(float x)
        {
            // 3a. 겹친 두 캡슐이 서로 밀지 않는다.
            Label("Case3_DownedPlayer_IsProtected", x);
            Player("Case3_Player", new Vector3(x, StandInHeight * 0.5f, 0f), GameLayers.Player);
            Player("Case3_Downed", new Vector3(x + 0.1f, StandInHeight * 0.5f, 0.1f), GameLayers.PlayerDowned);

            // 3b. 기절 캡슐 단독. 견인 파츠가 위로 떨어져도 통과해야 한다.
            Player("Case3_DownedAlone", new Vector3(x + 9f, StandInHeight * 0.5f, 0f), GameLayers.PlayerDowned);
            Part("Case3_CarriedPart", new Vector3(x + 9f, 9f, 0f), GameLayers.PartCarried, PhysicsScale.CarriedPartMass);
        }

        // 4. 1000kg 파츠 5단이 흔들리거나 미끄러지지 않아야 한다.
        static void CaseFivePartStack(float x)
        {
            Label("Case4_FivePartStack_Stability", x);
            for (var i = 0; i < 5; i++)
            {
                var y = PhysicsScale.PartHeight * (i + 0.5f);
                Part($"Case4_Part_{i}", new Vector3(x, y, 0f), GameLayers.Part);
            }
        }

        // 5. 고정쇠가 남아 있는 파츠는 kinematic 이라 무엇이 부딪혀도 밀리지 않는다.
        //    고정쇠를 모두 풀면 Free 로 넘어가 움직이기 시작한다.
        static void CaseClampedPartHoldsFirm(float x)
        {
            Label("Case5_ClampedPart_HoldsFirm", x);

            var go = Solid("Case5_ClampedPart", PrimitiveType.Cube,
                new Vector3(x, PhysicsScale.PartHeight * 0.5f, 0f),
                new Vector3(PhysicsScale.PartWidth, PhysicsScale.PartHeight, PhysicsScale.PartWidth),
                GameLayers.Part);
            go.AddComponent<Rigidbody>();

            var part = go.AddComponent<SpaceshipPart>();

            // 파츠 밑면 네 귀퉁이에 고정쇠를 둔다. 로컬 스케일이 파츠 크기이므로 좌표는 ±0.5 기준이다.
            var corners = new[]
            {
                new Vector3(-0.4f, -0.5f, -0.4f), new Vector3(0.4f, -0.5f, -0.4f),
                new Vector3(-0.4f, -0.5f, 0.4f), new Vector3(0.4f, -0.5f, 0.4f),
            };
            for (var i = 0; i < corners.Length; i++)
            {
                var clampGo = new GameObject($"Case5_Clamp_{i}") { layer = GameLayers.Interactable };
                clampGo.transform.SetParent(go.transform, false);
                clampGo.transform.localPosition = corners[i];

                var sphere = clampGo.AddComponent<SphereCollider>();
                sphere.radius = 0.12f;
                sphere.isTrigger = true;

                clampGo.AddComponent<PartClamp>();
            }

            // 씬에 저장되는 Rigidbody 값이 Locked 상태와 맞도록 여기서 적용해 둔다.
            part.Initialize();

            // 고정쇠가 버티는지 보여줄 충돌체.
            var hammer = Part("Case5_Hammer", new Vector3(x - 2.6f, PhysicsScale.PartHeight * 0.5f, 0f),
                GameLayers.PartCarried, PhysicsScale.CarriedPartMass);
            hammer.AddComponent<SandboxLauncher>().LaunchVelocity = new Vector3(CarrySpeed, 0f, 0f);
        }

        static GameObject Player(string name, Vector3 pos, int layer)
        {
            var go = Solid(name, PrimitiveType.Capsule, pos,
                new Vector3(StandInRadius * 2f, StandInHeight * 0.5f, StandInRadius * 2f),
                layer);
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = Tuning.BodyMass;
            rb.constraints = RigidbodyConstraints.FreezeRotation;
            return go;
        }

        static GameObject Part(string name, Vector3 pos, int layer, float mass = PhysicsScale.PartMass)
        {
            var go = Solid(name, PrimitiveType.Cube, pos,
                new Vector3(PhysicsScale.PartWidth, PhysicsScale.PartHeight, PhysicsScale.PartWidth), layer);
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = mass;
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
