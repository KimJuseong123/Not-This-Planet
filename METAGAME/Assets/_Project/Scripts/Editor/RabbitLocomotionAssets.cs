using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Metagame.EditorTools
{
    /// <summary>씬을 열거나 저장하지 않고 기존 토끼 뼈대용 보행 클립만 생성한다.</summary>
    public static class RabbitLocomotionAssets
    {
        public const string Root = "Assets/_Project/Animations/Characters/Rabbit";
        public const string Output = Root + "/Resources/RabbitLocomotion";
        const string RabbitPrefab = "Assets/_Project/Prefabs/Resources/SF_Rabbit_Base.prefab";
        static readonly string[] BoneNames =
        {
            "L_Upperarm", "L_Forearm", "L_Hand", "R_Upperarm", "R_Forearm", "R_Hand",
            "L_Thigh", "L_Calf", "L_Foot", "L_ToeBase", "R_Thigh", "R_Calf", "R_Foot", "R_ToeBase"
        };
        static readonly string[] HumanMapping =
        {
            "Hips:Hip", "Spine:Waist", "Chest:Spine01", "UpperChest:Spine02", "Neck:NeckTwist01", "Head:Head",
            "LeftShoulder:L_Clavicle", "LeftUpperArm:L_Upperarm", "LeftLowerArm:L_Forearm", "LeftHand:L_Hand",
            "RightShoulder:R_Clavicle", "RightUpperArm:R_Upperarm", "RightLowerArm:R_Forearm", "RightHand:R_Hand",
            "LeftUpperLeg:L_Thigh", "LeftLowerLeg:L_Calf", "LeftFoot:L_Foot", "LeftToes:L_ToeBase",
            "RightUpperLeg:R_Thigh", "RightLowerLeg:R_Calf", "RightFoot:R_Foot", "RightToes:R_ToeBase"
        };

        [MenuItem("Tools/Metagame/캐릭터/걷기·달리기 FBX 가져오기")]
        public static void ImportFromMenu()
        {
            string walk = EditorUtility.OpenFilePanel("Standard Walk.fbx 선택", "", "fbx");
            if (string.IsNullOrEmpty(walk)) return;
            string run = EditorUtility.OpenFilePanel("Running.fbx 선택", "", "fbx");
            if (string.IsNullOrEmpty(run)) return;
            ImportAndBake(walk, run);
        }

        public static void ImportBatch()
        {
            var args = Environment.GetCommandLineArgs();
            string Value(string key)
            {
                int index = Array.IndexOf(args, key);
                if (index < 0 || index + 1 >= args.Length) throw new ArgumentException(key + " is required");
                return args[index + 1];
            }
            ImportAndBake(Value("-rabbitWalkSource"), Value("-rabbitRunSource"));
        }

        public static void ImportAndBake(string walkSource, string runSource)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play mode before importing animations.");
            if (!File.Exists(walkSource) || !File.Exists(runSource)) throw new FileNotFoundException("Both FBX sources are required.");
            EnsureFolder(Root + "/Source");
            EnsureFolder(Output);
            ImportSource(walkSource, Root + "/Source/Standard Walk.fbx", "Standard Walk");
            ImportSource(runSource, Root + "/Source/Running.fbx", "Running");
            Bake(Root + "/Source/Standard Walk.fbx", "Rabbit_Walk");
            Bake(Root + "/Source/Running.fbx", "Rabbit_Run");
            Debug.Log("[RabbitLocomotion] Imported and baked walk/run. No scene or existing model was saved.");
        }

        static void ImportSource(string source, string path, string clipName)
        {
            if (!File.Exists(path)) File.Copy(source, path);
            else if (!File.ReadAllBytes(source).SequenceEqual(File.ReadAllBytes(path)))
                throw new IOException("A different source already exists; refusing to overwrite: " + path);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importCameras = false;
            importer.importLights = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.animationCompression = ModelImporterAnimationCompression.Off;
            var clips = importer.defaultClipAnimations;
            if (clips.Length != 1) throw new InvalidOperationException("Expected one motion in " + path);
            clips[0].name = clipName;
            clips[0].loopTime = true;
            clips[0].loopPose = true;
            clips[0].lockRootRotation = true;
            clips[0].lockRootHeightY = true;
            clips[0].lockRootPositionXZ = true;
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
            var avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
            if (avatar == null || !avatar.isValid || !avatar.isHuman)
                throw new InvalidOperationException("Source humanoid avatar is invalid: " + path);
        }

        static void Bake(string sourcePath, string name)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RabbitPrefab);
            if (prefab == null) throw new InvalidOperationException("Existing rabbit prefab is missing.");
            var scene = EditorSceneManager.NewPreviewScene();
            GameObject target = null;
            Avatar avatar = null;
            PlayableGraph graph = default;
            try
            {
                target = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                var transforms = target.GetComponentsInChildren<Transform>(true);
                var skeleton = transforms.Select(t => new SkeletonBone
                { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale }).ToArray();
                var human = HumanMapping.Select(p =>
                {
                    var parts = p.Split(':');
                    return new HumanBone { humanName = parts[0], boneName = parts[1], limit = new HumanLimit { useDefaultValues = true } };
                }).ToArray();
                avatar = AvatarBuilder.BuildHumanAvatar(target, new HumanDescription
                {
                    human = human, skeleton = skeleton, upperArmTwist = 0.5f, lowerArmTwist = 0.5f,
                    upperLegTwist = 0.5f, lowerLegTwist = 0.5f, armStretch = 0.05f, legStretch = 0.05f
                });
                if (!avatar.isHuman || !avatar.isValid) throw new InvalidOperationException("Existing rabbit cannot be retargeted.");
                var animator = target.GetComponent<Animator>();
                if (animator == null) animator = target.AddComponent<Animator>();
                animator.avatar = avatar;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var source = AssetDatabase.LoadAllAssetsAtPath(sourcePath).OfType<AnimationClip>()
                    .First(c => !c.name.StartsWith("__preview"));
                var bones = BoneNames.Select(n => transforms.Single(t => t.name == n)).ToArray();
                int frames = Mathf.Max(2, Mathf.CeilToInt(source.length * 30f));
                var rotations = new Quaternion[bones.Length, frames + 1];
                graph = PlayableGraph.Create("Rabbit retarget bake");
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var node = AnimationClipPlayable.Create(graph, source);
                node.SetApplyFootIK(false); node.SetSpeed(0);
                AnimationPlayableOutput.Create(graph, "Retarget", animator).SetSourcePlayable(node);
                graph.Play();
                for (int frame = 0; frame < frames; frame++)
                {
                    node.SetTime(source.length * frame / frames);
                    graph.Evaluate(0f);
                    for (int b = 0; b < bones.Length; b++) rotations[b, frame] = bones[b].localRotation;
                }
                // 끝을 시작 프레임과 맞추고 루트 이동 곡선은 출력하지 않는다.
                for (int b = 0; b < bones.Length; b++) rotations[b, frames] = rotations[b, 0];
                var clip = new AnimationClip { name = name, frameRate = 30f, wrapMode = WrapMode.Loop };
                for (int b = 0; b < bones.Length; b++)
                {
                    string path = AnimationUtility.CalculateTransformPath(bones[b], target.transform);
                    for (int axis = 0; axis < 4; axis++)
                    {
                        var keys = new Keyframe[frames + 1];
                        for (int frame = 0; frame <= frames; frame++)
                            keys[frame] = new Keyframe(source.length * frame / frames, rotations[b, frame][axis]);
                        var curve = new AnimationCurve(keys);
                        for (int k = 0; k < keys.Length; k++)
                        {
                            AnimationUtility.SetKeyLeftTangentMode(curve, k, AnimationUtility.TangentMode.Linear);
                            AnimationUtility.SetKeyRightTangentMode(curve, k, AnimationUtility.TangentMode.Linear);
                        }
                        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalRotation." + "xyzw"[axis]), curve);
                    }
                }
                clip.EnsureQuaternionContinuity();
                var settings = AnimationUtility.GetAnimationClipSettings(clip);
                settings.loopTime = true;
                AnimationUtility.SetAnimationClipSettings(clip, settings);
                string outputPath = Output + "/" + name + ".anim";
                var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(outputPath);
                if (existing == null) AssetDatabase.CreateAsset(clip, outputPath);
                else { EditorUtility.CopySerialized(clip, existing); UnityEngine.Object.DestroyImmediate(clip); clip = existing; }
                AssetDatabase.SaveAssetIfDirty(clip);
                Debug.Log("[RabbitLocomotion] " + name + ": " + clip.length + " seconds, " + bones.Length + " bones, loop, no root motion");
            }
            finally
            {
                if (graph.IsValid()) graph.Destroy();
                if (target != null) UnityEngine.Object.DestroyImmediate(target);
                if (avatar != null) UnityEngine.Object.DestroyImmediate(avatar);
                EditorSceneManager.ClosePreviewScene(scene);
            }
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
