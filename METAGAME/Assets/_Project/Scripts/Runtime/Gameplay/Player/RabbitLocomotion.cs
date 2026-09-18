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

        /// <summary>F2 춤 1번 '뮤직챌린지' (S15P21B202-563). 14.2초짜리 한 번짜리 동작.</summary>
        public const string DanceResource = "RabbitLocomotion/Rabbit_Dance";

        /// <summary>F2 춤 2번 'REDRED' (S15P21B202-576). 16.3초.</summary>
        public const string RedRedResource = "RabbitLocomotion/Rabbit_RedRed";

        /// <summary>F2 춤 3번 'BAD' (S15P21B202-593). 24.4초 — 셋 중 가장 길다.</summary>
        public const string BadResource = "RabbitLocomotion/Rabbit_Bad";

        // ── 엎드리기 (S15P21B202-616) ────────────────────────────────
        //
        // **몸이 눕는 것은 이 클립들에 없다.** 그건 물리가 한다(PlayerRig.Balance 의
        // 목표 회전). 클립은 그 위에 얹히는 팔다리 모양만 맡는다 — 둘 다 눕히면
        // 두 번 눕는다.
        public const string ProneIdleResource = "RabbitLocomotion/Rabbit_ProneIdle";
        public const string CrawlResource = "RabbitLocomotion/Rabbit_Crawl";
        public const string LieDownResource = "RabbitLocomotion/Rabbit_LieDown";
        public const string StandUpResource = "RabbitLocomotion/Rabbit_StandUp";

        /// <summary>
        /// 춤을 끊는 <b>밀림</b> 속도(m/s) (S15P21B202-596).
        ///
        /// <para><b>제 발로 걷는 것은 이 값이 잡지 않는다</b> — 방향키를 누르면
        /// <see cref="State.MoveInput"/> 이 즉시 끊는다. 이 문턱이 남아 있는 이유는
        /// 남이 밀거나 경사에서 미끄러지는 것처럼 <b>내 의지와 무관하게</b> 몸이
        /// 움직이는 경우다. 그때까지 춤을 이어 두면 다리는 춤추고 몸은 미끄러진다.</para>
        ///
        /// <para>예전에는 이 값이 0.15(걷기가 켜지는 문턱)였다. 그 값은 <b>방향키를
        /// 누르지 않았는데도</b> 제자리 흔들림이나 작은 접촉으로 넘어간다 — 춤이
        /// 이유 없이 끊기는 것이 그래서였다. 걷기 속도의 절반으로 올려, 밀려서
        /// 걷는 만큼 움직일 때만 끊는다.</para>
        /// </summary>
        public const float DanceShoveSpeed = Tuning.WalkSpeed * 0.5f;

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

        /// <summary>
        /// 한 번짜리 동작 하나. 던지기와 춤이 같은 틀을 쓴다 (S15P21B202-563).
        ///
        /// <para>춤을 넣으면서 던지기 전용이던 필드를 이 형태로 묶었다. 둘은
        /// <b>클립·믹서 자리·끊기는 조건</b>만 다르고 재생·섞기·끝내기는 완전히 같다 —
        /// 따로 두면 던지기에서 고친 것(<see cref="TossWeight"/> 봉투, 기절 시 끊기)이
        /// 춤에는 빠지게 된다.</para>
        /// </summary>
        sealed class OneShot
        {
            public AnimationClip Clip;
            public AnimationClipPlayable Node;
            public int Slot;                // 믹서 입력 번호
            public bool CancelOnMove;       // 걸으면 끊는가
            public float Time, Weight;
            public bool Active;
            public bool Ready => Clip != null;
        }

        readonly List<BoneLink> _bones = new();
        readonly AnimationClip _walk, _run;
        readonly GameObject _poseRoot;
        readonly OneShot _tossShot = new() { Slot = 2, CancelOnMove = false };
        readonly OneShot _danceShot = new() { Slot = 3, CancelOnMove = true };
        readonly OneShot _redredShot = new() { Slot = 4, CancelOnMove = true };
        readonly OneShot _badShot = new() { Slot = 5, CancelOnMove = true };
        // 자세 전환은 **걸어도 안 끊긴다** — 끊기면 눕다 만 자세로 굳는다.
        readonly OneShot _lieDownShot = new() { Slot = 6, CancelOnMove = false };
        readonly OneShot _standUpShot = new() { Slot = 7, CancelOnMove = false };
        OneShot[] OneShots => _oneShots ??= new[]
            { _tossShot, _danceShot, _redredShot, _badShot, _lieDownShot, _standUpShot };
        OneShot[] _oneShots;

        /// <summary>
        /// 번호로 고르는 춤들. <c>DanceCatalog.Names</c> 와 <b>순서도 개수도</b> 같아야 한다.
        /// </summary>
        OneShot[] DanceShots => _danceShots ??= new[] { _danceShot, _redredShot, _badShot };
        OneShot[] _danceShots;
        PlayableGraph _graph;
        AnimationClipPlayable _walkNode, _runNode;
        AnimationClip _proneIdle, _crawl;
        AnimationClipPlayable _proneIdleNode, _crawlNode;
        int ProneIdleSlot => 2 + OneShots.Length;
        int CrawlSlot => ProneIdleSlot + 1;
        float _proneWeight, _crawlBlend, _proneIdleTime;
        bool _proneLast;
        AnimationMixerPlayable _mixer;
        float _phase, _weight, _runBlend;
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
        public bool TossActive => _tossShot.Active;

        /// <summary>던지기 동작의 현재 세기(0~1). 검증과 표시가 읽는다.</summary>
        public float TossWeightNow => _tossShot.Weight;

        /// <summary>던지기 클립이 실제로 있는가. 없으면 <see cref="PlayToss"/> 가 조용히 아무것도 안 한다.</summary>
        public bool HasToss => _tossShot.Ready;

        /// <summary>춤이 하나라도 재생 중인가. 재생 중에는 보행이 물러난다.</summary>
        public bool DanceActive
        {
            get
            {
                foreach (var shot in DanceShots) if (shot.Active) return true;
                return false;
            }
        }

        /// <summary>지금 도는 춤의 세기(0~1). 도는 것이 없으면 0.</summary>
        public float DanceWeightNow
        {
            get
            {
                foreach (var shot in DanceShots) if (shot.Active) return shot.Weight;
                return 0f;
            }
        }

        /// <summary>목록에 있는 춤 수만큼 슬롯이 있는가. 검증이 읽는다.</summary>
        public int DanceSlotCount => DanceShots.Length;

        /// <summary>그 번호의 춤 클립이 있는가. 없으면 <see cref="PlayDance"/> 가 조용히 아무것도 안 한다.</summary>
        public bool HasDance(byte id) =>
            DanceCatalog.IsValid(id) && id <= DanceShots.Length && DanceShots[id - 1].Ready;

        /// <summary>그 번호의 춤 길이(초). 클립이 없으면 0.</summary>
        public float DanceLength(byte id) =>
            HasDance(id) ? DanceShots[id - 1].Clip.length : 0f;

        public static RabbitLocomotion Create(Transform model, GameObject reference)
        {
            var walk = Resources.Load<AnimationClip>(WalkResource);
            var run = Resources.Load<AnimationClip>(RunResource);
            if (walk == null || run == null || model == null || reference == null) return null;

            // 한 번짜리 클립들은 **없어도 된다.** 아직 굽지 않은 저장소에서도 걷기·달리기는
            // 그대로 돌아야 한다 — 여기서 null 을 돌려주면 캐릭터가 통째로 뻣뻣해진다.
            var toss = Resources.Load<AnimationClip>(TossResource);
            var dance = Resources.Load<AnimationClip>(DanceResource);
            var redred = Resources.Load<AnimationClip>(RedRedResource);
            var bad = Resources.Load<AnimationClip>(BadResource);
            var proneIdle = Resources.Load<AnimationClip>(ProneIdleResource);
            var crawl = Resources.Load<AnimationClip>(CrawlResource);
            var lieDown = Resources.Load<AnimationClip>(LieDownResource);
            var standUp = Resources.Load<AnimationClip>(StandUpResource);
            return new RabbitLocomotion(model, reference, walk, run, toss, dance, redred, bad,
                                        proneIdle, crawl, lieDown, standUp);
        }

        RabbitLocomotion(Transform model, GameObject reference, AnimationClip walk,
                         AnimationClip run, AnimationClip toss, AnimationClip dance,
                         AnimationClip redred, AnimationClip bad,
                         AnimationClip proneIdle, AnimationClip crawl,
                         AnimationClip lieDown, AnimationClip standUp)
        {
            _walk = walk;
            _run = run;
            _tossShot.Clip = toss;
            _danceShot.Clip = dance;
            _redredShot.Clip = redred;
            _badShot.Clip = bad;
            _lieDownShot.Clip = lieDown;
            _standUpShot.Clip = standUp;
            _proneIdle = proneIdle;
            _crawl = crawl;
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
            // 0 걷기 · 1 달리기 · 2 던지기 · 3~5 춤 · 6 엎드리기 · 7 일어서기
            // · 8 엎드린 대기 · 9 기어가기.
            // **한 번짜리가 늘면 이 수도 같이 늘려야 한다** — 안 늘리면 새 클립이
            // 믹서에 못 붙어 조용히 아무 일도 안 일어난다. 검증이 이 수를 센다.
            _mixer = AnimationMixerPlayable.Create(_graph, 2 + OneShots.Length + 2);
            _graph.Connect(_walkNode, 0, _mixer, 0);
            _graph.Connect(_runNode, 0, _mixer, 1);
            foreach (var shot in OneShots)
            {
                if (!shot.Ready) continue;
                shot.Node = AnimationClipPlayable.Create(_graph, shot.Clip);
                shot.Node.SetSpeed(0);
                shot.Node.SetApplyFootIK(false);
                _graph.Connect(shot.Node, 0, _mixer, shot.Slot);
            }
            if (_proneIdle != null)
            {
                _proneIdleNode = AnimationClipPlayable.Create(_graph, _proneIdle);
                _proneIdleNode.SetSpeed(0); _proneIdleNode.SetApplyFootIK(false);
                _graph.Connect(_proneIdleNode, 0, _mixer, ProneIdleSlot);
            }
            if (_crawl != null)
            {
                _crawlNode = AnimationClipPlayable.Create(_graph, _crawl);
                _crawlNode.SetSpeed(0); _crawlNode.SetApplyFootIK(false);
                _graph.Connect(_crawlNode, 0, _mixer, CrawlSlot);
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

            /// <summary>
            /// 방향키(또는 점프)를 누르고 있는가 (S15P21B202-596).
            ///
            /// <para><b>속도가 아니라 이것이 춤을 끝낸다.</b> 속도는 결과라서 늦고,
            /// 밀려서 생긴 속도와 제 발로 낸 속도를 구별하지 못한다. 사용자가 원한
            /// 것은 "방향키를 누르기 전까지는 고정된 춤" 이다.</para>
            /// </summary>
            public bool MoveInput;

            /// <summary>제 발로 엎드려 있는가 (S15P21B202-616).</summary>
            public bool Prone;
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
                MoveInput = input.Move.sqrMagnitude > 1e-4f || input.Jump,
                Prone = rig.Prone,
                LeftArmBusy = input.GrabLeft || input.ArmsUp || rig.LeftHand.Gripping || rig.LeftHand.Carrying || rig.HeldTool != null,
                RightArmBusy = input.GrabRight || input.ArmsUp || rig.RightHand.Gripping || rig.RightHand.Carrying || rig.HeldTool != null
            };
        }

        /// <summary>엎드린 자세를 그릴 수 있는가. 아직 안 구운 저장소에서도 게임은 돌아야 한다.</summary>
        bool ProneReady => _proneIdle != null && _crawl != null
            && _proneIdleNode.IsValid() && _crawlNode.IsValid();

        public static bool CanAnimate(State state) => state.Grounded && !state.Blocked &&
            state.Speed > 0.15f && Mathf.Abs(state.Direction.x) <= Mathf.Abs(state.Direction.y) + 0.1f;

        /// <summary>이동 거리나 골반 위치는 쓰지 않는다. 먼저 SpaceSuit.Sync를 호출해야 한다.</summary>
        public void Apply(State state, float deltaTime)
        {
            if (!_graph.IsValid()) return;
            deltaTime = Mathf.Clamp(deltaTime, 0f, 0.1f);

            // 엎드리기/일어서기 전환을 **여기서** 켠다 (S15P21B202-616). 자세가 바뀐
            // 것은 State 로 이미 들어오므로 밖에서 부르는 배관을 따로 늘리지 않는다.
            if (state.Prone != _proneLast)
            {
                Play(state.Prone ? _lieDownShot : _standUpShot);
                _proneLast = state.Prone;
            }

            // ── 던지기가 보행보다 앞선다 (S15P21B202-519) ──────────────────
            //
            // 한 번짜리 동작이라 걷기와 섞지 않는다. 섞으면 던지는 동안 다리가 걸음을
            // 걷는다. 기절·탑승 같은 것이 끼어들면 그냥 끊는다 — 그쪽이 몸을 통째로
            // 가져가므로 이어 봐야 어긋난 그림만 남는다.
            foreach (var shot in OneShots)
            {
                if (!shot.Active) continue;

                // 춤은 14~24초라 그 사이 걸어다니면 다리는 춤추고 몸은 미끄러진다.
                // **방향키가 먼저다**(S15P21B202-596) — 속도는 밀림만 잡는 뒷문이다.
                // 던지기(0.3초)는 이 검사를 받지 않는다 — 던지면서 걷는 것이 정상이다.
                if (state.Blocked
                    || (shot.CancelOnMove && (state.MoveInput || state.Speed > DanceShoveSpeed)))
                {
                    Cancel(shot);
                    continue;
                }

                shot.Time += deltaTime;
                shot.Weight = TossWeight(shot.Time, shot.Clip.length);
                if (shot.Time >= shot.Clip.length) { Cancel(shot); continue; }

                shot.Node.SetTime(shot.Time);
                _mixer.SetInputWeight(0, 0f);
                _mixer.SetInputWeight(1, 0f);
                foreach (var other in OneShots)
                    if (other.Ready) _mixer.SetInputWeight(other.Slot, other == shot ? 1f : 0f);
                _graph.Evaluate(0f);

                // **팔이 바쁜지 보지 않는다.** 던지는 사람은 반드시 남을 들고
                // 있으므로 그 검사를 그대로 두면 정작 던지는 두 팔이 빠진다.
                _weight = 0f;
                _phase = 0f;
                WriteBones(link => link.InToss, shot.Weight, state, respectArmBusy: false);
                return;
            }

            // ── 엎드린 자세 (S15P21B202-616) ────────────────────────────
            //
            // 걷기/달리기와 **같은 층**이다 — 뗄 때까지 유지되는 자세라
            // 한 번짜리(OneShot)로는 표현할 수 없다. 멈춰 있으면 대기,
            // 움직이면 기어가기로 섞는다.
            if (state.Prone && ProneReady)
            {
                _weight = 0f;
                _proneWeight = Mathf.MoveTowards(_proneWeight, 1f, deltaTime / 0.12f);
                _crawlBlend = Mathf.MoveTowards(_crawlBlend,
                    state.Speed > 0.12f ? 1f : 0f, deltaTime / 0.15f);

                _proneIdleTime = Mathf.Repeat(_proneIdleTime + deltaTime, _proneIdle.length);
                // 기어가는 주기를 **실제 속도에 맞춘다.** 고정 속도로 돌리면 제자리에서
                // 팔다리만 젓거나, 미끄러지듯 밀려간다.
                float crawlRate = Mathf.Clamp(state.Speed / PlayerRig.CrawlSpeed, 0f, 1.6f);
                _phase = Mathf.Repeat(_phase + deltaTime * crawlRate / Mathf.Max(_crawl.length, 0.01f), 1f);

                _proneIdleNode.SetTime(_proneIdleTime);
                _crawlNode.SetTime(_phase * _crawl.length);
                _mixer.SetInputWeight(0, 0f);
                _mixer.SetInputWeight(1, 0f);
                foreach (var shot in OneShots) if (shot.Ready) _mixer.SetInputWeight(shot.Slot, 0f);
                _mixer.SetInputWeight(ProneIdleSlot, 1f - _crawlBlend);
                _mixer.SetInputWeight(CrawlSlot, _crawlBlend);
                _graph.Evaluate(0f);

                // 엎드리면 팔도 자세의 일부다 — 던지기와 같은 이유로 바쁜 팔을 빼지 않는다.
                WriteBones(link => link.InToss, _proneWeight, state, respectArmBusy: false);
                return;
            }
            _proneWeight = 0f;
            _crawlBlend = 0f;

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
            foreach (var shot in OneShots) if (shot.Ready) _mixer.SetInputWeight(shot.Slot, 0f);
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
        public void PlayToss() => Play(_tossShot);

        /// <summary>
        /// 춤을 처음부터 재생한다. 클립이 없으면 아무 일도 하지 않는다 (S15P21B202-563).
        ///
        /// <para>던지기와 마찬가지로 이미 재생 중이어도 <b>처음으로 되돌린다.</b></para>
        /// </summary>
        public void PlayDance(byte id)
        {
            if (!DanceCatalog.IsValid(id) || id > DanceShots.Length) return;
            Play(DanceShots[id - 1]);
        }

        /// <summary>도는 춤을 즉시 끝낸다. 걷기 시작하면 <see cref="Apply"/> 가 알아서 부른다.</summary>
        public void CancelDance()
        {
            foreach (var shot in DanceShots) Cancel(shot);
        }

        void Play(OneShot shot)
        {
            if (!shot.Ready || !_graph.IsValid()) return;

            // 한 번에 하나만 돈다. 둘을 겹치면 두 클립이 같은 뼈를 두고 다툰다.
            foreach (var other in OneShots) if (other != shot) Cancel(other);

            shot.Active = true;
            shot.Time = 0f;
            shot.Weight = 0f;
        }

        void Cancel(OneShot shot)
        {
            shot.Active = false;
            shot.Time = 0f;
            shot.Weight = 0f;
            if (_mixer.IsValid() && shot.Ready) _mixer.SetInputWeight(shot.Slot, 0f);
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
