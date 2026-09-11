using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
        /// <para>8 MB 는 보통 한 판의 수십 배다. 여기 닿는다는 것은 무언가 잘못됐다는
        /// 뜻이므로, 그때는 <b>앞부분이 남는다</b> — 무엇이 폭주를 시작했는지가 앞에 있다.</para>
        /// </summary>
        public const long MaxBytes = 8L * 1024 * 1024;

        static long written;
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
        static string Folder()
        {
            try
            {
                var parent = Directory.GetParent(Application.dataPath);
                if (parent != null)
                {
                    string beside = System.IO.Path.Combine(parent.FullName, FolderName);
                    if (Writable(parent.FullName)) return beside;

                    Debug.LogWarning($"[RUN] 빌드 폴더에 쓸 수 없다 ({parent.FullName}) "
                                     + "— persistentDataPath 로 남긴다");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RUN] 빌드 폴더를 찾지 못했다 · {e.GetType().Name}");
            }

            try
            {
                return System.IO.Path.Combine(Application.persistentDataPath, FolderName);
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
                    // **상한에 닿으면 멈춘다.** 그 사실을 한 줄 남기지 않으면 "로그가 왜
                    // 여기서 끝났지" 를 아무도 모르고, 그 판을 다시 재현하려 든다.
                    if (written >= MaxBytes)
                    {
                        capped = true;
                        writer.WriteLine($"[W] 로그가 상한({MaxBytes / 1048576}MB)에 닿아 여기서 멈춘다 — 앞부분이 원인에 가깝다");
                        writer.Flush();
                        return;
                    }

                    writer.Write(Tag(type));
                    writer.Write(' ');
                    writer.WriteLine(message);
                    written += message.Length + 5;

                    // 스택은 예외에만 싣는다. 모든 줄에 싣던 Player.log 는 그 때문에
                    // 사람이 읽기 어렵고 용량도 몇십 배가 된다(2026-09-09 실측: 31MB).
                    if (type == LogType.Exception || type == LogType.Error)
                    {
                        if (!string.IsNullOrEmpty(stackTrace))
                        {
                            writer.WriteLine(stackTrace.TrimEnd());
                            written += stackTrace.Length;
                        }
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
        /// 오래된 파일을 지운다. 실험을 반복하면 쌓이기만 하고, 폴더를 통째로 압축해
        /// 넘기는 것이 이 기능의 사용법이므로 옛것이 섞이면 받는 쪽이 헷갈린다.
        /// </summary>
        static void Trim()
        {
            try
            {
                string folder = System.IO.Path.GetDirectoryName(Path);
                if (string.IsNullOrEmpty(folder)) return;

                var files = new List<string>(Directory.GetFiles(folder, "*.log"));
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
