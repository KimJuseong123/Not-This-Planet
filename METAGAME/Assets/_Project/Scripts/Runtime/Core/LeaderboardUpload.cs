using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace Metagame.Core
{
    /// <summary>
    /// 클리어 기록을 서버에 올리고 표를 받아 온다 (S15P21B202-656).
    ///
    /// <para><b>판이 끝난 자리에서 바로 보내지 않는다.</b> 이 저장소에는 "플레이 중에는
    /// 네트워킹을 쓰지 않는다" 는 팀 결정이 있고, <see cref="RunLogUpload"/> 가 같은
    /// 이유로 <b>디스크에 적어 두고 안전할 때 쓸어 담는</b> 방식을 쓴다. 여기서도 같다 —
    /// 클리어 순간에는 파일 하나를 쓰고, 보내는 것은 로비로 돌아온 뒤다.</para>
    ///
    /// <para><b>그 방식이 크래시에도 강하다.</b> 엔딩 도중에 죽어도 기록은 남아 있고
    /// 다음에 켤 때 올라간다. 종료 훅에 매달면 그때가 바로 못 받는 경우다.</para>
    ///
    /// <para><b>실패해도 게임을 막지 않는다.</b> 기록은 곁다리다 — 못 올려서 로비가
    /// 안 뜨면 그게 더 큰 문제다.</para>
    /// </summary>
    public static class LeaderboardUpload
    {
        /// <summary>보내지 못한 기록을 적어 두는 파일 이름.</summary>
        const string PendingFileName = "pending-runs.jsonl";

        /// <summary>요청 하나에 쓰는 시간(초). 작은 JSON 이라 길 이유가 없다.</summary>
        const int TimeoutSeconds = 10;

        /// <summary>표 조회에 쓰는 시간(초).</summary>
        const int FetchTimeoutSeconds = 8;

        static string PendingPath =>
            Path.Combine(Application.persistentDataPath, PendingFileName);

        /// <summary>
        /// 아직 못 올린 기록. <b>한 줄에 하나씩</b> 쌓는다(JSON Lines).
        ///
        /// <para><b>덮어쓰지 않는다</b>(!558 AI 리뷰). 예전에는 파일 하나를 덮어써서,
        /// 서버가 닿지 않는 동안 두 판을 깨면 <b>앞 판이 복구 없이 사라졌다</b> —
        /// 오프라인으로 연달아 하는 경우가 그렇다. 게다가 판이 인원별로 갈리므로
        /// 앞 판과 뒤 판이 <b>다른 표</b>에 오를 수도 있어, 최근 것만 남기면 그냥
        /// 기록 하나를 잃는 것이다.</para>
        ///
        /// <para>상한을 두는 것은 이 파일이 무한히 자라지 않게 하기 위해서다. 넘으면
        /// <b>오래된 것부터</b> 버린다 — 최근 판이 사용자가 기다리는 기록이다.</para>
        /// </summary>
        public const int MaxPending = 5;

        /// <summary>
        /// 지금 올리는 중인가. <b>겹쳐 돌면 같은 판이 두 줄 남는다</b>(!558 AI 리뷰).
        ///
        /// <see cref="SubmitPending"/> 를 로그인과 판 종료 두 곳에서 부르는데, 그 둘이
        /// 가까이 일어나면 코루틴이 겹친다. <see cref="Forget"/> 은 성공한 <b>뒤에야</b>
        /// 도므로 그 사이 두 번째가 같은 파일을 읽어 또 POST 한다.
        /// </summary>
        static bool sending;

        /// <summary>
        /// 클리어 기록을 적어 둔다. <b>네트워크를 쓰지 않는다.</b>
        ///
        /// <para>밀린 것이 있으면 뒤에 붙는다. 상한을 넘으면 오래된 것부터 버린다.</para>
        /// </summary>
        /// <returns>적어 두었으면 true. 보낼 수 없는 값이면 false.</returns>
        public static bool Remember(LeaderboardSubmission submission)
        {
            if (submission == null) return false;

            try
            {
                var lines = new List<string>(ReadPending());
                lines.Add(JsonUtility.ToJson(submission));
                while (lines.Count > MaxPending) lines.RemoveAt(0);

                File.WriteAllLines(PendingPath, lines);
                return true;
            }
            catch (Exception e)
            {
                // 디스크가 막혀도 게임에는 아무 영향이 없다. 다만 우리가 알아야 한다.
                Debug.LogWarning("[리더보드] 기록을 적어 두지 못했다: " + e.Message);
                return false;
            }
        }

        /// <summary>적어 둔 기록이 있는가. 로비가 "올리는 중" 을 보여 줄 때 쓴다.</summary>
        public static bool HasPending => File.Exists(PendingPath);

        /// <summary>줄 서 있는 기록 수. 검증이 대기열 동작을 보는 데 쓴다.</summary>
        public static int PendingCount => ReadPending().Length;

        /// <summary>적어 둔 것을 읽는다. 없거나 못 읽으면 빈 목록이다.</summary>
        static string[] ReadPending()
        {
            try
            {
                if (!File.Exists(PendingPath)) return Array.Empty<string>();

                var kept = new List<string>();
                foreach (var line in File.ReadAllLines(PendingPath))
                    if (!string.IsNullOrWhiteSpace(line))
                        kept.Add(line);

                return kept.ToArray();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[리더보드] 적어 둔 기록을 읽지 못했다: " + e.Message);
                return Array.Empty<string>();
            }
        }

        /// <summary>
        /// 적어 둔 기록을 <b>차례로</b> 보낸다. 없으면 아무 일도 하지 않는다.
        ///
        /// <para><b>성공과 거절만 지운다.</b> 서버가 400 으로 거절한 값은 다시 보내도
        /// 또 거절당하므로 남겨 두면 로비에 들어갈 때마다 같은 요청을 반복한다.
        /// 통신 실패는 남긴다 — 그건 값이 아니라 상황 문제다.</para>
        ///
        /// <para><b>통신이 막히면 거기서 멈춘다.</b> 뒤의 것을 계속 시도해 봐야 같은
        /// 이유로 실패하고, 로비에 들어갈 때마다 실패 요청을 다섯 번씩 내보내게 된다.</para>
        /// </summary>
        public static IEnumerator SubmitPending(string baseUrl, string token)
        {
            if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(token)) yield break;
            if (sending) yield break;
            if (!HasPending) yield break;

            sending = true;
            try
            {
                var queued = ReadPending();
                var remaining = new List<string>(queued);

                foreach (var json in queued)
                {
                    var result = new Result();
                    yield return SendOne(baseUrl, token, json, result);

                    if (!result.Settled) break;   // 통신이 막혔다 — 다음에 다시
                    remaining.Remove(json);
                }

                if (remaining.Count == 0) Forget();
                else if (remaining.Count != queued.Length) WritePending(remaining);
            }
            finally
            {
                sending = false;
            }
        }

        /// <summary>한 건의 결과. 코루틴은 값을 못 돌려주므로 상자에 담는다.</summary>
        sealed class Result
        {
            /// <summary>이 기록을 목록에서 빼도 되는가(성공했거나 영영 거절당했다).</summary>
            public bool Settled;
        }

        static IEnumerator SendOne(string baseUrl, string token, string json, Result result)
        {
            using var req = new UnityWebRequest(baseUrl + "/leaderboard", UnityWebRequest.kHttpVerbPOST);
            req.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + token);
            req.timeout = TimeoutSeconds;

            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                Debug.Log("[리더보드] 기록을 올렸다.");
                result.Settled = true;
                yield break;
            }

            // 400 은 값이 틀린 것이다 — 다시 보내도 같은 답이 온다.
            // 401(토큰 만료)·429(도배 제한)는 상황이라 남긴다.
            if (req.responseCode >= 400 && req.responseCode < 500
                && req.responseCode != 401 && req.responseCode != 429)
            {
                Debug.LogWarning($"[리더보드] 서버가 거절했다({req.responseCode}). 기록을 버린다: "
                                 + req.downloadHandler.text);
                result.Settled = true;
                yield break;
            }

            Debug.Log($"[리더보드] 올리지 못했다({req.responseCode}). 다음에 다시 보낸다.");
        }

        static void WritePending(List<string> lines)
        {
            try
            {
                File.WriteAllLines(PendingPath, lines);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[리더보드] 남은 기록을 적지 못했다: " + e.Message);
            }
        }

        /// <summary>적어 둔 기록을 모두 버린다.</summary>
        public static void Forget()
        {
            try
            {
                if (File.Exists(PendingPath)) File.Delete(PendingPath);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[리더보드] 기록 파일을 지우지 못했다: " + e.Message);
            }
        }

        /// <summary>
        /// 표를 받아 온다.
        ///
        /// <para><b>실패와 빈 표를 가른다</b>(!558 AI 리뷰). 예전에는 둘 다 길이 0 인
        /// 배열을 주어서, 서버가 죽어 있어도 화면에 "아직 아무도 못 깼어요" 가 떴다 —
        /// 고장을 기록 없음으로 읽게 만드는 쪽이 더 나쁘다. 실패는 <c>null</c> 이다.</para>
        /// </summary>
        /// <param name="onDone">받은 줄. <b>실패하면 <c>null</c></b>, 기록이 없으면 길이 0.</param>
        public static IEnumerator Fetch(
            string baseUrl, int crewSize, int limit, Action<LeaderboardRow[]> onDone)
        {
            if (string.IsNullOrEmpty(baseUrl) || !Leaderboard.IsValidCrew(crewSize))
            {
                onDone?.Invoke(null);
                yield break;
            }

            var url = $"{baseUrl}/leaderboard?crew={crewSize}&limit={Mathf.Clamp(limit, 1, 50)}";

            using var req = UnityWebRequest.Get(url);
            req.timeout = FetchTimeoutSeconds;
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.Log($"[리더보드] 표를 받지 못했다({req.responseCode}).");
                onDone?.Invoke(null);
                yield break;
            }

            onDone?.Invoke(ParseRows(req.downloadHandler.text));
        }

        /// <summary>
        /// 서버가 준 JSON 배열을 읽는다.
        ///
        /// <para><b>JsonUtility 는 최상위 배열을 못 읽는다.</b> 그래서 <c>{"rows":[...]}</c>
        /// 로 한 겹 감싸 넘긴다. 서버를 고쳐 감싸게 할 수도 있지만, 그러면 REST 응답이
        /// 이 클라이언트의 직렬화 사정에 맞춰 휘는 것이다.</para>
        /// </summary>
        public static LeaderboardRow[] ParseRows(string json)
        {
            if (string.IsNullOrEmpty(json)) return Array.Empty<LeaderboardRow>();

            try
            {
                var page = JsonUtility.FromJson<LeaderboardPage>("{\"rows\":" + json + "}");
                return page?.rows ?? Array.Empty<LeaderboardRow>();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[리더보드] 표를 읽지 못했다: " + e.Message);
                return Array.Empty<LeaderboardRow>();
            }
        }
    }
}
