using System;
using System.Collections.Generic;
using Metagame.Core;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Metagame.Gameplay
{
    /// <summary>
    /// Mixamo에서 기존 토끼 뼈대로 변환한 모션을 표시용 본에만 적용한다.
    /// 위치/충돌/잡기 판정은 여전히 PlayerRig와 래그돌이 소유한다.
    /// SpaceSuit의 물리 포즈 동기화 이후에 호출해야 한다.
    /// </summary>
    public sealed class RabbitLocomotion : IDisposable
    {
        public const string WalkResource = "RabbitLocomotion/Rabbit_Walk";
        public const string RunResource = "RabbitLocomotion/Rabbit_Run";
        public static readonly IReadOnlyList<string> BoneNames = Array.AsReadOnly(new[]
        {
            "L_Upperarm", "L_Forearm", "L_Hand", "R_Upperarm", "R_Forearm", "R_Hand",
            "L_Thigh", "L_Calf", "L_Foot", "L_ToeBase", "R_Thigh", "R_Calf", "R_Foot", "R_ToeBase"
        });

        sealed class BoneLink
        {
            public Transform Target, Pose;
            public Vector3 RestPosition, PhysicsPosition;
            public Quaternion PhysicsRotation;
            public int Arm; // 0: 다리, 1: 왼팔, 2: 오른팔
            public bool PreserveAnchor;
        }

        readonly List<BoneLink> _bones = new();
        readonly AnimationClip _walk, _run;
        readonly GameObject _poseRoot;
        PlayableGraph _graph;
        AnimationClipPlayable _walkNode, _runNode;
        AnimationMixerPlayable _mixer;
        float _phase, _weight, _runBlend;
        public float Weight => _weight;
        public float RunBlend => _runBlend;
        public float Phase => _phase;
        public int BoneCount => _bones.Count;

        public static RabbitLocomotion Create(Transform model, GameObject reference)
        {
            var walk = Resources.Load<AnimationClip>(WalkResource);
            var run = Resources.Load<AnimationClip>(RunResource);
            if (walk == null || run == null || model == null || reference == null) return null;
            return new RabbitLocomotion(model, reference, walk, run);
        }

        RabbitLocomotion(Transform model, GameObject reference, AnimationClip walk, AnimationClip run)
        {
            _walk = walk;
            _run = run;
            // 메시/콜라이더/스크립트 없이 Transform만 복제한다. 원본 프리팹을 수정하지 않는다.
            _poseRoot = CloneSkeleton(reference.transform, null).gameObject;
            _poseRoot.name = "~Rabbit locomotion pose (runtime only)";
            _poseRoot.hideFlags = HideFlags.HideAndDontSave;
            var modelBones = new Dictionary<string, Transform>();
            foreach (var t in model.GetComponentsInChildren<Transform>(true)) modelBones[t.name] = t;
            foreach (var t in _poseRoot.GetComponentsInChildren<Transform>(true))
            {
                if (!ContainsBone(t.name) || !modelBones.TryGetValue(t.name, out var target)) continue;
                _bones.Add(new BoneLink
                {
                    Target = target, Pose = t, RestPosition = t.localPosition,
                    Arm = t.name.Contains("arm") || t.name.EndsWith("Hand") ? (t.name.StartsWith("L_") ? 1 : 2) : 0,
                    PreserveAnchor = t.name.EndsWith("Upperarm") || t.name.EndsWith("Thigh")
                });
            }
            var animator = _poseRoot.AddComponent<Animator>();
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            _graph = PlayableGraph.Create("Rabbit walk/run visual pose");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            _walkNode = AnimationClipPlayable.Create(_graph, walk);
            _runNode = AnimationClipPlayable.Create(_graph, run);
            _walkNode.SetSpeed(0); _runNode.SetSpeed(0);
            _walkNode.SetApplyFootIK(false); _runNode.SetApplyFootIK(false);
            _mixer = AnimationMixerPlayable.Create(_graph, 2);
            _graph.Connect(_walkNode, 0, _mixer, 0);
            _graph.Connect(_runNode, 0, _mixer, 1);
            AnimationPlayableOutput.Create(_graph, "Pose only", animator).SetSourcePlayable(_mixer);
            _mixer.SetInputWeight(0, 1f);
            _graph.Play();
        }

        static bool ContainsBone(string name)
        {
            for (int i = 0; i < BoneNames.Count; i++) if (BoneNames[i] == name) return true;
            return false;
        }

        static Transform CloneSkeleton(Transform original, Transform parent)
        {
            var clone = new GameObject(original.name).transform;
            clone.gameObject.hideFlags = HideFlags.HideAndDontSave;
            clone.SetParent(parent, false);
            clone.localPosition = original.localPosition;
            clone.localRotation = original.localRotation;
            clone.localScale = original.localScale;
            foreach (Transform child in original) CloneSkeleton(child, clone);
            return clone;
        }

        /// <summary>상태 동기화 시에도 재사용할 수 있는 표시 전용 값.</summary>
        public struct State
        {
            public float Speed;
            public bool Grounded, Running, Blocked, LeftArmBusy, RightArmBusy;
            public Vector2 Direction;
        }

        public static State ReadState(PlayerRig rig)
        {
            var input = rig.CurrentInput;
            var velocity = rig.Hips.linearVelocity - rig.GroundVelocity;
            velocity.y = 0f;
            return new State
            {
                Speed = velocity.magnitude,
                Grounded = rig.Grounded,
                Running = input.Run,
                Blocked = rig.Downed || rig.Ragdolled || rig.Floating || rig.IsRiding || rig.Hanging,
                Direction = rig.MovementDirectionLocal,
                LeftArmBusy = input.GrabLeft || input.ArmsUp || rig.LeftHand.Gripping || rig.LeftHand.Carrying || rig.HeldTool != null,
                RightArmBusy = input.GrabRight || input.ArmsUp || rig.RightHand.Gripping || rig.RightHand.Carrying || rig.HeldTool != null
            };
        }

        public static bool CanAnimate(State state) => state.Grounded && !state.Blocked &&
            state.Speed > 0.15f && Mathf.Abs(state.Direction.x) <= Mathf.Abs(state.Direction.y) + 0.1f;

        /// <summary>이동 거리나 골반 위치는 쓰지 않는다. 먼저 SpaceSuit.Sync를 호출해야 한다.</summary>
        public void Apply(State state, float deltaTime)
        {
            if (!_graph.IsValid()) return;
            deltaTime = Mathf.Clamp(deltaTime, 0f, 0.1f);
            // 공중/기절/탑승은 즉시 물리로 돌아가고 일반 정지만 짧게 전환한다.
            if (state.Blocked || !state.Grounded) { _weight = 0f; return; }
            _weight = Mathf.MoveTowards(_weight, CanAnimate(state) ? 1f : 0f, deltaTime / 0.12f);
            if (_weight <= 0f) { _phase = 0f; return; }
            _runBlend = Mathf.MoveTowards(_runBlend, state.Running ? 1f : 0f, deltaTime / 0.16f);
            float duration = Mathf.Lerp(_walk.length, _run.length, _runBlend);
            float referenceSpeed = Mathf.Lerp(Tuning.WalkSpeed, Tuning.RunSpeed, _runBlend);
            float rate = Mathf.Clamp(state.Speed / referenceSpeed, 0.35f, 1.5f);
            float direction = state.Direction.y < -0.1f ? -1f : 1f;
            _phase = Mathf.Repeat(_phase + direction * deltaTime * rate / Mathf.Max(duration, 0.01f), 1f);
            _walkNode.SetTime(_phase * _walk.length);
            _runNode.SetTime(_phase * _run.length);
            _mixer.SetInputWeight(0, 1f - _runBlend);
            _mixer.SetInputWeight(1, _runBlend);
            _graph.Evaluate(0f);

            // 부모를 바꾼 뒤의 값이 섞이지 않도록 물리 포즈를 먼저 모두 저장한다.
            foreach (var link in _bones)
            {
                link.PhysicsPosition = link.Target.localPosition;
                link.PhysicsRotation = link.Target.localRotation;
            }
            foreach (var link in _bones)
            {
                if ((link.Arm == 1 && state.LeftArmBusy) || (link.Arm == 2 && state.RightArmBusy)) continue;
                link.Target.localRotation = Quaternion.Slerp(link.PhysicsRotation, link.Pose.localRotation, _weight);
                // 어깨/고관절은 기존 부착 위치를 유지하고 자식 뼈의 길이는 원본 값을 쓴다.
                if (!link.PreserveAnchor)
                    link.Target.localPosition = Vector3.Lerp(link.PhysicsPosition, link.RestPosition, _weight);
            }
        }

        public void Dispose()
        {
            if (_graph.IsValid()) _graph.Destroy();
            if (_poseRoot == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(_poseRoot);
            else UnityEngine.Object.DestroyImmediate(_poseRoot);
        }
    }
}
