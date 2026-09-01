using System.Collections.Generic;
using System.Text;
using Metagame.Core;
using Metagame.Gameplay;
using Metagame.Gameplay.Hazards;
using Metagame.Gameplay.Parts;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Metagame.EditorTools
{
    /// <summary>
    /// 1장 플레이테스트를 시작할 수 있는 상태인지 씬을 점검한다(S15P21B202-221).
    ///
    /// <b>왜 필요한가.</b> 기획 §26 의 확인 항목은 사람이 플레이해야 판정할 수 있다.
    /// 그런데 플레이를 시작조차 못 하는 상태가 눈에 띄지 않게 만들어진다 —
    /// 2026-08-31 에 두 번 겪었다.
    ///
    ///  · <c>TD_Stage1_Arena.asset</c> 이 체크아웃에서 깨져 <c>TerrainCollider</c> 에
    ///    데이터가 없고, 옛 평면은 꺼져 있어 <b>밟을 콜라이더가 0개</b>였다.
    ///    빌드는 "Succeeded" 로 끝나고 실행해도 예외가 없다. 화면을 봐야 알았다.
    ///  · <c>MeteorSpawner._meteorPrefab</c> 이 배치 도구를 다시 돌릴 때마다 조용히
    ///    풀려 운석이 회색 구체로 돌아갔다(S15P21B202-200 에서 발견).
    ///
    /// 둘 다 오류도 경고도 내지 않는다. 그래서 <b>기계가 볼 수 있는 것</b>을 여기 모아
    /// 매번 같은 순서로 확인한다. 사람의 판정을 대신하지 않는다 — 사람이 판정을
    /// 시작할 수 있는지만 본다.
    ///
    /// CI: Unity.exe -batchmode -quit -projectPath . -executeMethod Metagame.EditorTools.Stage1QaAudit.Run
    /// </summary>
    public static class Stage1QaAudit
    {
        const string ScenePath = "Assets/Scenes/Main.unity";

        static readonly List<string> Lines = new();
        static int _failed;
        static int _warned;

        [MenuItem("Tools/둥실둥실 우주 대탈출/1장 QA 점검", false, 21)]
        public static void Run()
        {
            Lines.Clear();
            _failed = 0;
            _warned = 0;

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            Lines.Add("===== 1장 QA 점검 =====");
            CheckGround(scene);
            CheckSpawn(scene);
            CheckParts(scene);
            CheckLaunchPad(scene);
            CheckMeteors(scene);
            CheckCraft(scene);
            CheckDesignValues();

            Lines.Add($"== 점검 끝: 실패 {_failed} · 경고 {_warned} ==");

            var report = string.Join("\n", Lines);
            if (_failed > 0) Debug.LogError(report);
            else Debug.Log(report);

            if (Application.isBatchMode && _failed > 0) EditorApplication.Exit(1);
        }

        // ---------------------------------------------------------------- 항목

        /// <summary>
        /// 밟을 지면이 있는가. <b>이것이 없으면 나머지 확인이 의미가 없다</b> —
        /// 플레이어가 서 있지 못하고 계속 떨어진다.
        /// </summary>
        static void CheckGround(Scene scene)
        {
            Section("지면");

            var probes = new[]
            {
                new Vector3(0f, 30f, 0f),
                new Vector3(0f, 30f, 10f),
                new Vector3(8f, 30f, -8f),
            };

            var missing = 0;
            foreach (var probe in probes)
            {
                var hits = UnityEngine.Physics.RaycastAll(probe, Vector3.down, 80f);
                var ground = System.Array.Find(hits, h => h.collider != null
                                                          && h.collider.gameObject.layer == GameLayers.Ground);

                if (ground.collider == null)
                {
                    missing++;
                    Fail($"({probe.x:0},{probe.z:0}) 아래에 Ground 레이어 콜라이더가 없다");
                }
                else
                {
                    Ok($"({probe.x:0},{probe.z:0}) 아래 {ground.distance:0.0}m 에 지면 — "
                       + Path(ground.collider.transform));
                }
            }

            if (missing == 0) return;

            // 원인을 함께 적는다. 이 실패를 보는 사람이 곧바로 어디를 볼지 알아야 한다.
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var terrain in root.GetComponentsInChildren<Terrain>(true))
                {
                    if (terrain.terrainData == null)
                        Note($"{Path(terrain.transform)} 의 TerrainData 가 null 이다 — "
                             + "TD_Stage1_Arena.asset 이 체크아웃에서 깨졌을 수 있다"
                             + "(.gitattributes 의 *.asset text 가 바이너리 자산을 텍스트로 다룬다)");
                }

                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name != "ENV_Ground_Arena_50m") continue;
                    if (!t.gameObject.activeInHierarchy)
                        Note($"{Path(t)} 이 꺼져 있다 — Terrain 이 대신 서야 하는데 그것이 비었다면 땅이 없다");
                }
            }
        }

        static void CheckSpawn(Scene scene)
        {
            Section("플레이어 스폰");

            var spawn = Find(scene, "PlayerSpawn");
            if (spawn == null)
            {
                Fail("PlayerSpawn 을 찾지 못했다");
                return;
            }

            Ok($"PlayerSpawn {spawn.position}");

            var hits = UnityEngine.Physics.RaycastAll(spawn.position + Vector3.up * 5f, Vector3.down, 60f);
            var ground = System.Array.Find(hits, h => h.collider != null
                                                      && h.collider.gameObject.layer == GameLayers.Ground);
            if (ground.collider == null) Fail("스폰 지점 아래에 지면이 없다 — 시작하자마자 떨어진다");
            else Ok($"스폰 아래 {ground.distance - 5f:0.0}m 에 지면");
        }

        static void CheckParts(Scene scene)
        {
            Section("파츠");

            var parts = new List<SpaceshipPart>();
            foreach (var root in scene.GetRootGameObjects())
                parts.AddRange(root.GetComponentsInChildren<SpaceshipPart>(true));

            if (parts.Count != PartSpec.All.Length)
                Fail($"파츠가 {parts.Count}개다. 기획은 {PartSpec.All.Length}개(§7.1)");
            else
                Ok($"파츠 {parts.Count}개");

            foreach (var spec in PartSpec.All)
            {
                var part = parts.Find(p => p.name == spec.Name);
                if (part == null)
                {
                    Fail($"{spec.Name}({spec.DisplayName})이 씬에 없다");
                    continue;
                }

                part.Initialize();
                part.RefreshClamps();

                var clamps = part.Clamps.Count;

                if (clamps != 4) Fail($"{spec.Name} 의 고정못이 {clamps}개다. 기획은 4개(§12.1)");

                // 용접 준비물을 검사하지 않는다. S15P21B202-230 에서 원형 접합선을
                // 걷어낸 뒤로 파츠에 미리 놓아 둘 것이 없다 — 비드는 용접기를 쏘는
                // 자리에 생기고, 검사할 것은 값(아래 CheckDesignValues)뿐이다.
                if (clamps == 4)
                    Ok($"{spec.Name}({spec.DisplayName}) 고정못 4 · 상태 {part.State}");
            }
        }

        static void CheckLaunchPad(Scene scene)
        {
            Section("발사대");

            var assembly = FindComponent<ShipAssembly>(scene);
            var launch = FindComponent<LaunchTest>(scene);
            var hatch = FindComponent<ShipHatch>(scene);

            if (assembly == null) Fail("ShipAssembly 가 없다 — 완성 판정이 돌지 않는다(§22.1)");
            else Ok($"ShipAssembly {Path(assembly.transform)}");

            if (launch == null) Fail("LaunchTest 가 없다 — 발사할 수 없다(§22.3)");
            else Ok("LaunchTest 있음");

            if (hatch == null)
            {
                Fail("ShipHatch 가 없다 — 탑승할 수 없다(§22.1)");
                return;
            }

            Ok($"ShipHatch {Path(hatch.transform)} · 탭 상호작용({hatch.HoldSeconds:0.0}초)");

            // 문이 판정·시험·스포너를 모두 붙들고 있어야 발사 흐름이 이어진다.
            if (hatch.GetAvailability(null).IsAllowed)
                Note("아직 아무것도 조립하지 않았는데 문이 열려 있다 — 완성 판정을 확인한다");
        }

        static void CheckMeteors(Scene scene)
        {
            Section("운석");

            var spawner = FindComponent<MeteorSpawner>(scene);
            if (spawner == null)
            {
                Fail("MeteorSpawner 가 없다 — 운석 공세가 없다(§17)");
                return;
            }

            Ok($"MeteorSpawner {Path(spawner.transform)}");

            // 프리팹이 비면 폴백 회색 구체가 나온다. 오류도 경고도 없이 스킨만 사라진다.
            var serialized = new SerializedObject(spawner);
            var prefab = serialized.FindProperty("_meteorPrefab");
            if (prefab == null) Fail("MeteorSpawner 에 _meteorPrefab 이 없다 — 필드 이름이 바뀌었는지 확인한다");
            else if (prefab.objectReferenceValue == null)
                Fail("운석 프리팹이 비었다 — 폴백 회색 구체가 나온다"
                     + "('운석 프리팹 만들고 스포너에 꽂기' 를 돌린다)");
            else Ok($"운석 프리팹 {prefab.objectReferenceValue.name}");
        }

        static void CheckCraft(Scene scene)
        {
            Section("견인 조종 모듈");

            var crafts = new List<TransportVehicle>();
            foreach (var root in scene.GetRootGameObjects())
                crafts.AddRange(root.GetComponentsInChildren<TransportVehicle>(true));

            if (crafts.Count == 0)
            {
                Fail("TransportVehicle 이 없다 — 파츠를 옮길 수단이 없다(§14)");
                return;
            }

            Ok($"견인 장치 {crafts.Count}기");

            foreach (var craft in crafts)
            {
                if (craft.Field == null)
                    Fail($"{Path(craft.transform)} 에 광역 저중력장이 없다(§14.3)");
            }
        }

        /// <summary>
        /// 구현 상수가 기획 수치와 맞는가. 플레이하지 않고도 볼 수 있는 부분이다.
        /// 어긋나면 플레이테스트에서 "느낌이 이상하다" 로 나오기 전에 여기서 잡힌다.
        /// </summary>
        static void CheckDesignValues()
        {
            Section("기획 수치");

            Value("고정못 1명 (§12.2 약 8초)", PhysicsScale.ClampPullSeconds, 8f);
            Value("고정못 2명 (§12.2 약 4.5초)", PartClamp.PullSecondsFor(2), 4.5f, 0.3f);
            Value("용접기 사거리 (§16.1 약 0.8m)", PhysicsScale.WeldRange, 0.8f, 0.05f);

            // 접합부 하나를 채우는 시간. 예전에는 접합선을 훑는 10초였고, 지금은
            // 사슬 하나를 끝까지 쌓는 시간이 그것을 대신한다(기획 §16.1 약 10초).
            Value("용접 최단 (§16.1 약 10초)",
                  PhysicsScale.WeldBeadBlobSeconds * PhysicsScale.WeldBeadBlobsPerChain,
                  10f, 1f);
            Value("비드 덩어리 상한 (§16.2)", PhysicsScale.WeldBeadMaxRadius, 0.10f, 0.01f);
            Value("완전 강도까지 덩어리 (§16.2)", PhysicsScale.WeldBeadFullStrengthBlobs, 3f);
            Value("기체 수평 (§14.1 8m/s)", 8f, 8f);
            Value("기체 가속 (§14.1 12m/s)", PhysicsScale.CraftBoostSpeed, 12f);
            Value("견인 중 감속 (§14.1 20%)", PhysicsScale.CraftTowSpeedFactor, 0.8f, 0.01f);
            Value("카운트다운 (§22.3 3초)", PhysicsScale.LaunchCountdownSeconds, 3f);
            Value("상승 (§22.5 8~10초)", PhysicsScale.AscentSeconds, 9f, 1f);
            Value("플레이어 부유 수평 (§14.3 3m/s)", PhysicsScale.FieldFloatSpeed, 3f);
            Value("플레이어 부유 상하 (§14.3 2m/s)", PhysicsScale.FieldFloatVerticalSpeed, 2f);
        }

        // ---------------------------------------------------------------- 도구

        static void Section(string name) => Lines.Add($"-- {name} --");

        static void Ok(string message) => Lines.Add($"   OK   {message}");

        static void Fail(string message)
        {
            _failed++;
            Lines.Add($"   실패 {message}");
        }

        static void Note(string message)
        {
            _warned++;
            Lines.Add($"   경고 {message}");
        }

        static void Value(string label, float actual, float expected, float tolerance = 0.01f)
        {
            if (Mathf.Abs(actual - expected) <= tolerance) Ok($"{label} = {actual:0.##}");
            else Fail($"{label} 인데 구현은 {actual:0.##} 이다");
        }

        static T FindComponent<T>(Scene scene) where T : Component
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var found = root.GetComponentInChildren<T>(true);
                if (found != null) return found;
            }

            return null;
        }

        static Transform Find(Scene scene, string name)
        {
            foreach (var root in scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == name)
                        return t;

            return null;
        }

        static string Path(Transform t)
        {
            var sb = new StringBuilder(t.name);
            while (t.parent != null)
            {
                t = t.parent;
                sb.Insert(0, t.name + "/");
            }

            return sb.ToString();
        }
    }
}
