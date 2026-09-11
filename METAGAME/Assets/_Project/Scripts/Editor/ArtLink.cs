using System.Collections.Generic;
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
        public const string HandFbx = "Assets/_Project/Models/Characters/Parts/CharacterHand/character_hand.fbx";

        /// <summary>런타임 코드가 이 이름으로 찾는다.</summary>
        public const string RabbitKey = "SF_Rabbit_Base";
        public const string CapsuleKey = "SF_Spaceship_Capsule";
        public const string HandKey = "CharacterHand";

        public static void BuildPrefabs()
        {
            Directory.CreateDirectory(ResourceDir);
            Link(RabbitFbx, RabbitKey);
            Link(CapsuleFbx, CapsuleKey);
            Link(HandFbx, HandKey);
            CleanStale();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        /// <summary>
        /// 런타임 키가 바뀌어 더 이상 만들지 않는 프리팹 이름. **여기 적은 것만 지운다.**
        ///
        /// <para>키를 바꾸거나 없앨 때 옛 이름을 여기 한 줄 더한다. 그 지식은 키를 고치는
        /// 사람에게만 있으므로, 코드가 추측하는 대신 적어 두게 한다.</para>
        /// </summary>
        static readonly string[] Retired = { };

        /// <summary>지금 ArtLink 가 만드는 것들. 이건 절대 지우지 않는다.</summary>
        static readonly string[] Generated = { RabbitKey, CapsuleKey, HandKey };

        /// <summary>
        /// 이 이름의 프리팹을 지워도 되는가.
        ///
        /// <para><b>모르는 이름은 지우지 않는다</b>(S15P21B202-351). 이 폴더는 ArtLink 것이
        /// 아니라 <c>Resources.Load</c> 를 쓰는 모두의 것이다 — 손으로 만든
        /// <c>PF_Stage1_Tablet</c> 이 여기 살고, 예전 규칙은 그것을 매 빌드 지웠다.</para>
        ///
        /// <para>테스트가 부를 수 있게 공개한 이유는 테스트가 이 판단만 따로 확인하기 때문이다 —
        /// 실제 <see cref="CleanStale"/> 을 테스트에서 돌리면 프로젝트 에셋을 건드린다.</para>
        /// </summary>
        public static bool ShouldDelete(string name)
        {
            if (System.Array.IndexOf(Generated, name) >= 0) return false;
            return System.Array.IndexOf(Retired, name) >= 0;
        }

        /// <summary>
        /// 안 쓰는 프리팹을 정리한다 — <b>이름을 아는 것만.</b>
        ///
        /// <para><b>예전에는 <see cref="ResourceDir"/> 에서 <see cref="RabbitKey"/> 셋을 뺀
        /// 나머지 전부를 지웠다.</b> 그런데 그 폴더는 ArtLink 것이 아니다 — 런타임이
        /// <c>Resources.Load</c> 로 집는 프리팹은 <b>모두</b> 여기 있어야 하므로 손으로
        /// 만든 것도 함께 산다. 그래서 <c>PF_Stage1_Tablet.prefab</c> 이
        /// <b>빌드마다 조용히 지워졌다</b>(S15P21B202-351).</para>
        ///
        /// <para>피해가 큰 이유는 세 가지가 겹치기 때문이다 — ① <see cref="BuildPrefabs"/> 를
        /// <c>SpaceBuild.Build</c> 가 맨 처음 부르므로 <b>지워진 상태로 플레이어가 빌드된다</b>
        /// ② <c>Resources</c> 아래는 <c>Resources.Load</c> 로만 쓰여 <b>컴파일 검사에 안 걸린다</b>
        /// ③ CI 워크스페이스는 매번 <c>git clean</c> 되므로 <b>삭제가 커밋되지 않아 흔적이
        /// 남지 않는다.</b> 종료 코드는 0이고 빌드는 "성공" 이다.</para>
        ///
        /// <para><b>왜 전용 폴더로 옮기지 않았나.</b> <c>Resources.Load</c> 는 이름이 아니라
        /// <c>Resources</c> 아래 <b>경로</b>로 찾는다. <c>Resources/ArtLink/</c> 로 옮기면 키가
        /// <c>"ArtLink/SF_Rabbit_Base"</c> 로 바뀌어 <c>SpaceSuit.ResourceKey</c> ·
        /// <c>FirstPersonHandView.ResourceKey</c> 같은 런타임 상수를 함께 고쳐야 한다.
        /// 에셋 이동까지 묶이므로 이 수정의 범위를 넘는다.</para>
        ///
        /// <para><b>이제 모르는 것은 지우지 않고 알린다.</b> 빌드 단계가 추적되는 에셋을
        /// 지우는 것은 어느 경우에도 기본 동작이면 안 된다.</para>
        /// </summary>
        static void CleanStale()
        {
            var unknown = new List<string>();

            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { ResourceDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string name = System.IO.Path.GetFileNameWithoutExtension(path);

                if (System.Array.IndexOf(Generated, name) >= 0) continue;

                if (ShouldDelete(name))
                {
                    AssetDatabase.DeleteAsset(path);
                    Debug.Log("[ArtLink] 쓰지 않게 된 프리팹 정리: " + path);
                    continue;
                }

                unknown.Add(name);
            }

            // 손으로 만든 것일 수 있으므로 지우지 않는다. 진짜 쓰레기라면 사람이 지우거나
            // 위 Retired 에 이름을 적는다.
            if (unknown.Count > 0)
                Debug.Log("[ArtLink] ArtLink 가 만들지 않은 프리팹 " + unknown.Count
                          + "개는 그대로 둔다 (지우려면 ArtLink.Retired 에 이름을 적는다): "
                          + string.Join(", ", unknown));
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
