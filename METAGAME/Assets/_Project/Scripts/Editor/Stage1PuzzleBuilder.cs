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

        /// <summary>콜라이더 상자 자식의 이름 앞부분. 스킨이 이 렌더러들을 끈다.</summary>
        public const string BodyPrefix = "Body_";

        /// <summary>고정쇠·용접점을 밑면 발자국의 몇 배 지점에 둘지. 0.5 가 모서리다.</summary>
        const float ClampInset = 0.40f;
        const float WeldInset = 0.25f;

        /// <summary>
        /// 상호작용 콜라이더 반지름(m). 예전에는 루트 스케일(최대 3.7배)이 곱해져
        /// 실제로는 0.44m·0.37m 였다. 루트 스케일이 1 이 되었으므로 실측값을 그대로 적는다.
        /// </summary>
        const float ClampRadius = 0.35f;
        const float WeldRadius = 0.30f;

        /// <summary>
        /// 출입문 상호작용 부피. 높이는 엔진 모듈(2.16m) 중간쯤이고, 반지름은 완성된
        /// 우주선의 밑동 옆에 서서 누를 수 있을 만큼이다.
        /// </summary>
        const float HatchHeight = 1.1f;
        const float HatchRadius = 1.6f;

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
            // Equipment는 퍼즐 재생성 때도 유지해야 하는 별도 장비 그룹이다.
            // 특히 현재 맵에 배치된 임시 용접 플라즈마 건을 지우지 않도록
            // 기존 그룹에서 잠시 분리했다가 새 Puzzle 아래에 되돌린다.
            var existing = gameplay.Find(GroupName);
            var preservedEquipment = existing != null ? existing.Find("Equipment") : null;
            if (preservedEquipment != null) preservedEquipment.SetParent(gameplay, true);
            if (existing != null) Object.DestroyImmediate(existing.gameObject);

            var group = new GameObject(GroupName).transform;
            group.SetParent(gameplay, false);

            if (preservedEquipment != null) preservedEquipment.SetParent(group, true);

            var marker = BuildLaunchPad(group);

            // 파츠 수는 명세가 정한다. 배치 좌표가 모자라면 명세를 못 채우므로 함께 확인한다.
            var count = Mathf.Min(PartSpec.All.Length, PartSpots.Length);
            if (PartSpec.All.Length != PartSpots.Length)
                Debug.LogWarning($"[Stage1Puzzle] 파츠 명세 {PartSpec.All.Length}개 · 배치 좌표 "
                                 + $"{PartSpots.Length}개 — 적은 쪽인 {count}개만 세운다.");

            var parts = new SpaceshipPart[count];
            for (var i = 0; i < count; i++)
                parts[i] = BuildPart(group, i, PartSpec.All[i]);

            var beams = BuildBeams(group);
            var spawner = BuildMeteors(group);

            // 문이 발사 중 운석을 멈추고 실패 후 재개한다(기획 §22.3 · §22.4).
            // 배치 순서상 스포너가 뒤에 만들어지므로 여기서 이어 준다.
            var hatch = marker.GetComponentInChildren<ShipHatch>();
            if (hatch != null) hatch.Configure(marker.GetComponent<ShipAssembly>(),
                                               marker.GetComponent<LaunchTest>(), spawner);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log($"[Stage1Puzzle] 배치 완료 — 파츠 {parts.Length}기, 빔 {beams.Length}기 → {ScenePath}");
        }

        /// <summary>
        /// 조립·발사 위치. 완성 판정과 하중 시험, 출입문이 여기 붙는다.
        ///
        /// 이름을 <c>AssemblyPoint</c> 로 유지한다 — <see cref="Stage1SkinBuilder"/> 가 이
        /// 이름으로 찾아 주변을 비워 둔다. 예전에는 좌표만 표시하는 빈 오브젝트였고,
        /// 그래서 우주선을 완성해도 발사할 방법이 없었다. <see cref="LaunchTest"/> 는
        /// S15P21B202-195 에서 만들었지만 <c>Begin</c> 을 부르는 곳이 없었다.
        ///
        /// 문을 파츠가 아니라 발사대에 붙이는 이유는 파츠가 움직이기 때문이다. 출입문은
        /// "완성된 우주선이 서 있는 자리" 의 상호작용 부피이고, 실제 문 메시와 열림 연출은
        /// [Art] 의 몫이다(기획 §22.1 후반).
        /// </summary>
        static Transform BuildLaunchPad(Transform group)
        {
            var pad = new GameObject("AssemblyPoint").transform;
            pad.SetParent(group, false);
            pad.localPosition = AssemblyPoint;

            var assembly = pad.gameObject.AddComponent<ShipAssembly>();
            assembly.Configure(pad);

            var launch = pad.gameObject.AddComponent<LaunchTest>();

            var hatchGo = new GameObject("Hatch") { layer = GameLayers.Interactable };
            hatchGo.transform.SetParent(pad, false);
            hatchGo.transform.localPosition = Vector3.up * HatchHeight;

            var sphere = hatchGo.AddComponent<SphereCollider>();
            sphere.radius = HatchRadius;
            sphere.isTrigger = true;

            hatchGo.AddComponent<ShipHatch>().Configure(assembly, launch);
            return pad;
        }

        /// <summary>
        /// 고정쇠로 박혀 있고 용접점을 가진 파츠 하나.
        ///
        /// 예전에는 프리미티브 큐브 하나를 3 x 3.7 x 3m 로 늘려 네 파츠에 똑같이 썼다. 지금은
        /// <see cref="PartSpec"/> 의 실측 상자들로 컴파운드 콜라이더를 만든다. 루트의 스케일은
        /// 1 이고 크기는 자식 상자들이 갖는다 — 스케일로 크기를 표현하면 자식 콜라이더의
        /// 반지름과 좌표까지 함께 늘어나 실제 치수를 읽을 수 없다.
        /// </summary>
        static SpaceshipPart BuildPart(Transform group, int index, PartSpec spec)
        {
            var go = new GameObject(spec.Name) { layer = GameLayers.Part };
            go.transform.SetParent(group, false);
            go.transform.localPosition = PartSpots[index] + Vector3.up * (spec.Height * 0.5f);

            for (var i = 0; i < spec.Boxes.Length; i++) AddBody(go.transform, spec, i);

            go.AddComponent<Rigidbody>();

            AddSocket(go.transform, spec, SocketRole.Base);
            AddSocket(go.transform, spec, SocketRole.Top);

            for (var i = 0; i < ClampsPerPart; i++) AddClamp(go.transform, spec, i);
            for (var i = 0; i < WeldPointsPerPart; i++) AddWeldPoint(go.transform, spec, i);

            var part = go.AddComponent<SpaceshipPart>();
            part.SetPlacedMass(spec.Mass);
            part.Initialize();   // Locked 상태의 물리 표현을 씬에 저장한다
            return part;
        }

        /// <summary>
        /// 콜라이더 상자 하나. 프리미티브 큐브를 쓰는 이유는 스킨을 걷어냈을 때 파츠가
        /// 여전히 보여야 하기 때문이다 — 스킨은 이 렌더러들만 끄고 메시는 그대로 둔다.
        /// </summary>
        static void AddBody(Transform parent, PartSpec spec, int index)
        {
            var box = spec.Boxes[index];

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = $"{BodyPrefix}{index}";
            go.layer = GameLayers.Part;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = box.CenterFromPartCenter(spec.Height);
            go.transform.localScale = box.Size;
        }

        /// <summary>
        /// 접합면. 밑면과 윗면의 한가운데다.
        /// 좌표는 실제 미터다 — 루트 스케일이 1 이므로 ±0.5 같은 비율값을 쓸 수 없다.
        /// </summary>
        static void AddSocket(Transform parent, PartSpec spec, SocketRole role)
        {
            var direction = role == SocketRole.Base ? Vector3.down : Vector3.up;

            var go = new GameObject($"Socket_{role}");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = direction * (spec.Height * 0.5f);
            go.transform.localRotation = Quaternion.LookRotation(direction, Vector3.forward);

            go.AddComponent<PartSocket>().SetRole(role);
        }

        /// <summary>밑면 네 귀퉁이의 고정쇠. 가장 아래 상자의 발자국 안쪽에 둔다.</summary>
        static void AddClamp(Transform parent, PartSpec spec, int index)
        {
            var bottom = spec.Boxes[0];
            var x = bottom.Width * ClampInset;
            var z = bottom.Depth * ClampInset;

            var corners = new[]
            {
                new Vector3(-x, 0f, -z), new Vector3(x, 0f, -z),
                new Vector3(-x, 0f, z), new Vector3(x, 0f, z),
            };

            var go = new GameObject($"Clamp_{index}") { layer = GameLayers.Interactable };
            go.transform.SetParent(parent, false);
            go.transform.localPosition = corners[index % corners.Length]
                                         + Vector3.down * (spec.Height * 0.5f);

            var sphere = go.AddComponent<SphereCollider>();
            sphere.radius = ClampRadius;
            sphere.isTrigger = true;

            go.AddComponent<PartClamp>();
        }

        /// <summary>밑면 둘레의 용접점. 조립된 뒤에만 용접할 수 있다.</summary>
        static void AddWeldPoint(Transform parent, PartSpec spec, int index)
        {
            var bottom = spec.Boxes[0];
            var x = bottom.Width * WeldInset;
            var z = bottom.Depth * 0.5f;

            var spots = new[]
            {
                new Vector3(-x, 0f, -z), new Vector3(x, 0f, -z),
                new Vector3(-x, 0f, z), new Vector3(x, 0f, z),
            };

            var go = new GameObject($"Weld_{index}") { layer = GameLayers.Interactable };
            go.transform.SetParent(parent, false);
            go.transform.localPosition = spots[index % spots.Length]
                                         + Vector3.down * (spec.Height * 0.5f);

            var sphere = go.AddComponent<SphereCollider>();
            sphere.radius = WeldRadius;
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

        static MeteorSpawner BuildMeteors(Transform group)
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

            // 운석 스킨을 여기서 꽂는다. 이 도구가 Puzzle 그룹을 통째로 다시 만들기 때문에
            // 스포너도 새것이 되고, 예전에는 _meteorPrefab 이 비어 폴백 구체로 돌아갔다.
            // 오류도 경고도 나지 않아서 눈으로 봐야 알았다 — S15P21B202-166 부터 그 상태였다.
            if (!Stage1MeteorAssets.TryWire(spawner))
                Debug.LogWarning("[Stage1Puzzle] 운석 프리팹이 없어 폴백 구체로 떨어진다. "
                                 + "'운석 프리팹 만들고 스포너에 꽂기' 를 한 번 돌린다.");

            return spawner;
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
