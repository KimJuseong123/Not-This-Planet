using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Metagame.Core
{
    /// <summary>
    /// 한 판에서 일어난 일을 <b>기계가 읽는 줄</b>로 남긴다 (S15P21B202-498).
    ///
    /// <para><b>평문 로그와 목적이 다르다.</b> <see cref="RunLog"/> 는 사람이 사고를 읽는
    /// 것이고 <c>Debug.Log</c> 를 전부 받아 적는다. 그래서 "파트 4개 중 몇 개째에서
    /// 포기하나" 를 물으려면 그 문구를 파싱해야 하고, 파싱 규칙은 문구가 바뀔 때마다
    /// 깨진다. 둘을 합치지 않고 나란히 둔다.
    ///
    /// <para><b>플레이 중에는 보내지 않는다.</b> 파일로만 쌓는다. 보내는 것은 다음 실행이
    /// 시작할 때 따로 한다 — 4 vCPU EC2 가 릴레이·매칭·CI 를 겸하므로 "플레이 중에
    /// 네트워킹을 쓰지 않는다" 는 <see cref="RunLogUpload"/> 가 이미 정한 것이다.
    ///
    /// <para><b>에디터에서는 돌지 않는다.</b> 에디터의 <c>Application.dataPath</c> 는 저장소
    /// 안이라 켜 두면 리포에 폴더가 생긴다 — <see cref="RunLog"/> 와 같은 이유다.
    /// </summary>
    public static class PlayEvents
    {
        /// <summary>빌드 폴더 옆의 폴더 이름. 평문 로그의 logs 와 나란히 둔다.</summary>
        public const string FolderName = "events";

        /// <summary>이 개수를 넘으면 오래된 것부터 지운다.</summary>
        public const int KeepFiles = 30;

        /// <summary>
        /// 한 판의 상한.
        ///
        /// <para>이벤트 한 줄이 120바이트 남짓이므로 2 MB 면 <b>보통 한 판의 수십 배</b>다.
        /// 여기 닿으면 <b>그 사실을 한 줄 남기고 멈춘다</b> — 조용히 끊기면 "왜 여기서
        /// 끝났지" 를 아무도 모른다.
        /// </summary>
        public const long MaxBytes = 2L * 1024 * 1024;

        static readonly object Gate = new object();
        static StreamWriter writer;
        static long written;
        static bool capped;
        static float startedAt;

        /// <summary>지금 쓰고 있는 파일. 없으면 빈 문자열.</summary>
        public static string Path { get; private set; } = string.Empty;

        /// <summary>그 판의 모든 줄에 붙는 번호. <b>빠진 번호가 곧 유실 신호</b>다.</summary>
        public static int Seq { get; private set; }

        /// <summary>이 사람의 이 판. 서버에서 (session, seq) 가 유일 열쇠가 된다.</summary>
        public static string Session { get; private set; } = string.Empty;

        /// <summary>돌고 있는가. 에디터·열기 실패에서는 거짓이다.</summary>
        public static bool Active => writer != null && !capped;

        /// <summary>파일 이름. 시각이 곧 정렬 순서다 — 평문 로그와 같은 규칙.</summary>
        public static string FileNameFor(DateTime when)
        {
            return when.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".ndjson";
        }

        /// <summary>
        /// 한 판을 연다. <paramref name="header"/> 는 첫 줄에 그대로 실린다.
        ///
        /// <para><b>머리줄에 지문을 싣는다</b>(S15P21B202-552). 이벤트의 숫자가 무엇을
        /// 뜻하는지는 게임 코드의 정의가 정하므로, 그 정의가 바뀌면 <b>옛 데이터와 새
        /// 데이터를 갈라 봐야 한다.</b> 지문이 없으면 조용히 섞인다.
        /// </summary>
        public static void Begin(IDictionary<string, object> header)
        {
            if (Application.isEditor) return;

            lock (Gate)
            {
                Stop();

                string folder = RunLog.FolderFor(FolderName, "EVT");
                if (string.IsNullOrEmpty(folder)) return;

                try
                {
                    Directory.CreateDirectory(folder);
                    Path = System.IO.Path.Combine(folder, FileNameFor(DateTime.Now));
                    // 크래시로 죽은 판이 정작 가장 알고 싶은 판이다 — 꼬리가 남아야 한다.
                    writer = new StreamWriter(Path, false) { AutoFlush = true };
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[EVT] 이벤트 파일을 열지 못했다 · " + folder + " · " + e.GetType().Name);
                    writer = null;
                    Path = string.Empty;
                    return;
                }

                Seq = 0;
                written = 0;
                capped = false;
                startedAt = Time.realtimeSinceStartup;
                Session = Guid.NewGuid().ToString("N");

                var head = header == null
                    ? new Dictionary<string, object>()
                    : new Dictionary<string, object>(header);
                head["session"] = Session;
                head["at"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

                Write("session_start", head);
                Trim(folder);
            }
        }

        /// <summary>한 줄 남긴다. 파일이 없으면 아무 일도 하지 않는다.</summary>
        public static void Write(string type, IDictionary<string, object> fields = null)
        {
            if (string.IsNullOrEmpty(type)) return;

            lock (Gate)
            {
                if (writer == null || capped) return;

                var sb = new StringBuilder(160);
                sb.Append('{');
                Pair(sb, "seq", ++Seq);
                sb.Append(',');
                Pair(sb, "t", Math.Round(Time.realtimeSinceStartup - startedAt, 1));
                sb.Append(',');
                Pair(sb, "type", type);
                if (fields != null)
                {
                    foreach (var kv in fields)
                    {
                        if (kv.Key == "seq" || kv.Key == "t" || kv.Key == "type") continue;
                        sb.Append(',');
                        Pair(sb, kv.Key, kv.Value);
                    }
                }
                sb.Append('}');

                string line = sb.ToString();
                try
                {
                    writer.WriteLine(line);
                }
                catch (Exception)
                {
                    Stop();
                    return;
                }

                written += line.Length + 1;
                if (written < MaxBytes) return;

                // 상한에 닿았다. **그 사실을 남기고 멈춘다** — 조용히 끊기면 안 된다.
                capped = true;
                try
                {
                    writer.WriteLine("{\"seq\":" + (++Seq).ToString(CultureInfo.InvariantCulture)
                                     + ",\"type\":\"capped\"}");
                }
                catch (Exception)
                {
                    // 상한 표시조차 못 쓰면 더 할 수 있는 것이 없다.
                }
            }
        }

        /// <summary>판을 닫는다. 두 번 불러도 괜찮다.</summary>
        public static void End(IDictionary<string, object> fields = null)
        {
            lock (Gate)
            {
                if (writer == null) return;

                // 마지막 한 줄은 상한과 무관하게 남긴다 — 그 줄이 곧 "어디서 포기했나" 다.
                bool wasCapped = capped;
                capped = false;
                Write("session_end", fields);
                capped = wasCapped;
                Stop();
            }
        }

        static void Stop()
        {
            if (writer == null) return;
            try
            {
                writer.Flush();
                writer.Dispose();
            }
            catch (Exception)
            {
                // 이미 닫혔을 수 있다.
            }
            writer = null;
        }

        /// <summary>오래된 파일부터 지운다. 반복하면 쌓이기만 한다.</summary>
        static void Trim(string folder)
        {
            try
            {
                var files = new List<string>(Directory.GetFiles(folder, "*.ndjson"));
                if (files.Count <= KeepFiles) return;
                files.Sort(StringComparer.Ordinal);
                for (int i = 0; i < files.Count - KeepFiles; i++)
                {
                    try
                    {
                        File.Delete(files[i]);
                    }
                    catch (Exception)
                    {
                        // 한 개를 못 지워도 나머지는 지운다.
                    }
                }
            }
            catch (Exception)
            {
                // 정리 실패가 판을 막을 이유는 없다.
            }
        }

        /// <summary>JSON 한 쌍.</summary>
        static void Pair(StringBuilder sb, string key, object value)
        {
            sb.Append('"').Append(Escape(key)).Append("\":");
            if (value == null)
            {
                sb.Append("null");
                return;
            }
            if (value is bool b)
            {
                sb.Append(b ? "true" : "false");
                return;
            }
            if (value is int i)
            {
                sb.Append(i.ToString(CultureInfo.InvariantCulture));
                return;
            }
            if (value is long l)
            {
                sb.Append(l.ToString(CultureInfo.InvariantCulture));
                return;
            }
            if (value is float f)
            {
                sb.Append(f.ToString("0.###", CultureInfo.InvariantCulture));
                return;
            }
            if (value is double d)
            {
                sb.Append(d.ToString("0.###", CultureInfo.InvariantCulture));
                return;
            }
            if (value is int[] arr)
            {
                sb.Append('[');
                for (int k = 0; k < arr.Length; k++)
                {
                    if (k > 0) sb.Append(',');
                    sb.Append(arr[k].ToString(CultureInfo.InvariantCulture));
                }
                sb.Append(']');
                return;
            }
            sb.Append('"').Append(Escape(value.ToString())).Append('"');
        }

        /// <summary>
        /// JSON 문자열로 안전하게.
        ///
        /// <para><b>줄바꿈이 섞이면 NDJSON 이 통째로 깨진다</b> — 한 줄이 한 이벤트라는
        /// 약속이 무너지면 그 파일 전체를 못 읽는다. 자유 문자열은 애초에 안 싣는 것이
        /// 규칙이지만, 규칙에 기대지 않고 여기서 막는다.
        /// </summary>
        public static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                if (c == '"') sb.Append("\\\"");
                else if (c == '\\') sb.Append("\\\\");
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\r') sb.Append("\\r");
                else if (c == '\t') sb.Append("\\t");
                else if (c < 32) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
