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
        const float BeamHoldDistance = TransportVehicle.BeamHoldDistance;

        /// <summary>실행 전 씬 미리보기용 운반 장치 위치. 실행 시에는 빈 지점으로 다시 섞는다.</summary>
        static readonly Vector3[] VehicleStartSpots =
        {
            new(-10f, TransportVehicle.GroundY, -17f),
            new(10f, TransportVehicle.GroundY, -17f),
        };

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

            var beams = BuildBeams(group);
            BuildMeteors(group);

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
        /// 견인 빔 두 기. Stage1SkinBuilder가 TractorCraft 프리팹을 기준점에 얹고,
        /// 런타임의 TractorBeamController가 기준점을 움직여 실제 운반에 사용한다.
        /// </summary>
        static TractorBeam[] BuildBeams(Transform group)
        {
            var beams = new TractorBeam[2];

            for (var i = 0; i < beams.Length; i++)
            {
                var go = new GameObject($"TractorBeam_{i + 1}");
                go.transform.SetParent(group, false);

                // 실행 전에는 바닥에 놓인 운반 장치로 보인다. 실행 시 컨트롤러가
                // 파츠와 조립 지점을 피한 랜덤 위치로 옮긴다.
                go.transform.localPosition = VehicleStartSpots[i % VehicleStartSpots.Length];
                go.transform.localRotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
                go.layer = GameLayers.Prop;

                var beam = go.AddComponent<TractorBeam>();
                beam.Configure(go.transform, PhysicsScale.BeamRange, BeamHoldDistance);
                var vehicle = go.AddComponent<TransportVehicle>();
                vehicle.Initialize(beam, BeamHoldDistance);
                vehicle.PlaceOnGround(go.transform.position, 0f);
                beams[i] = beam;

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
