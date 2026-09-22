using System.Collections.Generic;
using Metagame.Network;
using Metagame.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace Metagame.Gameplay.Tests
{
    public sealed class TeamStatusHudTests
    {
        readonly List<GameObject> _objects = new();
        readonly TeamStatusRoster _roster = new();

        [TestCase(false)]
        [TestCase(true)]
        public void MissingOrDestroyedSessionSkipsBothTeamHudEntryPoints(bool destroySession)
        {
            var previousSession = GameSession.Instance;
            var root = new GameObject("HUD session lifecycle"); _objects.Add(root);
            var hud = root.AddComponent<GameHud>();
            hud.Local = root.AddComponent<PlayerRig>();
            try
            {
                GameSession.Instance = null;
                if (destroySession)
                {
                    var session = root.AddComponent<GameSession>();
                    session.LocalPlayer = hud.Local;
                    GameSession.Instance = session;
                    Object.DestroyImmediate(session);
                    // Keep the destroyed Unity reference to cover Unity's null semantics.
                    GameSession.Instance = session;
                }
                Assert.That(TeamHudVisible(hud), Is.False);
                Assert.DoesNotThrow(() => InvokeTeamHud(hud, "LateUpdate"));
                Assert.DoesNotThrow(() => InvokeTeamHud(hud, "DrawTeamStatus", 1280f));
            }
            finally { GameSession.Instance = previousSession; }
        }

        [Test]
        public void TeamHudVisibilityFollowsSessionOwnershipAndSuppression()
        {
            var previousSession = GameSession.Instance;
            var root = new GameObject("HUD visibility"); _objects.Add(root);
            var hud = root.AddComponent<GameHud>();
            var local = root.AddComponent<PlayerRig>();
            var session = root.AddComponent<GameSession>();
            try
            {
                GameSession.Instance = session;
                session.LocalPlayer = local;
                Assert.That(TeamHudVisible(hud), Is.False, "No HUD owner yet");
                hud.Local = local;
                Assert.That(TeamHudVisible(hud), Is.True);
                session.SetInputSuppressed(true);
                Assert.That(TeamHudVisible(hud), Is.False);
                session.SetInputSuppressed(false);
                session.ScriptedInput = true;
                Assert.That(TeamHudVisible(hud), Is.False);
                session.ScriptedInput = false;
                Assert.That(TeamHudVisible(hud), Is.True);
                session.LocalPlayer = null;
                Assert.That(TeamHudVisible(hud), Is.False, "Session no longer owns this HUD");
            }
            finally { GameSession.Instance = previousSession; }
        }

        // Exercise the actual guards/entry points without opening or operating an Editor window.
        static bool TeamHudVisible(GameHud hud) => (bool)typeof(GameHud)
            .GetProperty("ShowTeamStatus", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .GetValue(hud);

        static void InvokeTeamHud(GameHud hud, string method, params object[] arguments) => typeof(GameHud)
            .GetMethod(method, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Invoke(hud, arguments);

        NetPlayer Player(byte slot, string nickname)
        {
            var go = new GameObject("Roster test"); _objects.Add(go);
            var player = go.AddComponent<NetPlayer>();
            player.teamSlot = slot; player.nickname = nickname;
            return player;
        }

        [TearDown]
        public void Cleanup()
        {
            foreach (var go in _objects) if (go != null) Object.DestroyImmediate(go);
            _objects.Clear();
        }

        [Test]
        public void SpawnOrderDoesNotChangeTeamOrderOrColors()
        {
            var a = Player(2, "가나다"); var b = Player(0, "둘");
            _roster.Refresh(new[] { a, b }, null, true);
            Assert.That(_roster.Members[0].Player, Is.SameAs(b));
            Assert.That(_roster.Members[1].Accent, Is.EqualTo(a.TeamAccentColor));
            Assert.That(_roster.Members[1].Name, Is.EqualTo("가나다"));
        }

        [Test]
        public void ServerStateDrivesDeathAndRescueWithoutRequiringAVisibleBody()
        {
            var player = Player(0, "팀원"); var players = new[] { player };
            _roster.Refresh(players, null, true);
            var member = _roster.Members[0];
            Assert.That(member.Downed, Is.False);
            player.downed = true; _roster.Refresh(players, null, true);
            Assert.That(member.Downed, Is.True);
            player.downed = false; player.health = 0; _roster.Refresh(players, null, true);
            Assert.That(member.Downed, Is.True);
            player.health = 50; player.riding = true; _roster.Refresh(players, null, true);
            Assert.That(member.Downed, Is.False);
            Assert.That(_roster.Members[0], Is.SameAs(member));
            Assert.That(player.health, Is.EqualTo(50));
        }

        [Test]
        public void DepartedAndDestroyedPlayersAreRemovedAndRejoinUsesNewIdentity()
        {
            var a = Player(0, "첫째"); var b = Player(1, "둘째");
            _roster.Refresh(new[] { a, b }, null, true);
            var old = _roster.Members[0];
            Object.DestroyImmediate(a.gameObject);
            _roster.Refresh(new[] { a, b }, null, true);
            Assert.That(_roster.Members.Count, Is.EqualTo(1));
            var replacement = Player(0, "재접속");
            _roster.Refresh(new[] { b, replacement }, null, true);
            Assert.That(_roster.Members[0], Is.Not.SameAs(old));
            Assert.That(_roster.Members[0].Name, Is.EqualTo("재접속"));
            _roster.Refresh(System.Array.Empty<NetPlayer>(), null, true);
            Assert.That(_roster.Members, Is.Empty);
        }

        [Test]
        public void ChangedAndMissingNamesRefreshWithoutRecreatingMembers()
        {
            var a = Player(0, " ");
            _roster.Refresh(new[] { a }, null, true);
            var member = _roster.Members[0];
            Assert.That(member.Name, Is.EqualTo("비행사"));
            a.nickname = "새이름"; _roster.Refresh(new[] { a }, null, true);
            Assert.That(_roster.Members[0], Is.SameAs(member));
            Assert.That(member.Name, Is.EqualTo("새이름"));
        }

        [Test]
        public void NetworkSpawnGapDoesNotAddSoloDuplicate()
        {
            var go = new GameObject("Solo"); _objects.Add(go);
            var local = go.AddComponent<PlayerRig>(); local.Nickname = "혼자";
            _roster.Refresh(System.Array.Empty<NetPlayer>(), local, false);
            Assert.That(_roster.Members.Count, Is.EqualTo(1));
            Assert.That(_roster.Members[0].Name, Is.EqualTo("혼자"));
            _roster.Refresh(System.Array.Empty<NetPlayer>(), local, true);
            Assert.That(_roster.Members, Is.Empty);
        }

        [Test]
        public void PortraitRendersHiddenMeshAndReleasesItsResources()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Graphics device required");
            var root = new GameObject("Portrait fixture"); _objects.Add(root);
            var rig = root.AddComponent<PlayerRig>();
            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.transform.SetParent(root.transform);
            head.transform.localScale = Vector3.one * .5f;
            var body = head.AddComponent<Rigidbody>(); body.isKinematic = true;
            rig.Doll = new Doll { Root = root.transform, Head = body };
            var renderer = head.GetComponent<Renderer>(); renderer.enabled = false;
            var material = renderer.sharedMaterial;
            var layer = head.layer;
            var active = RenderTexture.active;
            var portrait = new TeamPortrait();
            try
            {
                portrait.Refresh(rig, 0);
                Assert.That(RenderTexture.active, Is.SameAs(active));
                var texture = portrait.Texture as RenderTexture;
                Assert.That(texture, Is.Not.Null);
                var pixels = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
                try
                {
                    RenderTexture.active = texture;
                    pixels.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0); pixels.Apply();
                    Assert.That(pixels.GetPixel(64, 64).a, Is.GreaterThan(.9f));
                    Assert.That(pixels.GetPixel(0, 0).a, Is.LessThan(.01f));
                }
                finally { RenderTexture.active = active; Object.DestroyImmediate(pixels); }
                portrait.Refresh(rig, 0);
                Assert.That(portrait.Texture, Is.SameAs(texture));
                Assert.That(renderer.enabled, Is.False);
                Assert.That(renderer.sharedMaterial, Is.SameAs(material));
                Assert.That(head.layer, Is.EqualTo(layer));
                portrait.Dispose();
                Assert.That(portrait.Texture, Is.Null);
            }
            finally { RenderTexture.active = active; portrait.Dispose(); }
        }

        [TestCase(0, 0)]
        [TestCase(3, 1)]
        public void ActualRabbitAndHelmetProducePortrait(int tint, int helmet)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Graphics device required");
            var rig = PlayerRig.Create("초상화", Vector3.zero, Quaternion.identity, Color.white, Color.cyan);
            _objects.Add(rig.gameObject);
            Assert.That(rig.Doll.Suit, Is.Not.Null);
            int packed = new CustomizationData { Tint = tint, Helmet = helmet }.Packed;
            PlayerAppearance.Apply(rig.Doll, packed);
            using (var portrait = new TeamPortrait())
            {
                portrait.Refresh(rig, packed);
                Assert.That(portrait.Texture, Is.Not.Null);
                var previous = RenderTexture.active;
                var image = new Texture2D(128, 128, TextureFormat.RGBA32, false);
                try
                {
                    RenderTexture.active = (RenderTexture)portrait.Texture;
                    image.ReadPixels(new Rect(0, 0, 128, 128), 0, 0); image.Apply();
                    int opaque = 0;
                    foreach (var pixel in image.GetPixels32()) if (pixel.a > 200) opaque++;
                    Assert.That(opaque, Is.GreaterThan(1000));
                    string directory = System.IO.Path.Combine(Application.temporaryCachePath, "TeamStatusValidation");
                    System.IO.Directory.CreateDirectory(directory);
                    System.IO.File.WriteAllBytes(System.IO.Path.Combine(directory, $"rabbit-{tint}-{helmet}.png"), image.EncodeToPNG());
                    TestContext.WriteLine(directory);
                }
                finally { RenderTexture.active = previous; Object.DestroyImmediate(image); }
            }
        }
    }
}
