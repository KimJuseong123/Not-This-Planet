using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Metagame.EditorTools
{
    /// <summary>
    /// 생성한 Blender FBX가 Unity에서 의도한 구조로 임포트됐는지 검사한다.
    /// 메뉴와 batchmode 양쪽에서 동일한 검사를 실행한다.
    /// </summary>
    public static class SpaceFlopModelValidator
    {
        // 런타임이 쓰는 모델은 SF_Rabbit_Base 하나다 (SpaceSuit.ResourceKey, ArtLink.RabbitKey).
        // 예전에는 SF_Astronaut_Base 를 검사해, 실제로 쓰지 않는 파일을 검증하고 있었다.
        const string CharacterPath = "Assets/_Project/Models/Characters/SF_Rabbit_Base.fbx";
        const string ShipPath = "Assets/_Project/Models/Spaceships/SF_Spaceship_Capsule.fbx";

        /// <summary>
        /// SF_Rabbit_Base 의 필수 본. 이름이 Humanoid 규격과 다르다.
        ///
        /// 이 모델은 Tripo 로 생성해 온 것이라 Hip / Spine01 / L_Upperarm 처럼
        /// 자체 규칙을 쓴다. 예전에 검사하던 SF_Astronaut_Base 는 블렌더에서 손으로
        /// 만들어 Hips / Spine / LeftUpperArm 같은 Humanoid 이름을 썼다.
        /// 모델을 SF_Rabbit_Base 로 통일하면서 이름도 실제 모델에 맞췄다.
        ///
        /// 비틀림 본(*Twist*)과 발가락은 물리 인형이 쓰지 않으므로 필수에서 뺐다.
        /// </summary>
        static readonly string[] RequiredBones =
        {
            "Root", "Hip", "Waist", "Spine01", "Spine02", "Head",
            "L_Clavicle", "L_Upperarm", "L_Forearm", "L_Hand",
            "R_Clavicle", "R_Upperarm", "R_Forearm", "R_Hand",
            "L_Thigh", "L_Calf", "L_Foot",
            "R_Thigh", "R_Calf", "R_Foot",
        };

        static readonly string[] RequiredShipParts =
        {
            "Hull_Main", "Porthole_Glass", "Side_Hatch_Door", "Top_Dome",
            "Engine_Main_Bell", "RCS_Nozzle_L", "RCS_Nozzle_R",
            "Landing_Foot_FL", "Landing_Foot_FR", "Landing_Foot_BL", "Landing_Foot_BR",
            "Repair_Socket_POWER", "Repair_Socket_FUEL", "Repair_Socket_OXYGEN",
        };

        public static void ValidateFromMenu()
        {
            var failures = ValidateAll();
            if (failures.Count == 0)
                EditorUtility.DisplayDialog("SpaceFlop 3D 모델", "캐릭터와 우주선 FBX 검사를 통과했습니다.", "확인");
            else
                EditorUtility.DisplayDialog("SpaceFlop 3D 모델", string.Join("\n", failures.ToArray()), "확인");
        }

        /// <summary>
        /// CI:
        /// Unity.exe -batchmode -quit -projectPath .
        ///   -executeMethod SpaceFlop.EditorTools.SpaceFlopModelValidator.ValidateBatch
        /// </summary>
        public static void ValidateBatch()
        {
            var failures = ValidateAll();
            if (failures.Count == 0)
            {
                Debug.Log("[MODELTEST] PASS");
                return;
            }

            for (int i = 0; i < failures.Count; i++) Debug.LogError("[MODELTEST] " + failures[i]);
            Debug.LogError("[MODELTEST] FAIL " + failures.Count + "건");
            EditorApplication.Exit(1);
        }

        static List<string> ValidateAll()
        {
            var failures = new List<string>();
            ValidateCharacter(failures);
            ValidateShip(failures);
            if (failures.Count == 0)
                Debug.Log("[MODELTEST] 캐릭터/우주선 Unity 임포트 구조 정상");
            return failures;
        }

        static void ValidateCharacter(List<string> failures)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterPath);
            if (prefab == null)
            {
                failures.Add("캐릭터 FBX를 읽지 못함: " + CharacterPath);
                return;
            }

            var transforms = prefab.GetComponentsInChildren<Transform>(true);
            var names = new HashSet<string>();
            for (int i = 0; i < transforms.Length; i++) names.Add(transforms[i].name);
            for (int i = 0; i < RequiredBones.Length; i++)
                if (!names.Contains(RequiredBones[i])) failures.Add("캐릭터 본 누락: " + RequiredBones[i]);

            var skins = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var mats = CountMaterials(prefab.GetComponentsInChildren<Renderer>(true));
            Bounds bounds;
            if (!TryGetBounds(prefab, out bounds)) failures.Add("캐릭터 렌더 bounds 없음");
            // 기준치는 SF_Rabbit_Base 의 실제 구조다. 임포트 스케일 사고와 재질 유실을 잡는 것이 목적이다.
            // 실측값: 높이 1.084m, SkinnedMeshRenderer 1개, 재질 1개.
            // 예전 기준(높이 1.25~1.60m, 스킨 45개, 재질 8개)은 여러 부위로 쪼개져 있던
            // SF_Astronaut_Base 의 구조였다. 모델을 통일하면서 함께 맞췄다.
            else if (bounds.size.y < 0.95f || bounds.size.y > 1.25f)
                failures.Add("캐릭터 높이 이상: " + bounds.size.y.ToString("0.000") + "m");

            if (skins.Length < 1) failures.Add("캐릭터 SkinnedMeshRenderer 없음: " + skins.Length);
            if (mats < 1) failures.Add("캐릭터 재질 슬롯 유실: " + mats);
            Debug.Log("[MODELTEST] 캐릭터 skins=" + skins.Length + " materials=" + mats
                      + " height=" + (TryGetBounds(prefab, out bounds) ? bounds.size.y.ToString("0.000") : "없음"));
        }

        static void ValidateShip(List<string> failures)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ShipPath);
            if (prefab == null)
            {
                failures.Add("우주선 FBX를 읽지 못함: " + ShipPath);
                return;
            }

            var transforms = prefab.GetComponentsInChildren<Transform>(true);
            var names = new HashSet<string>();
            for (int i = 0; i < transforms.Length; i++) names.Add(transforms[i].name);
            for (int i = 0; i < RequiredShipParts.Length; i++)
                if (!names.Contains(RequiredShipParts[i])) failures.Add("우주선 부품 누락: " + RequiredShipParts[i]);

            var filters = prefab.GetComponentsInChildren<MeshFilter>(true);
            var mats = CountMaterials(prefab.GetComponentsInChildren<Renderer>(true));
            Bounds bounds;
            if (!TryGetBounds(prefab, out bounds)) failures.Add("우주선 렌더 bounds 없음");
            else if (bounds.size.y < 4.1f || bounds.size.y > 4.8f)
                failures.Add("우주선 높이 이상: " + bounds.size.y.ToString("0.000") + "m");

            if (filters.Length < 80) failures.Add("우주선 MeshFilter 부족: " + filters.Length);
            if (mats < 8) failures.Add("우주선 재질 슬롯 유실: " + mats);
            Debug.Log("[MODELTEST] 우주선 meshes=" + filters.Length + " materials=" + mats
                      + " height=" + (TryGetBounds(prefab, out bounds) ? bounds.size.y.ToString("0.000") : "없음"));
        }

        static int CountMaterials(Renderer[] renderers)
        {
            var materials = new HashSet<Material>();
            for (int i = 0; i < renderers.Length; i++)
            {
                var shared = renderers[i].sharedMaterials;
                for (int k = 0; k < shared.Length; k++) if (shared[k] != null) materials.Add(shared[k]);
            }
            return materials.Count;
        }

        static bool TryGetBounds(GameObject prefab, out Bounds bounds)
        {
            // FBX assets may carry an importer root-axis correction. Renderer.bounds on a
            // short-lived scene instance includes that correction exactly as gameplay does.
            var instance = UnityEngine.Object.Instantiate(prefab);
            instance.name = "~SpaceFlopModelValidation";
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            bool initialized = false;
            bounds = default(Bounds);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (!initialized) { bounds = renderers[i].bounds; initialized = true; }
                else bounds.Encapsulate(renderers[i].bounds);
            }
            UnityEngine.Object.DestroyImmediate(instance);
            return initialized;
        }
    }
}
