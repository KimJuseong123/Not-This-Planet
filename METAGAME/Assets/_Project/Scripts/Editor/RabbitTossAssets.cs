using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Metagame.EditorTools
{
    /// <summary>
    /// 블렌더가 만든 던지기 FBX를 토끼 뼈대용 클립으로 굽는다 (S15P21B202-519).
    ///
    /// <para><see cref="RabbitLocomotionAssets"/> 와 자리·출력은 같다 — 씬을 열거나
    /// 저장하지 않고 <c>Resources/RabbitLocomotion</c> 에 클립 하나를 남긴다.</para>
    ///
    /// <para><b>다른 점: 리타기팅도 재생도 없다.</b> 걷기·달리기는 Mixamo 모션이라
    /// 휴머노이드 아바타를 거쳐 토끼 뼈대로 옮겨야 했고, 그래서 프리팹에 물려 프레임마다
    /// 받아 적었다. 이 동작은 <b>처음부터 이 뼈대 위에서</b> 만들어졌으므로
    /// (<c>METAGAME/Tools/Blender/generate_rabbit_toss.py</c>) 커브를 <b>뼈 이름으로
    /// 옮겨 붙이기만</b> 하면 된다.</para>
    /// </summary>
    public static class RabbitTossAssets
    {
        public const string Root = "Assets/_Project/Animations/Characters/Rabbit";
        public const string Output = Root + "/Resources/RabbitLocomotion";
        public const string SourceFbx = Root + "/Source/Rabbit_TossUp.fbx";
        public const string ClipName = "Rabbit_TossUp";
        const string RabbitPrefab = "Assets/_Project/Prefabs/Resources/SF_Rabbit_Base.prefab";

        /// <summary>
        /// 굽는 뼈. <c>RabbitLocomotion.TossBoneNames</c> 와 같아야 한다.
        ///
        /// <para>에디터 어셈블리는 게임플레이 어셈블리를 참조하지 않으므로
        /// <see cref="RabbitLocomotionAssets"/> 와 같이 목록을 따로 적는다. 어긋나면
        /// 굽힌 커브가 있어도 읽는 쪽이 그 뼈를 건너뛴다 — 그래서 아래에서 개수를 센다.</para>
        /// </summary>
        static readonly string[] BoneNames =
        {
            "Waist", "Spine01", "Spine02", "Head",
            "L_Clavicle", "L_Upperarm", "L_Forearm", "L_Hand",
            "R_Clavicle", "R_Upperarm", "R_Forearm", "R_Hand",
            "L_Thigh", "L_Calf", "L_Foot", "R_Thigh", "R_Calf", "R_Foot"
        };

        [MenuItem("Tools/Metagame/캐릭터/던지기 클립 굽기")]
        public static void BakeFromMenu() => Bake();

        /// <summary>배치에서 부르는 자리. <c>-executeMethod</c> 로 쓴다.</summary>
        public static void BakeBatch() => Bake();

        public static void Bake()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play mode before baking animations.");
            if (!File.Exists(SourceFbx))
                throw new FileNotFoundException("블렌더가 만든 FBX가 없다: " + SourceFbx);

            ImportSource();

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RabbitPrefab);
            if (prefab == null) throw new InvalidOperationException("Existing rabbit prefab is missing.");

            var scene = EditorSceneManager.NewPreviewScene();
            GameObject target = null;
            try
            {
                target = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                var transforms = target.GetComponentsInChildren<Transform>(true);
                var bones = BoneNames.Select(n => transforms.SingleOrDefault(t => t.name == n)).ToArray();
                for (int i = 0; i < bones.Length; i++)
                    if (bones[i] == null)
                        throw new InvalidOperationException("프리팹에 없는 뼈: " + BoneNames[i]);

                var source = AssetDatabase.LoadAllAssetsAtPath(SourceFbx).OfType<AnimationClip>()
                    .FirstOrDefault(c => !c.name.StartsWith("__preview"));
                if (source == null) throw new InvalidOperationException("FBX 안에 클립이 없다: " + SourceFbx);

                // **뼈 이름으로 옮겨 붙인다. 클립을 재생해서 받아 적지 않는다.**
                //
                // 처음에는 걷기와 똑같이 프리팹에 물려 프레임마다 샘플링했는데, 움직인
                // 뼈가 0/18 이었다. FBX 안의 경로는 `Root/Hip/...` 인데 프리팹은
                // `우주 토끼 3D 모델/Root/Hip/...` 이다 — 아마튜어만 내보내면 그 오브젝트가
                // FBX 의 뿌리가 되어 이름 한 마디가 통째로 빠진다.
                //
                // 이름으로 짝지으면 그 층이 몇 마디든 상관없다. 걷기가 재생을 거치는 것은
                // **리타기팅이 필요해서**이지 이 방식이 모자라서가 아니다.
                var byName = bones.ToDictionary(t => t.name, t => t);
                var clip = new AnimationClip { name = ClipName, frameRate = 30f, wrapMode = WrapMode.Once };
                var moved = new System.Collections.Generic.HashSet<string>();

                foreach (var binding in AnimationUtility.GetCurveBindings(source))
                {
                    if (!binding.propertyName.StartsWith("m_LocalRotation.")) continue;

                    var slash = binding.path.LastIndexOf('/');
                    var boneName = slash < 0 ? binding.path : binding.path.Substring(slash + 1);
                    if (!byName.TryGetValue(boneName, out var bone)) continue;

                    var curve = AnimationUtility.GetEditorCurve(source, binding);
                    if (curve == null || curve.length == 0) continue;
                    if (Varies(curve)) moved.Add(boneName);

                    AnimationUtility.SetEditorCurve(clip,
                        EditorCurveBinding.FloatCurve(
                            AnimationUtility.CalculateTransformPath(bone, target.transform),
                            typeof(Transform), binding.propertyName),
                        curve);
                }

                // **정말 움직였는지 센다.** 짝이 안 맞으면 커브가 하나도 안 옮겨지는데,
                // 그래도 에러 없이 "비어 있지만 멀쩡한" 클립이 나온다 — 게임에서는
                // "던지는데 아무 일도 안 일어난다" 가 되고 원인을 찾기 어렵다.
                //
                // **하나라도 빠지면 실패다.** 처음에는 "절반 미만" 으로 두었는데, 그러면
                // 한두 뼈만 어긋났을 때 가드를 통과한 채 그 뼈만 조용히 안 움직인다
                // (!436 AI 리뷰). 어느 뼈가 빠졌는지도 메시지에 적는다 — 그것이 없으면
                // 실패해도 어디를 봐야 할지 모른다.
                var still = BoneNames.Where(n => !moved.Contains(n)).ToArray();
                if (still.Length > 0)
                    throw new InvalidOperationException(
                        $"클립이 움직이지 않는 뼈가 있다 ({still.Length}/{bones.Length}개): "
                        + string.Join(", ", still)
                        + ". FBX 안의 뼈 이름이 프리팹과 다른지, 블렌더 액션에 그 뼈의 키가 "
                        + "있는지 확인할 것.");
                clip.EnsureQuaternionContinuity();
                var settings = AnimationUtility.GetAnimationClipSettings(clip);
                settings.loopTime = false;      // 한 번짜리 동작이다
                AnimationUtility.SetAnimationClipSettings(clip, settings);

                EnsureFolder(Output);
                string outputPath = Output + "/" + ClipName + ".anim";
                // **언제나 새로 만든다. 덮어쓰지 않는다.**
                //
                // 걷기(RabbitLocomotionAssets)처럼 CopySerialized 로 덮으면
                // `m_ClipBindingConstant.genericBindings` 가 **빈 채로** 저장된다 —
                // 그 표는 새 에셋을 직렬화할 때 만들어지기 때문이다. 표가 비면
                // 에디터에서는 커브가 보이는데 <b>빌드에서는 아무 뼈도 안 움직인다.</b>
                // 재임포트(ForceUpdate)로는 되살아나지 않는 것을 실측으로 확인했다.
                //
                // 지우고 만들면 GUID 가 바뀌는데, 이 클립은 `Resources.Load` 로 이름으로만
                // 찾으므로 참조가 깨질 데가 없다(GUID 검색 0건).
                if (AssetDatabase.LoadAssetAtPath<AnimationClip>(outputPath) != null)
                    AssetDatabase.DeleteAsset(outputPath);
                AssetDatabase.CreateAsset(clip, outputPath);
                AssetDatabase.SaveAssetIfDirty(clip);

                var written = File.ReadAllText(outputPath);
                if (written.Contains("genericBindings: []"))
                    throw new InvalidOperationException(
                        "구운 클립에 런타임 바인딩 표가 비어 있다 — 빌드에서 아무 뼈도 "
                        + "안 움직인다: " + outputPath);

                Debug.Log($"[RabbitToss] {ClipName}: {clip.length:F2}초 · 뼈 {bones.Length}개 "
                          + $"(움직인 뼈 {moved.Count}개) · 반복 없음");
            }
            finally
            {
                if (target != null) UnityEngine.Object.DestroyImmediate(target);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        static void ImportSource()
        {
            AssetDatabase.ImportAsset(SourceFbx, ImportAssetOptions.ForceSynchronousImport);
            var importer = (ModelImporter)AssetImporter.GetAtPath(SourceFbx);

            // 리타기팅이 없으므로 Generic 이다. 모델 자체(SF_Rabbit_Base.fbx)도 Generic 이다.
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
            importer.importCameras = false;
            importer.importLights = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.animationCompression = ModelImporterAnimationCompression.Off;

            var clips = importer.defaultClipAnimations;
            if (clips.Length != 1) throw new InvalidOperationException("Expected one motion in " + SourceFbx);
            clips[0].name = ClipName;
            clips[0].loopTime = false;
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
        }

        /// <summary>이 커브가 실제로 값을 바꾸는가. 전부 같은 값이면 아무것도 안 한다.</summary>
        static bool Varies(AnimationCurve curve)
        {
            for (int i = 1; i < curve.length; i++)
                if (Mathf.Abs(curve[i].value - curve[0].value) > 0.0005f) return true;
            return false;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
