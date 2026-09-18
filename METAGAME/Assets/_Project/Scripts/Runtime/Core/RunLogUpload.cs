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
    /// <para><b>그 대가를 뒤에 하나 더 메웠다</b> (S15P21B202-607). 시작할 때만 보내면
    /// <i>"한 번 켜서 한 판 하고 끄는 사람"</i> 의 기록은 영영 오지 않는데, 데모에서는
    /// 그쪽이 다수다 — 알파 테스트의 "같은 사람이 여러 번 켠다" 는 전제가 깨진다.
    /// 그래서 <b>판이 끝날 때도 한 번 더 보낸다</b>(<see cref="SweepRequested"/>).
    /// 여전히 종료 훅은 쓰지 않는다 — 위 두 이유가 그대로다.</para>
    ///
    /// <para><b>로그인 뒤에 부른다.</b> 서버가 게스트 토큰을 요구한다 — 아무나 우리
    /// 서버에 파일을 부어넣지 못하게 하는 것이 그 토큰의 역할이다.</para>
    /// </summary>
    public static class RunLogUpload
    {
        /// <summary>
        /// 보낸 파일에 붙이는 꼬리표. <b>이름을 바꾸는 것 자체가 표시다</b> — 옆에 표시
        /// 파일을 따로 두면 그 쓰기가 실패했을 때 같은 로그를 매번 다시 보내고(서버는
        /// 받을 때마다 <b>새 이름</b>을 만들어 쌓으므로 덮어쓰이지도 않는다), 표시 파일
        /// 자체가 정리 대상에서 빠져 쌓인다.
        /// </summary>
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

        /// <summary>
        /// 지금 돌고 있는가. <b>"한 번 시작했으면 끝" 이 아니다</b> — 로그인 실패 뒤
        /// 다시 로그인하는 경우가 있고, 그때 앞의 시도가 중간에 끊겼으면 보낼 것이
        /// 그대로 남아 있다. 두 번 보내는 것은 이름 바꾸기가 막는다.
        /// </summary>
        static bool running;

        /// <summary>
        /// <b>판이 끝났을 때 한 번 더 보내 달라</b>는 요청을 받는 자리 (S15P21B202-607).
        ///
        /// <para><b>왜 필요한가.</b> 지금까지는 <b>로그인 직후 한 번만</b> 쓸어 담았다.
        /// 그래서 <i>"한 번 켜서 한 판 하고 끄는 사람"</i> 의 기록은 영영 오지 않는다 —
        /// 위 주석이 그 대가를 적어 두고 <i>"알파 테스트는 같은 사람이 여러 번 켜므로
        /// 감수한다"</i> 고 했는데, <b>데모에서는 그 전제가 깨진다.</b></para>
        ///
        /// <para><b>앱 종료가 아니라 판이 끝나는 자리다.</b> <c>OnApplicationQuit</c> 에서
        /// 시작한 코루틴은 끝나지 못하고, 끝내려면 종료를 붙잡아야 하는데 그것은 게임이
        /// 안 꺼지는 위험이다. 판이 끝날 때 보내면 그 경우가 대부분 덮인다 — 한 판 하고
        /// 끄는 사람은 그 시점에 이미 보냈다.</para>
        ///
        /// <para><b>아무도 듣지 않으면 아무 일도 없다.</b> 로그인 전이거나 세션 객체가
        /// 이미 내려갔으면 요청은 그냥 버려지고, 다음 로그인의 쓸어 담기가 잡는다.
        /// 보내는 일이 게임을 막을 이유는 없다.</para>
        /// </summary>
        public static Action SweepRequested;

        /// <summary>판이 끝났다고 알린다. 듣는 쪽이 없거나 실패해도 조용히 넘어간다.</summary>
        public static void Request()
        {
            var handler = SweepRequested;
            if (handler == null) return;
            try
            {
                handler();
            }
            catch (Exception e)
            {
                // 세션 객체가 내려가는 중이면 코루틴 시작이 던진다 — 그건 정상적인 끝이다.
                Debug.Log("[RUN] 판이 끝나 보내려 했으나 지금은 못 보낸다 · " + e.GetType().Name);
            }
        }

        /// <summary>
        /// 지금 나가 있는 요청. <b>끊을 수 있어야 한다</b> — 게임이 시작되면 남은 전송을
        /// 그 자리에서 멈춘다(<see cref="StopNow"/>).
        /// </summary>
        static UnityWebRequest inFlight;

        /// <summary>
        /// 지난 실행들의 로그를 보낸다. 로그인 직후 <b>한 번만</b> 부른다.
        ///
        /// <para>실패는 조용히 넘긴다 — 로그를 못 보내는 것이 게임을 막을 이유는 아니다.
        /// 다만 <b>왜 못 보냈는지는 로그에 남긴다.</b> 그 로그가 다음 실행에 올라간다.</para>
        /// </summary>
        public static IEnumerator SweepOnce(string baseUrl, string token, long userId)
        {
            if (running) yield break;
            running = true;

            if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(token))
            {
                running = false;
                yield break;
            }

            List<string> targets;
            try
            {
                targets = Pending();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RUN] 보낼 로그를 찾지 못했다 · {e.GetType().Name}");
                running = false;
                yield break;
            }
            if (targets.Count == 0)
            {
                running = false;
                yield break;
            }

            Debug.Log($"[RUN] 지난 실행 로그 {targets.Count}개를 보낸다");
            foreach (var path in targets)
            {
                // 사이사이에 확인한다 — 한 개를 보내는 동안 게임이 시작될 수 있다.
                if (!running) yield break;

                yield return Send(baseUrl, token, userId, path);
            }

            running = false;
        }

        /// <summary>
        /// <b>지금 당장 멈춘다.</b>
        ///
        /// <para><b>왜 필요한가.</b> 최대 3개 × 최대 8 MB 를 요청당 15초 안에 보내므로,
        /// 느린 회선에서는 전송이 <b>1분 가까이</b> 이어질 수 있다. 그 사이에 매칭이
        /// 시작되면 이슈의 팀 결정 — <b>플레이 중에는 네트워킹을 쓰지 않는다</b> — 을
        /// 실제로는 못 지킨다(4 vCPU EC2 가 릴레이·매칭·CI 를 겸한다).</para>
        ///
        /// <para>못 보낸 것은 표시가 없으므로 <b>다음 실행에서 다시 간다.</b> 그것이
        /// 이 설계의 기본 동작이라 여기서 잃는 것이 없다.</para>
        ///
        /// <para>세션이 사라질 때도 부른다 — 코루틴이 중간에 끊기면
        /// <see cref="running"/> 이 선 채로 남아 <b>그 프로세스가 끝날 때까지 다시는
        /// 안 돈다.</b></para>
        /// </summary>
        public static void StopNow()
        {
            running = false;

            var req = inFlight;
            inFlight = null;
            if (req == null) return;

            try { req.Abort(); }
            catch (Exception) { /* 이미 끝났을 수 있다 */ }
        }

        /// <summary>
        /// 아직 안 보낸 로그. <b>이번 실행의 파일은 제외한다</b> — 지금 쓰고 있는 중이라
        /// 보내 봐야 반쪽이고, 다음 실행에서 온전한 것이 간다.
        /// </summary>
        public static List<string> Pending()
        {
            // **로그와 이벤트를 번갈아 담는다** (S15P21B202-607). 한쪽을 먼저 다 담으면
            // 다른 쪽이 굶는다 — 밀린 로그 3개가 있으면 이벤트는 영영 안 가고, 반대도
            // 마찬가지다. `MaxPerRun` 은 **합계**로 지킨다(서버가 사용자당 6건/창이다).
            return Interleave(
                PendingIn(FolderOf(RunLog.Path), "*.log", RunLog.Path),
                PendingIn(EventsFolder(), "*" + PlayEvents.Extension, OpenEventFile()),
                MaxPerRun);
        }

        static string FolderOf(string path)
        {
            return string.IsNullOrEmpty(path) ? null : Path.GetDirectoryName(path);
        }

        /// <summary>
        /// 이벤트 폴더. <b><see cref="PlayEvents"/> 와 같은 판단을 쓴다</b>
        /// (<c>!514</c> AI 리뷰).
        ///
        /// <para>전에는 <c>PlayEvents.Path</c> 에서 폴더를 역산했다. 그런데 <b>로그인 직후
        /// 쓸어 담을 때는 이번 실행이 아직 파일을 연 적이 없어</b> 그 값이 비어 있다 —
        /// 폴더가 <c>null</c> 이 되어 <b>events 를 통째로 건너뛴다.</b> 그게 이 기능의 주
        /// 경로라, 수집이 조용히 0건이 될 뻔했다.</para>
        /// </summary>
        static string EventsFolder()
        {
            var resolve = PlayEvents.FolderResolver;
            return resolve == null ? null : resolve();
        }

        /// <summary>
        /// <b>지금 쓰고 있는</b> 이벤트 파일. 닫혀 있으면 없다(<c>!514</c> AI 리뷰).
        ///
        /// <para><see cref="PlayEvents.End"/> 는 <c>Path</c> 를 비우지 않는다 — 방금 닫은
        /// 파일의 이름이 그대로 남는다. 그것을 "쓰는 중" 으로 보고 빼면 <b>판이 끝나며
        /// 보내려던 바로 그 파일</b>이 목록에서 사라진다. 열려 있을 때만 뺀다.</para>
        /// </summary>
        static string OpenEventFile()
        {
            return PlayEvents.Open ? PlayEvents.Path : null;
        }

        /// <summary>
        /// 한 폴더에서 아직 안 보낸 파일을 새것부터. <b>지금 쓰고 있는 것은 뺀다.</b>
        /// </summary>
        public static List<string> PendingIn(string folder, string pattern, string current)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return result;

            var files = new List<string>(Directory.GetFiles(folder, pattern));
            // 이름이 yyyyMMdd-HHmmss 라 문자열 정렬이 곧 시각 정렬이다. 새것부터 보낸다 —
            // 밀려 있을 때 오래된 것보다 최근 것이 쓸모 있다.
            files.Sort(StringComparer.Ordinal);
            files.Reverse();

            foreach (var f in files)
            {
                if (!string.IsNullOrEmpty(current)
                    && string.Equals(f, current, StringComparison.OrdinalIgnoreCase)) continue;

                // 보낸 것은 이름이 `*.sent` 라 위의 목록에 아예 안 들어온다. 다만 이름을
                // 못 바꾼 경우를 대비해 표시 파일도 본다(아래 Mark 참조).
                if (File.Exists(f + SentSuffix)) continue;

                result.Add(f);
            }
            return result;
        }

        /// <summary>
        /// 두 목록을 번갈아 <paramref name="max"/> 개까지. <b>순수 함수다</b> — 굶는
        /// 경우를 검사가 값으로 잡을 수 있어야 한다.
        /// </summary>
        public static List<string> Interleave(List<string> a, List<string> b, int max)
        {
            var result = new List<string>();
            for (int i = 0; result.Count < max; i++)
            {
                bool took = false;
                if (a != null && i < a.Count) { result.Add(a[i]); took = true; }
                if (result.Count < max && b != null && i < b.Count) { result.Add(b[i]); took = true; }
                if (!took) break;
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
                req.SetRequestHeader("X-Run-Build", BuildTag());
                req.SetRequestHeader("X-Run-Backend", BackendEndpoints.IsDev ? "dev" : "prod");
                req.SetRequestHeader("X-Run-User", userId.ToString());

                // 끊을 수 있게 등록해 둔다. 게임이 시작되면 `StopNow` 가 이것을 끊는다.
                inFlight = req;
                yield return req.SendWebRequest();
                if (inFlight == req) inFlight = null;

                if (req.result == UnityWebRequest.Result.Success)
                {
                    Mark(path);
                }
                else
                {
                    // 다시 시도할 수 있게 표시를 남기지 않는다. 다음 실행에서 또 온다.
                    // 끊긴 경우도 같다 — `Aborted` 는 실패로 떨어지고 표시가 없으니
                    // 다음 실행에서 그대로 다시 간다.
                    Debug.LogWarning($"[RUN] {name} 전송 실패 · {req.responseCode} {req.result}");
                }
            }
        }

        /// <summary>
        /// 보냈다고 표시한다. 지우지 않는 이유: 사람이 직접 열어 볼 때가 있다.
        ///
        /// <para><b>이름을 바꾸는 것이 표시다.</b> 옆에 표시 파일을 두는 방식은 그 쓰기가
        /// 실패했을 때 조용히 넘어가면 같은 로그가 실행마다 다시 올라간다 — 서버는 받을
        /// 때마다 <b>받은 시각으로 새 이름</b>을 만들므로 덮어쓰이지 않고 쌓이고,
        /// 사용자별 빈도 상한(10분에 6개)까지 그것으로 채워진다.</para>
        ///
        /// <para>이름 바꾸기가 실패하는 경우(파일이 열려 있다 등)에만 표시 파일로
        /// 물러선다. <b>둘 다 실패하면 남긴다</b> — 조용히 넘기면 위의 중복이 그대로
        /// 일어나는데 아무도 이유를 모른다.</para>
        /// </summary>
        static void Mark(string path)
        {
            try
            {
                File.Move(path, path + SentSuffix);
                return;
            }
            catch (Exception)
            {
                // 아래에서 다시 시도한다.
            }

            try
            {
                File.WriteAllText(path + SentSuffix, string.Empty);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RUN] {Path.GetFileName(path)} 를 보냈다고 표시하지 못했다 "
                                 + $"· {e.GetType().Name} — 다음 실행에서 다시 올라간다");
            }
        }

        /// <summary>
        /// 파일의 <b>뒤쪽</b> 최대 <paramref name="cap"/> 바이트. 상한을 넘는 옛 파일은
        /// 끝이 더 쓸모 있다 — 문제는 보통 마지막에 있다.
        ///
        /// <para><b>자르는 자리는 줄 경계다</b> (S15P21B202-467). 바이트 수로 아무 데나
        /// 자르면 UTF-8 글자 가운데가 깨진다. <see cref="RunLog"/> 의 압축도 같은
        /// 방식이다 — 두 곳이 달라 한쪽만 깨지는 일이 없어야 한다.</para>
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

                // **줄 경계에서 시작한다** (S15P21B202-467). 바이트 수로 아무 데나 자르면
                // UTF-8 여러 바이트 글자의 **가운데가 잘려** 받은 첫 줄이 깨진 채 서버에
                // 저장된다. `RunLog.Compact` 는 같은 이유로 줄바꿈에 맞추는데 여기만
                // 안 맞춰 두 곳이 어긋나 있었다(리뷰가 잡았다).
                //
                // `\n` 바이트는 여러 바이트 문자 안에 나타나지 않으므로 줄 경계로 자르면
                // 글자가 깨지지 않는다.
                int from = 0;
                for (int i = 0; i < buf.Length; i++)
                {
                    if (buf[i] != (byte)'\n') continue;
                    from = i + 1;
                    break;
                }
                if (from == 0 || from >= buf.Length)
                {
                    // 쓸 만한 줄 경계가 없다(줄바꿈이 아예 없거나 마지막 바이트뿐).
                    // **그래도 글자 가운데에서 시작하지는 않게** 이어 바이트만 버린다
                    // (`!378` 리뷰). UTF-8 의 이어 바이트는 `10xxxxxx` 다.
                    //
                    // 비워서 돌려주지 않는다 — 깨진 글자 하나보다 **로그가 통째로
                    // 사라지는 쪽**이 훨씬 나쁘다.
                    from = 0;
                    while (from < buf.Length && (buf[from] & 0xC0) == 0x80) from++;
                    if (from == 0 || from >= buf.Length) return buf;
                }

                var trimmed = new byte[buf.Length - from];
                Array.Copy(buf, from, trimmed, 0, trimmed.Length);
                return trimmed;
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

        /// <summary>
        /// 헤더에 실을 <b>빌드 식별자</b> (S15P21B202-501).
        ///
        /// <para><b>스탬프 전체를 싣지 않는다.</b> 그 값은 사람이 읽으라고 만든 것이라
        /// 한글과 <c>·</c> 가 들어 있고, 헤더는 그것을 받지 못한다. 걸러 넣으면 읽을 수
        /// 없는 토막만 남는다.</para>
        ///
        /// <para><b>대신 이미 갈라 둔 ASCII 값을 쓴다.</b> <see cref="BuildInfo"/> 가
        /// 스탬프를 읽으면서 <c>버전=</c> 줄을 <see cref="BuildInfo.ReleaseVersion"/> 로
        /// 따로 담아 둔다. 릴리스 빌드에서는 그것이 곧 <c>v1.1.0</c> 이다.</para>
        ///
        /// <para>그 값이 비는 빌드(에디터·실험)에서는 스탬프를 걸러 쓴다 — 읽기는 나빠도
        /// <b>아무것도 없는 것보다 낫다.</b> 어느 쪽이든 사람이 읽는 원문은 로그 본문
        /// 첫 줄(<c>[빌드] …</c>)에 그대로 있다.</para>
        /// </summary>
        public static string BuildTag()
        {
            // **신원 줄을 먼저 본다** (S15P21B202-597). `버전=` 은 릴리스 빌드에만 있어서,
            // 개발·QA 빌드의 로그는 아래 폴백으로 내려가 한글 스탬프가 걸러진 토막이나
            // <c>unknown</c> 으로 올라갔다 — **QA 로그가 정작 어느 빌드인지 못 말했다.**
            // 릴리스에서는 두 값이 같으므로(`BuildStamp` 가 버전을 신원으로도 쓴다)
            // 헤더는 그대로다.
            string id = string.IsNullOrEmpty(BuildInfo.BuildId) ? BuildInfo.ReleaseVersion : BuildInfo.BuildId;
            return BuildTag(id, BuildInfo.Stamp);
        }

        /// <summary>
        /// 값을 받아서 고르는 쪽 — <b>검사가 부를 수 있게 갈라 둔다</b>
        /// (S15P21B202-501, <c>!463</c> 리뷰).
        ///
        /// <para><b>왜 갈랐나.</b> 위 함수는 <see cref="BuildInfo"/> 의 실제 값을 읽으므로,
        /// 에디터에서는 스탬프가 늘 차 있어 <b>빈 값 경로를 한 번도 지나지 않는다.</b>
        /// 그러면 아래 폴백을 지워도 검사가 초록이라 회귀를 못 막는다 — 검사가 있는데
        /// 아무것도 안 지키는 상태가 된다. <c>-523</c> 에서 인자 계산을 순수 함수로
        /// 떼어 낸 것과 같은 이유다.</para>
        ///
        /// <para><b><c>internal</c> 이 아니라 <c>public</c> 이다.</b> 검사는 별도
        /// 어셈블리(<c>Metagame.Gameplay.Tests.Editor</c>)에 있어 <c>internal</c> 을 볼 수
        /// 없다 — 처음에 그렇게 뒀다가 <b>컴파일 오류 4건</b>으로 막혔다.</para>
        /// </summary>
        public static string BuildTag(string version, string stamp)
        {
            string tag = Sanitize(string.IsNullOrEmpty(version) ? stamp : version);

            // **빈 값도 헤더에 못 넣는다** (`!459` AI 리뷰). `SetRequestHeader` 는 빈
            // 문자열에도 던지므로, 여기서 비워 보내면 이 이슈가 고친 실패 경로가
            // 그대로 되살아난다 — 요청 전 예외 → `.sent` 미표시 → 매 실행 반복.
            //
            // 빌 수 있다. `BuildInfo.Read()` 의 마지막 경로가 `lines[0].Trim()` 이라
            // 빌드 스탬프 파일의 **첫 줄이 비어 있으면** `Stamp` 가 빈 문자열이 되고,
            // 릴리스 버전도 없으면 둘 다 빈 채로 여기 온다. 스탬프가 제어문자뿐이어도
            // `Sanitize` 가 전부 걸러 같은 자리에 닿는다.
            //
            // 무엇인지 모르는 것과 값이 없는 것은 다르다 — 받는 쪽이 구분할 수 있게
            // 이름을 준다.
            return string.IsNullOrEmpty(tag) ? "unknown" : tag;
        }

        /// <summary>
        /// 헤더에 넣을 수 있게 만든다 — <b>ASCII 만 남긴다</b> (S15P21B202-501).
        ///
        /// <para><b>비 ASCII 를 걸러야 하는 이유.</b> HTTP 헤더는 ASCII 만 받는다. 한글도
        /// <c>·</c>(U+00B7)도 못 들어간다. 전에는 제어문자만 걸러 한글이 그대로 지나갔고,
        /// <c>SetRequestHeader</c> 가 <c>Header value contains invalid characters</c> 로
        /// 던졌다. 그 예외가 <c>SendWebRequest</c> <b>전에</b> 나서 요청이 아예 안 나갔고,
        /// <see cref="Mark"/> 에도 못 가서 <b>같은 파일이 실행마다 같은 자리에서 또
        /// 던졌다.</b> 업로드 실패가 게임을 막지 않도록 돼 있어(옳다) 아무도 몰랐다 —
        /// <c>-443</c> 이 만든 수집 경로는 <b>한 건도 배달한 적이 없다</b>(2026-09-15
        /// 실측: 배포 서버의 런로그 폴더에 파일 0개).</para>
        ///
        /// <para><b>이 함수는 던지지 않는다.</b> 무엇이 들어와도 헤더에 넣을 수 있는 값이
        /// 나온다 — 그것이 이 함수의 유일한 약속이다. 값을 만드는 쪽이 실수해도 수집이
        /// 통째로 멈추지 않는다.</para>
        ///
        /// <para><b>버리는 대신 표시한다.</b> 그냥 지우면 <c>버전=v1.1.0</c> 이
        /// <c>=v1.1.0</c> 이 되어 앞이 무엇이었는지 알 수 없다. 이어진 비 ASCII 한 덩어리를
        /// <c>?</c> 하나로 바꿔 <b>자리가 있었다는 것</b>을 남긴다.</para>
        /// </summary>
        public static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new System.Text.StringBuilder(s.Length);
            bool dropping = false;
            foreach (char c in s)
            {
                if (c == '\n' || c == '\r') { dropping = false; sb.Append(' '); continue; }
                if (c < 32 || c == 127) continue;
                if (c > 126)                    // 헤더가 받지 못하는 자리
                {
                    if (!dropping) { sb.Append('?'); dropping = true; }
                    continue;
                }
                dropping = false;
                sb.Append(c);
            }
            return sb.ToString().Trim();
        }
    }
}
