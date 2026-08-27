using System.IO;
using UnityEditor;
using UnityEngine;

namespace Metagame.EditorTools
{
    /// <summary>
    /// FBX 를 런타임에서 <c>Resources.Load</c> 로 집을 수 있게 얇은 프리팹으로 이어 준다.
    ///
    /// 이 프로젝트는 씬에 부트스트랩 하나만 두고 나머지를 코드가 세우는 구조라
    /// 인스펙터로 에셋을 꽂아 줄 자리가 없다. 그렇다고 FBX 를 Resources 로 옮기면
    /// 모델 생성 스크립트(Tools/Blender/generate_spaceflop_models.py)가 쓰는 경로가 깨진다.
    /// 그래서 <b>원본은 _Project/Models/ 에 그대로 두고</b>, 그것을 가리키는 프리팹만 Resources 에 만든다.
    /// 메시와 재질은 FBX 안에 그대로 있으므로 용량이 두 배가 되지 않는다.
    /// </summary>
    public static class ArtLink
    {
        public const string ResourceDir = "Assets/_Project/Prefabs/Resources";
        // 캐릭터 모델은 SF_Rabbit_Base 하나로 통일했다.
        // 다른 모델을 쓰려면 SpaceSuit.ResourceKey 를 바꾸고 여기에 한 줄 더한다.
        public const string RabbitFbx = "Assets/_Project/Models/Characters/SF_Rabbit_Base.fbx";
        public const string CapsuleFbx = "Assets/_Project/Models/Spaceships/SF_Spaceship_Capsule.fbx";

        /// <summary>런타임 코드가 이 이름으로 찾는다.</summary>
        public const string RabbitKey = "SF_Rabbit_Base";
        public const string CapsuleKey = "SF_Spaceship_Capsule";

        [MenuItem("Tools/둥실둥실 우주 대탈출/모델 → Resources 프리팹 갱신", false, 61)]
        public static void BuildPrefabs()
        {
            Directory.CreateDirectory(ResourceDir);
            Link(RabbitFbx, RabbitKey);
            Link(CapsuleFbx, CapsuleKey);
            CleanStale();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        /// <summary>더 이상 쓰지 않는 프리팹은 지운다 — 안 그러면 빌드에 계속 딸려 들어간다.</summary>
        static void CleanStale()
        {
            string[] keep = { RabbitKey, CapsuleKey };
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { ResourceDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string name = System.IO.Path.GetFileNameWithoutExtension(path);
                if (System.Array.IndexOf(keep, name) >= 0) continue;
                AssetDatabase.DeleteAsset(path);
                Debug.Log("[ArtLink] 안 쓰는 프리팹 정리: " + path);
            }
        }

        static void Link(string fbxPath, string key)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (asset == null)
            {
                Debug.LogWarning("[ArtLink] FBX 를 찾지 못했다: " + fbxPath);
                return;
            }

            // PrefabUtility.InstantiatePrefab 으로 만들면 저장할 때 '프리팹 변형' 이 되어
            // FBX 재임포트마다 링크가 흔들린다. 평범한 클론을 저장해 독립된 프리팹으로 둔다.
            var clone = Object.Instantiate(asset);
            clone.name = key;

            string path = ResourceDir + "/" + key + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(clone, path);
            Object.DestroyImmediate(clone);

            Debug.Log("[ArtLink] " + key + " → " + path);
        }
    }
}
