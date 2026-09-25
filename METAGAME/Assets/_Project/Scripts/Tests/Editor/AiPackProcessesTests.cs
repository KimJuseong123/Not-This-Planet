using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Metagame.Network;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 추가팩 폴더를 잡은 채 남은 프로세스가 설치를 막지 않는가 (S15P21B202-745).
    ///
    /// <para>2026-09-25: 부모를 잃은 <c>llama-server.exe</c> 넷이 설치된 팩 폴더를 잡고 있어, 설치가
    /// 다운로드·풀기를 다 마친 뒤 교체에서 매번 "디스크 권한" 으로 실패했다.</para>
    /// </summary>
    public sealed class AiPackProcessesTests
    {
        const string Root = @"C:\Users\u\AppData\LocalLow\Not This Planet\Not This Planet\AI\p3";
        const string Game = @"C:\Games\NotThisPlanet\NotThisPlanet.exe";
        static readonly string Worker = Root + @"\runtime\ntp-ai-worker\ntp-ai-worker.exe";
        static readonly string Ollama = Root + @"\runtime\ntp-ai-worker\ollama\ollama.exe";
        static readonly string Llama = Root + @"\runtime\ntp-ai-worker\ollama\lib\ollama\llama-server.exe";

        static AiPackProcesses.Seen P(int pid, int parent, long started, string image)
            => new AiPackProcesses.Seen { Pid = pid, Parent = parent, Started = started, Image = image };

        [Test]
        public void OnlyPathsInsideThePackFolderCount()
        {
            Assert.That(AiPackProcesses.Under(Llama, Root), Is.True);
            Assert.That(AiPackProcesses.Under(Llama.ToUpperInvariant(), Root), Is.True, "Windows 경로는 대소문자를 가리지 않는다");
            Assert.That(AiPackProcesses.Under(Llama.Replace('\\', '/'), Root + "/"), Is.True);
            Assert.That(AiPackProcesses.Under(Root + @".backup-0123\runtime\x.exe", Root), Is.False, "옆 폴더는 팩이 아니다");
            Assert.That(AiPackProcesses.Under(Root + @"0\x.exe", Root), Is.False, "p30 은 p3 가 아니다");
            Assert.That(AiPackProcesses.Under(Game, Root), Is.False);
            Assert.That(AiPackProcesses.Under(null, Root), Is.False, "경로를 모르는 프로세스는 팩의 것으로 보지 않는다");
        }

        [Test]
        public void ALiveGameOwnsItsWholeTree()
        {
            var all = new[] { P(10, 1, 100, Game), P(20, 10, 200, Worker), P(30, 20, 300, Ollama), P(40, 30, 400, Llama) };
            Assert.That(AiPackProcesses.Abandoned(all, Root), Is.Empty, "게임 창이 살아 있는 AI 를 끝냈다 — 다른 창의 AI 를 죽인다");
            Assert.That(AiPackProcesses.Owned(all, Root), Is.EqualTo(3));
        }

        [Test]
        public void ProcessesWhoseChainBreaksAreAbandoned()
        {
            // 2026-09-25 의 모양: ollama 가 끝나고 llama-server 만 남았다
            var orphan = new[] { P(10, 1, 100, Game), P(20, 10, 200, Worker), P(40, 30, 400, Llama) };
            Assert.That(AiPackProcesses.Abandoned(orphan, Root), Is.EqualTo(new[] { 40 }));

            // 게임이 죽었다 — 그 아래 팩 프로세스는 모두 주인이 없다
            var gone = new[] { P(20, 10, 200, Worker), P(30, 20, 300, Ollama), P(40, 30, 400, Llama) };
            Assert.That(AiPackProcesses.Abandoned(gone, Root), Is.EquivalentTo(new[] { 20, 30, 40 }));

            // 부모 pid 가 다른 프로그램에 다시 쓰였다 — 부모가 자식보다 늦게 태어났으면 진짜 부모가 아니다
            var reused = new[] { P(30, 1, 900, @"C:\Windows\notepad.exe"), P(40, 30, 400, Llama) };
            Assert.That(AiPackProcesses.Abandoned(reused, Root), Is.EqualTo(new[] { 40 }));

            // 팩 밖의 프로세스는 부모가 없어도 건드리지 않는다
            Assert.That(AiPackProcesses.Abandoned(new[] { P(50, 7, 1, Game) }, Root), Is.Empty);
        }

        [Test]
        public void AParentWeCannotReadIsTreatedAsAnOwner()
        {
            // 권한이 없어 경로·시각을 모르는 부모 — 모를 때는 끝내지 않는다
            var all = new[] { P(10, 1, 0, null), P(20, 10, 200, Worker) };
            Assert.That(AiPackProcesses.Abandoned(all, Root), Is.Empty);
        }

        [Test]
        public void ALockedFolderIsReportedAsInUseNotAsDiskPermission()
        {
            Assert.That(AiPackInstaller.Blocked(new IOException("Access to the path is denied.")), Is.EqualTo(AiPrepFailure.InUse));
            Assert.That(AiPackInstaller.Blocked(new UnauthorizedAccessException()), Is.EqualTo(AiPrepFailure.InUse));
            Assert.That(AiPackInstaller.Blocked(new InvalidOperationException()), Is.EqualTo(AiPrepFailure.Apply));
            string text = AiPrepProgress.Display(new AiPrepSnapshot { Phase = AiPrepPhase.Failed, Failure = AiPrepFailure.InUse });
            Assert.That(text, Does.Contain("다른 프로그램이 쓰고 있습니다"), text);
            Assert.That(text, Does.Not.Contain("디스크 권한"), "사용 중인 것을 권한 문제로 안내하면 사용자가 풀 수 없다");
        }

        [Test]
        public void OnlyOurOldPackNamesAreDropped()
        {
            Assert.That(AiPackInstaller.IsOldPack("p3.backup-0f0743dde4a749a7832aa42cdcd4e10e", "p3"), Is.True);
            Assert.That(AiPackInstaller.IsOldPack("p3", "p3"), Is.False, "설치된 팩");
            Assert.That(AiPackInstaller.IsOldPack("p3-old-0.1.0", "p3"), Is.False, "우리가 붙인 이름이 아니다");
            Assert.That(AiPackInstaller.IsOldPack("p3.backup-mine", "p3"), Is.False);
            Assert.That(AiPackInstaller.IsOldPack("p3.backup-0f0743dde4a749a7832aa42cdcd4e10eX", "p3"), Is.False);
            Assert.That(AiPackInstaller.IsOldPack(".download", "p3"), Is.False, "받아 둔 압축");
        }

        [Test]
        public void TheInstallerAndTheRuntimeSweepBeforeTheyTouchThePack()
        {
            string install = TestSource.Method(TestSource.Read("Runtime/Network/AiPackInstaller.cs"), "IEnumerator Install(AiPackSource source)");
            int sweep = install.IndexOf("EndAbandoned(root);", StringComparison.Ordinal);
            int promote = install.IndexOf("Promote(staging, root, out backup);", StringComparison.Ordinal);
            Assert.That(sweep, Is.GreaterThanOrEqualTo(0), "교체 전에 남은 프로세스를 끝내지 않는다");
            Assert.That(promote, Is.GreaterThan(sweep), "끝내기보다 교체가 먼저다");
            Assert.That(install, Does.Contain("Fail(Blocked(blocked),"), "잡힌 폴더를 '사용 중' 으로 가리지 않는다");
            Assert.That(install, Does.Contain("DropOldPacks(root);"), "교체 뒤 옛 팩을 지우지 않는다 — 업데이트마다 약 2GB 가 남는다");

            string start = TestSource.Method(TestSource.Read("Runtime/Network/AiLocalRuntime.cs"), "void StartRuntime()");
            Assert.That(start, Does.Contain("AiPackProcesses.EndAbandoned(PackRoot);"), "워커를 띄우기 전에 남은 프로세스를 끝내지 않는다");
        }

        /// <summary>
        /// 실제 프로세스로: 부모를 잃은 것은 끝내고, 이 에디터가 주인인 것은 두는가.
        /// 팩 폴더 흉내는 임시 폴더에 <c>cmd.exe</c> · <c>ping.exe</c> 를 복사해 만든다.
        /// </summary>
        [Test]
        public void RealOrphansEndAndOwnedChildrenStay()
        {
            if (Application.platform != RuntimePlatform.WindowsEditor) Assert.Ignore("Windows 전용");
            string root = Path.Combine(Path.GetTempPath(), "ai745-" + Guid.NewGuid().ToString("N"), "p3");
            Directory.CreateDirectory(root);
            string cmd = Path.Combine(root, "cmd.exe"), ping = Path.Combine(root, "ping.exe");
            File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), cmd);
            File.Copy(Path.Combine(Environment.SystemDirectory, "ping.exe"), ping);
            Process owned = null;
            try
            {
                owned = Process.Start(new ProcessStartInfo(ping, "-n 30 127.0.0.1") { UseShellExecute = false, CreateNoWindow = true });
                // cmd 가 ping 을 띄우고 곧 끝난다 — ping 은 부모를 잃는다
                using (var parent = Process.Start(new ProcessStartInfo(cmd, "/c start \"\" /b \"" + ping + "\" -n 30 127.0.0.1")
                           { UseShellExecute = false, CreateNoWindow = true }))
                    parent.WaitForExit(5000);
                int orphan = 0;
                for (int i = 0; i < 50 && orphan == 0; i++)
                {
                    var found = AiPackProcesses.Abandoned(AiPackProcesses.Snapshot(), root);
                    if (found.Count > 0) orphan = found[0]; else Thread.Sleep(100);
                }
                Assert.That(orphan, Is.Not.EqualTo(0), "부모를 잃은 ping 을 찾지 못했다");
                Assert.That(orphan, Is.Not.EqualTo(owned.Id), "에디터가 주인인 ping 을 주인 없는 것으로 봤다");

                Assert.That(AiPackProcesses.EndAbandoned(root), Is.EqualTo(1));
                Assert.That(AiPackProcesses.Snapshot().Any(p => p.Pid == orphan && AiPackProcesses.Under(p.Image, root)), Is.False,
                    "끝냈다는데 남아 있다");
                Assert.That(owned.HasExited, Is.False, "주인이 살아 있는 것을 끝냈다 — 다른 게임 창의 AI 를 죽인다");
                Assert.That(AiPackProcesses.Running(root), Is.EqualTo(1));
            }
            finally
            {
                try { if (owned != null && !owned.HasExited) { owned.Kill(); owned.WaitForExit(2000); } } catch (Exception) { }
                owned?.Dispose();
                AiPackProcesses.EndAbandoned(root);
                try { Directory.Delete(Path.GetDirectoryName(root), true); } catch (Exception) { }
            }
        }
    }
}
