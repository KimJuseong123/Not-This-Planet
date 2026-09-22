using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Metagame.Core
{
    /// <summary>
    /// 실험 빌드가 <b>실행마다 새 로그 파일</b>을 빌드 폴더에 남긴다 (S15P21B202-380).
    ///
    /// <para><b>왜 필요한가.</b> Unity 플레이어는 늘 같은 경로에 쓰고
    /// (<c>%USERPROFILE%\AppData\LocalLow\…\Player.log</c>) <b>다시 실행하면 덮어쓴다</b> —
    /// 직전 것 하나만 <c>Player-prev.log</c> 로 남는다. 빌드가 달라도 같은 경로라 평소
    /// 빌드와 실험 빌드를 번갈아 돌리면 섞인다. 그래서 사람이 매번 손으로 복사해 이름을
    /// 붙여야 했고, 2026-09-09 에는 그러다 <b>누가 호스트였는지 잘못 읽었다.</b></para>
    ///
    /// <para><b>빌드 폴더 안에 쓴다.</b> 실험이 끝나면 그 폴더를 그대로 압축해 넘기면
    /// 된다 — 파일 이름을 손으로 붙이는 단계가 없어야 한다. 역할·라벨·커밋은 파일
    /// 앞머리에 들어 있다.</para>
    ///
    /// <para><b>모든 빌드에서 돈다</b> (S15P21B202-443). 전에는 실험 빌드에서만 켜졌는데,
    /// 그러면 <b>배포판에서 문제가 나도 로그가 아예 없다</b> — 실유저는 로그를 찾아
    /// 보내 주지 않고, 참가자 화면에서만 나는 문제는 그 사람의 로그가 있어야 판정된다.</para>
    ///
    /// <para><b>단, 에디터는 예외다.</b> 에디터의 <c>Application.dataPath</c> 는 저장소
    /// 안이라 켜 두면 <b>리포에 로그 폴더가 생긴다.</b> 예전의 실험 라벨 검사가 이것을
    /// 겸해서 막고 있었으므로, 라벨 검사를 없애는 대신 에디터를 명시적으로 막는다.</para>
    ///
    /// <para><b>대신 상한을 둔다.</b> 배포판은 몇 시간씩 켜 두는 사람이 있고, 로그가
    /// 무한히 커지면 디스크를 먹고 올릴 수도 없다. <see cref="MaxBytes"/> 를 넘으면
    /// <b>그 사실을 한 줄 남기고 쓰기를 멈춘다</b> — 조용히 끊기면 "로그가 왜 여기서
    /// 끝났지" 를 아무도 모른다.</para>
    ///
    /// </summary>
    public static class RunLog
    {
        /// <summary>빌드 폴더 안의 로그 폴더 이름.</summary>
        public const string FolderName = "logs";

        /// <summary>이 개수를 넘으면 오래된 것부터 지운다. 실험을 반복하면 쌓이기만 한다.</summary>
        public const int KeepFiles = 30;

        /// <summary>
        /// 한 판의 로그 상한 (S15P21B202-443).
        ///
        /// <para>2026-09-11 QA 에서 호스트 로그가 109,643줄이었다(그 97%는 `-433` 이
        /// 없앤 경고다). 그래도 상한이 없으면 오래 켜 두는 사람의 파일이 계속 큰다 —
        /// 디스크도 문제지만 <b>서버로 올릴 수 없게 되는 것</b>이 더 문제다.</para>
        ///
        /// <para>8 MB 는 보통 한 판의 수십 배다. 여기 닿으면 <b>가운데를 버리고 머리와
        /// 꼬리를 남긴다</b> — <see cref="Compact"/> 가 그 이유를 적고 있다.</para>
        /// </summary>
        public const long MaxBytes = 8L * 1024 * 1024;

        /// <summary>
        /// 상한에 닿아도 남기는 <b>머리</b> 크기. 빌드·역할·커밋이 여기 있고, 그것이
        /// 없으면 받은 쪽이 <b>무엇의 로그인지</b> 알 수 없다.
        /// </summary>
        public const long HeadBytes = 64L * 1024;

        /// <summary>더 쓸 수 없게 된 상태. 압축에 실패했을 때만 선다.</summary>
        static bool capped;

        static readonly object Gate = new object();
        static StreamWriter writer;

        /// <summary>지금 쓰고 있는 파일. 없으면 빈 문자열.</summary>
        public static string Path { get; private set; } = string.Empty;

        /// <summary>
        /// 파일 이름. <b>시각순으로 정렬되고 사람이 읽을 수 있어야 한다</b> —
        /// 여러 사람이 보낸 것을 한 폴더에 모아 놓고 보게 된다.
        /// </summary>
        public static string FileNameFor(DateTime when)
        {
            return when.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".log";
        }

        /// <summary>
        /// 시작한다. <see cref="BuildInfo"/> 가 라벨을 읽은 <b>직후</b> 부른다.
        ///
        /// <para>여기서 <c>RuntimeInitializeOnLoadMethod</c> 를 따로 달지 않는 이유:
        /// 같은 단계의 초기화 순서는 보장되지 않아 <see cref="BuildInfo.Experiment"/> 가
        /// 아직 비어 있을 수 있다. 읽은 쪽이 부르면 순서가 정해진다.</para>
        /// </summary>
        internal static void Start()
        {
            // **에디터에서는 돌지 않는다.** 에디터의 `Application.dataPath` 는 저장소
            // 안(`METAGAME/Assets`)이라 그 부모에 쓰면 **리포에 `METAGAME/logs/` 가
            // 생긴다.** 전에는 실험 라벨 검사가 이것까지 겸해서 막고 있었는데,
            // `-443` 이 그 검사를 없애면서 막는 것이 사라졌다(CI 가 잡아냈다).
            //
            // 막아야 할 것은 **라벨이 아니라 에디터**다 — 배포 빌드에서 켜는 것이
            // 이 변경의 목적이고, 배포·실험 빌드는 둘 다 에디터가 아니다.
            if (Application.isEditor) return;

            lock (Gate)
            {
                if (writer != null) return;

                string folder = Folder();
                if (string.IsNullOrEmpty(folder)) return;

                try
                {
                    Directory.CreateDirectory(folder);
                    Path = System.IO.Path.Combine(folder, FileNameFor(DateTime.Now));
                    writer = new StreamWriter(Path, false)
                    {
                        // 크래시로 죽어도 꼬리가 남아야 한다 — 그때가 로그가 가장 필요한 때다.
                        // 테스트 빌드라 이 정도 비용은 치른다.
                        AutoFlush = true,
                    };
                }
                catch (Exception e)
                {
                    // 로그를 못 남기는 것이 게임을 멈출 이유는 아니다. 다만 조용히 넘기지
                    // 않는다 — 로그가 없는 이유를 사람이 알아야 한다.
                    Debug.LogWarning($"[RUN] 로그 파일을 열지 못했다 · {folder} · {e.GetType().Name}: {e.Message}");
                    writer = null;
                    Path = string.Empty;
                    return;
                }

                Application.logMessageReceivedThreaded += OnLog;
                Application.quitting += Stop;
            }

            Debug.Log($"[RUN] 이 실행의 로그: {Path}");
            Trim();
        }

        /// <summary>
        /// 쓸 폴더. <b>빌드 폴더 안</b>이 기본이고, 막히면 <c>persistentDataPath</c> 로 떨어진다.
        ///
        /// <para>Program Files 같은 곳에 설치하면 실행 파일 옆에 쓸 수 없다. 그때 조용히
        /// 실패하면 "로그가 안 남는다" 가 되므로 대체 위치를 쓰고 그 사실을 남긴다.</para>
        /// </summary>
        static string Folder() => FolderFor(FolderName, "RUN");

        /// <summary>
        /// 빌드 폴더 옆의 <paramref name="name"/> 폴더. 막히면 <c>persistentDataPath</c>.
        ///
        /// <para><b>플레이 이벤트도 같은 규칙을 쓴다</b>(S15P21B202-498). 규칙을 두 벌로
        /// 두면 한쪽만 고쳐져 <b>로그는 남는데 이벤트는 안 남는</b> 상태가 생긴다 —
        /// 그때 원인을 찾기가 가장 어렵다.</para>
        /// </summary>
        internal static string FolderFor(string name, string tag)
        {
            try
            {
                var parent = Directory.GetParent(Application.dataPath);
                if (parent != null)
                {
                    string beside = System.IO.Path.Combine(parent.FullName, name);
                    if (Writable(parent.FullName)) return beside;

                    Debug.LogWarning($"[{tag}] 빌드 폴더에 쓸 수 없다 ({parent.FullName}) "
                                     + "— persistentDataPath 로 남긴다");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[{tag}] 빌드 폴더를 찾지 못했다 · {e.GetType().Name}");
            }

            try
            {
                return System.IO.Path.Combine(Application.persistentDataPath, name);
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        /// <summary>실제로 써 보고 판단한다 — 권한 규칙을 흉내 내면 틀린다.</summary>
        static bool Writable(string folder)
        {
            string probe = System.IO.Path.Combine(folder, ".runlog-probe");
            try
            {
                File.WriteAllText(probe, "x");
                File.Delete(probe);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        static void OnLog(string message, string stackTrace, LogType type)
        {
            lock (Gate)
            {
                if (writer == null || capped) return;

                try
                {
                    // **상한 판정은 실제 파일 크기로 한다.** 글자 수로 세면 한글이
                    // UTF-8 에서 글자당 3바이트라 파일이 상한의 세 배까지 큰다 —
                    // 그러면 서버(8 MB)와 nginx(9 MB)가 거절한다.
                    // `AutoFlush` 라 Position 이 곧 파일 크기다.
                    if (writer.BaseStream.Position >= MaxBytes) Compact();
                    if (writer == null || capped) return;

                    writer.Write(Tag(type));
                    writer.Write(' ');
                    writer.WriteLine(message);

                    // 스택은 예외에만 싣는다. 모든 줄에 싣던 Player.log 는 그 때문에
                    // 사람이 읽기 어렵고 용량도 몇십 배가 된다(2026-09-09 실측: 31MB).
                    if (type == LogType.Exception || type == LogType.Error)
                    {
                        if (!string.IsNullOrEmpty(stackTrace))
                            writer.WriteLine(stackTrace.TrimEnd());
                    }
                }
                catch (Exception)
                {
                    // 쓰는 도중 실패하면 더 쓰지 않는다. 여기서 Debug.Log 를 부르면
                    // 이 핸들러가 다시 불려 무한히 감긴다.
                    writer = null;
                }
            }
        }

        /// <summary>
        /// 상한에 닿으면 <b>가운데를 버리고 머리와 꼬리를 남긴다</b> (S15P21B202-443).
        ///
        /// <para><b>왜 그냥 멈추지 않는가.</b> 멈추면 앞부분만 남는다. 그런데 사람이
        /// 신고하는 문제는 보통 <b>마지막에</b> 일어난다 — 튕김·멈춤·크래시가 그렇다.
        /// 상한에 닿을 만큼 커졌다는 것은 무언가 폭주했다는 뜻이고, 그때 정작 필요한
        /// 마지막 구간이 통째로 사라진다.</para>
        ///
        /// <para><b>왜 꼬리만 남기지 않는가.</b> 파일 앞머리에 빌드·역할·커밋이 있다.
        /// 그것이 없으면 받은 쪽이 무엇의 로그인지 알 수 없다.</para>
        ///
        /// <para>자르는 자리는 <b>줄바꿈에 맞춘다.</b> UTF-8 에서 <c>\n</c> 바이트는 여러
        /// 바이트 문자의 가운데에 나타나지 않으므로, 줄 경계로 자르면 글자가 깨지지
        /// 않는다. 바이트 수로 아무 데나 자르면 받은 로그의 그 줄이 깨져 나온다.</para>
        ///
        /// <para>압축 뒤 파일은 약 <see cref="HeadBytes"/> + <see cref="MaxBytes"/>/2 라,
        /// 다음 압축까지 다시 4 MB 가 필요하다 — 줄마다 도는 일이 아니다.</para>
        /// </summary>
        static void Compact()
        {
            string path = Path;
            try
            {
                writer.Flush();
                writer.Dispose();
                writer = null;

                byte[] head, tail;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                {
                    long len = fs.Length;

                    int headLen = (int)Math.Min(HeadBytes, len);
                    head = new byte[headLen];
                    ReadExactly(fs, head);

                    int tailLen = (int)Math.Min(MaxBytes / 2, len - headLen);
                    tail = new byte[tailLen];
                    fs.Seek(len - tailLen, SeekOrigin.Begin);
                    ReadExactly(fs, tail);
                }

                int headEnd = LastNewline(head);     // 마지막 완전한 줄까지
                int tailStart = FirstNewline(tail);  // 첫 줄바꿈 **다음**부터

                using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
                {
                    fs.Write(head, 0, headEnd);

                    var mark = Encoding.UTF8.GetBytes(Environment.NewLine
                        + $"[W] 로그가 상한({MaxBytes / 1048576}MB)에 닿아 가운데를 버렸다 — 머리와 꼬리만 남는다"
                        + Environment.NewLine);
                    fs.Write(mark, 0, mark.Length);

                    fs.Write(tail, tailStart, tail.Length - tailStart);
                }

                writer = new StreamWriter(path, true) { AutoFlush = true };
            }
            catch (Exception)
            {
                // 압축에 실패하면 더 쓰지 않는다. 여기서 Debug 를 부르면 이 핸들러가
                // 다시 불려 무한히 감긴다 — 그래서 조용히 닫는다.
                try { if (writer != null) writer.Dispose(); }
                catch (Exception) { /* 끝내는 중이다 */ }
                writer = null;
                capped = true;
            }
        }

        static void ReadExactly(Stream s, byte[] buf)
        {
            int got = 0;
            while (got < buf.Length)
            {
                int n = s.Read(buf, got, buf.Length - got);
                if (n <= 0) break;
                got += n;
            }
        }

        /// <summary>마지막 줄바꿈까지의 길이. 없으면 전체(자를 곳이 없다).</summary>
        static int LastNewline(byte[] buf)
        {
            for (int i = buf.Length - 1; i >= 0; i--)
                if (buf[i] == (byte)'\n') return i + 1;
            return buf.Length;
        }

        /// <summary>첫 줄바꿈 다음 위치. 없으면 0(자를 곳이 없다).</summary>
        static int FirstNewline(byte[] buf)
        {
            for (int i = 0; i < buf.Length; i++)
                if (buf[i] == (byte)'\n') return i + 1;
            return 0;
        }

        static string Tag(LogType type)
        {
            switch (type)
            {
                case LogType.Error: return "[E]";
                case LogType.Assert: return "[A]";
                case LogType.Warning: return "[W]";
                case LogType.Exception: return "[X]";
                default: return "[I]";
            }
        }

        static void Stop()
        {
            lock (Gate)
            {
                if (writer == null) return;

                Application.logMessageReceivedThreaded -= OnLog;
                try
                {
                    writer.Flush();
                    writer.Dispose();
                }
                catch (Exception)
                {
                    // 끝내는 중이다. 여기서 할 수 있는 것이 없다.
                }
                writer = null;
            }
        }

        /// <summary>
        /// 판이 끝났다 — 지금 파일을 닫고 <b>다음 판을 새 파일에 쓴다</b>
        /// (S15P21B202-685). 닫은 파일의 경로를 돌려준다(없었으면 빈 문자열).
        ///
        /// <para><b>이것이 없으면 방금 한 판의 로그가 다음 실행까지 올라가지 않는다.</b>
        /// 쓸이(<c>RunLogUpload.Pending</c>)는 <see cref="Path"/> 를 일부러 제외하는데
        /// 그 제외가 맞다 — 아직 쓰고 있는 파일을 보내면 꼬리가 잘린다. 그래서 보낼 수
        /// 있게 <b>닫아 주는 쪽</b>이 필요하다. 2026-09-22 QA 에서 가장 보고 싶은 판이
        /// 늘 로컬에만 남아 사람이 손으로 옮겼다.</para>
        ///
        /// <para><b>앱 종료 경로에서는 부르지 않는다.</b> 거기서 시작한 전송은 끝나지
        /// 못한다 — 그 결정은 <c>RunLogUpload</c> 주석에 있고 <c>PlayObserver</c> 의
        /// <c>if (sweep)</c> 가 그것을 지킨다. 이 함수는 판이 끝나고 게임이 살아 있는
        /// 동안만 쓴다.</para>
        ///
        /// <para><b>훅은 떼지 않는다.</b> <see cref="Stop"/> 는 끝내는 길이라
        /// <c>logMessageReceivedThreaded</c> 를 떼지만, 여기서 떼었다 붙이면 그 사이의
        /// 줄이 사라진다 — 판이 막 끝난 직후가 정리 로그가 가장 많은 때다.</para>
        /// </summary>
        internal static string Rotate()
        {
            if (Application.isEditor) return string.Empty;

            lock (Gate)
            {
                if (writer == null) return string.Empty;

                string closed = Path;
                try
                {
                    writer.Flush();
                    writer.Dispose();
                }
                catch (Exception)
                {
                    // 닫는 데 실패해도 다음 판은 새 파일에 써야 한다.
                }
                writer = null;

                string folder = Folder();
                if (string.IsNullOrEmpty(folder))
                {
                    Path = string.Empty;
                    return closed;
                }

                try
                {
                    Path = NextFreePath(folder, DateTime.Now);
                    writer = new StreamWriter(Path, false) { AutoFlush = true };
                }
                catch (Exception e)
                {
                    // **조용히 넘기지 않는다.** 여기서 실패하면 이 판 뒤의 로그가
                    // 아무 데도 안 남는다 — 그 사실이 닫은 파일에는 못 들어가므로
                    // 화면 로그로라도 남아야 한다.
                    Debug.LogWarning("[RUN] 판이 끝나 새 로그 파일을 열지 못했다 · "
                                     + folder + " · " + e.GetType().Name);
                    writer = null;
                    Path = string.Empty;
                    return closed;
                }

                Debug.Log("[RUN] 판이 끝나 로그를 닫았다: " + closed
                          + " · 다음 판은 " + Path);
                return closed;
            }
        }

        /// <summary>
        /// 아직 없는 파일 이름. 같은 초에 두 번 회전하면 이름이 겹치는데, 그때
        /// <c>new StreamWriter(path, false)</c> 는 <b>앞 판을 지운다</b> — 보내기도
        /// 전에 사라진다. 그래서 겹치면 뒤에 번호를 붙인다.
        /// </summary>
        public static string NextFreePath(string folder, DateTime when)
        {
            string name = FileNameFor(when);
            string candidate = System.IO.Path.Combine(folder, name);
            if (!File.Exists(candidate)) return candidate;

            string stem = name.Substring(0, name.Length - 4);   // ".log" 를 뗀다
            for (var n = 2; n <= 99; n++)
            {
                candidate = System.IO.Path.Combine(folder, stem + "-" + n + ".log");
                if (!File.Exists(candidate)) return candidate;
            }
            return candidate;
        }

        /// <summary>
        /// 오래된 파일을 지운다. 실험을 반복하면 쌓이기만 하고, 폴더를 통째로 압축해
        /// 넘기는 것이 이 기능의 사용법이므로 옛것이 섞이면 받는 쪽이 헷갈린다.
        /// </summary>
        static void Trim()
        {
            try
            {
                string folder = System.IO.Path.GetDirectoryName(Path);
                if (string.IsNullOrEmpty(folder)) return;

                // `*.log.sent` 도 센다 — 보낸 뒤 이름을 바꾸므로(`RunLogUpload`),
                // `*.log` 만 세면 보낸 파일이 영원히 쌓인다.
                var files = new List<string>(Directory.GetFiles(folder, "*.log"));
                files.AddRange(Directory.GetFiles(folder, "*.log" + RunLogUpload.SentSuffix));
                if (files.Count <= KeepFiles) return;

                // 이름이 yyyyMMdd-HHmmss 라 문자열 정렬이 곧 시각 정렬이다.
                files.Sort(StringComparer.Ordinal);
                for (int i = 0; i < files.Count - KeepFiles; i++)
                {
                    try { File.Delete(files[i]); }
                    catch (Exception) { /* 열려 있을 수 있다. 다음 실행에서 다시 시도된다 */ }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RUN] 오래된 로그를 정리하지 못했다 · {e.GetType().Name}");
            }
        }
    }
}
