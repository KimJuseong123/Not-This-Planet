using System.Collections.Generic;
using System.Reflection;
using Metagame.Network;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Tests.Editor
{
    /// <summary>
    /// 참가자 화면에서 부품이 덜덜 떨리던 것을 지킨다 (S15P21B202-538).
    ///
    /// <para><b>무엇이 문제였나.</b> 자세는 제대로 갔다. 참가자가 그것을 <c>Update</c> 에서
    /// 지수보간으로 쫓았고, 그때 서로 맞지 않는 시계가 셋 겹쳤다 — 보내는 주기(30Hz),
    /// 참가자의 프레임 주기, 강체 <c>transform</c> 이 갱신되는 물리 주기(100Hz).
    /// 그래서 프레임마다 전진하는 양이 들쭉날쭉했다.</para>
    ///
    /// <para><b>무엇을 재는가.</b> <b>이웃한 두 프레임의 이동량 비율</b>이다. 1.00배면 옆
    /// 프레임과 같은 속도로 간 것이고, 2.00배면 한 프레임에 두 배씩 튀었다는 뜻이다.</para>
    ///
    /// <para><b>전체 구간의 변동계수로 재면 안 된다.</b> 조사 때 그렇게 쟀다가 틀렸다 —
    /// 물체가 가속했다 감속하는 진짜 속도 변화가 지표를 삼켜 호스트와 참가자가 똑같이
    /// 나왔다(둘 다 16배·cv 0.9). "덜덜" 은 국소 현상이라 국소적으로 재야 한다.</para>
    /// </summary>
    public sealed class WorldSyncFollowCadenceTests
    {
        const float FixedStep = 0.01f;      // ProjectSettings 의 고정 스텝
        const float SendInterval = 1f / 30f;

        // ── 한 번의 상승을 두 방식으로 화면에 그려 본다 ──────────────────

        /// <summary>
        /// 저중력장에 뜬 부품처럼 <b>일정한 속도로 오래</b> 올라가는 서버 자세.
        /// 물리가 적분한 결과이므로 매 스텝 정확히 같은 양씩 간다.
        /// </summary>
        static float ServerAt(float time) => -6f + 1.4f * time;

        /// <summary>
        /// 옛 방식: 프레임마다 <c>Time.deltaTime</c> 으로 한 걸음, 강체 보간 없음.
        /// 화면에 나오는 것은 마지막 물리 스텝에 굳은 자세다.
        /// </summary>
        static List<float> RenderOldWay(float frameRate, float seconds)
        {
            float frameTime = 1f / frameRate;
            float follow = ServerAt(0f), target = ServerAt(0f);
            float shown = follow, clock = 0f, accumulator = 0f, sendTimer = 0f;
            var frames = new List<float>();

            int count = Mathf.RoundToInt(seconds * frameRate);
            for (int f = 0; f < count; f++)
            {
                accumulator += frameTime;
                while (accumulator >= FixedStep)
                {
                    accumulator -= FixedStep;
                    clock += FixedStep;
                    shown = follow;                 // 물리 스텝이 자세를 굳힌다
                }

                sendTimer += frameTime;
                if (sendTimer >= SendInterval) { sendTimer = 0f; target = ServerAt(clock); }

                follow = Mathf.Lerp(follow, target, WorldSync.FollowFactor(frameTime));
                frames.Add(shown);
            }
            return frames;
        }

        /// <summary>
        /// 지금 방식: 물리 스텝마다 <c>Time.fixedDeltaTime</c> 으로 한 걸음, 강체 보간 켬.
        /// 보간은 마지막 두 물리 자세 사이를 프레임 시각으로 메운다 — 유니티가 하는 것과 같다.
        /// </summary>
        static List<float> RenderNewWay(float frameRate, float seconds)
        {
            float frameTime = 1f / frameRate;
            float follow = ServerAt(0f), target = ServerAt(0f);
            float previous = follow, current = follow;
            float clock = 0f, accumulator = 0f, sendTimer = 0f;
            var frames = new List<float>();

            int count = Mathf.RoundToInt(seconds * frameRate);
            for (int f = 0; f < count; f++)
            {
                accumulator += frameTime;
                while (accumulator >= FixedStep)
                {
                    accumulator -= FixedStep;
                    clock += FixedStep;

                    follow = Mathf.Lerp(follow, target, WorldSync.FollowFactor(FixedStep));
                    previous = current;
                    current = follow;
                }

                sendTimer += frameTime;
                if (sendTimer >= SendInterval)
                {
                    sendTimer = WorldSync.NextSendTimer(sendTimer, SendInterval);
                    target = ServerAt(clock);
                }

                frames.Add(Mathf.Lerp(previous, current, Mathf.Clamp01(accumulator / FixedStep)));
            }
            return frames;
        }

        /// <summary>
        /// 이웃한 두 프레임의 이동량이 평균 몇 배씩 어긋나는가. 1.00 이면 등속이다.
        /// 그 프레임에 물리 스텝이 없어 제자리인 경우는 <b>두 방식에 똑같이</b> 생기므로 뺀다.
        /// </summary>
        static float NeighbourSwing(List<float> frames)
        {
            var steps = new List<float>();
            for (int i = 1; i < frames.Count; i++) steps.Add(Mathf.Abs(frames[i] - frames[i - 1]));

            float sum = 0f;
            int n = 0;
            for (int i = 1; i < steps.Count; i++)
            {
                if (steps[i] <= 1e-7f || steps[i - 1] <= 1e-7f) continue;
                sum += Mathf.Abs(Mathf.Log(steps[i] / steps[i - 1]));
                n++;
            }
            return n == 0 ? 0f : Mathf.Exp(sum / n);
        }

        // ── 본론 ─────────────────────────────────────────────────────

        [TestCase(120f)]
        [TestCase(144f)]
        [TestCase(165f)]
        public void AboveThePhysicsRate_TheNewCadenceIsSteadierThanTheOldOne(float frameRate)
        {
            var oldWay = NeighbourSwing(RenderOldWay(frameRate, 2f));
            var newWay = NeighbourSwing(RenderNewWay(frameRate, 2f));

            Assert.Less(newWay, oldWay,
                $"{frameRate}fps 에서 새 방식이 더 매끄럽지 않다 — 옛 {oldWay:F2}배 · 새 {newWay:F2}배");
        }

        [TestCase(60f)]
        [TestCase(72f)]
        [TestCase(100f)]
        [TestCase(120f)]
        [TestCase(144f)]
        [TestCase(165f)]
        public void TheNewCadenceStaysSteadyAtEveryFrameRate(float frameRate)
        {
            // 조사에서 잰 값은 1.24~1.37배였다. 1.45 는 그 위에 여유를 둔 선이다.
            Assert.Less(NeighbourSwing(RenderNewWay(frameRate, 2f)), 1.45f,
                $"{frameRate}fps 에서 이웃 프레임 이동량이 너무 흔들린다");
        }

        [Test]
        public void TheOldCadenceWasWorseAtHighFrameRates_WhichIsWhatWasReported()
        {
            // 이 테스트가 없으면 위의 두 개가 "원래 문제가 없었다" 로도 통과한다.
            // 신고된 조건(고프레임 참가자)에서 옛 방식이 실제로 나빴음을 못박는다.
            Assert.Greater(NeighbourSwing(RenderOldWay(165f, 2f)), 1.6f,
                "옛 방식이 165fps 에서 흔들리지 않는다 — 이 테스트가 재는 것이 "
                + "신고된 현상이 맞는지 다시 볼 것");
        }

        [Test]
        public void TheFrameRateNoLongerChangesHowSteadyItLooks()
        {
            // 옛 방식의 본질은 **프레임레이트가 화면 품질을 바꾼다**는 것이었다.
            float worst = 0f, best = float.MaxValue;
            foreach (var rate in new[] { 60f, 72f, 100f, 120f, 144f, 165f })
            {
                float swing = NeighbourSwing(RenderNewWay(rate, 2f));
                worst = Mathf.Max(worst, swing);
                best = Mathf.Min(best, swing);
            }
            Assert.Less(worst - best, 0.25f,
                $"프레임레이트에 따라 매끄러움이 {best:F2}~{worst:F2}배로 갈린다");
        }

        // ── 보내는 쪽 타이머 ──────────────────────────────────────────

        [Test]
        public void TheSendTimerKeepsTheLeftoverInsteadOfThrowingItAway()
        {
            // 0 으로 되돌리면 실제 주기가 프레임 시간만큼 늘어난다.
            Assert.AreEqual(0.01f, WorldSync.NextSendTimer(0.04f, 0.03f), 1e-6f);
        }

        [Test]
        public void TheSendRateMatchesTheConfiguredIntervalNotTheFrameBoundary()
        {
            // 144fps 호스트에서 10초 동안 몇 번 보내는가. 30Hz 면 300번이어야 한다.
            float frameTime = 1f / 144f, timer = 0f;
            int sent = 0;
            for (int f = 0; f < 1440; f++)
            {
                timer += frameTime;
                if (timer < SendInterval) continue;
                timer = WorldSync.NextSendTimer(timer, SendInterval);
                sent++;
            }

            // 프레임 경계에 걸리므로 정확히 300 은 될 수 없다. 옛 방식은 288 이었다.
            Assert.GreaterOrEqual(sent, 298, $"전송 주기가 설정보다 느리다 — 10초에 {sent}번");
            Assert.LessOrEqual(sent, 301, $"전송 주기가 설정보다 빠르다 — 10초에 {sent}번");
        }

        [Test]
        public void ALongHitchDoesNotMakeItSendABurstAfterwards()
        {
            // 0.5초 멈췄다 돌아왔을 때 밀린 15번을 몰아 보내지 않는다.
            float timer = WorldSync.NextSendTimer(0.5f, SendInterval);
            Assert.LessOrEqual(timer, SendInterval + 1e-6f,
                "끊긴 시간만큼 빚이 남아 다음 프레임부터 몰아 보낸다");
        }

        [Test]
        public void AZeroIntervalDoesNotSpinTheTimerIntoNonsense()
        {
            Assert.AreEqual(0f, WorldSync.NextSendTimer(0.2f, 0f), 1e-6f);
            Assert.AreEqual(0f, WorldSync.FollowFactor(0f), 1e-6f);
            Assert.AreEqual(0f, WorldSync.FollowFactor(-1f), 1e-6f);
        }

        // ── 배선 ─────────────────────────────────────────────────────

        [Test]
        public void TheFollowRunsOnThePhysicsStep_NotOnTheFrame()
        {
            var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            Assert.NotNull(typeof(WorldSync).GetMethod("FixedUpdate", flags),
                "WorldSync 에 FixedUpdate 가 없다 — 추종이 다시 프레임 시계로 돌아갔다");
        }

        [Test]
        public void PreparingAClientBodyMakesItKinematicAndInterpolated()
        {
            var go = new GameObject("참가자 화면의 부품");
            try
            {
                var body = go.AddComponent<Rigidbody>();
                body.isKinematic = false;
                body.interpolation = RigidbodyInterpolation.None;   // 씬의 Part_1~4 가 이렇다

                var prepare = typeof(WorldSync).GetMethod(
                    "PrepareClientBody", BindingFlags.Static | BindingFlags.NonPublic);
                Assert.NotNull(prepare, "PrepareClientBody 가 없다");
                prepare.Invoke(null, new object[] { body });

                Assert.IsTrue(body.isKinematic, "참가자 부품이 제 물리를 돌린다 — 서버 자세와 다툰다");
                Assert.AreEqual(RigidbodyInterpolation.Interpolate, body.interpolation,
                    "보간이 꺼져 있다 — 프레임레이트가 물리 주기보다 빠르면 다시 덜덜거린다");

                // 죽은 참조를 받아도 터지지 않아야 한다. Collect 가 세는 목록에는 섞여 든다.
                Assert.DoesNotThrow(() => prepare.Invoke(null, new object[] { null }));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// <b>보조 장치다.</b> 위의 수치 검증은 두 방식을 이 테스트 안에서 모형으로 그리므로,
        /// 제품 코드가 어느 시계에 매달려 있는지는 잡지 못한다. 그 배선만 글자로 지킨다.
        /// </summary>
        [Test]
        public void TheFollowIsWiredToTheFixedStepAndMovesRatherThanTeleports()
        {
            var path = System.IO.Path.Combine(Application.dataPath,
                "_Project/Scripts/Runtime/Network/WorldSync.cs");
            Assert.IsTrue(System.IO.File.Exists(path), $"WorldSync.cs 를 못 찾았다: {path}");
            var source = System.IO.File.ReadAllText(path);

            // 주석은 떼고 본다. 설명에 적힌 "예전에는 Time.deltaTime 이었다" 를
            // 코드로 착각하면 고쳐 놓고도 빨간불이 난다(실제로 그랬다).
            var update = CodeOf(source, "void Update()");
            var fixedUpdate = CodeOf(source, "void FixedUpdate()");
            var follow = CodeOf(source, "void FollowOnClient()");
            var prepare = CodeOf(source, "static void PrepareClientBody(Rigidbody body)");

            StringAssert.Contains("FollowOnClient()", fixedUpdate,
                "FixedUpdate 가 추종을 부르지 않는다");
            Assert.IsFalse(update.Contains("FollowOnClient()"),
                "Update 가 아직 추종을 부른다 — 프레임 시계로 되돌아갔다");

            StringAssert.Contains("Time.fixedDeltaTime", follow,
                "추종이 고정 스텝 폭으로 밟지 않는다");
            Assert.IsFalse(follow.Contains("Time.deltaTime"),
                "추종이 아직 프레임 폭으로 밟는다 — 프레임레이트마다 다르게 보인다");

            StringAssert.Contains("MovePosition", follow);
            StringAssert.Contains("MoveRotation", follow);
            Assert.IsFalse(follow.Contains("body.position ="),
                "위치를 직접 대입한다 — 순간이동으로 취급되어 그 스텝의 보간이 건너뛰어진다");
            Assert.IsFalse(follow.Contains("body.rotation ="),
                "회전을 직접 대입한다 — 위치와 같은 이유로 보간이 건너뛰어진다");

            StringAssert.Contains("RigidbodyInterpolation.Interpolate", prepare,
                "참가자 부품에 보간을 켜지 않는다");

            // **대조 실험이 여기를 비워 두었음을 알려 줬다.** NextSendTimer 를 아무리
            // 검증해도 보내는 쪽이 그것을 부르지 않으면 소용이 없다 — 실제로 호출부를
            // `sendTimer = 0f` 로 되돌렸더니 19개가 전부 통과했다.
            var send = CodeOf(source, "void SendFromServer()");
            StringAssert.Contains("NextSendTimer(", send,
                "보내는 쪽이 남은 시간을 NextSendTimer 로 넘기지 않는다");
            Assert.IsFalse(send.Contains("sendTimer = 0f"),
                "보내는 쪽이 타이머를 0 으로 되돌린다 — 남은 시간을 버려 주기가 흔들린다");
        }

        /// <summary>메서드 하나의 본문에서 주석을 뺀 코드만 돌려준다.</summary>
        static string CodeOf(string source, string signature)
        {
            var body = BodyOf(source, signature);
            var lines = body.Split('\n');
            var kept = new List<string>();
            foreach (var line in lines)
            {
                var at = line.IndexOf("//", System.StringComparison.Ordinal);
                var code = at < 0 ? line : line.Substring(0, at);
                if (code.TrimStart().StartsWith("///")) continue;
                kept.Add(code);
            }
            return string.Join("\n", kept);
        }

        /// <summary>중괄호를 세어 메서드 하나의 본문만 떼어 온다.</summary>
        static string BodyOf(string source, string signature)
        {
            int at = source.IndexOf(signature, System.StringComparison.Ordinal);
            Assert.Greater(at, -1, $"{signature} 를 못 찾았다");

            int open = source.IndexOf('{', at);
            Assert.Greater(open, -1, $"{signature} 의 본문이 없다");

            int depth = 0;
            for (int i = open; i < source.Length; i++)
            {
                if (source[i] == '{') depth++;
                else if (source[i] == '}' && --depth == 0)
                    return source.Substring(open, i - open + 1);
            }
            throw new AssertionException($"{signature} 의 본문이 닫히지 않았다");
        }

        [Test]
        public void TheFollowFactorReachesTheTargetButNeverOvershoots()
        {
            foreach (var dt in new[] { 0.001f, 0.01f, 0.016f, 0.1f, 1f })
            {
                float t = WorldSync.FollowFactor(dt);
                Assert.Greater(t, 0f, $"{dt}초에 한 걸음도 못 간다");
                Assert.LessOrEqual(t, 1f, $"{dt}초에 목표를 지나친다");
            }
        }
    }
}
