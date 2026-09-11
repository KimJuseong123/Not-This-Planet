using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace Metagame.Core
{
    /// <summary>
    /// 지난 실행의 로그를 서버로 보낸다 (S15P21B202-443).
    ///
    /// <para><b>왜 필요한가.</b> 지금 로그는 각자 기계에만 남고 사람이 손으로 모아
    /// 온다. 배포하면 그 방법이 끝난다 — 실유저는 로그를 찾아 보내 주지 않는다.
    /// 그리고 참가자 화면에서만 나는 문제는 <b>그 사람의 로그</b>가 있어야 판정된다.</para>
    ///
    /// <para><b>왜 종료가 아니라 시작할 때 보내나.</b> 이슈의 팀 결정은 "게임 종료 때
    /// 한 번" 이었고, 그 이유는 <b>플레이 중에 네트워킹을 쓰지 않는다</b>였다(4 vCPU
    /// EC2 가 릴레이·매칭·CI 를 겸한다). 시작할 때 보내도 그 이유는 그대로 지켜진다.
    /// 그러면서 두 가지가 더 좋아진다:</para>
    /// <list type="bullet">
    ///   <item><b>종료가 한 순간도 지연되지 않는다.</b> 유니티에서 종료 중에 HTTP 를
    ///         끝내려면 종료를 붙잡아야 하는데, 그것이 완료 조건이 금지한 바로 그 일이다</item>
    ///   <item><b>크래시로 죽은 판도 회수된다.</b> 이슈가 "정작 가장 알고 싶은 경우" 라고
    ///         적어 둔 그 판이다 — 종료 훅은 그때 아예 안 불린다</item>
    /// </list>
    ///
    /// <para>대가는 하나다. <b>한 번만 플레이하고 다시 켜지 않는 사람의 로그는 오지
    /// 않는다.</b> 알파 테스트는 같은 사람이 여러 번 켜므로 감수한다.</para>
    ///
    /// <para><b>로그인 뒤에 부른다.</b> 서버가 게스트 토큰을 요구한다 — 아무나 우리
    /// 서버에 파일을 부어넣지 못하게 하는 것이 그 토큰의 역할이다.</para>
    /// </summary>
    public static class RunLogUpload
    {
        /// <summary>보낸 파일에 붙이는 표시. 이름을 바꿔 두면 두 번 보내지 않는다.</summary>
        public const string SentSuffix = ".sent";

        /// <summary>한 번에 보내는 개수. 밀린 것이 많아도 시작을 붙잡지 않는다.</summary>
        public const int MaxPerRun = 3;

        /// <summary>
        /// 보낼 수 있는 최대 크기. <see cref="RunLog.MaxBytes"/> 와 같게 둔다 —
        /// 쓰는 쪽이 그보다 크게 만들지 않으므로, 이 값에 걸리는 것은 옛 빌드가 남긴
        /// 파일뿐이다. 그때는 <b>뒤쪽</b>을 보낸다(문제는 보통 끝에 있다).
        /// </summary>
        public const long MaxUploadBytes = RunLog.MaxBytes;

        /// <summary>한 요청의 시간 상한. 서버가 죽어 있어도 시작이 느려지면 안 된다.</summary>
        public const int TimeoutSeconds = 15;

        static bool ran;

        /// <summary>
        /// 지난 실행들의 로그를 보낸다. 로그인 직후 <b>한 번만</b> 부른다.
        ///
        /// <para>실패는 조용히 넘긴다 — 로그를 못 보내는 것이 게임을 막을 이유는 아니다.
        /// 다만 <b>왜 못 보냈는지는 로그에 남긴다.</b> 그 로그가 다음 실행에 올라간다.</para>
        /// </summary>
        public static IEnumerator SweepOnce(string baseUrl, string token, long userId)
        {
            if (ran) yield break;
            ran = true;

            if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(token)) yield break;

            List<string> targets;
            try
            {
                targets = Pending();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RUN] 보낼 로그를 찾지 못했다 · {e.GetType().Name}");
                yield break;
            }
            if (targets.Count == 0) yield break;

            Debug.Log($"[RUN] 지난 실행 로그 {targets.Count}개를 보낸다");
            foreach (var path in targets)
            {
                yield return Send(baseUrl, token, userId, path);
            }
        }

        /// <summary>
        /// 아직 안 보낸 로그. <b>이번 실행의 파일은 제외한다</b> — 지금 쓰고 있는 중이라
        /// 보내 봐야 반쪽이고, 다음 실행에서 온전한 것이 간다.
        /// </summary>
        public static List<string> Pending()
        {
            var result = new List<string>();
            string folder = RunLog.Path.Length > 0 ? Path.GetDirectoryName(RunLog.Path) : null;
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return result;

            string current = RunLog.Path;
            var files = new List<string>(Directory.GetFiles(folder, "*.log"));
            // 이름이 yyyyMMdd-HHmmss 라 문자열 정렬이 곧 시각 정렬이다. 새것부터 보낸다 —
            // 밀려 있을 때 오래된 것보다 최근 것이 쓸모 있다.
            files.Sort(StringComparer.Ordinal);
            files.Reverse();

            foreach (var f in files)
            {
                if (string.Equals(f, current, StringComparison.OrdinalIgnoreCase)) continue;
                if (File.Exists(f + SentSuffix)) continue;
                result.Add(f);
                if (result.Count >= MaxPerRun) break;
            }
            return result;
        }

        static IEnumerator Send(string baseUrl, string token, long userId, string path)
        {
            byte[] body;
            string name;
            try
            {
                name = Path.GetFileName(path);
                body = Tail(path, MaxUploadBytes);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RUN] {Path.GetFileName(path)} 를 읽지 못했다 · {e.GetType().Name}");
                yield break;
            }
            if (body == null || body.Length == 0)
            {
                Mark(path);      // 빈 파일은 다시 시도할 값이 없다
                yield break;
            }

            string url = $"{baseUrl.TrimEnd('/')}/logs";
            using (var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
            {
                req.uploadHandler = new UploadHandlerRaw(body) { contentType = "text/plain; charset=utf-8" };
                req.downloadHandler = new DownloadHandlerBuffer();
                req.timeout = TimeoutSeconds;

                // 토큰은 헤더로만 보낸다. 쿼리에 실으면 nginx 접근 로그에 남는다.
                req.SetRequestHeader("Authorization", "Bearer " + token);

                // **무엇으로 만든 빌드의 누구인지**를 함께 싣는다. 이것이 없으면 모아
                // 놓고도 어느 빌드의 어느 판인지 알 수 없다(-443 완료 조건).
                req.SetRequestHeader("X-Run-File", name);
                req.SetRequestHeader("X-Run-Build", Sanitize(BuildInfo.Stamp));
                req.SetRequestHeader("X-Run-Backend", BackendEndpoints.IsDev ? "dev" : "prod");
                req.SetRequestHeader("X-Run-User", userId.ToString());

                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.Success)
                {
                    Mark(path);
                }
                else
                {
                    // 다시 시도할 수 있게 표시를 남기지 않는다. 다음 실행에서 또 온다.
                    Debug.LogWarning($"[RUN] {name} 전송 실패 · {req.responseCode} {req.result}");
                }
            }
        }

        /// <summary>보냈다고 표시한다. 파일을 지우지 않는 이유: 사람이 직접 볼 때가 있다.</summary>
        static void Mark(string path)
        {
            try { File.WriteAllText(path + SentSuffix, string.Empty); }
            catch (Exception) { /* 표시를 못 해도 서버가 같은 이름을 덮어쓴다 */ }
        }

        /// <summary>
        /// 파일의 <b>뒤쪽</b> 최대 <paramref name="cap"/> 바이트. 상한을 넘는 옛 파일은
        /// 끝이 더 쓸모 있다 — 문제는 보통 마지막에 있다.
        /// </summary>
        public static byte[] Tail(string path, long cap)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                long len = fs.Length;
                if (len <= cap)
                {
                    var all = new byte[len];
                    ReadExactly(fs, all);
                    return all;
                }

                fs.Seek(len - cap, SeekOrigin.Begin);
                var buf = new byte[cap];
                ReadExactly(fs, buf);
                return buf;
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

        /// <summary>헤더에 넣을 수 있게 줄바꿈과 제어문자를 없앤다.</summary>
        public static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (c == '\n' || c == '\r') { sb.Append(' '); continue; }
                if (c < 32 || c == 127) continue;
                sb.Append(c);
            }
            return sb.ToString().Trim();
        }
    }
}
