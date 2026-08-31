using System.Collections.Generic;
using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>탐험 파츠의 결정적 정렬, 근접 발견, 태블릿 상태를 검증한다.</summary>
    public sealed class Stage1ExplorationTests
    {
        readonly List<GameObject> _spawned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned)
                if (go != null)
                    Object.DestroyImmediate(go);

            _spawned.Clear();
        }

        [Test]
        public void Configure_OrdersScenePartsByPartNumber()
        {
            var part2 = CreatePart("Part_2", new Vector3(2f, 0f, 0f));
            var part1 = CreatePart("Part_1", new Vector3(1f, 0f, 0f));
            var part4 = CreatePart("Part_4", new Vector3(4f, 0f, 0f));
            var part3 = CreatePart("Part_3", new Vector3(3f, 0f, 0f));

            var exploration = CreateExploration();
            exploration.Configure(new[] { part3, part1, part4, part2 });

            Assert.AreEqual("SF_Stage1_Spaceship_HoverPod", exploration.Parts[0].AssetName);
            Assert.AreSame(part1, exploration.Parts[0].Part);
            Assert.AreSame(part2, exploration.Parts[1].Part);
            Assert.AreSame(part3, exploration.Parts[2].Part);
            Assert.AreSame(part4, exploration.Parts[3].Part);
        }

        [Test]
        public void DiscoverAt_FindsOnlyPartsInsideHorizontalRadius()
        {
            var near = CreatePart("Part_1", new Vector3(2f, 30f, 2f));
            var far = CreatePart("Part_2", new Vector3(5f, 0f, 0f));

            var exploration = CreateExploration();
            exploration.Configure(new[] { near, far });

            var discovered = exploration.DiscoverAt(new Vector3(0f, -100f, 0f));

            Assert.AreEqual(1, discovered);
            Assert.AreEqual(1, exploration.DiscoveredCount);
            Assert.IsTrue(exploration.Parts[0].IsDiscovered);
            Assert.IsFalse(exploration.Parts[1].IsDiscovered);
        }

        [Test]
        public void DiscoveredPart_RemainsDiscoveredAfterItMoves()
        {
            var part = CreatePart("Part_1", Vector3.zero);
            var exploration = CreateExploration();
            exploration.Configure(new[] { part });

            Assert.IsTrue(exploration.TryDiscover(part));
            part.transform.position = Vector3.one * 50f;

            exploration.DiscoverAt(Vector3.zero);

            Assert.AreEqual(1, exploration.DiscoveredCount);
            Assert.IsTrue(exploration.Parts[0].IsDiscovered);
        }

        [Test]
        public void ResetRun_ClearsDiscoveryAndTabletState()
        {
            var part = CreatePart("Part_1", Vector3.zero);
            var exploration = CreateExploration();
            exploration.Configure(new[] { part });
            exploration.TryDiscover(part);
            exploration.SetTabletOpen(true);
            exploration.NextTabletPage();

            exploration.ResetRun();

            Assert.AreEqual(0, exploration.DiscoveredCount);
            Assert.IsFalse(exploration.Parts[0].IsDiscovered);
            Assert.IsFalse(exploration.TabletOpen);
            Assert.AreEqual(0, exploration.TabletPage);
        }

        [Test]
        public void ToggleTablet_RaisesStateChangeOncePerTransition()
        {
            var exploration = CreateExploration();
            exploration.Configure(null);

            var changes = 0;
            exploration.TabletStateChanged += _ => changes++;

            exploration.ToggleTablet();
            exploration.ToggleTablet();

            Assert.AreEqual(2, changes);
            Assert.IsFalse(exploration.TabletOpen);
        }

        [Test]
        public void TabletPageNavigation_ClampsAtBothEndsAndRaisesOnlyOnChange()
        {
            var exploration = CreateExploration();
            exploration.Configure(null);

            var changes = 0;
            exploration.TabletPageChanged += _ => changes++;

            Assert.AreEqual(0, exploration.TabletPage);
            exploration.PreviousTabletPage();
            Assert.AreEqual(0, exploration.TabletPage);
            Assert.AreEqual(0, changes);

            for (var i = 0; i < Stage1Exploration.TabletPageCount + 1; i++)
                exploration.NextTabletPage();

            Assert.AreEqual(Stage1Exploration.TabletPageCount - 1, exploration.TabletPage);
            Assert.AreEqual(Stage1Exploration.TabletPageCount - 1, changes);

            exploration.NextTabletPage();
            Assert.AreEqual(Stage1Exploration.TabletPageCount - 1, exploration.TabletPage);
            Assert.AreEqual(Stage1Exploration.TabletPageCount - 1, changes);

            exploration.PreviousTabletPage();
            Assert.AreEqual(Stage1Exploration.TabletPageCount - 2, exploration.TabletPage);
            Assert.AreEqual(Stage1Exploration.TabletPageCount, changes);
        }

        [Test]
        public void PlayerHealth_RemainsNumericForGameplayWhileHealthRatioTracksDamage()
        {
            var go = new GameObject("PlayerRig");
            _spawned.Add(go);
            var player = go.AddComponent<PlayerRig>();

            player.ResetHealth();
            Assert.AreEqual(player.MaxHealth, player.Health, 0.001f);
            Assert.AreEqual(1f, player.Health01, 0.001f);

            player.ApplyDamage(25f);
            Assert.AreEqual(75f, player.Health, 0.001f);
            Assert.AreEqual(0.75f, player.Health01, 0.001f);

            player.ApplyDamage(float.NaN);
            player.ApplyDamage(float.PositiveInfinity);
            player.ApplyDamage(-10f);
            Assert.AreEqual(75f, player.Health, 0.001f);

            player.ResetHealth();
            Assert.AreEqual(player.MaxHealth, player.Health, 0.001f);
            Assert.AreEqual(1f, player.Health01, 0.001f);
        }

        Stage1Exploration CreateExploration()
        {
            var go = new GameObject("Stage1Exploration");
            _spawned.Add(go);
            return go.AddComponent<Stage1Exploration>();
        }

        SpaceshipPart CreatePart(string name, Vector3 position)
        {
            var go = new GameObject(name);
            go.transform.position = position;
            go.AddComponent<BoxCollider>();
            go.AddComponent<Rigidbody>();
            var part = go.AddComponent<SpaceshipPart>();
            part.Initialize();
            _spawned.Add(go);
            return part;
        }
    }
}
