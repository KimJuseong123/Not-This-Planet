using System;
using System.Collections;
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
        const string PendingFileName = "pending-run.json";

        /// <summary>요청 하나에 쓰는 시간(초). 작은 JSON 이라 길 이유가 없다.</summary>
        const int TimeoutSeconds = 10;

        /// <summary>표 조회에 쓰는 시간(초).</summary>
        const int FetchTimeoutSeconds = 8;

        static string PendingPath =>
            Path.Combine(Application.persistentDataPath, PendingFileName);

        /// <summary>
        /// 클리어 기록을 적어 둔다. <b>네트워크를 쓰지 않는다.</b>
        ///
        /// <para><b>덮어쓴다.</b> 밀린 기록을 여러 개 쌓지 않는 이유는, 표에 오르는 것이
        /// 개인 최고가 아니라 전체 순위이고 <b>가장 최근 판</b>이 가장 관심 있는 기록이기
        /// 때문이다. 줄을 세우면 로비에 들어갈 때마다 옛 판이 뒤늦게 올라온다.</para>
        /// </summary>
        /// <returns>적어 두었으면 true. 보낼 수 없는 값이면 false.</returns>
        public static bool Remember(LeaderboardSubmission submission)
        {
            if (submission == null) return false;

            try
            {
                File.WriteAllText(PendingPath, JsonUtility.ToJson(submission));
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

        /// <summary>
        /// 적어 둔 기록을 한 번 보낸다. 없으면 아무 일도 하지 않는다.
        ///
        /// <para><b>성공했을 때만 지운다.</b> 서버가 400 으로 거절한 경우도 지운다 —
        /// 그 값은 다시 보내도 또 거절당하고, 남겨 두면 로비에 들어갈 때마다 같은
        /// 요청을 반복한다. 통신 실패는 남긴다: 그건 값이 아니라 상황 문제다.</para>
        /// </summary>
        public static IEnumerator SubmitPending(string baseUrl, string token)
        {
            if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(token)) yield break;
            if (!HasPending) yield break;

            string json;
            try
            {
                json = File.ReadAllText(PendingPath);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[리더보드] 적어 둔 기록을 읽지 못했다: " + e.Message);
                yield break;
            }

            if (string.IsNullOrEmpty(json)) { Forget(); yield break; }

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
                Forget();
                yield break;
            }

            // 400 은 값이 틀린 것이다 — 다시 보내도 같은 답이 온다.
            if (req.responseCode >= 400 && req.responseCode < 500 && req.responseCode != 401
                && req.responseCode != 429)
            {
                Debug.LogWarning($"[리더보드] 서버가 거절했다({req.responseCode}). 기록을 버린다: "
                                 + req.downloadHandler.text);
                Forget();
                yield break;
            }

            // 401(토큰 만료)·429(도배 제한)·통신 실패는 다음에 다시 시도한다.
            Debug.Log($"[리더보드] 올리지 못했다({req.responseCode}). 다음에 다시 보낸다.");
        }

        /// <summary>적어 둔 기록을 버린다.</summary>
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
        /// 표를 받아 온다. <b>실패하면 빈 표를 준다</b> — 로비가 멈추면 안 된다.
        /// </summary>
        /// <param name="onDone">받은 줄. 실패하면 길이 0.</param>
        public static IEnumerator Fetch(
            string baseUrl, int crewSize, int limit, Action<LeaderboardRow[]> onDone)
        {
            if (string.IsNullOrEmpty(baseUrl) || !Leaderboard.IsValidCrew(crewSize))
            {
                onDone?.Invoke(Array.Empty<LeaderboardRow>());
                yield break;
            }

            var url = $"{baseUrl}/leaderboard?crew={crewSize}&limit={Mathf.Clamp(limit, 1, 50)}";

            using var req = UnityWebRequest.Get(url);
            req.timeout = FetchTimeoutSeconds;
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.Log($"[리더보드] 표를 받지 못했다({req.responseCode}).");
                onDone?.Invoke(Array.Empty<LeaderboardRow>());
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
