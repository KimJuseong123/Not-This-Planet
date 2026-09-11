using System.Collections.Generic;
using Metagame.Core;
using Metagame.Gameplay;
using Metagame.Gameplay.Hazards;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 방망이 타격을 눈으로 납득할 수 있는지 검증한다(기획 §18.1, S15P21B202-276).
    ///
    /// <b>판정은 고칠 것이 없었다.</b> <see cref="MeteorBat.FindTarget"/> 은 운석의
    /// 현재 위치를 보지 않고 <b>예측 착탄 지점</b>을 보므로, S15P21B202-274 로 운석이
    /// 대각선으로 와도 판정이 변하지 않는다. 그 사실을 측정해 이 스위트에 잠근다.
    ///
    /// <b>지켜지지 않았던 것은 §18.1 의 마지막 줄이다</b> — "실제 방망이 메시가 운석과
    /// 정확히 닿지 않아도 <b>넓은 에너지 궤적으로 판정을 납득시킨다</b>". 이 기믹은
    /// 처음부터 물리적 접촉이 아니었다. 타격 순간의 운석은 수직 낙하에서도 12.8m 위에
    /// 있었고 대각선에서는 약 15m 밖에 있는데, 나오던 것은 0.05m 파티클 세 개였다.
    ///
    /// 대각선 운석은 <b>스포너를 거치지 않고 직접</b> 만든다. 그러면 274 가 머지되기
    /// 전에도 이 검증이 성립하고, 판정이 낙하 경로와 무관하다는 것 자체를 보여준다.
    /// </summary>
    public sealed class BatSwingArcTests
    {
        readonly List<GameObject> _spawned = new();
        bool _trailsWere;
        bool _puffWas;

        [SetUp]
        public void SetUp()
        {
            Meteor.All.Clear();
            PlayerRig.All.Clear();

            _trailsWere = Meteor.TrailsEnabled;
            _puffWas = Puff.Enabled;
            Meteor.TrailsEnabled = false;   // 판정 검증에 연출은 필요 없다
            Puff.Enabled = false;
        }

        [TearDown]
        public void TearDown()
        {
            Meteor.TrailsEnabled = _trailsWere;
            Puff.Enabled = _puffWas;

            foreach (var go in _spawned)
                if (go != null)
                    Object.DestroyImmediate(go);

            _spawned.Clear();
            Meteor.All.Clear();
            PlayerRig.All.Clear();
        }

        // --- 판정 재확인: 대각선에서도 그대로다 -------------------------------

        /// <summary>
        /// 대각선으로 오는 운석도 착탄 1초 전부터 쳐낼 수 있다(§18.1).
        ///
        /// 판정이 <b>예측 착탄 지점</b>을 보기 때문이다 — 운석이 8m 옆 12m 위에 있어도
        /// 착탄할 곳이 발밑이면 대상이 된다.
        /// </summary>
        [Test]
        public void ADiagonalMeteor_IsStrikableJustBeforeImpact()
        {
            var meteor = MakeDiagonalMeteor(Vector3.zero, 0.8f);

            var found = MeteorBat.FindTarget(Vector3.zero);

            Assert.AreSame(meteor, found, "대각선이어도 쳐낼 수 있어야 한다.");
        }

        [Test]
        public void ADiagonalMeteor_IsNotStrikableTooEarly()
        {
            MakeDiagonalMeteor(Vector3.zero, PhysicsScale.BatSwingWindowSeconds + 0.6f);

            Assert.IsNull(MeteorBat.FindTarget(Vector3.zero),
                "너무 이르면 헛스윙이다(§18.1).");
        }

        /// <summary>
        /// <b>운석이 머리 위에 있어도</b> 낙하 지점에서 멀면 못 쳐낸다(§18.1 반경 2.5m).
        ///
        /// 대각선이면 운석의 수평 위치와 착탄 지점이 다르다. 판정이 운석의 현재 위치를
        /// 본다면 이 검증이 실패한다 — 그것이 이 검증의 목적이다.
        /// </summary>
        [Test]
        public void StandingUnderTheMeteorButFarFromTheCircle_CannotStrike()
        {
            var impact = Vector3.zero;
            var meteor = MakeDiagonalMeteor(impact, 0.8f);

            var underMeteor = meteor.transform.position;
            underMeteor.y = 0f;

            Assert.Greater(new Vector3(underMeteor.x, 0f, underMeteor.z).magnitude,
                           PhysicsScale.BatAssistRadius,
                           "이 검증이 의미를 가지려면 운석이 원 밖에 있어야 한다.");

            Assert.IsNull(MeteorBat.FindTarget(underMeteor),
                "머리 위에 있어도 낙하 지점에서 멀면 못 쳐낸다.");
        }

        [Test]
        public void StandingInsideTheCircle_CanStrike()
        {
            var impact = new Vector3(6f, 0f, -3f);
            var meteor = MakeDiagonalMeteor(impact, 0.8f);

            var justInside = impact + new Vector3(PhysicsScale.BatAssistRadius - 0.3f, 0f, 0f);

            Assert.AreSame(meteor, MeteorBat.FindTarget(justInside));
        }

        /// <summary>§18.1 "같은 범위에 운석이 여러 개면 가장 먼저 착탄할 운석".</summary>
        [Test]
        public void TheSoonestToLandIsChosen_NotTheNearest()
        {
            var far = MakeDiagonalMeteor(new Vector3(1.5f, 0f, 0f), 0.3f);
            MakeDiagonalMeteor(Vector3.zero, 0.9f);

            Assert.AreSame(far, MeteorBat.FindTarget(Vector3.zero),
                "가까운 쪽이 아니라 먼저 떨어질 쪽을 고른다.");
        }

        /// <summary>
        /// 반사 방향은 <b>조준 방향</b>이다(§18.2 "카메라의 수평 방향에 약간의 위쪽
        /// 각도"). 운석이 어디서 왔는지와 무관하다 — 대각선이 되어도 바뀌지 않는다.
        /// </summary>
        [Test]
        public void TheDeflectionFollowsTheAimNotTheIncomingPath()
        {
            var bat = MakeBat();
            var meteor = MakeDiagonalMeteor(Vector3.zero, 0.5f);

            var incoming = meteor.GetComponent<Rigidbody>().linearVelocity;

            Assert.IsTrue(bat.Swing(Vector3.zero, Vector3.forward));
            Assert.AreSame(meteor, bat.LastDeflected);

            var after = meteor.GetComponent<Rigidbody>().linearVelocity;
            var horizontal = new Vector3(after.x, 0f, after.z).normalized;

            Assert.AreEqual(1f, Vector3.Dot(horizontal, Vector3.forward), 0.01f,
                "조준한 쪽으로 날아가야 한다.");
            Assert.Greater(after.y, 0f, "약간 들려야 한다(§18.2).");
            Assert.Less(Vector3.Dot(after.normalized, incoming.normalized), 0.9f,
                "들어온 방향을 이어받지 않는다.");
        }

        // --- §18.1 "넓은 에너지 궤적" -----------------------------------------

        [Test]
        public void TheArcIsSampledAlongTheSwing()
        {
            var into = new Vector3[PhysicsScale.BatArcSamples];

            var count = BatSwingLook.ArcPoints(Vector3.zero, Vector3.forward, 0.5f, into);

            Assert.AreEqual(PhysicsScale.BatArcSamples, count);
            for (var i = 1; i < count; i++)
                Assert.AreNotEqual(into[i - 1], into[i], "점이 겹치면 곡선이 아니다.");
        }

        /// <summary>
        /// 궤적이 §18.1 의 "넓은" 을 만족하는가. <b>각도로 잰다.</b>
        ///
        /// 현의 길이로 재면 안 된다 — 호의 현은 각도가 180도를 넘기 전까지 늘 반지름보다
        /// 짧을 수 있어서, 넓은 궤적도 "좁다" 로 나온다. 처음에 그렇게 재서 통과할 수
        /// 없는 검증을 만들었다.
        ///
        /// 맞는 순간(<see cref="PhysicsScale.BatSwingContactAt"/>)에 가장 넓다. 백스윙
        /// <see cref="PhysicsScale.BatSwingWindupYawDegrees"/>(110도)에서 정면까지
        /// 훑기 때문이다.
        /// </summary>
        [Test]
        public void TheArcIsWide()
        {
            var into = new Vector3[PhysicsScale.BatArcSamples];
            var origin = Vector3.zero;

            var count = BatSwingLook.ArcPoints(origin, Vector3.forward,
                                               PhysicsScale.BatSwingContactAt, into);

            var first = (into[0] - origin).normalized;
            var last = (into[count - 1] - origin).normalized;
            var degrees = Vector3.Angle(first, last);

            Assert.Greater(degrees, 60f,
                $"궤적이 {degrees:0}도뿐이면 §18.1 의 \"넓은\" 이 아니다.");

            // 실제 길이도 본다 — 각도만 넓고 반지름이 작으면 화면에서 좁다.
            var chord = (into[count - 1] - into[0]).magnitude;
            Assert.Greater(chord, PhysicsScale.MeteorRadius * 2f,
                "적어도 운석 지름보다는 넓어야 닿았다고 읽힌다.");
        }

        /// <summary>
        /// 모든 점이 플레이어에서 <see cref="PhysicsScale.BatArcRadius"/> 만큼 떨어져
        /// 있다. 스윙 동작과 <b>같은 함수</b>에서 나오므로 어긋날 수 없다.
        /// </summary>
        [Test]
        public void EveryArcPointSitsAtTheSwingRadius()
        {
            var into = new Vector3[PhysicsScale.BatArcSamples];
            var origin = new Vector3(3f, 1f, -2f);

            var count = BatSwingLook.ArcPoints(origin, Vector3.right, 0.7f, into);

            for (var i = 0; i < count; i++)
                Assert.AreEqual(PhysicsScale.BatArcRadius, (into[i] - origin).magnitude, 0.01f);
        }

        /// <summary>지나온 만큼만 그린다 — 앞으로 갈 곳은 빛나지 않는다.</summary>
        [Test]
        public void TheArcTrailsBehindTheSwing()
        {
            var into = new Vector3[PhysicsScale.BatArcSamples];

            BatSwingLook.ArcPoints(Vector3.zero, Vector3.forward, 0.5f, into);
            var head = into[PhysicsScale.BatArcSamples - 1];

            var atHalf = MeteorBat.SwingAim(Vector3.forward, 0.5f) * PhysicsScale.BatArcRadius;

            Assert.AreEqual(0f, (head - atHalf).magnitude, 0.01f,
                "마지막 점이 지금 방망이가 있는 자리여야 한다.");
        }

        [Test]
        public void AtTheStartOfTheSwing_TheTrailIsClamped()
        {
            var into = new Vector3[PhysicsScale.BatArcSamples];

            BatSwingLook.ArcPoints(Vector3.zero, Vector3.forward, 0.05f, into);

            var tail = MeteorBat.SwingAim(Vector3.forward, 0f) * PhysicsScale.BatArcRadius;

            Assert.AreEqual(0f, (into[0] - tail).magnitude, 0.01f,
                "0 아래로 내려가면 안 된다.");
        }

        [Test]
        public void TheArcIsHiddenWhenNotSwinging()
        {
            Assert.IsFalse(BatSwingLook.ShowsArc(false, 0.5f));
            Assert.IsFalse(BatSwingLook.ShowsArc(true, 0f));
            Assert.IsFalse(BatSwingLook.ShowsArc(true, 1f));
            Assert.IsTrue(BatSwingLook.ShowsArc(true, 0.5f));
        }

        // --- 타격 줄기 -------------------------------------------------------

        [Test]
        public void TheStreakFadesOutInItsTime()
        {
            Assert.AreEqual(1f, BatSwingLook.StreakFade(0f), 0.001f);
            Assert.AreEqual(0.5f, BatSwingLook.StreakFade(PhysicsScale.BatStreakSeconds * 0.5f),
                            0.01f);
            Assert.AreEqual(0f, BatSwingLook.StreakFade(PhysicsScale.BatStreakSeconds), 0.001f);
        }

        [Test]
        public void WithNoHit_ThereIsNoStreak()
        {
            Assert.AreEqual(0f, BatSwingLook.StreakFade(-1f),
                "헛스윙과 맞은 것이 같아 보이면 대기 1초의 이유를 알 수 없다.");
        }

        [Test]
        public void TheStreakOutlastsNoFrames()
        {
            Assert.AreEqual(0f, BatSwingLook.StreakFade(PhysicsScale.BatStreakSeconds + 1f));
        }

        // --- 리그 ------------------------------------------------------------

        MeteorBat MakeBat()
        {
            var go = new GameObject("운석 방망이");
            _spawned.Add(go);

            return go.AddComponent<MeteorBat>();
        }

        /// <summary>
        /// <paramref name="seconds"/> 뒤에 <paramref name="impact"/> 에 닿는 <b>대각선</b>
        /// 운석. 스포너를 거치지 않으므로 S15P21B202-274 와 무관하게 성립한다.
        /// </summary>
        Meteor MakeDiagonalMeteor(Vector3 impact, float seconds)
        {
            var gravity = -UnityEngine.Physics.gravity.y;

            // 수평은 등속, 수직은 h = v0*t + 0.5*g*t^2 다. 274 의 스포너와 같은 산술이다.
            var height = 12f;
            var fallSpeed = (height - 0.5f * gravity * seconds * seconds) / seconds;
            var sideways = new Vector3(8.4f * seconds, 0f, 0f);

            var go = new GameObject("대각선 운석");
            _spawned.Add(go);

            go.transform.position = impact + sideways + Vector3.up * height;

            var body = go.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.linearVelocity = -sideways / seconds + Vector3.down * fallSpeed;

            var meteor = go.AddComponent<Meteor>();
            meteor.Initialize();
            meteor.SetSize(MeteorSize.Small);
            meteor.SetGroundHeight(impact.y);

            return meteor;
        }
    }
}
