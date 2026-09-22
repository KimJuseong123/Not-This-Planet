using System.Collections.Generic;
using System.IO;
using Metagame.Core;
using Metagame.Network;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    public sealed class ProximityVoiceTests
    {
        [Test] public void HearingIncludesBoundaryAndHeight()
        {
            Assert.That(VoiceRules.Audible(Vector3.zero, Vector3.right * 20), Is.True);
            Assert.That(VoiceRules.Audible(Vector3.zero, Vector3.right * 20.01f), Is.False);
            Assert.That(VoiceRules.Audible(Vector3.zero, Vector3.up * 21), Is.False);
            Assert.That(VoiceRules.Audible(Vector3.zero, new Vector3(float.NaN, 0, 0)), Is.False);
        }

        [Test] public void SilenceDoesNotTransmitAndSpeechDoes()
        {
            var filter = new VoiceFilter(); var samples = new float[VoiceRules.Frame];
            Assert.That(filter.Process(samples, 1), Is.False);
            for (int i = 0; i < samples.Length; i++) samples[i] = Mathf.Sin(i * .2f) * .1f;
            Assert.That(filter.Process(samples, 1), Is.True);
            for (int n = 0; n < 10; n++) { System.Array.Clear(samples, 0, samples.Length); filter.Process(samples, 1); }
            Assert.That(filter.Process(samples, 1), Is.False);
        }

        [Test] public void MuteSurvivesSettingsClone()
        {
            var settings = new GameSettingsData(); settings.voice.microphoneMuted = true;
            Assert.That(settings.Clone().voice.microphoneMuted, Is.True);
        }

        /// <summary>
        /// 마이크는 <b>V 를 누르는 동안에만</b> 열린다 (S15P21B202-705).
        ///
        /// <para><b>값으로는 볼 수 없다.</b> 확인하려면 진짜 마이크 장치가 있어야 하고, CI 에는
        /// 없다. 그래서 문을 여는 조건 자체를 글자로 못박는다.</para>
        ///
        /// <para><b>무엇이 걸리나.</b> 설정으로 되돌리는 것이다 — 예전에는 <c>pushToTalk</c> 가
        /// 꺼져 있으면 오픈 마이크로 늘 열렸다. 그 설정이 다시 들어오면 이 검사가 먼저 깨진다.</para>
        /// </summary>
        [Test] public void TheMicrophoneOnlyOpensWhileVIsHeld()
        {
            var update = TestSource.Method(
                TestSource.Read("Runtime/Network/ProximityVoice.cs"), "void Update()");

            Assert.That(update, Does.Contain("Input.GetKey(KeyCode.V)"),
                "V 를 보지 않는다 — 무엇이 마이크를 여는지 알 수 없다");
            Assert.That(update, Does.Contain("Time.unscaledTime < holdUntil"),
                "여는 조건이 V 에 매여 있지 않다. 이 줄이 빠지면 마이크가 상시로 열린다");
            Assert.That(update, Does.Contain("if (!allowed) { Stop(); return; }"),
                "조건이 거짓일 때 닫지 않는다 — 한 번 열리면 계속 열려 있게 된다");
            Assert.That(update, Does.Not.Contain("pushToTalk"),
                "오픈 마이크 설정이 되살아났다. V 전용은 고를 수 있는 것이 아니다 — "
                + "플레이어끼리 말하는 길이 없으므로 오픈 마이크가 보낼 곳도 없다(-705)");
        }

        /// <summary>
        /// 플레이어끼리 음성을 보내는 길이 <b>어디에도 없다</b> (S15P21B202-705).
        ///
        /// <para><b>왜 한 파일이 아니라 전부를 훑나.</b> 되살아난다면 예전 자리로 돌아오리라는
        /// 보장이 없다. 되돌리는 사람은 보통 <b>새 파일</b>에 다시 만든다.</para>
        ///
        /// <para><b>왜 지워졌나.</b> 음성이 <b>호스트를 거쳐 되뿌려졌다</b> — 참가자는 자기 것
        /// 하나만 올리는데 호스트는 n명을 받아 n명에게 나눠 보냈다. 사람 수의 제곱으로 늘어
        /// 호스트만 렉이 심했고, 같은 PC 에서 AI 모델이 CPU 로 도는 판에서는 감당할 수 없었다.</para>
        /// </summary>
        [Test] public void NoCodeSendsVoiceBetweenPlayers()
        {
            string root = Path.Combine(Application.dataPath, "_Project", "Scripts", "Runtime");
            var files = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories);
            // 훑는 자리가 틀리면 아래 단정이 전부 헛돈다 — 먼저 거기서 깨지게 한다.
            Assert.That(files.Length, Is.GreaterThan(100), "런타임 소스를 못 찾았다: " + root);

            string microphone = null;
            var found = new List<string>();
            foreach (string file in files)
            {
                string code = TestSource.Blank(File.ReadAllText(file));
                if (code.Contains("class ProximityMicrophone")) microphone = file;
                foreach (string banned in new[] { "ProximityVoicePlayback", "CmdProximityVoice",
                                                  "TargetProximityVoice", "SendProximityVoice" })
                    if (code.Contains(banned)) found.Add(Path.GetFileName(file) + " · " + banned);
            }
            Assert.That(microphone, Is.Not.Null,
                "ProximityMicrophone 을 못 찾았다 — 훑는 자리나 주석 지우기가 틀렸다는 뜻이다");
            Assert.That(found, Is.Empty,
                "플레이어끼리 음성을 보내는 길이 되살아났다. 호스트가 그것을 사람 수만큼 "
                + "되뿌리게 되고, 그 비용은 사람 수의 제곱으로 는다(-705): " + string.Join(", ", found));
        }

        /// <summary>
        /// 놓는 쪽과 보내는 쪽이 <b>같은 꼬리를 쓴다</b> (S15P21B202-705).
        ///
        /// <para>V 를 놓는 순간 마이크를 닫으면 링 버퍼에 아직 읽지 않은 마지막 조각이 남은 채
        /// 버려진다 — <b>끝 낱말이 잘린다.</b> 두 쪽이 따로 값을 들고 있으면 한쪽만 바뀌어
        /// 다시 잘리는데, 그것이 <b>말은 되는 문장으로 잘리므로</b> 아무도 알아채지 못한다.</para>
        /// </summary>
        [Test] public void BothSidesShareTheSameReleaseTail()
        {
            string controls = TestSource.Read("Runtime/UI/AiVoiceControls.cs");

            Assert.That(controls, Does.Contain("ProximityMicrophone.ReleaseTail"),
                "보내는 쪽이 자기 값을 들고 있다 — 마이크가 여는 시간과 어긋나면 끝 낱말이 잘린다");
            Assert.That(TestSource.Method(controls, "void Update()"),
                Does.Not.Contain("Input.GetKeyUp(KeyCode.V)"),
                "놓자마자 보낸다. 마이크가 꼬리만큼 더 받은 마지막 조각이 실려 가지 않는다");
        }
    }
}
