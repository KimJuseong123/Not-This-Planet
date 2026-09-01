using Metagame.Gameplay;
using Metagame.Gameplay.Hazards;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Metagame.EditorTools
{
    /// <summary>
    /// 운석 프리팹을 만들어 씬의 스포너에 꽂는다.
    ///
    /// <see cref="MeteorSpawner"/> 는 이미 <c>_meteorPrefab</c> 자리를 갖고 있고, 비어 있으면
    /// 프리미티브 구체를 만들어 쓴다. 그 폴백에 달린 주석이 "아트가 들어오면 프리팹을 꽂아
    /// 대체한다" 이므로 그 의도대로 프리팹을 만들어 꽂는다. 런타임 코드는 건드리지 않는다.
    ///
    /// 프리팹 구성은 폴백이 코드로 만들던 것과 같다.
    ///
    /// <code>
    /// PF_Stage1_Meteor      layer Meteor · Rigidbody(질량 60kg) · SphereCollider(r 0.45)
    ///  │                    · Meteor
    ///  └─ Visual            PF_Stage1_MeteorMain — 겉보기 지름을 0.9m 로 맞춘 배율
    /// </code>
    ///
    /// 카탈로그 규칙 ②(프리팹에 Collider·Rigidbody 를 붙이지 않는다)는 배치용 아트 프리팹에
    /// 대한 규칙이다. 이쪽은 스폰되는 게임플레이 프리팹이라 물리가 프리팹에 있어야 한다.
    /// 아트 프리팹(<c>PF_Stage1_MeteorMain</c>)은 규칙대로 시각물만 갖고, 그것을 자식으로 쓴다.
    ///
    /// 크기는 <see cref="PhysicsScale.MeteorRadius"/> 가 기준이다. 폴백 구체가 지름
    /// <c>MeteorRadius * 2</c> 였으므로 모델도 같은 지름에 맞춘다 — 물리값은 바꾸지 않는다.
    ///
    /// 이 프리팹은 도구가 만든다. 다시 실행하면 덮어쓰므로 인스펙터로 조정한 값은 남지 않는다.
    /// 조정할 값이 생기면 <see cref="PhysicsScale"/> 이나 이 파일에 넣는다.
    ///
    /// CI: Unity.exe -batchmode -quit -projectPath . -executeMethod Metagame.EditorTools.Stage1MeteorAssets.Apply
    /// </summary>
    public static class Stage1MeteorAssets
    {
        const string ScenePath = "Assets/Scenes/Main.unity";
        const string PrefabDir = "Assets/_Project/Prefabs/Hazards/Stage1";
        const string PrefabPath = PrefabDir + "/PF_Stage1_Meteor.prefab";

        /// <summary>낙하하는 운석 본체. 파편 2종은 아직 쓰는 시스템이 없다.</summary>
        const string ArtName = "MeteorMain";

        const string VisualName = "Visual";

        public static void Apply()
        {
            Stage1EnvironmentAssets.Build();          // 운석 Material·아트 프리팹이 없으면 먼저 만든다

            if (BuildPrefab() == null) return;

            int wired = AssignToSpawners();
            Debug.Log($"[Stage1Meteor] 운석 적용 완료 — 프리팹 {PrefabPath}, 스포너 {wired}기에 연결");
        }

        /// <summary>폴백 구체와 같은 구성의 운석 프리팹. 시각물만 모델로 바꾼 것이다.</summary>
        static GameObject BuildPrefab()
        {
            string artPath = Stage1EnvironmentAssets.PrefabPath(ArtName);
            var art = AssetDatabase.LoadAssetAtPath<GameObject>(artPath);
            if (art == null)
            {
                Debug.LogError("[Stage1Meteor] 아트 프리팹을 찾지 못했다: " + artPath);
                return null;
            }

            System.IO.Directory.CreateDirectory(PrefabDir);
            AssetDatabase.Refresh();

            var root = new GameObject("PF_Stage1_Meteor") { layer = GameLayers.Meteor };

            // Meteor 가 RequireComponent 로 Rigidbody 를 요구한다. 질량은 Meteor.Initialize 도
            // 같은 값으로 덮지만, 프리팹만 열어 봐도 보이도록 여기서 넣어 둔다.
            var body = root.AddComponent<Rigidbody>();
            body.mass = PhysicsScale.MeteorMass;

            // 착탄은 OnCollisionEnter 로 잡는다. 콜라이더가 없으면 영원히 떨어진다.
            var sphere = root.AddComponent<SphereCollider>();
            sphere.radius = PhysicsScale.MeteorRadius;

            // DamageSource 를 붙이지 않는다 — Meteor 가 §20.1 의 세 단계를 직접 정한다
            // (S15P21B202-247). 폴백(MeteorSpawner)과 동일하다.
            root.AddComponent<Meteor>();

            var visual = (GameObject)PrefabUtility.InstantiatePrefab(art);
            visual.name = VisualName;
            visual.transform.SetParent(root.transform, false);

            if (Measure(visual, out var size, out var center))
            {
                float diameter = PhysicsScale.MeteorRadius * 2f;
                float scale = diameter / Mathf.Max(size.x, Mathf.Max(size.y, size.z));

                visual.transform.localScale = Vector3.one * scale;
                visual.transform.localPosition = -center * scale;   // 메시 중심을 콜라이더 중심에

                Debug.Log($"[Stage1Meteor] 시각물 배율 {scale:0.000} — 원본 {size.x:0.00} x {size.y:0.00} x {size.z:0.00}m "
                          + $"→ 지름 {diameter:0.00}m");
            }

            var saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);

            if (saved == null) Debug.LogError("[Stage1Meteor] 프리팹 저장에 실패했다: " + PrefabPath);
            return saved;
        }

        /// <summary>
        /// 씬의 모든 MeteorSpawner 에 프리팹을 꽂는다. 필드가 private 이라 SerializedObject 로 쓴다.
        ///
        /// 프리팹은 <b>씬을 연 뒤에</b> 경로로 다시 불러온다. 씬을 로드하면 Unity 가 참조 없는 에셋을
        /// 언로드하는데, 그때 방금 만든 프리팹을 가리키던 C# 참조가 fake-null 이 된다.
        /// 그 상태로 비어 있는 필드와 <c>==</c> 로 비교하면 "이미 같다" 로 읽혀 조용히 넘어간다 —
        /// 처음에 이 함수가 아무것도 연결하지 않고 성공한 것처럼 끝난 이유가 이것이었다.
        /// 대입 결과도 <see cref="SerializedObject.ApplyModifiedPropertiesWithoutUndo"/> 의 반환값으로
        /// 판정해서, 비교에 기대지 않는다.
        /// </summary>
        /// <summary>
        /// 스포너 하나에 운석 프리팹을 꽂는다. 프리팹이 아직 없으면 <c>false</c>.
        ///
        /// <see cref="Stage1PuzzleBuilder"/> 도 이것을 쓴다. 배치 도구가 <c>Puzzle</c> 그룹을
        /// 통째로 다시 만들면서 스포너도 새로 만들기 때문이다. 예전에는 이 연결이 이 파일에만
        /// 있어서 <b>배치를 다시 돌릴 때마다 운석 스킨이 조용히 풀렸다</b> — 폴백 구체로
        /// 돌아가는데 오류도 경고도 나지 않으니 눈으로 봐야 알았다.
        ///
        /// 프리팹 경로를 아는 곳은 이 파일 하나로 유지한다.
        /// </summary>
        public static bool TryWire(MeteorSpawner spawner)
        {
            if (spawner == null) return false;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var meteor = prefab != null ? prefab.GetComponent<Meteor>() : null;
            if (meteor == null) return false;

            var serialized = new SerializedObject(spawner);
            var field = serialized.FindProperty("_meteorPrefab");
            if (field == null)
            {
                Debug.LogWarning("[Stage1Meteor] MeteorSpawner 에 _meteorPrefab 이 없다. 필드 이름이 바뀌었는지 확인한다.");
                return false;
            }

            field.objectReferenceValue = meteor;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }

        static int AssignToSpawners()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var meteor = prefab != null ? prefab.GetComponent<Meteor>() : null;
            if (meteor == null)
            {
                Debug.LogError("[Stage1Meteor] 프리팹에서 Meteor 컴포넌트를 불러오지 못했다: " + PrefabPath);
                return 0;
            }

            var wired = 0;
            var found = 0;

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var spawner in root.GetComponentsInChildren<MeteorSpawner>(true))
                {
                    found++;
                    if (TryWire(spawner)) wired++;
                }
            }

            if (found == 0)
                Debug.LogWarning("[Stage1Meteor] 씬에서 MeteorSpawner 를 찾지 못했다. Stage1PuzzleBuilder 를 먼저 돌린다.");

            if (wired > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }

            return wired;
        }

        /// <summary>렌더러 경계로 실제 크기와 중심을 잰다. 배율 1 · 회전 없음 상태에서 불러야 한다.</summary>
        static bool Measure(GameObject probe, out Vector3 size, out Vector3 center)
        {
            Bounds bounds = default;
            var first = true;

            foreach (var renderer in probe.GetComponentsInChildren<Renderer>(true))
            {
                if (first) { bounds = renderer.bounds; first = false; }
                else bounds.Encapsulate(renderer.bounds);
            }

            size = bounds.size;
            center = bounds.center;

            if (first) Debug.LogWarning("[Stage1Meteor] 렌더러가 없어 크기를 재지 못했다: " + probe.name);
            return !first;
        }
    }
}
