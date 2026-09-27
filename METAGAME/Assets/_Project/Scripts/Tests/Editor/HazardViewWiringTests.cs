using System.Collections.Generic;
using Metagame.Core;
using Metagame.Gameplay.Hazards;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 운석 표현이 <b>스스로 붙는지</b> 검증한다(S15P21B202-275).
    ///
    /// <b>세 이슈가 죽어 있었다.</b> S15P21B202-241 이 낙하 예고 원
    /// (<see cref="MeteorWarningView"/>)을, 251 이 분화구(<see cref="MeteorScarView"/>)를
    /// 만들고 268 이 줄어드는 원으로 바꿨지만, 그 컴포넌트가 씬에도 프리팹에도 붙어
    /// 있지 않았고 런타임에 붙이는 코드도 없었다 — <b>인게임에서 한 번도 실행된 적이
    /// 없다.</b>
    ///
    /// <b>기존 검증이 이것을 잡을 수 없었던 이유</b>는 뷰를 직접 만들어 붙이기
    /// 때문이다(<c>MeteorWarningTests.MakeView</c>). 그러면 "그리는 규칙" 은 잠기지만
    /// "게임에서 아무도 만들지 않는다" 는 늘 통과한다. 254 에서 예고가 안 보이는 원인을
    /// 지면 높이로 잡은 것도 같은 함정이었다 — 필요한 수정이었지만, 컴포넌트가 있는지는
    /// 확인하지 않았다.
    ///
    /// 그래서 이 스위트는 <b>규칙이 아니라 배선</b>을 본다. 세계에 실제로 있는지는
    /// <c>SpaceSelfTest.CheckHazardViews</c> 가 실행 중에 본다.
    /// </summary>
    public sealed class HazardViewWiringTests
    {
        const float Step = 0.02f;

        readonly List<GameObject> _spawned = new();

        [SetUp]
        public void SetUp() => Meteor.All.Clear();

        [TearDown]
        public void TearDown()
        {
            foreach (var meteor in Object.FindObjectsByType<Meteor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (meteor != null)
                    Object.DestroyImmediate(meteor.gameObject);

            foreach (var go in _spawned)
                if (go != null)
                    Object.DestroyImmediate(go);

            _spawned.Clear();
            Meteor.All.Clear();
        }

        // --- 붙는다 -----------------------------------------------------------

        [Test]
        public void ASpawnerWithNoViews_GetsTheWarningRing()
        {
            var spawner = MakeSpawner();

            Assert.IsNull(spawner.GetComponent<MeteorWarningView>(),
                "이 검증이 의미를 가지려면 처음에는 없어야 한다.");

            // 흔적 원반은 S15P21B202-653 에서 걷어냈다 — 이제 붙는 것은 예고 원뿐이다.
            Assert.AreEqual(1, spawner.EnsureViews());

            Assert.IsNotNull(spawner.GetComponent<MeteorWarningView>(),
                "낙하 예고 원이 없으면 방망이(§18)가 성립하지 않는다.");
        }

        [Test]
        public void CallingItTwice_AddsNothingMore()
        {
            var spawner = MakeSpawner();

            spawner.EnsureViews();

            Assert.AreEqual(0, spawner.EnsureViews(), "두 번 불러도 하나씩만 있어야 한다.");
            Assert.AreEqual(1, spawner.GetComponents<MeteorWarningView>().Length);
        }

        /// <summary>씬에서 이미 붙여 둔 것을 덮어쓰지 않는다.</summary>
        [Test]
        public void AnExistingViewIsKept()
        {
            var spawner = MakeSpawner();
            var existing = spawner.gameObject.AddComponent<MeteorWarningView>();

            Assert.AreEqual(0, spawner.EnsureViews(), "이미 있으므로 새로 붙을 것이 없다.");
            Assert.AreSame(existing, spawner.GetComponent<MeteorWarningView>());
        }

        // --- 붙은 것이 실제로 그린다 (이 이슈의 본체) --------------------------

        /// <summary>
        /// <b>손으로 붙이지 않고</b> 예고 표시가 나오는지 본다. 기존 검증은 뷰를 직접
        /// 만들어 붙였으므로 이 경로를 한 번도 지나지 않았다.
        /// </summary>
        [Test]
        public void TheWarningAppearsWithoutAnyManualSetup()
        {
            var spawner = MakeSpawner();
            spawner.EnsureViews();

            AdvanceUntilPending(spawner);

            var view = spawner.GetComponent<MeteorWarningView>();
            view.Tick(0f);

            Assert.AreEqual(spawner.Pending.Count, view.VisibleCount);
            Assert.Greater(view.VisibleCount, 0, "예고가 화면에 나와야 한다(§17.2).");
        }

        /// <summary>
        /// 착탄 자리에 <b>원반을 그리지 않는다</b>(S15P21B202-653).
        ///
        /// 색으로 흉내 낸 그을음이 회색 반투명 원반으로 보여서 걷어냈다. 그리는 것은
        /// 사라졌지만 <b>기록은 남아야 한다</b> — 그것이 운석 소멸 타이머이고,
        /// 없애면 박힌 돌이 영구히 쌓인다(S15P21B202-438).
        /// </summary>
        [Test]
        public void TheImpactLeavesNoDiscButStillRemembers()
        {
            var spawner = MakeSpawner();
            spawner.EnsureViews();

            var meteor = MakeMeteor(new Vector3(0f, 4f, 0f));
            AddToLive(spawner, meteor);
            meteor.ApplyImpact(Vector3.zero);
            spawner.Tick(Step);

            Assert.AreEqual(1, spawner.Scars.Count,
                "기록은 남아야 한다 — 이것이 소멸 타이머다.");

            foreach (var renderer in spawner.GetComponentsInChildren<Renderer>(true))
                Assert.IsFalse(renderer.gameObject.name.StartsWith("MeteorScar"),
                    $"착탄 흔적 원반이 그려졌다: {renderer.gameObject.name}");
        }

        /// <summary>
        /// 배선된 뷰가 <b>그 스포너</b>를 읽는가. <c>Configure</c> 없이도
        /// <c>FindAnyObjectByType</c> 으로 찾아지지만, 그 우연에 의존하면 스포너가 둘일
        /// 때 엉뚱한 쪽을 그린다.
        /// </summary>
        [Test]
        public void TheViewReadsItsOwnSpawner()
        {
            var quiet = MakeSpawner();     // 예고를 진행시키지 않는 쪽
            var busy = MakeSpawner();

            busy.EnsureViews();
            AdvanceUntilPending(busy);

            var view = busy.GetComponent<MeteorWarningView>();
            view.Tick(0f);

            Assert.AreEqual(0, quiet.Pending.Count, "조용한 쪽에는 예고가 없어야 한다.");
            Assert.AreEqual(busy.Pending.Count, view.VisibleCount);
        }

        // --- 지면 위에 그린다 -------------------------------------------------

        /// <summary>
        /// 표시가 <b>지면 위</b>에 있는가. S15P21B202-254 가 착탄 지점을 실제 지면
        /// 표면으로 내렸으므로, 거기서 <see cref="PhysicsScale.MeteorWarningLift"/> 만큼
        /// 띄우면 묻히지 않는다.
        ///
        /// 예전에는 스폰 영역 transform 의 y(0)를 썼는데 바닥 메시가 0.06 에 있어
        /// 0.03 을 띄워도 <b>바닥 아래</b>였다.
        /// </summary>
        [Test]
        public void TheWarningSitsAboveTheGroundSurface()
        {
            var ground = MakeGround(0.06f);

            var spawner = MakeSpawner();
            spawner.EnsureViews();
            AdvanceUntilPending(spawner);

            var view = spawner.GetComponent<MeteorWarningView>();
            view.Tick(0f);

            var marker = FirstMarker(view);

            Assert.Greater(marker.position.y, ground,
                "바닥 메시보다 위에 있어야 보인다.");
            Assert.AreEqual(ground + PhysicsScale.MeteorWarningLift, marker.position.y, 0.01f);
        }

        // --- 리그 ------------------------------------------------------------

        static Transform FirstMarker(MeteorWarningView view)
        {
            var container = view.transform.Find("MeteorWarnings");
            Assert.IsNotNull(container, "표시를 담는 자리가 있어야 한다.");
            Assert.Greater(container.childCount, 0, "표시가 하나는 있어야 한다.");
            return container.GetChild(0);
        }

        static void AdvanceUntilPending(MeteorSpawner spawner)
        {
            var guard = 0;
            while (spawner.Pending.Count == 0 && guard++ < 200000)
                spawner.Tick(Tuning.FixedStep);

            Assert.Greater(spawner.Pending.Count, 0, "예고가 떠야 한다.");
        }

        static void AddToLive(MeteorSpawner spawner, Meteor meteor)
        {
            var field = typeof(MeteorSpawner).GetField(
                "_live", System.Reflection.BindingFlags.Instance
                         | System.Reflection.BindingFlags.NonPublic);

            ((List<Meteor>)field.GetValue(spawner)).Add(meteor);
        }

        /// <summary>씬의 <c>ENV_Ground_Arena_50m</c> 처럼 y 가 0 이 아닌 바닥.</summary>
        float MakeGround(float y)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _spawned.Add(go);

            go.name = "바닥";
            go.transform.position = new Vector3(0f, y - 0.5f, 0f);
            go.transform.localScale = new Vector3(200f, 1f, 200f);

            UnityEngine.Physics.SyncTransforms();
            return y;
        }

        MeteorSpawner MakeSpawner()
        {
            var go = new GameObject("MeteorSpawner");
            _spawned.Add(go);

            var spawner = go.AddComponent<MeteorSpawner>();
            spawner.Configure(12345);
            return spawner;
        }

        Meteor MakeMeteor(Vector3 position)
        {
            var go = new GameObject("운석");
            _spawned.Add(go);

            go.transform.position = position;

            var meteor = go.AddComponent<Meteor>();
            meteor.Initialize();
            meteor.SetSize(MeteorSize.Small);
            return meteor;
        }
    }
}
