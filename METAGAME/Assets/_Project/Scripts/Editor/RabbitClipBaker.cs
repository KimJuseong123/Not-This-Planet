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
        public static void Bake(string sourceFbx, string clipName, string[] boneNames, bool loop,
                                float keyTolerance = 0f)
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

                if (keyTolerance > 0f) Reduce(clip, boneNames, target.transform, byName, keyTolerance);

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


        /// <summary>
        /// 값이 거의 안 변하는 키를 걷어낸다. 걷어낸 뒤 <b>실제 회전이 얼마나 달라졌는지
        /// 재고</b>, 눈에 띌 만큼 달라졌으면 실패시킨다.
        ///
        /// <para>왜 필요한가: 30fps · 14.2초 · 18뼈 · 쿼터니언 4성분이면 키가 3만 개가 넘고,
        /// 텍스트 에셋으로 16MB 다. 저장소 위생 검사(비-LFS 한도)에 걸린다.</para>
        ///
        /// <para><b>성분별로 줄이고 각도로 확인한다.</b> 쿼터니언 네 성분을 따로 줄이면
        /// 성분마다의 오차는 작아도 합쳐진 회전은 그보다 크게 틀어질 수 있다 — 그래서
        /// 줄이기 전후의 회전을 60Hz 로 훑어 <b>각도 차이의 최대값</b>을 잰다.</para>
        /// </summary>
        static void Reduce(AnimationClip clip, string[] boneNames, Transform root,
                           Dictionary<string, Transform> byName, float tolerance)
        {
            const float MaxDegrees = 1.5f;   // 이보다 틀어지면 동작이 바뀐 것으로 본다

            var before = new Dictionary<string, AnimationCurve[]>();
            var after = new Dictionary<string, AnimationCurve[]>();
            var order = new[] { "x", "y", "z", "w" };
            int kept = 0, dropped = 0;

            foreach (var name in boneNames)
            {
                var path = AnimationUtility.CalculateTransformPath(byName[name], root);
                var old = new AnimationCurve[4];
                var neu = new AnimationCurve[4];
                for (int i = 0; i < 4; i++)
                {
                    var binding = EditorCurveBinding.FloatCurve(path, typeof(Transform),
                        "m_LocalRotation." + order[i]);
                    var curve = AnimationUtility.GetEditorCurve(clip, binding);
                    if (curve == null || curve.length == 0) { old[i] = neu[i] = curve; continue; }
                    old[i] = curve;
                    neu[i] = Thin(curve, tolerance, ref kept, ref dropped);
                }
                before[name] = old;
                after[name] = neu;
            }

            // **먼저 재고 나서 쓴다.** 써 놓고 재면 실패해도 이미 망가진 클립이 남는다.
            float worst = 0f;
            string worstBone = string.Empty;
            var all = new List<float>();
            for (float t = 0f; t <= clip.length; t += 1f / 60f)
            {
                foreach (var name in boneNames)
                {
                    var a = Sample(before[name], t);
                    var b = Sample(after[name], t);
                    if (!a.HasValue || !b.HasValue) continue;
                    var angle = Quaternion.Angle(a.Value, b.Value);
                    all.Add(angle);
                    if (angle > worst) { worst = angle; worstBone = name; }
                }
            }
            all.Sort();
            float Pick(float q) => all.Count == 0 ? 0f
                : all[Mathf.Clamp(Mathf.FloorToInt(q * (all.Count - 1)), 0, all.Count - 1)];
            Debug.Log($"[RabbitClip] 틀어짐 분포: 중앙 {Pick(0.5f):F3}도 · 상위10% {Pick(0.9f):F3}도 "
                      + $"· 상위1% {Pick(0.99f):F3}도 · 최대 {worst:F3}도 · 표본 {all.Count}");
            Debug.Log($"[RabbitClip] 키 줄이기(tolerance {tolerance}): {kept + dropped} → {kept}개 "
                      + $"(뺀 것 {dropped}) · 최대 틀어짐 {worst:F3}도 ({worstBone})");
            if (worst > MaxDegrees)
                throw new InvalidOperationException(
                    $"키를 줄였더니 동작이 바뀐다 — {worstBone} 이 최대 {worst:F2}도 틀어졌다 "
                    + $"(허용 {MaxDegrees}도). tolerance 를 낮출 것.");

            foreach (var name in boneNames)
            {
                var path = AnimationUtility.CalculateTransformPath(byName[name], root);
                for (int i = 0; i < 4; i++)
                {
                    if (after[name][i] == null) continue;
                    AnimationUtility.SetEditorCurve(clip,
                        EditorCurveBinding.FloatCurve(path, typeof(Transform),
                            "m_LocalRotation." + order[i]),
                        after[name][i]);
                }
            }

        }

        /// <summary>
        /// 키를 솎는다. <b>탄젠트는 건드리지 않는다.</b>
        ///
        /// <para>처음에는 "이웃 둘의 직선값과 가까우면 뺀다" 로 하고 남은 키의 탄젠트를
        /// 다시 매끄럽게 했는데, 그것이 곡선 모양을 통째로 바꿔 <b>R_Calf 가 120.73도
        /// 틀어졌다</b>(각도 확인이 잡았다). 애니메이션 커브는 직선이 아니라 탄젠트가
        /// 정하는 곡선이라, 직선으로 재고 탄젠트를 새로 만들면 둘 다 틀린다.</para>
        ///
        /// <para>그래서 <b>남긴 키로 실제로 그려 본 곡선</b>과 원본 값을 견주고, 제일 많이
        /// 어긋나는 자리를 하나씩 도로 넣는다(Douglas-Peucker). 탄젠트는 원본 것을 그대로
        /// 들고 간다.</para>
        /// </summary>
        static AnimationCurve Thin(AnimationCurve curve, float tolerance, ref int kept, ref int dropped)
        {
            int count = curve.length;
            if (count <= 2) { kept += count; return curve; }

            var keep = new bool[count];
            keep[0] = keep[count - 1] = true;
            Split(curve, 0, count - 1, tolerance, keep);

            var keys = new List<Keyframe>();
            for (int i = 0; i < count; i++)
            {
                if (keep[i]) keys.Add(curve[i]);
                else dropped++;
            }
            kept += keys.Count;
            return new AnimationCurve(keys.ToArray());
        }

        /// <summary>a..b 를 두 키만으로 그렸을 때 가장 많이 어긋나는 자리를 도로 넣는다.</summary>
        static void Split(AnimationCurve curve, int a, int b, float tolerance, bool[] keep)
        {
            if (b - a < 2) return;

            var probe = new AnimationCurve(new[] { curve[a], curve[b] });
            float worst = 0f;
            int at = -1;
            for (int i = a + 1; i < b; i++)
            {
                float error = Mathf.Abs(probe.Evaluate(curve[i].time) - curve[i].value);
                if (error > worst) { worst = error; at = i; }
            }
            if (at < 0 || worst <= tolerance) return;

            keep[at] = true;
            Split(curve, a, at, tolerance, keep);
            Split(curve, at, b, tolerance, keep);
        }

        static Quaternion? Sample(AnimationCurve[] curves, float time)
        {
            if (curves[0] == null || curves[1] == null || curves[2] == null || curves[3] == null)
                return null;
            return new Quaternion(curves[0].Evaluate(time), curves[1].Evaluate(time),
                                  curves[2].Evaluate(time), curves[3].Evaluate(time)).normalized;
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
