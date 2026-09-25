using System.Collections;
using System.Collections.Generic;
using System.Text;
using Metagame.Core;
using Metagame.Gameplay.Hazards;
using Metagame.Gameplay.Parts;
using Metagame.Network;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 실제 스테이지(Stage1_Astra)에서 시연용 명령을 <b>사람과 같은 입력으로</b> 해 보고 성공률을 잰다
    /// (S15P21B202-746). 결과는 <c>[AI 시연]</c> 줄로 남긴다 — 숫자가 떨어지면 어디서인지 바로 읽힌다.
    /// </summary>
    public sealed class AiDemoScenarioTests
    {
        readonly List<GameObject> spawned = new List<GameObject>();

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            foreach (var go in spawned) if (go != null) Object.Destroy(go);
            spawned.Clear();
            if (Application.isPlaying) yield return new ExitPlayMode();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        // EnterPlayMode 와 기다림은 테스트 본체에서 넘겨야 한다 — 에디트 모드 UnityTest 는 중첩 열거자를 받지 않는다.
        static void OpenStage() => EditorSceneManager.OpenScene("Assets/Scenes/Stage1_Astra.unity", OpenSceneMode.Single);

        static AiNavigationWorld Bake(GameSession session)
        {
            Assert.That(session, Is.Not.Null);
            session.ScriptedInput = true;
            var world = session.GetComponent<AiNavigationWorld>() ?? session.gameObject.AddComponent<AiNavigationWorld>();
            Physics.SyncTransforms(); world.Rebuild(new Bounds(Vector3.zero, new Vector3(512, 128, 512)));
            return world;
        }

        static bool TryFreeSpot(Vector3 around, float radius, out Vector3 spot)
        {
            foreach (float scale in new[] { 1f, .85f, 1.15f, .7f, 1.3f })
            for (int i = 0; i < 24; i++)
            {
                var candidate = around + Quaternion.Euler(0, i * 15, 0) * Vector3.forward * radius * scale;
                if (!NavMesh.SamplePosition(candidate, out var hit, .8f, NavMesh.AllAreas)) continue;
                if (Physics.CheckCapsule(hit.position + Vector3.up * .5f, hit.position + Vector3.up * 1.3f,
                        .4f, GameLayers.SurfaceMask, QueryTriggerInteraction.Ignore)) continue;
                spot = hit.position + Vector3.up * .1f; return true;
            }
            spot = around; return false;
        }

        static Vector3 FreeSpotNear(Vector3 around, float radius)
        {
            Assert.That(TryFreeSpot(around, radius, out var spot), Is.True, "AI 를 세울 빈자리가 없다 · " + around);
            return spot;
        }

        /// <summary>
        /// "고정못 뽑아" 를 거듭 시킨다. 매번 <b>그 동료에게서 가장 가까운</b> 못으로 가서 뽑아야 한다.
        /// </summary>
        [Category("Heavy")]
        [UnityTest]
        public IEnumerator PinsNearestToTheCompanionArePulledOneAfterAnother()
        {
            GameSession session = null;
            OpenStage();
            yield return new EnterPlayMode();
            yield return null;
            session = GameSession.Instance;
            var world = Bake(session);
            float baked = Time.realtimeSinceStartup + 60f;
            while (!world.Ready && Time.realtimeSinceStartup < baked) yield return null;
            Assert.That(world.Ready, Is.True, "길찾기를 굽지 못했다");
            var speaker = session.LocalPlayer;
            var self = PlayerRig.Create("AI 시연 · 못", FreeSpotNear(speaker.FootPosition, 3f), Quaternion.identity, Color.yellow, Color.white, false);
            spawned.Add(self.gameObject);
            var work = new AiWorkTask();
            var log = new StringBuilder();
            int ok = 0, tries = 0;
            float until = Time.realtimeSinceStartup + 240f;
            while (tries < 6 && Time.realtimeSinceStartup < until)
            {
                tries++;
                PartClamp nearest = null; float best = float.PositiveInfinity;
                foreach (var pin in PartClamp.All)
                {
                    if (pin == null || !pin.isActiveAndEnabled || !pin.IsRequired || pin.IsReleased) continue;
                    float d = (pin.transform.position - self.FootPosition).sqrMagnitude;
                    if (d < best) { best = d; nearest = pin; }
                }
                if (nearest == null) break;
                float began = Time.time;
                bool started = work.Begin("pull_pin", self, speaker);
                while (started && work.Active && Time.realtimeSinceStartup < until)
                {
                    self.SetInput(work.Tick());
                    yield return new WaitForFixedUpdate();
                }
                self.SetInput(default);
                bool pulled = work.Outcome == AiTaskOutcome.Succeeded;
                if (pulled) ok++;
                log.AppendLine($"  {tries}: {(pulled ? "뽑음" : "실패")} {Time.time - began:0.0}초 · {work.Reason} · 가장 가까운 못 {Mathf.Sqrt(best):0.0}m {(nearest.IsReleased ? "(그것을 뽑음)" : "")}");
                for (int i = 0; i < 20; i++) yield return new WaitForFixedUpdate();
            }
            Debug.Log($"[AI 시연] 고정못 {ok}/{tries}\n{log}");
            Assert.That(ok, Is.GreaterThanOrEqualTo(Mathf.CeilToInt(tries * .8f)), $"고정못 {ok}/{tries}\n{log}");
        }

        /// <summary>
        /// 방망이를 주워 떨어지는 운석을 쳐낸다. 닿을 수 있게 떨어뜨린 것은 대부분 쳐내고, 닿을 수 없는 것은 쫓지 않는다.
        /// </summary>
        [Category("Heavy")]
        [UnityTest]
        public IEnumerator ReachableMeteorsAreStruckAndUnreachableOnesAreLeft()
        {
            GameSession session = null;
            OpenStage();
            yield return new EnterPlayMode();
            yield return null;
            session = GameSession.Instance;
            var world = Bake(session);
            float baked = Time.realtimeSinceStartup + 60f;
            while (!world.Ready && Time.realtimeSinceStartup < baked) yield return null;
            Assert.That(world.Ready, Is.True, "길찾기를 굽지 못했다");
            var speaker = session.LocalPlayer;
            var self = PlayerRig.Create("AI 시연 · 운석", FreeSpotNear(speaker.FootPosition, 3f), Quaternion.identity, Color.green, Color.white, false);
            spawned.Add(self.gameObject);
            // 게임과 같은 길 — 막힘 복구(AiMovementRecovery)까지 거친다.
            var work = new AiCompanionController();
            Assert.That(work.Apply("defend_meteors", speaker, self, out var refused), Is.True, refused);
            float until = Time.realtimeSinceStartup + 60f;
            while (work.Busy && self.ActiveTool is not MeteorBat && Time.realtimeSinceStartup < until)
            { self.SetInput(work.Tick(self, true)); yield return new WaitForFixedUpdate(); }
            Assert.That(self.ActiveTool, Is.InstanceOf<MeteorBat>(), "방망이를 집지 못했다 · " + work.Status + " · " + work.WorkReason);

            var log = new StringBuilder();
            int reachable = 0, struck = 0, farChased = 0;
            var random = new System.Random(746);
            for (int round = 0; round < 10 && work.Busy; round++)
            {
                bool far = round % 5 == 4;       // 다섯 번에 한 번은 닿을 수 없는 곳에
                float seconds = 2.2f + (float)random.NextDouble() * .8f;
                float distance = far ? 30f : 2f + (float)random.NextDouble() * 4f;
                if (!TryFreeSpot(self.FootPosition, distance, out var spot) && !TryFreeSpot(speaker.FootPosition, distance, out spot)) { log.AppendLine($"  {round}: {distance:0.0}m 에 떨어뜨릴 자리가 없어 건너뜀"); continue; }
                var ground = spot - Vector3.up * .1f;
                var meteor = Drop(ground, seconds);
                if (!far) reachable++;
                bool chasedFar = false, ran = false;
                var bat = (MeteorBat)self.ActiveTool;
                uint swingsBefore = bat.RhythmSequence;
                string swingNote = "안 휘두름";
                float closest = float.PositiveInfinity, eligibleAt = -1f;
                float end = Time.time + seconds + 1f;
                while (Time.time < end && work.Busy)
                {
                    self.SetInput(work.Tick(self, true));
                    if (work.Status.StartsWith("운석 쪽으로")) { if (far) chasedFar = true; ran = true; }
                    if (meteor != null && !meteor.HasImpacted && meteor.TryPredictImpact(out var t, out var p))
                    {
                        var d = p - self.Position; d.y = 0; closest = Mathf.Min(closest, d.magnitude);
                        if (eligibleAt < 0 && MeteorBat.FindTarget(self.Position) == meteor) eligibleAt = t;
                        if (bat.RhythmSequence != swingsBefore && swingNote == "안 휘두름")
                            swingNote = $"휘두름(남은 {t:0.00}초 · 등급 {bat.RhythmGrade} · 맞음 {bat.LastSwingConnected})";
                    }
                    yield return new WaitForFixedUpdate();
                }
                bool hit = meteor != null && meteor.WasDeflected;
                if (hit && !far) struck++;
                if (chasedFar) farChased++;
                log.AppendLine($"  {round}: {(far ? "먼 곳" : "닿는 곳")} {distance:0.0}m · {seconds:0.0}초 → {(hit ? "쳐냄" : "놓침")} · {(ran ? "뜀" : "안 뜀")} · {swingNote} · 착지점까지 최소 {closest:0.00}m · 칠 수 있게 된 때 {(eligibleAt < 0 ? "없음" : eligibleAt.ToString("0.00") + "초 전")} · {work.Status} · 땅 {self.Grounded} 공중 {self.AirTime:0.0}s 넘어짐 {self.Ragdolled} 뜸 {self.Floating} 엉덩이 {self.Doll.Hips.linearVelocity.magnitude:0.0}m/s 발 {self.FootPosition}");
                if (meteor != null) Object.Destroy(meteor.gameObject);
                for (int i = 0; i < 25; i++) { self.SetInput(work.Tick(self, true)); yield return new WaitForFixedUpdate(); }
            }
            work.Stop("검사 끝");
            Debug.Log($"[AI 시연] 운석 {struck}/{reachable} · 먼 운석 쫓음 {farChased}\n{log}");
            Assert.That(farChased, Is.Zero, "닿을 수 없는 운석으로 뛰어간다\n" + log);
            Assert.That(struck, Is.GreaterThanOrEqualTo(Mathf.CeilToInt(reachable * .7f)), $"운석 {struck}/{reachable}\n{log}");
        }

        /// <summary>"이쪽으로 와" — 멀리 있는 동료가 말한 사람 곁 2m 안으로 와서 멈춘다.</summary>
        [Category("Heavy")]
        [UnityTest]
        public IEnumerator ComeHereWalksToTheSpeakerAndStops()
        {
            GameSession session = null;
            OpenStage();
            yield return new EnterPlayMode();
            yield return null;
            session = GameSession.Instance;
            var world = Bake(session);
            float baked = Time.realtimeSinceStartup + 60f;
            while (!world.Ready && Time.realtimeSinceStartup < baked) yield return null;
            Assert.That(world.Ready, Is.True, "길찾기를 굽지 못했다");
            var speaker = session.LocalPlayer;
            var self = PlayerRig.Create("AI 시연 · 오기", FreeSpotNear(speaker.FootPosition, 12f), Quaternion.identity, Color.red, Color.white, false);
            spawned.Add(self.gameObject);
            var work = new AiWorkTask();
            Assert.That(work.Begin("come", self, speaker), Is.True, work.Reason);
            float until = Time.realtimeSinceStartup + 40f;
            while (work.Active && Time.realtimeSinceStartup < until)
            { self.SetInput(work.Tick()); yield return new WaitForFixedUpdate(); }
            self.SetInput(default);
            var offset = speaker.FootPosition - self.FootPosition; offset.y = 0;
            Debug.Log($"[AI 시연] 이쪽으로 와 · {work.Outcome} · {work.Reason} · 남은 거리 {offset.magnitude:0.0}m");
            Assert.That(work.Outcome, Is.EqualTo(AiTaskOutcome.Succeeded), work.Reason);
            Assert.That(offset.magnitude, Is.LessThanOrEqualTo(AiWorkTask.ComeStopDistance + .5f));
        }

        /// <summary>
        /// <paramref name="ground"/> 에 <paramref name="seconds"/> 뒤 떨어지는 운석. 실제 중력으로 떨어진다.
        /// </summary>
        Meteor Drop(Vector3 ground, float seconds)
        {
            float g = -Physics.gravity.y, height = 18f;
            float down = (height - .5f * g * seconds * seconds) / seconds;
            var go = new GameObject("시연 검사 운석");
            spawned.Add(go);
            go.transform.position = ground + Vector3.up * height;
            var body = go.AddComponent<Rigidbody>();
            body.useGravity = true;
            body.linearVelocity = Vector3.down * down;
            go.AddComponent<SphereCollider>().radius = .35f;
            go.layer = GameLayers.Meteor;
            var meteor = go.AddComponent<Meteor>();
            meteor.Initialize();
            meteor.SetSize(MeteorSize.Small);
            meteor.SetGroundHeight(ground.y);
            return meteor;
        }
    }
}
