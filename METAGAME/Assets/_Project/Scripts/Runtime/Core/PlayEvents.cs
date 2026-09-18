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
    /// 안이라 켜 두면 리포에 폴더가 생긴다 — <see cref="RunLog"/> 와 같은 이유다. 그
    /// 판단은 <see cref="DefaultFolder"/> 한 곳에 있고, 검사는 거기만 바꿔 끼운다.
    /// </summary>
    public static class PlayEvents
    {
        /// <summary>빌드 폴더 옆의 폴더 이름. 평문 로그의 logs 와 나란히 둔다.</summary>
        public const string FolderName = "events";

        /// <summary>
        /// 이벤트 파일의 확장자. <b>보내는 쪽(<see cref="RunLogUpload"/>)이 이 값으로
        /// 찾는다</b> (S15P21B202-607) — 양쪽에 글자를 따로 적어 두면 한쪽만 바뀌는 날
        /// 수집이 조용히 멈춘다.
        /// </summary>
        public const string Extension = ".ndjson";

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

        /// <summary>더 쓸 것인가. 에디터·열기 실패·상한에서는 거짓이다.</summary>
        public static bool Active => writer != null && !capped;

        /// <summary>
        /// 파일이 열려 있는가. <b>상한에 닿아도 참이다.</b>
        ///
        /// <para><see cref="Active"/> 와 갈라 둔다. 상한에 닿은 판은 "더 쓰지 않는다" 이지
        /// "닫혔다" 가 아니다 — <see cref="End"/> 는 그때도 마지막 한 줄을 남긴다. 닫는
        /// 쪽이 <c>Active</c> 를 보고 일찍 돌아서면 <b>가장 알고 싶은 판에서 그 줄이
        /// 사라진다</b>(<c>!471</c> AI 리뷰).
        /// </summary>
        public static bool Open => writer != null;

        /// <summary>
        /// 어디에 쓸 것인가. <b>검사에서만 바꾼다.</b>
        ///
        /// <para>여기를 갈라 두기 전에는 <see cref="Begin"/> 이 <c>Application.isEditor</c>
        /// 로 즉시 돌아서서, 에디터 검사가 <b>파일에 관한 것은 아무것도 확인하지 못했다</b>
        /// — 상한·종료 같은 정작 조용히 틀리는 자리가 통째로 검사 밖이었다
        /// (<c>!471</c> AI 리뷰). 배포판 동작은 <see cref="DefaultFolder"/> 그대로다.
        /// </summary>
        public static Func<string> FolderResolver = DefaultFolder;

        /// <summary>
        /// 배포판이 쓰는 자리. <b>에디터에서는 빈 값</b>이라 아무것도 안 열린다.
        ///
        /// <para>에디터의 <c>Application.dataPath</c> 는 저장소 안이라, 켜 두면 리포에
        /// <c>METAGAME/events/</c> 가 생긴다 — <see cref="RunLog"/> 와 같은 이유다.
        /// </summary>
        public static string DefaultFolder()
        {
            return Application.isEditor ? string.Empty : RunLog.FolderFor(FolderName, "EVT");
        }

        /// <summary>파일 이름. 시각이 곧 정렬 순서다 — 평문 로그와 같은 규칙.</summary>
        public static string FileNameFor(DateTime when)
        {
            return when.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + Extension;
        }

        /// <summary>
        /// 아직 없는 이름으로. <b>이름이 초 단위라 같은 초에 두 판이 열릴 수 있다.</b>
        ///
        /// <para>그대로 열면 <c>StreamWriter</c> 가 앞 파일을 통째로 비운다 — 방금 제대로
        /// 닫아 둔 판이 사라진다. 꼬리는 <c>_2</c> 처럼 붙이는데, <c>_</c> 가 <c>.</c> 보다
        /// 뒤라 <b>정렬 순서도 그대로</b> 유지된다(오래된 것부터 지우는 규칙이 여기 기댄다).
        /// </summary>
        static string Unused(string folder, string name)
        {
            string full = System.IO.Path.Combine(folder, name);
            if (!File.Exists(full)) return full;

            string stem = name.Substring(0, name.Length - Extension.Length);
            for (int i = 2; i < 100; i++)
            {
                full = System.IO.Path.Combine(folder, stem + "_" + i.ToString(CultureInfo.InvariantCulture) + Extension);
                if (!File.Exists(full)) return full;
            }
            return full;   // 같은 초에 백 판을 열었다면 덮는 것이 문제가 아니다
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
            lock (Gate)
            {
                // **앞 판이 열려 있으면 제대로 닫고 연다.** 그냥 덮으면 그 파일만
                // `session_end` 없이 끊겨, 하필 그 판에서 "어디까지 갔나" 를 못 읽는다
                // (`!471` AI 리뷰). 같은 실행에서 다시 시작하는 길이 실제로 있다 —
                // `SpaceBootstrap` 이 관찰자를 재사용한다.
                if (writer != null)
                    End(new Dictionary<string, object> { ["result"] = "restart" });

                Path = string.Empty;

                string folder = FolderResolver == null ? string.Empty : FolderResolver();
                if (string.IsNullOrEmpty(folder)) return;

                try
                {
                    Directory.CreateDirectory(folder);
                    Path = Unused(folder, FileNameFor(DateTime.Now));
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

        /// <summary>한 줄 남긴다. 파일이 없거나 상한에 닿았으면 아무 일도 하지 않는다.</summary>
        public static void Write(string type, IDictionary<string, object> fields = null)
        {
            lock (Gate)
            {
                if (capped) return;
                Emit(type, fields, false);
            }
        }

        /// <summary>
        /// 실제로 한 줄을 쓴다.
        ///
        /// <para><paramref name="last"/> 면 <b>상한을 무시하고 쓰며, 상한 표시도 더 붙이지
        /// 않는다.</b> 전에는 <see cref="End"/> 가 <c>capped</c> 를 잠깐 내렸다 올리는
        /// 식이었는데, 그러면 <c>session_end</c> 를 쓴 직후 이 함수가 상한을 <b>다시</b>
        /// 판정해 <c>capped</c> 줄을 한 번 더 붙였다 — "마지막 줄이 <c>session_end</c>" 라는
        /// 약속이 상한에 닿은 판에서 정확히 깨졌다(<c>!471</c> AI 리뷰).
        /// </summary>
        static void Emit(string type, IDictionary<string, object> fields, bool last)
        {
            if (writer == null || string.IsNullOrEmpty(type)) return;

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
            if (last || written < MaxBytes) return;

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

        /// <summary>판을 닫는다. 두 번 불러도 괜찮다.</summary>
        public static void End(IDictionary<string, object> fields = null)
        {
            lock (Gate)
            {
                if (writer == null) return;

                // 마지막 한 줄은 상한과 무관하게 남긴다 — 그 줄이 곧 "어디서 포기했나" 다.
                Emit("session_end", fields, true);
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
                var files = new List<string>(Directory.GetFiles(folder, "*" + Extension));
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
