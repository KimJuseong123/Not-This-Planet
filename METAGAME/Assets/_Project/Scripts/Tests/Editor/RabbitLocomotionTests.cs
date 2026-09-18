using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Metagame.Core;

namespace Metagame.Gameplay.Tests
{
    public sealed class RabbitLocomotionTests
    {
        PlayerRig _player;
        bool _puffEnabled;
        SpaceSuit Suit => _player.Doll.Suit;

        [SetUp]
        public void SetUp()
        {
            _puffEnabled = Puff.Enabled;
            Puff.Enabled = false;
            _player = PlayerRig.Create("Animation test", Vector3.zero, Quaternion.identity, Color.white, Color.gray);
            Assert.NotNull(Suit, "Existing rabbit prefab must still load.");
            Suit.InitializeLocomotion();
            Assert.NotNull(Suit.Locomotion, "Both baked resources must be included.");
        }

        [TearDown]
        public void TearDown()
        {
            // EditMode에서는 MonoBehaviour.OnDestroy가 보장되지 않으므로 테스트가 정리한다.
            if (_player != null)
            {
                Suit?.Locomotion?.Dispose();
                Object.DestroyImmediate(_player.gameObject);
            }
            Puff.Enabled = _puffEnabled;
        }

        static RabbitLocomotion.State Walking => new()
        { Grounded = true, Speed = Tuning.WalkSpeed, Direction = Vector2.up };

        void Advance(RabbitLocomotion.State state, int frames = 15)
        {
            for (int i = 0; i < frames; i++)
            {
                Suit.Sync();
                Suit.Locomotion.Apply(state, 1f / 60f);
            }
        }

        Transform Bone(string name) => Suit.Model.GetComponentsInChildren<Transform>(true).Single(t => t.name == name);

        [TestCase(RabbitLocomotion.WalkResource, 1.166667f)]
        [TestCase(RabbitLocomotion.RunResource, 0.7f)]
        public void ClipsAreLoopingTransformOnlyMotions(string key, float seconds)
        {
            var clip = Resources.Load<AnimationClip>(key);
            Assert.NotNull(clip);
            Assert.That(clip.length, Is.EqualTo(seconds).Within(0.001f));
            Assert.True(clip.isLooping);
            Assert.False(clip.humanMotion, "Retargeted clips must not depend on a runtime avatar.");
            var bindings = AnimationUtility.GetCurveBindings(clip);
            Assert.That(bindings.Length, Is.EqualTo(14 * 4));
            foreach (var binding in bindings)
            {
                Assert.That(binding.path, Is.Not.Empty);
                Assert.That(binding.propertyName, Does.StartWith("m_LocalRotation."));
                Assert.That(RabbitLocomotion.BoneNames, Does.Contain(binding.path.Split('/').Last()));
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                Assert.That(Mathf.Abs(Mathf.Abs(curve.Evaluate(0)) - Mathf.Abs(curve.Evaluate(clip.length))), Is.LessThan(0.001f));
            }
        }

        [Test]
        public void WalkingChangesTheExistingRabbitLeg()
        {
            // 짝지은 뼈 전체가 아니라 **보행이 움직이는 뼈**를 센다 (S15P21B202-519).
            // 던지기가 허리·척추까지 쓰게 되면서 짝 목록은 합집합이 되었다 — 전체 수를
            // 재면 던지기가 뼈를 하나 더 쓸 때마다 멀쩡한 걷기 검증이 깨진다.
            Assert.That(Suit.Locomotion.WalkBoneCount, Is.EqualTo(14));
            Advance(Walking);
            Quaternion first = Bone("L_Thigh").localRotation;
            Advance(Walking);
            Assert.Greater(Quaternion.Angle(first, Bone("L_Thigh").localRotation), 1f);
            Assert.That(Suit.Locomotion.Weight, Is.EqualTo(1f));
            Assert.That(Suit.Locomotion.RunBlend, Is.Zero);
        }

        /// <summary>
        /// 걷기는 허리를 물리에 맡긴다 (S15P21B202-519).
        ///
        /// <para>던지기가 허리·척추까지 쓰게 되면서 짝 목록이 <b>합집합</b>이 되었다.
        /// 쓸 때 거르지 않으면 걷는 동안 허리가 물리가 아니라 <b>기준 자세로</b> 끌려가
        /// 멀쩡하던 걷기가 뻣뻣해진다 — 눈에 잘 안 띄는 회귀라 여기서 잠근다.</para>
        /// </summary>
        [Test]
        public void WalkingLeavesTheWaistToPhysics()
        {
            var waist = Bone("Waist");
            Advance(Walking);

            Suit.Sync();                                   // 물리 자세로 되돌린다
            var physics = waist.localRotation;
            Suit.Locomotion.Apply(Walking, 1f / 60f);      // 걷기를 한 번 얹는다

            Assert.That(Quaternion.Angle(physics, waist.localRotation), Is.LessThan(0.1f),
                "걷기가 허리를 건드리면 안 된다.");
        }

        [Test]
        public void RunningSelectsRunningAndReturnsToWalk()
        {
            var state = Walking;
            state.Running = true; state.Speed = Tuning.RunSpeed;
            Advance(state);
            Assert.That(Suit.Locomotion.RunBlend, Is.EqualTo(1f));
            Advance(Walking);
            Assert.That(Suit.Locomotion.RunBlend, Is.Zero);
        }

        [Test]
        public void StoppingRestoresThePhysicsPose()
        {
            Advance(Walking);
            var state = Walking; state.Speed = 0f;
            Advance(state);
            Assert.That(Suit.Locomotion.Weight, Is.Zero);
            Assert.That(Suit.WorstDrift(), Is.LessThan(0.0001f));
        }

        /// <summary>
        /// 기절·탑승처럼 <b>몸을 빼앗긴</b> 상태는 즉시 물리로 돌아간다.
        ///
        /// <para><b>공중은 이제 여기서 빠진다</b>(S15P21B202-628). 예전에는 공중도
        /// 물리로 돌려보냈는데 — 그래서 체공 1.36초 동안 아무 자세도 없었다 —
        /// 지금은 세로 속도로 고른 공중 자세를 쓴다. 전제를 바꿨으면 그 전제를
        /// 재는 검증도 같이 바꿔야 한다(576 의 유형 R).</para>
        /// </summary>
        [Test]
        public void BeingBlockedImmediatelyReturnsToPhysics()
        {
            Advance(Walking);
            var state = Walking; state.Blocked = true;
            Advance(state, 1);
            Assert.That(Suit.Locomotion.Weight, Is.Zero);
            Assert.That(Suit.WorstDrift(), Is.LessThan(0.0001f));
        }

        /// <summary>
        /// 공중에서는 <b>걷기는 꺼지되 자세는 남는다</b> (S15P21B202-628).
        ///
        /// <para>둘을 같이 재는 이유: 걷기가 안 꺼지면 공중에서 다리가 걸음을 걷고,
        /// 자세가 안 써지면 1.36초 동안 허우적임만 보인다.</para>
        /// </summary>
        [Test]
        public void GoingAirborneStopsWalkingButKeepsAPose()
        {
            Advance(Walking);
            var state = Walking; state.Grounded = false; state.VerticalSpeed = -2f;
            Advance(state, 12);

            Assert.That(Suit.Locomotion.Weight, Is.Zero, "공중에서 걷기가 안 꺼졌다.");
            Assert.That(Suit.WorstDrift(), Is.GreaterThan(0.0001f),
                "공중인데 물리 자세 그대로다 — 공중 자세가 안 써지고 있다.");
        }

        [Test]
        public void SidewaysMovementKeepsExistingSideStep()
        {
            var state = Walking; state.Direction = Vector2.right;
            Advance(state);
            Assert.That(Suit.Locomotion.Weight, Is.Zero);
        }

        [Test]
        public void BackwardPlaybackWrapsWithoutNegativePhase()
        {
            var state = Walking; state.Direction = Vector2.down;
            Advance(state, 120);
            Assert.That(Suit.Locomotion.Phase, Is.InRange(0f, 1f));
        }

        [TestCase(true, false, "L_Hand")]
        [TestCase(false, true, "R_Hand")]
        public void BusyHandRetainsItsExactPhysicsPose(bool leftBusy, bool rightBusy, string bone)
        {
            Suit.Sync();
            var hand = Bone(bone);
            Vector3 position = hand.position;
            Quaternion rotation = hand.rotation;
            var state = Walking; state.LeftArmBusy = leftBusy; state.RightArmBusy = rightBusy;
            Advance(state);
            Assert.That(Vector3.Distance(position, hand.position), Is.LessThan(0.0001f));
            Assert.That(Quaternion.Angle(rotation, hand.rotation), Is.LessThan(0.01f));
        }

        [Test]
        public void AnimationDoesNotMoveAnyRigidBodyOrChangeItsVelocity()
        {
            var bodies = _player.Doll.Bodies;
            var positions = bodies.Select(b => b.position).ToArray();
            var rotations = bodies.Select(b => b.rotation).ToArray();
            var velocities = bodies.Select(b => b.linearVelocity).ToArray();
            Advance(Walking, 90);
            for (int i = 0; i < bodies.Count; i++)
            {
                Assert.That(bodies[i].position, Is.EqualTo(positions[i]));
                Assert.That(bodies[i].rotation, Is.EqualTo(rotations[i]));
                Assert.That(bodies[i].linearVelocity, Is.EqualTo(velocities[i]));
            }
        }

        [Test]
        public void DownedPlayerDisallowsLocomotion()
        {
            _player.GoDown("animation test");
            Assert.True(RabbitLocomotion.ReadState(_player).Blocked);
        }

        [Test]
        public void GrabInputReservesTheCorrespondingArm()
        {
            _player.SetInput(new InputFrame { GrabLeft = true, Run = true });
            var state = RabbitLocomotion.ReadState(_player);
            Assert.True(state.LeftArmBusy);
            Assert.False(state.RightArmBusy);
            Assert.True(state.Running);
        }
    }
}
