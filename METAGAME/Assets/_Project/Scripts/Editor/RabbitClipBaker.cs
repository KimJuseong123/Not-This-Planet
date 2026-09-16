using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Metagame.EditorTools
{
    /// <summary>
    /// 블렌더가 이 뼈대 위에서 직접 만든 FBX를 토끼용 클립으로 굽는다.
    ///
    /// <para><see cref="RabbitTossAssets"/>(S15P21B202-519)가 쓰던 절차를 그대로
    /// 옮겨 온 것이다. 춤(S15P21B202-563)이 같은 절차를 필요로 하는데, 아래 세 개의
    /// 안전장치는 전부 <b>실제로 당한 뒤에</b> 생긴 것이라 한쪽에만 있으면 다른 쪽이
    /// 같은 사고를 다시 당한다.</para>
    ///
    /// <para><b>리타기팅도 재생도 없다.</b> 걷기·달리기는 Mixamo 모션이라 휴머노이드
    /// 아바타를 거쳐야 했고(<see cref="RabbitLocomotionAssets"/>) 그래서 프리팹에 물려
    /// 프레임마다 받아 적는다. 여기 오는 동작은 처음부터 이 뼈대 위에서 만들어졌으므로
    /// 커브를 <b>뼈 이름으로 옮겨 붙이기만</b> 하면 된다.</para>
    /// </summary>
    public static class RabbitClipBaker
    {
        public const string Root = "Assets/_Project/Animations/Characters/Rabbit";
        public const string Output = Root + "/Resources/RabbitLocomotion";
        const string RabbitPrefab = "Assets/_Project/Prefabs/Resources/SF_Rabbit_Base.prefab";

        /// <summary>
        /// <paramref name="sourceFbx"/> 안의 클립을 <paramref name="clipName"/> 으로 굽는다.
        /// <paramref name="boneNames"/> 중 하나라도 움직이지 않으면 실패한다.
        /// </summary>
        public static void Bake(string sourceFbx, string clipName, string[] boneNames, bool loop)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play mode before baking animations.");
            if (!File.Exists(sourceFbx))
                throw new FileNotFoundException("블렌더가 만든 FBX가 없다: " + sourceFbx);

            ImportSource(sourceFbx, clipName, loop);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RabbitPrefab);
            if (prefab == null) throw new InvalidOperationException("Existing rabbit prefab is missing.");

            var scene = EditorSceneManager.NewPreviewScene();
            GameObject target = null;
            try
            {
                target = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                var transforms = target.GetComponentsInChildren<Transform>(true);
                var bones = boneNames.Select(n => transforms.SingleOrDefault(t => t.name == n)).ToArray();
                for (int i = 0; i < bones.Length; i++)
                    if (bones[i] == null)
                        throw new InvalidOperationException("프리팹에 없는 뼈: " + boneNames[i]);

                var source = AssetDatabase.LoadAllAssetsAtPath(sourceFbx).OfType<AnimationClip>()
                    .FirstOrDefault(c => !c.name.StartsWith("__preview"));
                if (source == null) throw new InvalidOperationException("FBX 안에 클립이 없다: " + sourceFbx);

                // **뼈 이름으로 옮겨 붙인다. 클립을 재생해서 받아 적지 않는다.**
                //
                // 처음에는 걷기와 똑같이 프리팹에 물려 프레임마다 샘플링했는데, 움직인
                // 뼈가 0/18 이었다. FBX 안의 경로는 `Root/Hip/...` 인데 프리팹은
                // `우주 토끼 3D 모델/Root/Hip/...` 이다 — 아마튜어만 내보내면 그 오브젝트가
                // FBX 의 뿌리가 되어 이름 한 마디가 통째로 빠진다.
                //
                // 이름으로 짝지으면 그 층이 몇 마디든 상관없다.
                var byName = bones.ToDictionary(t => t.name, t => t);
                var clip = new AnimationClip
                {
                    name = clipName,
                    frameRate = 30f,
                    wrapMode = loop ? WrapMode.Loop : WrapMode.Once
                };
                var moved = new HashSet<string>();

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
                // "동작을 시켰는데 아무 일도 안 일어난다" 가 되고 원인을 찾기 어렵다.
                //
                // **하나라도 빠지면 실패다.** 처음에는 "절반 미만" 으로 두었는데, 그러면
                // 한두 뼈만 어긋났을 때 가드를 통과한 채 그 뼈만 조용히 안 움직인다
                // (!436 AI 리뷰). 어느 뼈가 빠졌는지도 메시지에 적는다.
                var still = boneNames.Where(n => !moved.Contains(n)).ToArray();
                if (still.Length > 0)
                    throw new InvalidOperationException(
                        $"클립이 움직이지 않는 뼈가 있다 ({still.Length}/{bones.Length}개): "
                        + string.Join(", ", still)
                        + ". FBX 안의 뼈 이름이 프리팹과 다른지, 블렌더 액션에 그 뼈의 키가 "
                        + "있는지 확인할 것.");

                clip.EnsureQuaternionContinuity();
                var settings = AnimationUtility.GetAnimationClipSettings(clip);
                settings.loopTime = loop;
                AnimationUtility.SetAnimationClipSettings(clip, settings);

                EnsureFolder(Output);
                string outputPath = Output + "/" + clipName + ".anim";
                // **언제나 새로 만든다. 덮어쓰지 않는다.**
                //
                // 걷기(RabbitLocomotionAssets)처럼 CopySerialized 로 덮으면
                // `m_ClipBindingConstant.genericBindings` 가 **빈 채로** 저장된다 —
                // 그 표는 새 에셋을 직렬화할 때 만들어지기 때문이다. 표가 비면
                // 에디터에서는 커브가 보이는데 <b>빌드에서는 아무 뼈도 안 움직인다.</b>
                // 재임포트(ForceUpdate)로는 되살아나지 않는 것을 실측으로 확인했다.
                //
                // 지우고 만들면 GUID 가 바뀌는데, 이 클립들은 `Resources.Load` 로 이름으로만
                // 찾으므로 참조가 깨질 데가 없다.
                if (AssetDatabase.LoadAssetAtPath<AnimationClip>(outputPath) != null)
                    AssetDatabase.DeleteAsset(outputPath);
                AssetDatabase.CreateAsset(clip, outputPath);
                AssetDatabase.SaveAssetIfDirty(clip);

                var written = File.ReadAllText(outputPath);
                if (written.Contains("genericBindings: []"))
                    throw new InvalidOperationException(
                        "구운 클립에 런타임 바인딩 표가 비어 있다 — 빌드에서 아무 뼈도 "
                        + "안 움직인다: " + outputPath);

                Debug.Log($"[RabbitClip] {clipName}: {clip.length:F2}초 · 뼈 {bones.Length}개 "
                          + $"(움직인 뼈 {moved.Count}개) · {(loop ? "반복" : "반복 없음")}");
            }
            finally
            {
                if (target != null) UnityEngine.Object.DestroyImmediate(target);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        static void ImportSource(string sourceFbx, string clipName, bool loop)
        {
            AssetDatabase.ImportAsset(sourceFbx, ImportAssetOptions.ForceSynchronousImport);
            var importer = (ModelImporter)AssetImporter.GetAtPath(sourceFbx);

            // 리타기팅이 없으므로 Generic 이다. 모델 자체(SF_Rabbit_Base.fbx)도 Generic 이다.
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
            importer.importCameras = false;
            importer.importLights = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.animationCompression = ModelImporterAnimationCompression.Off;

            var clips = importer.defaultClipAnimations;
            if (clips.Length != 1) throw new InvalidOperationException("Expected one motion in " + sourceFbx);
            clips[0].name = clipName;
            clips[0].loopTime = loop;
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
