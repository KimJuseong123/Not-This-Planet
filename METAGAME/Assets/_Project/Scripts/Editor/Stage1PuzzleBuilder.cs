using Metagame.Gameplay;
using Metagame.Gameplay.Demo;
using Metagame.Gameplay.Hazards;
using Metagame.Gameplay.Parts;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Metagame.EditorTools
{
    /// <summary>
    /// Main 씬에 1장 퍼즐 내용을 배치한다.
    ///
    /// 열려 있는 씬의 <c>Stage1/Gameplay/Puzzle</c> 그룹만 지우고 다시 만든다.
    /// 씬 전체를 새로 만들지 않는 것이 중요하다. 그렇게 하면 레벨과 부트스트랩이 사라진다.
    /// 예전 SandboxSceneBuilder 와 SpaceSceneBuilder 가 그 방식이라 위험했다.
    ///
    /// 배치는 기획 1장을 따른다. 파츠 네 개가 기지 주변에 고정쇠로 박혀 있고,
    /// 조립 위치가 따로 있고, 운석이 떨어진다.
    /// </summary>
    public static class Stage1PuzzleBuilder
    {
        const string ScenePath = "Assets/Scenes/Main.unity";
        const string GameplayPath = "Stage1/Gameplay";
        const string GroupName = "Puzzle";

        /// <summary>조립 위치. 아레나(50x50m) 중앙에서 약간 뒤.</summary>
        static readonly Vector3 AssemblyPoint = new(0f, 0f, 10f);

        /// <summary>파츠가 건물처럼 박혀 있는 자리.</summary>
        static readonly Vector3[] PartSpots =
        {
            new(-15f, 0f, -8f),
            new(15f, 0f, -8f),
            new(-15f, 0f, 6f),
            new(15f, 0f, 6f),
        };

        /// <summary>빔 기준점에서 파츠 중심까지의 거리.</summary>
        const float BeamHoldDistance = 8f;

        /// <summary>운반 중 파츠 중심 높이. 지면과 이미 놓인 파츠를 넘어야 한다.</summary>
        const float TransportHeight = 11f;

        const int ClampsPerPart = 4;
        const int WeldPointsPerPart = 4;

        [MenuItem("Tools/둥실둥실 우주 대탈출/Main 씬에 1장 퍼즐 배치", false, 3)]
        public static void Build()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var gameplay = FindByPath(scene, GameplayPath);
            if (gameplay == null)
            {
                Debug.LogError($"[Stage1Puzzle] '{GameplayPath}' 를 찾지 못했다. 레벨 구조가 바뀌었는지 확인한다.");
                return;
            }

            // 이 도구가 만든 것만 지운다. 레벨은 건드리지 않는다.
            var existing = gameplay.Find(GroupName);
            if (existing != null) Object.DestroyImmediate(existing.gameObject);

            var group = new GameObject(GroupName).transform;
            group.SetParent(gameplay, false);

            var marker = new GameObject("AssemblyPoint").transform;
            marker.SetParent(group, false);
            marker.localPosition = AssemblyPoint;

            var parts = new SpaceshipPart[PartSpots.Length];
            for (var i = 0; i < PartSpots.Length; i++)
                parts[i] = BuildPart(group, i);

            var beams = BuildBeams(group, parts);
            BuildMeteors(group);
            BuildDemo(group, parts, beams);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log($"[Stage1Puzzle] 배치 완료 — 파츠 {parts.Length}기, 빔 {beams.Length}기 → {ScenePath}");
        }

        /// <summary>고정쇠로 박혀 있고 용접점을 가진 파츠 하나.</summary>
        static SpaceshipPart BuildPart(Transform group, int index)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = $"Part_{index + 1}";
            go.layer = GameLayers.Part;
            go.transform.SetParent(group, false);
            go.transform.localPosition = PartSpots[index] + Vector3.up * (PhysicsScale.PartHeight * 0.5f);
            go.transform.localScale = new Vector3(
                PhysicsScale.PartWidth, PhysicsScale.PartHeight, PhysicsScale.PartWidth);

            go.AddComponent<Rigidbody>();

            AddSocket(go.transform, SocketRole.Base);
            AddSocket(go.transform, SocketRole.Top);

            for (var i = 0; i < ClampsPerPart; i++) AddClamp(go.transform, i);
            for (var i = 0; i < WeldPointsPerPart; i++) AddWeldPoint(go.transform, i);

            var part = go.AddComponent<SpaceshipPart>();
            part.Initialize();   // Locked 상태의 물리 표현을 씬에 저장한다
            return part;
        }

        static void AddSocket(Transform parent, SocketRole role)
        {
            var direction = role == SocketRole.Base ? Vector3.down : Vector3.up;

            var go = new GameObject($"Socket_{role}");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = direction * 0.5f;
            go.transform.localRotation = Quaternion.LookRotation(direction, Vector3.forward);

            go.AddComponent<PartSocket>().SetRole(role);
        }

        /// <summary>밑면 네 귀퉁이의 고정쇠. 로컬 스케일이 파츠 크기라 좌표는 ±0.5 기준이다.</summary>
        static void AddClamp(Transform parent, int index)
        {
            var corners = new[]
            {
                new Vector3(-0.4f, -0.5f, -0.4f), new Vector3(0.4f, -0.5f, -0.4f),
                new Vector3(-0.4f, -0.5f, 0.4f), new Vector3(0.4f, -0.5f, 0.4f),
            };

            var go = new GameObject($"Clamp_{index}") { layer = GameLayers.Interactable };
            go.transform.SetParent(parent, false);
            go.transform.localPosition = corners[index % corners.Length];

            var sphere = go.AddComponent<SphereCollider>();
            sphere.radius = 0.12f;
            sphere.isTrigger = true;

            go.AddComponent<PartClamp>();
        }

        /// <summary>밑면 둘레의 용접점. 조립된 뒤에만 용접할 수 있다.</summary>
        static void AddWeldPoint(Transform parent, int index)
        {
            var spots = new[]
            {
                new Vector3(-0.25f, -0.5f, -0.5f), new Vector3(0.25f, -0.5f, -0.5f),
                new Vector3(-0.25f, -0.5f, 0.5f), new Vector3(0.25f, -0.5f, 0.5f),
            };

            var go = new GameObject($"Weld_{index}") { layer = GameLayers.Interactable };
            go.transform.SetParent(parent, false);
            go.transform.localPosition = spots[index % spots.Length];

            var sphere = go.AddComponent<SphereCollider>();
            sphere.radius = 0.1f;
            sphere.isTrigger = true;

            go.AddComponent<WeldPoint>();
        }

        /// <summary>
        /// 견인 빔 두 기. 도구 형태가 미정이라 기준점만 두고 위에서 아래를 겨눈다.
        /// 시연 드라이버가 각각 파츠 하나를 조립 위치로 옮긴다.
        /// </summary>
        static TractorBeam[] BuildBeams(Transform group, SpaceshipPart[] parts)
        {
            var beams = new TractorBeam[2];

            for (var i = 0; i < beams.Length; i++)
            {
                var go = new GameObject($"TractorBeam_{i + 1}");
                go.transform.SetParent(group, false);

                // 담당 파츠 위에서 아래를 본다. up 을 forward 로 주지 않으면 축이 겹쳐 실패한다.
                go.transform.localPosition = PartSpots[i] + Vector3.up * 12f;
                go.transform.localRotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);

                var beam = go.AddComponent<TractorBeam>();
                beam.Configure(go.transform, PhysicsScale.BeamRange, BeamHoldDistance);
                beams[i] = beam;

                // 조립 위치까지 높이 운반한 뒤 제자리에서 내려온다. 두 번째 빔은 첫 파츠가
                // 놓인 뒤에 움직인다.
                //
                // 두 단계로 나누는 이유가 있다. 놓을 높이로 곧장 이동하면 파츠 밑면이 지면에
                // 끌리면서 마찰로 쓰러지고, 높은 곳에서 놓으면 낙하하며 쓰러진다.
                // 빔은 기준점 아래 HoldDistance 만큼에 파츠 중심을 둔다.
                var restHeight = PhysicsScale.PartHeight * (0.5f + i);
                var above = new Vector3(AssemblyPoint.x, TransportHeight + BeamHoldDistance, AssemblyPoint.z);
                var down = new Vector3(AssemblyPoint.x, restHeight + BeamHoldDistance, AssemblyPoint.z);

                var driver = go.AddComponent<DemoBeamDriver>();
                driver.Configure(grabDelay: 4f + i * 20f, releaseAfter: 18f + i * 20f);
                driver.SetWaypoints(4f, above, down);
            }

            return beams;
        }

        static void BuildMeteors(Transform group)
        {
            var go = new GameObject("MeteorSpawner");
            go.transform.SetParent(group, false);
            go.transform.localPosition = Vector3.zero;

            var spawner = go.AddComponent<MeteorSpawner>();
            spawner.Configure(20260827,
                PhysicsScale.MeteorIntervalStart,
                PhysicsScale.MeteorIntervalMin,
                PhysicsScale.MeteorRampSeconds);

            // 아레나(50x50m)보다 좁게 잡아 경계 밖으로 떨어지지 않게 한다.
            spawner.SetArea(go.transform, new Vector2(36f, 36f));

            go.AddComponent<DemoMeteorDriver>();
        }

        /// <summary>
        /// 입력 바인딩이 붙기 전까지 Play 로 흐름을 볼 수 있게 하는 시연 장치.
        /// 빔이 옮길 두 파츠의 고정쇠를 시간차로 풀어 준다.
        /// </summary>
        static void BuildDemo(Transform group, SpaceshipPart[] parts, TractorBeam[] beams)
        {
            var go = new GameObject("~시연 (입력 붙으면 삭제)");
            go.transform.SetParent(group, false);

            var releaser = go.AddComponent<DemoClampReleaser>();
            releaser.Configure(new[]
            {
                new DemoClampReleaser.Entry { Part = parts[0], DelaySeconds = 2f },
                new DemoClampReleaser.Entry { Part = parts[1], DelaySeconds = 14f },
            });
        }

        static Transform FindByPath(UnityEngine.SceneManagement.Scene scene, string path)
        {
            var parts = path.Split('/');

            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name != parts[0]) continue;

                var current = root.transform;
                for (var i = 1; i < parts.Length && current != null; i++)
                    current = current.Find(parts[i]);

                return current;
            }

            return null;
        }
    }
}
