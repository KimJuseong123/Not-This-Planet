using System.Collections.Generic;
using Metagame.Gameplay.Hazards;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 공세가 시작되면 프레임이 떨어지던 것 (S15P21B202-684).
    ///
    /// <para><b>무엇이 일어나나.</b> 공세를 켜면 낙하 시계가 1개에서 8개가 된다(기획
    /// §17.8). 시계마다 1~5초를 다시 뽑으므로 평균 초당 2.7발이고, 돌이 몇 초를
    /// 떨어지므로 <b>동시에 떠 있는 수가 1~2개에서 열 개쯤</b>으로 늘어난다.
    /// 2026-09-22 QA 로그의 <c>[NETDIAG] live</c> 가 <c>t=91</c> 에 3 에서 9 로 뛰었고,
    /// 같은 구간에서 호스트 119→89fps · 참가자 40→24fps 였다.</para>
    ///
    /// <para><b>스폰 빈도는 기획 값이므로 건드리지 않는다.</b> 줄일 수 있는 것은 돌
    /// 하나당 드는 비용과, <b>그 비용이 한 프레임에 몰리는 것</b>이다.</para>
    ///
    /// <para><b>이 검사가 고정하지 못하는 것.</b> 실제 프레임 개선치는 여기서 잴 수
    /// 없다 — 페어 호스트/참가자 <c>[NETDIAG] fps</c> 로 다음 QA 에서 잰다. 여기서
    /// 지키는 것은 "다시 쓴다 · 계층을 다시 훑지 않는다 · 한 프레임에 몰지 않는다"
    /// 세 가지 배선이고, 그것들이 조용히 빠지는 것을 막는다.</para>
    /// </summary>
    public sealed class AssaultFrameCostTests
    {
        readonly List<GameObject> _objects = new();

        [TearDown]
        public void Cleanup()
        {
            // **비활성까지 쓸어야 한다** — 세워 둔 그림은 꺼져 있어서
            // `FindObjectsInactive.Exclude` 로는 보이지 않는다(그래서 다음 검사로 샌다).
            foreach (var meteor in Object.FindObjectsByType<Meteor>(FindObjectsInactive.Include,
                                                                    FindObjectsSortMode.None))
                if (meteor != null) Object.DestroyImmediate(meteor.gameObject);

            foreach (var go in _objects) if (go != null) Object.DestroyImmediate(go);
            _objects.Clear();
            Meteor.All.Clear();
        }

        MeteorSpawner Spawner()
        {
            var go = new GameObject("spawner");
            _objects.Add(go);
            var spawner = go.AddComponent<MeteorSpawner>();
            spawner.Configure(661);
            return spawner;
        }

        static List<MeteorSpawner.NetworkMeteorPose> Poses(int count)
        {
            var poses = new List<MeteorSpawner.NetworkMeteorPose>(count);
            for (var i = 0; i < count; i++)
                poses.Add(new MeteorSpawner.NetworkMeteorPose
                {
                    Position = new Vector3(i, 20f + i, 0f),
                    Rotation = Quaternion.identity,
                    Velocity = Vector3.down * 10f,
                });

            return poses;
        }

        static int StonesInScene() =>
            Object.FindObjectsByType<Meteor>(FindObjectsInactive.Include,
                                             FindObjectsSortMode.None).Length;

        // ── 다시 쓴다 ─────────────────────────────────────────────────────

        /// <summary>
        /// 줄었다 늘면 <b>같은 돌이 돌아온다</b>.
        ///
        /// <para>공세 내내 개수가 오르내리므로, 지웠다 만들면 초당 두세 번의
        /// <c>Instantiate</c>/<c>Destroy</c> 가 프레임에 실린다. 돌은 서로 구별되지
        /// 않으니(<c>ApplyNetworkMeteors</c> 가 번호로 짝짓지 않는 이유가 그것이다)
        /// 다시 쓰면 된다.</para>
        /// </summary>
        [Test]
        public void ShrinkingAndGrowingAgainReusesTheSameStones()
        {
            var spawner = Spawner();

            spawner.ApplyNetworkMeteors(Poses(6));

            // **id 가 아니라 객체로 견준다.** `GetInstanceID` 는 이 Unity 버전에서
            // obsolete 고(경고를 오류로 다루므로 컴파일이 안 된다), "같은 돌이 돌아왔나" 는
            // 객체 같음으로 바로 말할 수 있다 — 버전에 딸리지 않는다.
            var first = new List<Meteor>();
            for (var i = 0; i < 6; i++) first.Add(spawner.Ghosts[i]);
            Assert.AreEqual(6, StonesInScene(), "그림 여섯 개면 돌도 여섯 개다");

            spawner.ApplyNetworkMeteors(Poses(0));
            Assert.AreEqual(0, spawner.Ghosts.Count);
            Assert.AreEqual(6, StonesInScene(),
                "줄어든 그림을 지웠다 — 공세 내내 만들고 지우기가 반복된다");

            spawner.ApplyNetworkMeteors(Poses(6));
            var again = new List<Meteor>();
            for (var i = 0; i < 6; i++) again.Add(spawner.Ghosts[i]);

            CollectionAssert.AreEquivalent(first, again,
                "세워 둔 것을 쓰지 않고 새로 만들었다");
            Assert.AreEqual(6, StonesInScene(), "돌이 열두 개가 됐다 — 다시 쓰지 않았다");
        }

        /// <summary>
        /// <b>세워 둔 것은 <c>live</c> 에 세지 않는다.</b>
        ///
        /// <para><c>NetDiagnostics</c> 가 <c>live</c> 를 <c>Live.Count + Ghosts.Count</c>
        /// 로 찍는다. 세워 둔 것까지 세면 참가자 쪽 <c>live</c> 가 호스트보다 커져
        /// <b>세계가 어긋난 것처럼 보인다</b> — 그 수치로 QA 를 판정하기로 했으므로
        /// (<c>-654</c>) 이 함정이 가장 비싸다.</para>
        /// </summary>
        [Test]
        public void ParkedStonesAreInvisibleToTheDiagnosticsAndToMeteorAll()
        {
            var spawner = Spawner();
            spawner.ApplyNetworkMeteors(Poses(3));
            var parked = spawner.Ghosts[2];

            spawner.ApplyNetworkMeteors(Poses(0));

            Assert.AreEqual(0, spawner.Ghosts.Count, "세워 둔 것이 live 로 새어 나간다");
            Assert.IsFalse(parked.gameObject.activeSelf,
                "꺼 두지 않았다 — 안 보이는 돌이 그려지고 물리를 밟는다");
            Assert.IsFalse(Meteor.All.Contains(parked),
                "Meteor.All 에 남았다 — 남의 순회가 세워 둔 돌을 집는다");
        }

        /// <summary>
        /// 꺼냈으면 <b>설정을 다시 세운다</b> — 지난 쓰임을 물고 오지 않는다.
        ///
        /// <para>다시 쓰는 것의 값은 <c>Instantiate</c> 를 아끼는 것이고, 값은
        /// "꺼낸 돌이 새로 만든 돌과 구별되지 않는다" 일 때만 유효하다.</para>
        /// </summary>
        [Test]
        public void AReusedStoneLooksExactlyLikeAFreshOne()
        {
            var spawner = Spawner();
            spawner.ApplyNetworkMeteors(Poses(1));
            spawner.ApplyNetworkMeteors(Poses(0));
            spawner.ApplyNetworkMeteors(Poses(1));

            var ghost = spawner.Ghosts[0];
            Assert.IsTrue(ghost.gameObject.activeSelf, "꺼낸 돌을 켜지 않았다 — 안 보인다");
            Assert.IsFalse(ghost.enabled,
                "Meteor 컴포넌트가 살아 있다 — 참가자 기계가 착탄을 스스로 판정한다");

            foreach (var collider in ghost.Colliders)
                Assert.IsFalse(collider.enabled,
                    "낙하 그림의 콜라이더가 켜져 있다 — 낡은 자리에서 참가자만 막힌다");

            var body = ghost.Body;
            Assert.IsFalse(body.isKinematic,
                "키네마틱으로 남았다 — ApplyNetworkMeteors 의 속도 대입이 거부된다");
            Assert.IsTrue(body.useGravity);
            Assert.IsFalse(body.detectCollisions);
        }

        /// <summary>
        /// <b>흔적 그림은 이 풀에서 나오지 않는다.</b>
        ///
        /// <para>흔적은 콜라이더를 켜고 레이어를 <c>Prop</c> 으로 옮기고 kinematic 으로
        /// 세운다(S15P21B202-658). 낙하 그림과 한 풀에 섞으면 꺼낸 돌이 지난 쓰임의
        /// 설정을 물고 와서, <c>-658</c> 이 고친 "참가자가 돌을 통과한다" 가
        /// <b>확률적으로</b> 되돌아온다 — 그런 것은 QA 에서 못 잡는다.</para>
        /// </summary>
        [Test]
        public void ScarStonesNeverComeOutOfTheFallingPool()
        {
            var spawner = Spawner();
            spawner.ApplyNetworkMeteors(Poses(1));
            var parked = spawner.Ghosts[0];
            spawner.ApplyNetworkMeteors(Poses(0));

            spawner.ApplyNetworkScars(new List<MeteorSpawner.NetworkScarState>
            {
                new MeteorSpawner.NetworkScarState
                {
                    Id = 7, Position = Vector3.zero, Rotation = Quaternion.identity,
                },
            }, full: true);

            Assert.IsTrue(spawner.TryGetScarGhost(7, out var scar), "흔적 그림이 없다");
            Assert.AreNotEqual(parked, scar,
                "세워 둔 낙하 그림을 흔적으로 꺼냈다 — 콜라이더가 꺼진 흔적이 된다");

            foreach (var collider in scar.Colliders)
                Assert.IsTrue(collider.enabled,
                    "흔적의 콜라이더가 꺼져 있다 — 참가자가 착지한 돌을 통과한다(-658)");
            Assert.IsTrue(scar.Body.isKinematic);
            Assert.AreEqual(GameLayers.Prop, scar.gameObject.layer);

            // 낙하 그림 쪽은 그대로 남아 있다 — 흔적이 훔쳐 가지 않았다.
            spawner.ApplyNetworkMeteors(Poses(1));
            Assert.AreEqual(parked, spawner.Ghosts[0]);
        }

        /// <summary>매치가 끝나면 <b>세워 둔 것까지</b> 지운다.</summary>
        [Test]
        public void ClearingAlsoThrowsAwayWhatWasParked()
        {
            var spawner = Spawner();
            spawner.ApplyNetworkMeteors(Poses(4));
            spawner.ApplyNetworkMeteors(Poses(1));   // 세 개가 세워진다

            spawner.ClearNetworkMeteors();

            Assert.AreEqual(0, spawner.Ghosts.Count);
            Assert.AreEqual(0, StonesInScene(),
                "\"씬에 남겨 두지 않는다\" 는 계약인데 꺼진 돌이 남았다");
        }

        /// <summary>
        /// 세워 두는 수에 <b>상한이 있다</b>. 쥐고 있어 봐야 다시 쓰이지 않는다.
        ///
        /// <para>동시에 떠 있는 수의 상한이 16 이므로(<c>_maxIndependentLiveMeteors</c>)
        /// 그보다 많이 쥐고 있을 이유가 없다.</para>
        /// </summary>
        [Test]
        public void TheParkDoesNotGrowWithoutBound()
        {
            var spawner = Spawner();
            spawner.ApplyNetworkMeteors(Poses(20));
            spawner.ApplyNetworkMeteors(Poses(0));

            Assert.That(StonesInScene(), Is.LessThanOrEqualTo(16),
                "세워 둔 돌이 상한 없이 쌓인다");
        }

        // ── 한 프레임에 몰지 않는다 ────────────────────────────────────────

        /// <summary>
        /// 프레임이 길어져 시계 여덟 개가 함께 만료돼도 <b>그 프레임에 여덟 발을 내지
        /// 않는다</b>.
        ///
        /// <para><c>MeteorDropSchedule.Tick</c> 은 <b>한 레인 안에서</b> 밀린 낙하를 몰아
        /// 내지 않게 막는다. 그런데 프레임이 길면 <b>여러 레인이 같은 틱에</b> 만료되고,
        /// 그러면 이미 렉이 난 프레임에 여덟 번의 <c>Instantiate</c> 와 예고와 네트워크
        /// 묶음이 함께 실린다 — <b>렉이 다음 렉을 만든다</b>.</para>
        ///
        /// <para><b>버리지 않는다.</b> 버리면 긴 프레임이 낙하 수를 줄여, 기획 값이
        /// 프레임률에 따라 달라진다.</para>
        /// </summary>
        [Test]
        public void ALongFrameSpreadsTheLaunchesInsteadOfDroppingOrBunchingThem()
        {
            var spawner = Spawner();
            spawner.EnableIndependentDrops();
            spawner.BeginAssault();

            var perFrame = 0;
            var total = 0;
            spawner.DropScheduled += _ => { perFrame++; total++; };

            // 5초가 넘는 한 프레임 — 시계 여덟 개가 모두 만료된다(due == 255).
            spawner.Tick(5.1f);
            Assert.That(perFrame, Is.GreaterThan(0), "한 발도 안 나왔다");
            Assert.That(perFrame, Is.LessThanOrEqualTo(2),
                "긴 프레임에 여덟 발이 몰렸다 — 그 프레임이 곧 다음 렉이다");

            for (var i = 0; i < 8; i++)
            {
                perFrame = 0;
                spawner.Tick(0.001f);
                Assert.That(perFrame, Is.LessThanOrEqualTo(2),
                    "미룬 낙하가 한 프레임에 몰려 나왔다");
            }

            Assert.AreEqual(8, total,
                "미룬 낙하가 사라지거나 두 번 나왔다 — 긴 프레임이 낙하 수를 바꾼다");
            Assert.AreEqual(total, spawner.Live.Count, "예고만 나가고 돌이 안 나왔다");
        }

        /// <summary>
        /// 정상 진행에서는 <b>이 상한에 걸리지 않는다</b> — 낙하 빈도가 그대로다.
        ///
        /// <para>시계 여덟 개가 1~5초를 뽑으므로 평균 초당 2.7발이고, 120fps 면 프레임당
        /// 0.02발이다. 상한이 낙하 빈도를 깎으면 <b>기획 값이 프레임률에 딸린다</b>.</para>
        ///
        /// <para><b>수를 눈대중으로 보지 않는다.</b> 같은 시드의 시계를 따로 돌려 만료된
        /// 레인 수를 세고, 그 수와 <b>정확히 같은지</b> 본다 — "충분히 많다" 는 상한이
        /// 반쯤 깎아도 통과한다. 창은 3초로 잡는다. 동시에 떠 있는 수의 상한(16)에
        /// 닿으면 그쪽이 낙하를 버려서 이 비교가 뜻을 잃는다.</para>
        /// </summary>
        [Test]
        public void TheFrameCapDoesNotChangeHowOftenStonesFall()
        {
            var spawner = Spawner();
            spawner.EnableIndependentDrops();
            spawner.BeginAssault();   // 시계를 시드로 다시 뽑는다

            var total = 0;
            spawner.DropScheduled += _ => total++;

            // 스포너가 쥔 것과 같은 상태의 시계. Configure(661) → Reset(661) 이다.
            var mirror = new MeteorDropSchedule(661);
            var expected = 0;

            const float Step = 1f / 120f;
            for (var i = 0; i < 360; i++)
            {
                var due = mirror.Tick(Step, MeteorDropSchedule.SpawnerCount);
                for (var lane = 0; lane < MeteorDropSchedule.SpawnerCount; lane++)
                    if ((due & (1 << lane)) != 0) expected++;

                spawner.Tick(Step);
            }

            Assert.That(expected, Is.GreaterThan(0), "3초에 만료된 시계가 없다 — 창이 너무 짧다");
            Assert.That(expected, Is.LessThan(16), "동시 상한에 닿았다 — 이 비교가 뜻을 잃는다");
            Assert.AreEqual(expected, total,
                "프레임 상한이 낙하 빈도를 바꿨다 — 기획 값이 프레임률에 딸린다");
        }

        // ── 배선 ──────────────────────────────────────────────────────────

        /// <summary>
        /// 스폰 경로가 <b>계층을 훑지 않고 강체를 다시 찾지 않는다</b>.
        ///
        /// <para>값으로는 볼 수 없다 — 두 방식 모두 같은 결과를 내고, 다른 것은 프레임에
        /// 실리는 비용뿐이다. 흔적 쪽은 <c>!321</c> AI 리뷰 때 이미 <c>Meteor.Body</c> 로
        /// 고쳐졌는데 <b>낙하 그림 쪽에는 같은 결함이 남아 있었다</b> — 한 문제를 두 곳이
        /// 다르게 푼 모양이고, 이 검사가 그것을 다시 갈라지지 않게 막는다.</para>
        /// </summary>
        [Test]
        public void TheSpawnPathDoesNotWalkTheHierarchyOrSearchForTheBody()
        {
            var src = TestSource.Read("Runtime/Gameplay/Hazards/MeteorSpawner.cs");

            var create = TestSource.Method(src, "Meteor CreateGhost(");
            Assert.That(create, Does.Not.Contain("GetComponentsInChildren"),
                "그림을 만들 때마다 계층을 훑는다 — Meteor.Colliders 가 한 번만 구해 준다");
            Assert.That(create, Does.Not.Contain("GetComponent<"),
                "그림을 만들 때마다 강체를 다시 찾는다 — Meteor.Body 가 쥐고 있다");

            var apply = TestSource.Method(src, "public void ApplyNetworkMeteors(");
            Assert.That(apply, Does.Not.Contain("GetComponent"),
                "매 묶음마다 그림 전부에서 강체를 찾는다 — 공세 때 초당 수백 번이 된다");

            // 흔적 쪽이 먼저 옳았다. 그 상태가 유지되는지도 함께 본다.
            var scars = TestSource.Method(src, "public void ApplyNetworkScars(");
            Assert.That(scars, Does.Not.Contain("GetComponent"),
                "흔적 쪽이 되돌아갔다 (`!321` AI 리뷰)");
        }
    }
}
