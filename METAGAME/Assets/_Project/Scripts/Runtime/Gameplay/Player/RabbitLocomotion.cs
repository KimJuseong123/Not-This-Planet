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

        /// <summary>동료를 양손으로 들어 위로 내던지는 한 번짜리 동작 (S15P21B202-519).</summary>
        public const string TossResource = "RabbitLocomotion/Rabbit_TossUp";

        public static readonly IReadOnlyList<string> BoneNames = Array.AsReadOnly(new[]
        {
            "L_Upperarm", "L_Forearm", "L_Hand", "R_Upperarm", "R_Forearm", "R_Hand",
            "L_Thigh", "L_Calf", "L_Foot", "L_ToeBase", "R_Thigh", "R_Calf", "R_Foot", "R_ToeBase"
        });

        /// <summary>
        /// 던지기가 움직이는 뼈. <b>보행보다 넓다</b> (S15P21B202-519).
        ///
        /// <para>보행은 팔·다리만 건드린다 — 걷는 동안 허리와 머리는 물리가 그리는 것이
        /// 자연스럽다. 던지기는 <b>힘이 다리와 상체 폄에서 나오는 동작</b>이라 허리·척추를
        /// 빼면 팔만 허우적거리는 그림이 된다.</para>
        ///
        /// <para><b>그래서 목록을 하나로 합치지 않았다.</b> 보행 클립에는 이 뼈들의 커브가
        /// 없으므로, 합치면 걷는 동안 허리·쇄골·머리가 <b>물리가 아니라 기준 자세로</b>
        /// 끌려간다 — 지금 멀쩡한 걷기가 뻣뻣해진다.</para>
        /// </summary>
        public static readonly IReadOnlyList<string> TossBoneNames = Array.AsReadOnly(new[]
        {
            "Waist", "Spine01", "Spine02", "Head",
            "L_Clavicle", "L_Upperarm", "L_Forearm", "L_Hand",
            "R_Clavicle", "R_Upperarm", "R_Forearm", "R_Hand",
            "L_Thigh", "L_Calf", "L_Foot", "R_Thigh", "R_Calf", "R_Foot"
        });

        sealed class BoneLink
        {
            public Transform Target, Pose;
            public Vector3 RestPosition, PhysicsPosition;
            public Quaternion PhysicsRotation;
            public int Arm; // 0: 다리, 1: 왼팔, 2: 오른팔
            public bool PreserveAnchor;
            public bool InWalk, InToss;
        }

        readonly List<BoneLink> _bones = new();
        readonly AnimationClip _walk, _run, _toss;
        readonly GameObject _poseRoot;
        PlayableGraph _graph;
        AnimationClipPlayable _walkNode, _runNode, _tossNode;
        AnimationMixerPlayable _mixer;
        float _phase, _weight, _runBlend;
        float _tossTime, _tossWeight;
        bool _tossActive;
        public float Weight => _weight;
        public float RunBlend => _runBlend;
        public float Phase => _phase;
        /// <summary>
        /// 짝지은 뼈 전체. 보행과 던지기의 <b>합집합</b>이다 (S15P21B202-519).
        ///
        /// <para>이 수로 "보행이 몇 개를 움직이나" 를 재면 안 된다 —
        /// <see cref="WalkBoneCount"/> 가 그 값이다.</para>
        /// </summary>
        public int BoneCount => _bones.Count;

        /// <summary>보행이 실제로 움직이는 뼈의 수.</summary>
        public int WalkBoneCount
        {
            get
            {
                var count = 0;
                for (var i = 0; i < _bones.Count; i++) if (_bones[i].InWalk) count++;
                return count;
            }
        }

        /// <summary>던지기가 실제로 움직이는 뼈의 수.</summary>
        public int TossBoneCount
        {
            get
            {
                var count = 0;
                for (var i = 0; i < _bones.Count; i++) if (_bones[i].InToss) count++;
                return count;
            }
        }

        /// <summary>던지기 동작이 재생 중인가. 재생 중에는 보행이 물러난다.</summary>
        public bool TossActive => _tossActive;

        /// <summary>던지기 동작의 현재 세기(0~1). 검증과 표시가 읽는다.</summary>
        public float TossWeightNow => _tossWeight;

        /// <summary>던지기 클립이 실제로 있는가. 없으면 <see cref="PlayToss"/> 가 조용히 아무것도 안 한다.</summary>
        public bool HasToss => _toss != null;

        public static RabbitLocomotion Create(Transform model, GameObject reference)
        {
            var walk = Resources.Load<AnimationClip>(WalkResource);
            var run = Resources.Load<AnimationClip>(RunResource);
            if (walk == null || run == null || model == null || reference == null) return null;

            // 던지기 클립은 **없어도 된다.** 아직 굽지 않은 저장소에서도 걷기·달리기는
            // 그대로 돌아야 한다 — 여기서 null 을 돌려주면 캐릭터가 통째로 뻣뻣해진다.
            var toss = Resources.Load<AnimationClip>(TossResource);
            return new RabbitLocomotion(model, reference, walk, run, toss);
        }

        RabbitLocomotion(Transform model, GameObject reference,
                         AnimationClip walk, AnimationClip run, AnimationClip toss)
        {
            _walk = walk;
            _run = run;
            _toss = toss;
            // 메시/콜라이더/스크립트 없이 Transform만 복제한다. 원본 프리팹을 수정하지 않는다.
            _poseRoot = CloneSkeleton(reference.transform, null).gameObject;
            _poseRoot.name = "~Rabbit locomotion pose (runtime only)";
            _poseRoot.hideFlags = HideFlags.HideAndDontSave;
            var modelBones = new Dictionary<string, Transform>();
            foreach (var t in model.GetComponentsInChildren<Transform>(true)) modelBones[t.name] = t;
            foreach (var t in _poseRoot.GetComponentsInChildren<Transform>(true))
            {
                var inWalk = Contains(BoneNames, t.name);
                var inToss = Contains(TossBoneNames, t.name);
                if ((!inWalk && !inToss) || !modelBones.TryGetValue(t.name, out var target)) continue;
                _bones.Add(new BoneLink
                {
                    Target = target, Pose = t, RestPosition = t.localPosition,
                    Arm = t.name.Contains("arm") || t.name.EndsWith("Hand") ? (t.name.StartsWith("L_") ? 1 : 2) : 0,
                    PreserveAnchor = t.name.EndsWith("Upperarm") || t.name.EndsWith("Thigh"),
                    InWalk = inWalk, InToss = inToss
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
            _mixer = AnimationMixerPlayable.Create(_graph, 3);
            _graph.Connect(_walkNode, 0, _mixer, 0);
            _graph.Connect(_runNode, 0, _mixer, 1);
            if (_toss != null)
            {
                _tossNode = AnimationClipPlayable.Create(_graph, _toss);
                _tossNode.SetSpeed(0);
                _tossNode.SetApplyFootIK(false);
                _graph.Connect(_tossNode, 0, _mixer, 2);
            }
            AnimationPlayableOutput.Create(_graph, "Pose only", animator).SetSourcePlayable(_mixer);
            _mixer.SetInputWeight(0, 1f);
            _graph.Play();
        }

        static bool Contains(IReadOnlyList<string> names, string name)
        {
            for (int i = 0; i < names.Count; i++) if (names[i] == name) return true;
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

            // ── 던지기가 보행보다 앞선다 (S15P21B202-519) ──────────────────
            //
            // 한 번짜리 동작이라 걷기와 섞지 않는다. 섞으면 던지는 동안 다리가 걸음을
            // 걷는다. 기절·탑승 같은 것이 끼어들면 그냥 끊는다 — 그쪽이 몸을 통째로
            // 가져가므로 이어 봐야 어긋난 그림만 남는다.
            if (_tossActive)
            {
                if (state.Blocked) { CancelToss(); }
                else
                {
                    _tossTime += deltaTime;
                    _tossWeight = TossWeight(_tossTime, _toss.length);
                    if (_tossTime >= _toss.length) { CancelToss(); }
                    else
                    {
                        _tossNode.SetTime(_tossTime);
                        _mixer.SetInputWeight(0, 0f);
                        _mixer.SetInputWeight(1, 0f);
                        _mixer.SetInputWeight(2, 1f);
                        _graph.Evaluate(0f);

                        // **팔이 바쁜지 보지 않는다.** 던지는 사람은 반드시 남을 들고
                        // 있으므로 그 검사를 그대로 두면 정작 던지는 두 팔이 빠진다.
                        _weight = 0f;
                        _phase = 0f;
                        WriteBones(link => link.InToss, _tossWeight, state, respectArmBusy: false);
                        return;
                    }
                }
            }

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
            _mixer.SetInputWeight(2, 0f);
            _graph.Evaluate(0f);

            WriteBones(link => link.InWalk, _weight, state, respectArmBusy: true);
        }

        /// <summary>
        /// 만들어 둔 자세를 진짜 뼈에 옮긴다.
        ///
        /// <para>보행과 던지기가 <b>같은 코드를 쓴다</b> — 다른 것은 어느 뼈에 쓰느냐,
        /// 얼마나 섞느냐, 그리고 바쁜 팔을 빼느냐 셋뿐이다.</para>
        /// </summary>
        void WriteBones(Func<BoneLink, bool> include, float weight, State state, bool respectArmBusy)
        {
            // 부모를 바꾼 뒤의 값이 섞이지 않도록 물리 포즈를 먼저 모두 저장한다.
            foreach (var link in _bones)
            {
                link.PhysicsPosition = link.Target.localPosition;
                link.PhysicsRotation = link.Target.localRotation;
            }
            foreach (var link in _bones)
            {
                if (!include(link)) continue;
                if (respectArmBusy
                    && ((link.Arm == 1 && state.LeftArmBusy) || (link.Arm == 2 && state.RightArmBusy)))
                    continue;

                link.Target.localRotation = Quaternion.Slerp(link.PhysicsRotation, link.Pose.localRotation, weight);
                // 어깨/고관절은 기존 부착 위치를 유지하고 자식 뼈의 길이는 원본 값을 쓴다.
                if (!link.PreserveAnchor)
                    link.Target.localPosition = Vector3.Lerp(link.PhysicsPosition, link.RestPosition, weight);
            }
        }

        /// <summary>
        /// 던지기 동작을 처음부터 재생한다. 클립이 없으면 아무 일도 하지 않는다.
        ///
        /// <para>이미 재생 중이어도 <b>처음으로 되돌린다</b> — 연달아 두 번 던지면 두 번째
        /// 동작이 보여야 한다.</para>
        /// </summary>
        public void PlayToss()
        {
            if (_toss == null || !_graph.IsValid()) return;

            _tossActive = true;
            _tossTime = 0f;
            _tossWeight = 0f;
        }

        void CancelToss()
        {
            _tossActive = false;
            _tossTime = 0f;
            _tossWeight = 0f;
            if (_mixer.IsValid()) _mixer.SetInputWeight(2, 0f);
        }

        /// <summary>
        /// 던지기 동작이 지금 얼마나 세게 먹는가.
        ///
        /// <para>물리 자세에서 <b>들어갔다 나온다</b>. 갑자기 1 로 켜면 팔이 순간이동하고,
        /// 갑자기 0 으로 끄면 끝나는 순간 팔이 툭 떨어진다.</para>
        ///
        /// <para>조건문으로 <see cref="Apply"/> 안에만 두면 검증이 닿지 못한다 —
        /// <c>-400</c>·<c>-438</c>·<c>-448</c>·<c>-505</c> 에서 네 번 겪은 자리다.</para>
        /// </summary>
        public static float TossWeight(float time, float length)
        {
            const float rampIn = 0.08f;
            const float rampOut = 0.18f;

            if (length <= 0f || time < 0f || time >= length) return 0f;

            var rise = Mathf.Clamp01(time / rampIn);
            var fall = Mathf.Clamp01((length - time) / rampOut);
            return Mathf.Min(rise, fall);
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
