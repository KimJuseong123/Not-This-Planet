using Metagame.Network;
using NUnit.Framework;

namespace Metagame.Gameplay.Tests
{
    /// <summary>추가팩이 <b>자기 폴더 안에서 완결되는가</b> (S15P21B202-707).</summary>
    public sealed class AiPackHomeTests
    {
        /// <summary>
        /// 홈이 <b>추가팩 안</b>이다 (S15P21B202-707).
        ///
        /// <para><c>ollama serve</c> 는 처음 뜰 때 홈에 키 파일을 만든다. 그 자리가 사용자
        /// 프로파일이면 <b>둘이 틀어진다</b> — 잠긴 PC 에서는 아예 못 뜨고, 추가팩을 지워도
        /// 흔적이 남는다.</para>
        /// </summary>
        [Test] public void TheHomeLivesInsideThePack()
        {
            string root = System.IO.Path.Combine("C:", "ntp", "AI", "p3");
            string home = AiLocalRuntime.PackHome(root);

            Assert.That(home, Does.StartWith(root), "홈이 추가팩 밖이다 — 지워도 흔적이 남는다");
            Assert.That(home, Is.Not.EqualTo(root), "추가팩 뿌리를 그대로 홈으로 쓴다");
            Assert.That(AiLocalRuntime.PackHome(null), Is.Null);
            Assert.That(AiLocalRuntime.PackHome(""), Is.Null);
        }

        /// <summary>
        /// 띄우기 전에 <b>홈을 정해 준다</b> (S15P21B202-707).
        ///
        /// <para><b>값으로는 볼 수 없다.</b> 확인하려면 진짜 워커 프로세스를 띄워야 하고, CI
        /// 에는 추가팩이 없다.</para>
        ///
        /// <para><b>왜 여기서 고치나.</b> 워커는 <c>os.environ.copy()</c> 로 환경을 물려받아
        /// ollama 에 넘긴다. 띄우는 쪽이 정하면 그대로 내려가므로, 워커를 고쳐 2.34GB 추가팩을
        /// 다시 구워 올리지 않아도 된다. 대신 <b>이 배선이 빠지면 조용히 예전으로 돌아간다</b> —
        /// 홈이 멀쩡한 PC 에서는 아무 증상이 없어서 아무도 모른다.</para>
        /// </summary>
        [Test] public void TheWorkerIsToldWhereHomeIsBeforeItStarts()
        {
            var source = TestSource.Read("Runtime/Network/AiLocalRuntime.cs");
            var startRuntime = TestSource.Method(source, "void StartRuntime()");

            int told = startRuntime.IndexOf("PointHomeIntoThePack(start)", System.StringComparison.Ordinal);
            int started = startRuntime.IndexOf("worker.Start()", System.StringComparison.Ordinal);
            Assert.That(told, Is.GreaterThanOrEqualTo(0), "홈을 정해 주지 않고 띄운다");
            Assert.That(started, Is.GreaterThan(told),
                "띄운 뒤에 홈을 정한다 — 이미 물려받은 환경은 바뀌지 않는다");
            Assert.That(startRuntime, Does.Contain("UseShellExecute = false"),
                "UseShellExecute 가 켜지면 Environment 를 넘길 수 없다 — 배선이 조용히 무효가 된다");

            // 배선은 글자로, 값은 값으로. 키를 문자열로 두면 TestSource.Blank 가 지워
            // 이 단정이 헛돌았다 — 이름을 상수로 뺀 이유다.
            var point = TestSource.Method(source, "void PointHomeIntoThePack(ProcessStartInfo start)");
            Assert.That(point, Does.Contain("HomeVariableWindows"), "Windows 의 홈을 안 넘긴다");
            Assert.That(point, Does.Contain("HomeVariableUnix"), "Windows 밖의 홈을 안 넘긴다");
            Assert.That(AiLocalRuntime.HomeVariableWindows, Is.EqualTo("USERPROFILE"),
                "ollama 가 홈을 찾는 이름이 아니다 — 넘겨도 아무 일이 없다");
            Assert.That(AiLocalRuntime.HomeVariableUnix, Is.EqualTo("HOME"));
        }

        /// <summary>
        /// 홈을 못 만들어도 <b>AI 를 막지 않는다</b> (S15P21B202-707).
        ///
        /// <para>이것은 <b>고치는 것이지 요구하는 것이 아니다.</b> 여기서 멈추면 홈이 멀쩡한
        /// PC 까지 AI 를 못 쓰게 되어, 고치기 전보다 나빠진다.</para>
        /// </summary>
        [Test] public void AHomeThatCannotBeMadeDoesNotBlockTheRuntime()
        {
            var point = TestSource.Method(
                TestSource.Read("Runtime/Network/AiLocalRuntime.cs"),
                "void PointHomeIntoThePack(ProcessStartInfo start)");

            Assert.That(point, Does.Contain("catch"), "폴더를 못 만들면 그대로 터진다");
            Assert.That(point, Does.Not.Contain("Fail("),
                "홈을 못 만들었다고 AI 를 실패로 떨어뜨린다 — 고치기 전보다 나빠진다");
            Assert.That(point, Does.Not.Contain("throw"),
                "홈을 못 만들었다고 예외를 올린다 — 부르는 쪽이 실행 실패로 처리한다");
        }
    }
}
