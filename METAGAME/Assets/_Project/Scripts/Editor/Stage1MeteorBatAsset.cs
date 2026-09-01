using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Metagame.Gameplay;

namespace Metagame.EditorTools
{
    /// <summary>
    /// [Art] 가 준 운석 방망이 프리팹에 게임플레이 컴포넌트를 붙이고 Main 씬에 놓는다
    /// (기획 §18, S15P21B202-233).
    ///
    /// <b>프리팹을 새로 만들지 않는다.</b> S15P21B202-187 이 이미 FBX·텍스처·Material·
    /// 프리팹을 넘겼고, 자산 목록 5-1 에 "휘두르기 애니메이션·타격 판정·운석 반사·장착·
    /// 네트워크 로직은 포함하지 않는다" 고 적혀 있다. 그 프리팹 위에 빠진 것만 얹는다.
    ///
    /// 절차는 <see cref="Stage1WeldingPlasmaGunAsset"/> 과 같다 — 프리팹을 열어
    /// 컴포넌트를 보장하고, 씬에 인스턴스가 없으면 하나 놓고, <b>이미 있으면 건드리지
    /// 않는다.</b> 손으로 옮겨 둔 배치를 도구가 되돌리면 안 된다.
    /// </summary>
    public static class Stage1MeteorBatAsset
    {
        const string ScenePath = "Assets/Scenes/Main.unity";
        const string PrefabPath =
            "Assets/_Project/Prefabs/Props/Stage1/PF_Stage1_MeteorBat.prefab";

        const string EquipmentPath = "Stage1/Gameplay/Puzzle/Equipment";
        const string InstanceName = "MeteorBat_Stage1";

        /// <summary>
        /// 방망이 질량(kg).
        ///
        /// 용접기(4kg)보다 가볍게 잡았다. 기획 §9.3 이 장비를 한 손에 들게 하고,
        /// §18.1 이 "휘두른다" 고 하므로 들고 뛰는 데 부담이 없어야 한다.
        /// </summary>
        const float BatMass = 3f;

        /// <summary>발사대에서 이만큼 떨어진 곳에 둔다. 조립대 반경(9m) 안이다.</summary>
        static readonly Vector3 FallbackPosition = new(4f, 0.7f, -3f);

        [MenuItem("Tools/둥실둥실 우주 대탈출/운석 방망이 배치", false, 10)]
        public static void Build()
        {
            var prefab = EnsureToolComponent();
            if (prefab == null)
            {
                Debug.LogError(
                    "[Stage1MeteorBat] 프리팹을 찾지 못했다: " + PrefabPath +
                    " — [Art] 의 S15P21B202-187 산출물이 있어야 한다.");
                return;
            }

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var puzzle = FindByPath("Stage1/Gameplay/Puzzle");
            if (puzzle == null)
            {
                Debug.LogError("[Stage1MeteorBat] Stage1/Gameplay/Puzzle 를 찾지 못했다.");
                return;
            }

            var equipment = puzzle.Find("Equipment");
            if (equipment == null)
            {
                equipment = new GameObject("Equipment").transform;
                equipment.SetParent(puzzle, false);
            }

            if (equipment.Find(InstanceName) == null)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                instance.name = InstanceName;
                instance.transform.SetParent(equipment, false);

                var pad = FindByPath("Stage1/Gameplay/Puzzle/AssemblyPoint");
                var position = pad != null
                    ? pad.position + new Vector3(4f, 0.7f, -3f)
                    : FallbackPosition;

                instance.transform.SetPositionAndRotation(position, Quaternion.identity);
                EditorSceneManager.MarkSceneDirty(scene);
                Debug.Log("[Stage1MeteorBat] Main 씬에 방망이 배치 → " + position);
            }
            else
            {
                Debug.Log("[Stage1MeteorBat] 기존 배치를 유지한다: " +
                          EquipmentPath + "/" + InstanceName);
            }

            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Stage1MeteorBat] 완료 — " + PrefabPath);
        }

        /// <summary>
        /// 프리팹에 <see cref="MeteorBat"/> 과 집을 수 있는 조건을 보장한다.
        ///
        /// <see cref="Grabbable"/> 의 역할을 <c>Tool</c> 로 준다. 그래야
        /// <see cref="PlayerRig"/> 가 집을 때 도구 장착 경로를 타고
        /// <see cref="ToolBase.Equip"/> 가 불린다 — 용접기와 같은 규약이다.
        /// </summary>
        static GameObject EnsureToolComponent()
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (asset == null) return null;

            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                root.layer = GameLayers.Prop;

                var body = root.GetComponent<Rigidbody>();
                if (body == null) body = root.AddComponent<Rigidbody>();
                body.mass = BatMass;
                body.useGravity = true;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

                // 콜라이더가 없으면 집을 수도, 바닥에 놓일 수도 없다. [Art] 프리팹은
                // 순수 아트라 콜라이더가 없을 수 있으므로 없을 때만 보탠다.
                if (root.GetComponentInChildren<Collider>(true) == null)
                {
                    var collider = root.AddComponent<BoxCollider>();
                    collider.size = new Vector3(0.34f, 1.12f, 0.34f);
                    collider.center = new Vector3(0f, 0.56f, 0f);
                }

                var grabbable = Grabbable.Mark(root, "운석 방망이", false, 1f, 2f,
                                               GrabbableRole.Tool);
                grabbable.CanGrab = true;

                if (root.GetComponent<MeteorBat>() == null) root.AddComponent<MeteorBat>();

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        }

        static Transform FindByPath(string path)
        {
            var parts = path.Split('/');
            var root = GameObject.Find(parts[0]);
            if (root == null) return null;

            var current = root.transform;
            for (var i = 1; i < parts.Length && current != null; i++)
                current = current.Find(parts[i]);

            return current;
        }
    }
}
