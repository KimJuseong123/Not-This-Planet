using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Metagame.Core;

namespace Metagame.Gameplay.Tests
{
    public sealed class TeamPingTests
    {
        readonly List<GameObject> spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (int i = spawned.Count - 1; i >= 0; i--)
                if (spawned[i] != null) Object.DestroyImmediate(spawned[i]);
            spawned.Clear();
        }

        [Test]
        public void PingLifetime_ReplacesPreviousAndExpiresAfterThreeSeconds()
        {
            var state = new PingLifetimeState();
            state.Place(new Vector3(1f, 2f, 3f), 10d, Tuning.PingLifetimeSeconds);
            state.Place(new Vector3(4f, 5f, 6f), 11d, Tuning.PingLifetimeSeconds);

            state.Tick(13.99d);
            Assert.IsTrue(state.IsActive);
            Assert.AreEqual(new Vector3(4f, 5f, 6f), state.Position);

            state.Tick(14d);
            Assert.IsFalse(state.IsActive);
        }

        [Test]
        public void PingPlacement_UsesNearestWorldSurface()
        {
            Vector3 origin = new Vector3(10000f, 10000f, 10000f);
            var surface = MakeBox("Ping surface", origin + Vector3.forward * 10f,
                                  Vector3.one * 2f, GameLayers.Ground, false);
            Physics.SyncTransforms();

            bool resolved = PingPlacement.TryResolve(origin, Vector3.forward, null,
                                                      out var point, out var hitSurface, 20f);

            Assert.IsTrue(resolved);
            Assert.IsTrue(hitSurface);
            Assert.That(point.z, Is.EqualTo(origin.z + 9f).Within(0.01f));
            Assert.That(surface, Is.Not.Null);
        }

        [Test]
        public void PingPlacement_FallsBackToMaximumDistanceWhenNothingIsHit()
        {
            Vector3 origin = new Vector3(20000f, 20000f, 20000f);
            bool resolved = PingPlacement.TryResolve(origin, Vector3.forward, null,
                                                      out var point, out var hitSurface, 7f);

            Assert.IsTrue(resolved);
            Assert.IsFalse(hitSurface);
            Assert.That(point, Is.EqualTo(origin + Vector3.forward * 7f));
        }

        [Test]
        public void PingPlacement_IgnoresPlayersAndTriggers()
        {
            Vector3 origin = new Vector3(30000f, 30000f, 30000f);
            MakeBox("Ping player", origin + Vector3.forward * 4f,
                    Vector3.one * 2f, GameLayers.Player, false);
            MakeBox("Ping trigger", origin + Vector3.forward * 6f,
                    Vector3.one * 2f, GameLayers.Ground, true);
            MakeBox("Ping surface", origin + Vector3.forward * 10f,
                    Vector3.one * 2f, GameLayers.Ground, false);
            Physics.SyncTransforms();

            bool resolved = PingPlacement.TryResolve(origin, Vector3.forward, null,
                                                      out var point, out var hitSurface, 20f);

            Assert.IsTrue(resolved);
            Assert.IsTrue(hitSurface);
            Assert.That(point.z, Is.EqualTo(origin.z + 9f).Within(0.01f));
        }

        GameObject MakeBox(string name, Vector3 position, Vector3 size, int layer, bool trigger)
        {
            var go = new GameObject(name);
            spawned.Add(go);
            go.layer = layer;
            go.transform.position = position;
            var collider = go.AddComponent<BoxCollider>();
            collider.size = size;
            collider.isTrigger = trigger;
            return go;
        }
    }
}
