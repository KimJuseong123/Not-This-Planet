using System.Collections.Generic;
using Metagame.Core;
using Metagame.Gameplay.Hazards;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 공세 강도 프리셋, 충격파 반경 배율, 플레이어 주변 집중을 검증한다
    /// (치트 시험장에서 "혼자 공세를 피하며 중앙까지" 를 재기 위한 세 가지).
    ///
    /// <para><b>왜 이 셋을 같이 본다.</b> 셋은 한 화면에서 같이 움직인다 — 강도를 고르면
    /// 반경과 목표 비율이 함께 바뀐다. 따로 검증하면 "프리셋은 맞는데 반경이 예고 원과
    /// 다르다" 같은 어긋남을 못 잡는다.</para>
    /// </summary>
    public sealed class MeteorWavePresetTests
    {
        readonly List<GameObject> _spawned = new();

        [SetUp]
        public void SetUp() => PlayerRig.All.Clear();

        [TearDown]
        public void TearDown()
        {
            foreach (var meteor in Object.FindObjectsByType<Meteor>(FindObjectsInactive.Exclude))
                if (meteor != null) Object.DestroyImmediate(meteor.gameObject);

            foreach (var go in _spawned)
                if (go != null) Object.DestroyImmediate(go);

            _spawned.Clear();
            PlayerRig.All.Clear();
        }

        // ── 프리셋 ───────────────────────────────────────────────────────────

        [Test]
        public void Presets_GetStrongerInOrder()
        {
            var all = MeteorWavePreset.All;
            Assert.GreaterOrEqual(all.Length, 3, "고를 것이 최소 셋은 있어야 한다.");

            for (var i = 1; i < all.Length; i++)
            {
                Assert.LessOrEqual(all[i].WaveIntervalSeconds, all[i - 1].WaveIntervalSeconds,
                    $"{all[i].Name} 은 {all[i - 1].Name} 보다 자주 와야 한다.");
                Assert.GreaterOrEqual(all[i].MaxMeteors, all[i - 1].MaxMeteors,
                    $"{all[i].Name} 은 {all[i - 1].Name} 보다 많이 와야 한다.");
                Assert.GreaterOrEqual(all[i].ImpactRadiusScale, all[i - 1].ImpactRadiusScale,
                    $"{all[i].Name} 의 충격파가 더 좁으면 안 된다.");
                Assert.GreaterOrEqual(all[i].NearPlayerWeight, all[i - 1].NearPlayerWeight,
                    $"{all[i].Name} 이 플레이어를 덜 노리면 안 된다.");
            }
        }

        [Test]
        public void ApplyPreset_PinsIntensity_RegardlessOfElapsedTime()
        {
            var spawner = CreateSpawner();
            var preset = MeteorWavePreset.Find("후반");

            spawner.ApplyPreset(preset);
            var pinnedInterval = spawner.CurrentInterval;

            // 20분을 건너뛰어도 고정한 강도가 유지돼야 한다.
            spawner.SkipTo(20f * 60f);

            Assert.AreEqual(preset.WaveIntervalSeconds, pinnedInterval, 1e-4f);
            Assert.AreEqual(preset.WaveIntervalSeconds, spawner.CurrentInterval, 1e-4f,
                "강도를 고정했으면 경과 시간이 간격을 바꾸면 안 된다.");
            Assert.AreEqual(preset.MediumChance, spawner.MediumChanceAt(spawner.Elapsed), 1e-4f);
        }

        [Test]
        public void ApplyPreset_PinsCrewToFour_SoTheCountMatchesTheLabel()
        {
            var spawner = CreateSpawner();
            spawner.ApplyPreset(MeteorWavePreset.Find("후반"));

            spawner.SetCrew(1);

            Assert.AreEqual(4, spawner.Crew,
                "혼자 골라도 프리셋에 적힌 규모가 그대로 나와야 한다(§17.6 보정을 끈다).");
        }

        [Test]
        public void ClearPreset_ReturnsToTheTimeTable()
        {
            var spawner = CreateSpawner();
            spawner.ApplyPreset(MeteorWavePreset.Find("과부하"));
            spawner.ClearPreset();

            Assert.IsNull(spawner.ActivePreset);
            Assert.AreEqual(0, spawner.CrewOverride, "인원 강제도 함께 풀려야 한다.");
            Assert.AreEqual(spawner.IntervalAt(spawner.Elapsed), spawner.CurrentInterval, 1e-4f);
        }

        // ── 충격파 반경 ──────────────────────────────────────────────────────

        [Test]
        public void RadiusScale_AppliesToWarningAndMeteorAlike()
        {
            var spawner = CreateSpawner();
            var preset = MeteorWavePreset.Find("후반");
            spawner.ApplyPreset(preset);

            spawner.LaunchAt(Vector3.zero, MeteorSize.Small);

            var expected = PhysicsScale.MeteorSmallImpactRadius * preset.ImpactRadiusScale;
            Assert.AreEqual(expected, spawner.ScaledImpactRadiusFor(MeteorSize.Small), 1e-3f);
            Assert.AreEqual(expected, spawner.Pending[0].ImpactRadius, 1e-3f,
                "예고 원이 실제 충격 범위와 같아야 한다(기획 §17.2).");
            Assert.AreEqual(expected, spawner.Live[0].ImpactRadius, 1e-3f,
                "떨어지는 운석의 반경도 같아야 한다.");
        }

        [Test]
        public void RadiusScale_WidensBatReachByTheSameFactor()
        {
            var spawner = CreateSpawner();
            spawner.ApplyPreset(MeteorWavePreset.Find("과부하"));
            spawner.LaunchAt(Vector3.zero, MeteorSize.Small);

            var meteor = spawner.Live[0];
            var ratio = meteor.BatAssistRadius / meteor.ImpactRadius;
            var baseRatio = PhysicsScale.BatAssistRadius / PhysicsScale.MeteorSmallImpactRadius;

            Assert.AreEqual(baseRatio, ratio, 1e-3f,
                "반경만 넓어지면 예고 원 안인데 칠 수 없는 고리가 생긴다(기획 §18.1).");
        }

        [Test]
        public void HeavyZone_StaysHalfOfWhateverRadiusIsInUse()
        {
            var spawner = CreateSpawner();
            spawner.ApplyPreset(MeteorWavePreset.Find("중반"));

            var radius = spawner.ScaledImpactRadiusFor(MeteorSize.Small);
            var fraction = spawner.Tuning.HeavyImpactFraction;

            Assert.AreEqual(MeteorImpactTier.Heavy,
                MeteorImpact.TierFor(radius * fraction * 0.5f, radius, fraction),
                "안쪽은 크게 날아간다(§20.1).");
            Assert.AreEqual(MeteorImpactTier.Shockwave,
                MeteorImpact.TierFor(radius * 0.9f, radius, fraction),
                "바깥쪽은 밀리기만 한다(§20.1).");
            Assert.AreEqual(MeteorImpactTier.None,
                MeteorImpact.TierFor(radius * 1.1f, radius, fraction),
                "반경 밖은 아무 일도 없다.");
        }

        /// <summary>
        /// 시간대 표는 <b>아직 반경을 키우지 않는다</b>. 곡선은 프리셋에만 있고, 시험으로
        /// 정해 [Plan] 확인을 받은 뒤에야 이 표로 옮긴다. 그 전까지 스테이지는 §17.7 의
        /// 값 그대로여야 한다 — 이 검증이 실수로 켜지는 것을 막는다.
        /// </summary>
        [Test]
        public void WaveTable_DoesNotChangeRadiusYet()
        {
            foreach (var row in MeteorWaveTable.Rows)
                Assert.AreEqual(1f, row.ImpactRadiusScale, 1e-4f,
                    $"{row.FromMinutes}분 시간대가 기획 §17.7 의 반경을 바꾸고 있다.");
        }

        [Test]
        public void SavedScenesWithoutTheNewColumn_KeepFullRadius()
        {
            // 이 칸이 생기기 전에 저장된 씬은 0 으로 역직렬화된다. 그대로 쓰면 반경이 0 이
            // 되어 운석이 아무에게도 닿지 않는다.
            var band = new MeteorWaveBand(0f, 30f, 1, 2, 0f, 0f);

            Assert.AreEqual(1f, band.ImpactRadiusScale, 1e-4f);
        }

        // ── 플레이어 주변 집중 ───────────────────────────────────────────────

        [Test]
        public void NearPlayerWeight_LandsMostMeteorsBesideTheSoloPlayer()
        {
            var spawner = CreateSpawner();
            var player = CreatePlayerAt(new Vector3(20f, 0f, -14f));

            spawner.ApplyPreset(MeteorWavePreset.Find("후반"));
            spawner.OverrideNearPlayerWeight(0.9f);

            const int rounds = 60;
            var points = CollectImpactPoints(spawner, rounds);

            var near = 0;
            for (var i = 0; i < points.Count; i++)
                if (Flat(points[i], player.Position) <= Reach) near++;

            // 90% 를 걸었으니 80% 는 넘어야 한다. 두 가지가 이 값을 갉아먹은 적이 있다 —
            // 혼자일 때 연속 금지에 걸려 후보가 0 이 되던 것(절반에서 멈췄다)과, 거리
            // 검사가 근처 목표만 골라 탈락시키던 것(68%). 둘 다 이 검증이 잡는다.
            Assert.Greater(near, rounds * 0.8f,
                $"플레이어 주변 비율 90% 인데 {points.Count} 발 중 {near} 발만 근처에 떨어졌다.");
        }

        [Test]
        public void SoloPlayer_IsTargetableTwiceInARow()
        {
            var spawner = CreateSpawner();
            var player = CreatePlayerAt(Vector3.zero);

            spawner.ApplyPreset(MeteorWavePreset.Find("후반"));
            spawner.OverrideNearPlayerWeight(1f);

            var points = CollectImpactPoints(spawner, 2);

            Assert.AreEqual(2, points.Count);
            Assert.Less(Flat(points[0], player.Position), Reach, "첫 발은 근처여야 한다.");
            Assert.Less(Flat(points[1], player.Position), Reach,
                "혼자일 때 §17.5 의 연속 금지가 두 번째를 멀리 밀어내면 안 된다.");
        }

        [Test]
        public void NearPlayerWeight_LeavesTheRestProportional()
        {
            var spawner = CreateSpawner();

            spawner.OverrideNearPlayerWeight(0.8f);
            Assert.AreEqual(0.8f, spawner.NearPlayerWeight, 1e-4f);

            spawner.OverrideNearPlayerWeight(-1f);
            Assert.AreEqual(0.30f, spawner.NearPlayerWeight, 0.02f,
                "풀면 기획 §17.5 의 30% 로 돌아가야 한다.");
        }

        // ── 도우미 ───────────────────────────────────────────────────────────

        /// <summary>플레이어 주변 후보 고리(1~3m)에 여유를 더한 거리.</summary>
        static float Reach => PhysicsScale.MeteorPlayerTargetMaxRadius + 1.5f;

        static float Flat(Vector3 a, Vector3 b) =>
            new Vector2(a.x - b.x, a.z - b.z).magnitude;

        /// <summary>
        /// 공세를 실제로 돌려 예고된 착탄 지점을 모은다.
        ///
        /// <b>목표 선택 함수를 직접 부르지 않는다.</b> 재려는 것은 "공세를 돌렸을 때 내
        /// 주변에 떨어지는가" 이고, 연속 금지·거리 재시도·환경 대체가 모두 그 경로에
        /// 걸려 있다. 내부 함수를 부르면 그 셋을 건너뛰어 실제와 다른 것을 재게 된다.
        /// </summary>
        static List<Vector3> CollectImpactPoints(MeteorSpawner spawner, int count)
        {
            var points = new List<Vector3>();
            void OnScheduled(PendingMeteorDrop drop) => points.Add(drop.ImpactPoint);

            spawner.DropScheduled += OnScheduled;
            try
            {
                var guard = 0;
                while (points.Count < count && guard++ < 400000)
                    spawner.Tick(Tuning.FixedStep);
            }
            finally
            {
                spawner.DropScheduled -= OnScheduled;
            }

            return points;
        }

        MeteorSpawner CreateSpawner(int seed = 4242)
        {
            var go = new GameObject("MeteorSpawner");
            _spawned.Add(go);
            var spawner = go.AddComponent<MeteorSpawner>();
            spawner.Configure(seed);
            return spawner;
        }

        PlayerRig CreatePlayerAt(Vector3 foot)
        {
            var rig = PlayerRig.Create("시험", foot, Quaternion.identity,
                                       Color.white, Color.white, useModel: false);
            _spawned.Add(rig.gameObject);
            return rig;
        }
    }
}
