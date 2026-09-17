using System.Collections.Generic;
using Metagame.Core;
using Metagame.Gameplay.Hazards;
using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 운석이 <b>어디를</b> 노리는지 검증한다(S15P21B202-234, 기획 §17.5).
    ///
    /// 예전에는 사각 영역 안 균등 무작위였다. 넓은 맵에서 아무 데나 떨어지면 플레이어
    /// 근처에 올 일이 거의 없어 <b>운석이 방해가 되지 않는다</b> — §17.5 가 비율을
    /// 정해 둔 이유다.
    ///
    /// 지키려는 것은 넷이다.
    /// <list type="number">
    /// <item>비율이 §17.5 에 수렴한다.</item>
    /// <item>플레이어의 <b>정확한 위치</b>를 노리지 않는다 (1~3m 떨어진다).</item>
    /// <item>기절한 플레이어를 노리지 않는다.</item>
    /// <item>같은 자리를 연속으로 고르지 않는다.</item>
    /// </list>
    /// </summary>
    public sealed class MeteorTargetingTests
    {
        readonly List<GameObject> _spawned = new();

        [SetUp]
        public void SetUp() => ClearRegistries();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned)
                if (go != null)
                    Object.DestroyImmediate(go);

            _spawned.Clear();
            ClearRegistries();
        }

        static void ClearRegistries()
        {
            PlayerRig.All.Clear();
            Meteor.All.Clear();
            PartClamp.All.Clear();
            WeldBead.All.Clear();
            PartSocket.All.Clear();
        }

        // --- 표 자체 ------------------------------------------------------

        /// <summary>기획 §17.5 의 네 후보 비율이 표에 그대로 들어 있어야 한다.</summary>
        [Test]
        public void TheTargetTable_MatchesTheDesign()
        {
            Assert.AreEqual(0.35f, MeteorWaveTable.TargetWeights[0], 0.001f, "환경 35%");
            Assert.AreEqual(0.30f, MeteorWaveTable.TargetWeights[1], 0.001f, "플레이어 주변 30%");
            Assert.AreEqual(0.25f, MeteorWaveTable.TargetWeights[2], 0.001f, "작업 지역 25%");
            Assert.AreEqual(0.10f, MeteorWaveTable.TargetWeights[3], 0.001f, "발사 구역 10%");

            var sum = 0f;
            for (var i = 0; i < MeteorWaveTable.TargetWeights.Length; i++)
                sum += MeteorWaveTable.TargetWeights[i];

            Assert.AreEqual(1f, sum, 0.001f, "합이 1 이어야 한다.");
        }

        [Test]
        public void TargetKind_FollowsTheWeights()
        {
            Assert.AreEqual(MeteorTargetKind.Environment, MeteorWaveTable.TargetKindFor(0.0));
            Assert.AreEqual(MeteorTargetKind.Environment, MeteorWaveTable.TargetKindFor(0.34));
            Assert.AreEqual(MeteorTargetKind.NearPlayer, MeteorWaveTable.TargetKindFor(0.36));
            Assert.AreEqual(MeteorTargetKind.NearPlayer, MeteorWaveTable.TargetKindFor(0.64));
            Assert.AreEqual(MeteorTargetKind.WorkArea, MeteorWaveTable.TargetKindFor(0.66));
            Assert.AreEqual(MeteorTargetKind.WorkArea, MeteorWaveTable.TargetKindFor(0.89));
            Assert.AreEqual(MeteorTargetKind.LaunchPad, MeteorWaveTable.TargetKindFor(0.95));
            Assert.AreEqual(MeteorTargetKind.LaunchPad, MeteorWaveTable.TargetKindFor(1.0));
        }

        [TestCase(false, 1f, 3f)]
        [TestCase(true, 4f, 6f)]
        public void MixedRandomDropsIncludeAreaAndConfiguredPlayerRadius(bool assault, float min, float max)
        {
            var player = MakePlayer(Outside);
            var spawner = MakeSpawner(mixed: true);
            var settings = new UnityEditor.SerializedObject(spawner);
            settings.FindProperty("_playerTargetMinRadius").floatValue = min;
            settings.FindProperty("_playerTargetMaxRadius").floatValue = max;
            settings.ApplyModifiedPropertiesWithoutUndo();
            if (assault) spawner.BeginAssault();
            var near = 0;
            var environment = 0;
            foreach (var point in Collect(spawner, 200))
            {
                var offset = point - player.Position;
                offset.y = 0f;
                if (offset.magnitude <= max + 0.01f)
                {
                    Assert.GreaterOrEqual(offset.magnitude, min - 0.01f);
                    near++;
                }
                else
                {
                    Assert.That(point.x, Is.InRange(-AreaSize / 2f, AreaSize / 2f));
                    Assert.That(point.z, Is.InRange(-AreaSize / 2f, AreaSize / 2f));
                    environment++;
                }
            }
            Assert.Greater(near, 20);
            Assert.Greater(environment, 80);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void MixedRandomDropsFallBackToAreaWithoutEligiblePlayer(bool downed)
        {
            if (downed) MakePlayer(Outside).GoDown("검증");
            var spawner = MakeSpawner(mixed: true);
            foreach (var point in Collect(spawner, 50))
            {
                Assert.That(point.x, Is.InRange(-AreaSize / 2f, AreaSize / 2f));
                Assert.That(point.z, Is.InRange(-AreaSize / 2f, AreaSize / 2f));
            }
        }

        [Test]
        public void ZeroPlayerChanceKeepsAllDropsInsideArea()
        {
            MakePlayer(Outside);
            var spawner = MakeSpawner(mixed: true);
            var settings = new UnityEditor.SerializedObject(spawner);
            settings.FindProperty("_randomAreaNearPlayerChance").floatValue = 0f;
            settings.ApplyModifiedPropertiesWithoutUndo();
            foreach (var point in Collect(spawner, 50))
            {
                Assert.That(point.x, Is.InRange(-AreaSize / 2f, AreaSize / 2f));
                Assert.That(point.z, Is.InRange(-AreaSize / 2f, AreaSize / 2f));
            }
        }

        // --- 플레이어를 노린다 ----------------------------------------------

        /// <summary>
        /// 플레이어 근처가 <b>실제로</b> 나와야 한다. 예전 균등 무작위에서는 40x40m
        /// 영역에 3m 반경이라 확률이 1% 도 되지 않았다.
        /// </summary>
        [Test]
        public void SomeDropsLandNearThePlayer()
        {
            var player = MakePlayer(Outside);
            var spawner = MakeSpawner();

            var near = 0;
            foreach (var point in Collect(spawner, 200))
            {
                var offset = point - player.Position;
                offset.y = 0f;
                if (offset.magnitude <= PhysicsScale.MeteorPlayerTargetMaxRadius + 0.5f) near++;
            }

            // 기획은 30% 다. 다른 후보(작업 지역·발사대)가 없으면 환경으로 돌아가므로
            // 하한만 본다 — 균등 무작위로는 1% 도 안 나오던 값이다.
            Assert.Greater(near, 20, $"플레이어 주변이 200번 중 {near}번뿐이다.");
        }

        /// <summary>
        /// 기획 §17.5 "플레이어의 정확한 위치에 강제로 명중시키지 않는다".
        /// 피할 수 없는 공격은 §3.3 "실패는 대부분 복구 가능하게 만든다" 와 어긋난다.
        /// </summary>
        [Test]
        public void NoDropLandsExactlyOnThePlayer()
        {
            var player = MakePlayer(Outside);
            var spawner = MakeSpawner();

            foreach (var point in Collect(spawner, 200))
            {
                var offset = point - player.Position;
                offset.y = 0f;

                if (offset.magnitude > PhysicsScale.MeteorPlayerTargetMaxRadius + 0.5f) continue;

                Assert.GreaterOrEqual(offset.magnitude,
                    PhysicsScale.MeteorPlayerTargetMinRadius - 0.01f,
                    "플레이어 주변을 노릴 때는 최소 1m 는 떨어져야 한다.");
            }
        }

        /// <summary>기획 §17.5 "기절한 플레이어는 일반 운석 목표에서 제외한다".</summary>
        [Test]
        public void ADownedPlayer_IsNotTargeted()
        {
            var player = MakePlayer(Outside);
            player.GoDown("검증");
            Assert.IsTrue(player.Downed);

            var spawner = MakeSpawner();

            foreach (var point in Collect(spawner, 200))
            {
                var offset = point - player.Position;
                offset.y = 0f;

                Assert.Greater(offset.magnitude, PhysicsScale.MeteorPlayerTargetMaxRadius + 0.5f,
                    "쓰러진 사람을 계속 때리면 §20.4 의 구조가 성립하지 않는다.");
            }
        }

        // --- 작업 지역 ----------------------------------------------------

        /// <summary>
        /// 기획 §17.5 "고정못 제거·운반·용접 중인 작업 지역 주변 25%".
        ///
        /// "작업 중" 을 <b>당기고 있는 고정못</b>으로 읽었다.
        /// </summary>
        [Test]
        public void ClampBeingPulled_BecomesATarget()
        {
            var clamp = MakeClampBeingPulled(Outside);
            var spawner = MakeSpawner();

            var near = 0;
            foreach (var point in Collect(spawner, 200))
            {
                var offset = point - clamp.transform.position;
                offset.y = 0f;
                if (offset.magnitude <= PhysicsScale.MeteorPlayerTargetMaxRadius + 0.5f) near++;
            }

            Assert.Greater(near, 15, $"당기는 중인 고정못 주변이 200번 중 {near}번뿐이다.");
        }

        /// <summary>아무도 작업하지 않으면 그 후보는 환경으로 돌아간다.</summary>
        [Test]
        public void WithNoWorkArea_DropsStayInsideTheArea()
        {
            var spawner = MakeSpawner();

            foreach (var point in Collect(spawner, 200))
            {
                Assert.LessOrEqual(Mathf.Abs(point.x), AreaSize * 0.5f + 0.01f);
                Assert.LessOrEqual(Mathf.Abs(point.z), AreaSize * 0.5f + 0.01f);
            }
        }

        // --- 연속 금지 ----------------------------------------------------

        /// <summary>기획 §17.5 "동일한 플레이어나 위치를 연속 선택하지 않는다".</summary>
        [Test]
        public void TheSameSpot_IsNotChosenTwiceInARow()
        {
            MakePlayer(Outside);
            var spawner = MakeSpawner();

            var points = Collect(spawner, 200);

            for (var i = 1; i < points.Count; i++)
                Assert.GreaterOrEqual(Vector3.Distance(points[i], points[i - 1]),
                    PhysicsScale.MeteorTargetRepeatDistance - 0.01f,
                    $"{i}번째와 그 앞이 너무 가깝다.");
        }

        // --- 발사 구역 ----------------------------------------------------

        /// <summary>
        /// 기획 §17.5 "중앙 발사 구역 10% 도 우주선 중심이 아니라 <b>주변의 열린 지면</b>을
        /// 선택한다". 중심을 노리면 쌓아 둔 탑을 직접 때려, §22.4 의 "발사 하중으로
        /// 품질을 검증한다" 보다 운이 앞선다.
        /// </summary>
        [Test]
        public void TheLaunchPadCenter_IsNeverTargeted()
        {
            var pad = new GameObject("LaunchPad");
            pad.transform.position = Outside;
            _spawned.Add(pad);

            var spawner = MakeSpawner();
            spawner.SetLaunchPad(pad.transform);

            var onPadEdge = 0;
            foreach (var point in Collect(spawner, 200))
            {
                var offset = point - pad.transform.position;
                offset.y = 0f;
                var distance = offset.magnitude;

                if (distance > PhysicsScale.LaunchPadRadius + 0.01f) continue;

                Assert.Greater(distance, PhysicsScale.LaunchPadRadius * 0.5f,
                    "발사 구역을 노릴 때는 중심이 아니라 테두리여야 한다.");
                onPadEdge++;
            }

            Assert.Greater(onPadEdge, 5, $"발사 구역 주변이 200번 중 {onPadEdge}번뿐이다.");
        }

        // --- 리그 ---------------------------------------------------------

        const float AreaSize = 40f;

        /// <summary>
        /// 환경 후보가 절대 닿지 않는 자리.
        ///
        /// <b>대상을 영역 안에 두면 검증이 흐려진다.</b> 40x40m 안에서 균등 무작위로
        /// 200번 뽑으면 반경 3.5m 안에 5번쯤은 우연히 들어온다 — 그것과 "노려서 떨어진
        /// 것" 을 구분할 수 없다. 영역 밖에 두면 그 자리에 떨어진 것은 <b>노린 것뿐</b>이다.
        /// </summary>
        static Vector3 Outside => new(AreaSize, 0f, AreaSize);

        MeteorSpawner MakeSpawner(int seed = 20260901, bool mixed = false)
        {
            var go = new GameObject("MeteorSpawner");
            go.transform.position = Vector3.zero;
            _spawned.Add(go);

            var spawner = go.AddComponent<MeteorSpawner>();
            var settings = new UnityEditor.SerializedObject(spawner);
            settings.FindProperty("_randomAreaTargets").boolValue = mixed;
            settings.ApplyModifiedPropertiesWithoutUndo();
            spawner.Configure(seed);
            spawner.SetArea(go.transform, new Vector2(AreaSize, AreaSize));
            spawner.SetCrew(4);
            return spawner;
        }

        /// <summary>예고된 착탄 지점을 <paramref name="count"/> 개 모은다.</summary>
        static List<Vector3> Collect(MeteorSpawner spawner, int count)
        {
            var points = new List<Vector3>();
            void OnScheduled(PendingMeteorDrop drop) => points.Add(drop.ImpactPoint);

            spawner.DropScheduled += OnScheduled;

            var guard = 0;
            while (points.Count < count && guard++ < 4000000) spawner.Tick(Tuning.FixedStep);

            spawner.DropScheduled -= OnScheduled;

            Assert.GreaterOrEqual(points.Count, count, "표본을 다 모으지 못했다.");
            return points.GetRange(0, count);
        }

        PlayerRig MakePlayer(Vector3 foot)
        {
            LogAssert.ignoreFailingMessages = true;
            var player = PlayerRig.Create("테스트", foot, Quaternion.identity,
                                          Color.white, Color.gray, false);
            _spawned.Add(player.gameObject);
            UnityEngine.Physics.SyncTransforms();
            return player;
        }

        /// <summary>당기는 중인 고정못 하나. 파츠에 달려 있어야 초기화가 된다.</summary>
        PartClamp MakeClampBeingPulled(Vector3 position)
        {
            var partGo = new GameObject("Part") { layer = GameLayers.Part };
            partGo.transform.position = position;
            partGo.AddComponent<BoxCollider>();
            partGo.AddComponent<Rigidbody>();
            _spawned.Add(partGo);

            var part = partGo.AddComponent<SpaceshipPart>();

            var clampGo = new GameObject("Clamp") { layer = GameLayers.Interactable };
            clampGo.transform.SetParent(partGo.transform, false);
            clampGo.AddComponent<SphereCollider>().isTrigger = true;

            var clamp = clampGo.AddComponent<PartClamp>();
            part.Initialize();
            part.RefreshClamps();
            clamp.Initialize();

            Assert.IsTrue(clamp.TryJoinPull(), "당기기가 시작돼야 한다.");
            Assert.Greater(clamp.Pullers, 0);

            UnityEngine.Physics.SyncTransforms();
            return clamp;
        }
    }
}
