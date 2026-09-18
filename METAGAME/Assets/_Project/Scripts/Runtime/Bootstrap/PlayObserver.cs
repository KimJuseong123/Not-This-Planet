using System.Collections.Generic;
using Metagame.Core;
using Metagame.Gameplay;
using Metagame.Gameplay.Parts;
using UnityEngine;

namespace Metagame.Bootstrap
{
    /// <summary>
    /// 판이 도는 동안 <b>값이 바뀌는 순간</b>을 보고 이벤트를 남긴다 (S15P21B202-498).
    ///
    /// <para><b>왜 관찰인가.</b> 물리와 진행도가 서버에서만 돌기 때문에 알고 싶은 것의
    /// 대부분이 <b>이미 값으로 존재한다.</b> 게임플레이 코드 여기저기에 호출을 심으면
    /// 씬·프리팹을 건드리게 되고(<c>-351</c>), 프레임에도 얹힌다(<c>-374</c>). 값이
    /// 바뀌는 순간만 보면 그 둘을 피한다.
    ///
    /// <para><b>세계 상태는 호스트만 낸다.</b> 그 값들이 권위 있는 자리가 거기다. 참가자
    /// 화면에서만 나는 일(튕김 등)은 각자가 자기 것으로 남긴다.
    ///
    /// <para><b><see cref="ShipAssembly.Floors"/> 하나가 대부분을 답한다.</b> 판이 끝난
    /// 순간의 그 배열이 곧 "어디까지 갔나" 이고, <c>WrongPart</c>·<c>Tilted</c> 가 몇 층에
    /// 얼마나 오래 있었나가 "어디서 막혔나" 다.
    /// </summary>
    public sealed class PlayObserver : MonoBehaviour
    {
        /// <summary>
        /// 몇 초마다 보는가.
        ///
        /// <para>낮게 잡는다. 이 값이 프레임에 얹히는 유일한 자리이고, 보는 것은 이미
        /// 계산돼 있는 값 몇 개뿐이라 <b>자주 볼 값어치가 없다.</b> 사람이 파트를 옮기고
        /// 용접하는 일은 초 단위로 일어난다.
        /// </summary>
        public const float Interval = 0.5f;

        ShipAssembly assembly;
        GameSession session;

        float next;
        bool authority;

        // 직전에 본 값. 바뀐 것만 남긴다.
        int[] floors;
        string blocker = "";
        int checkpoint = -1;
        int falls = -1;
        bool cleared;

        /// <summary>
        /// 이 판을 시작한다. <paramref name="header"/> 는 첫 줄에 실린다.
        ///
        /// <para><see cref="PlayEvents.Begin"/> 은 에디터에서 아무 일도 하지 않으므로
        /// 여기서 따로 막지 않는다 — 막는 자리를 두 곳에 두면 한쪽만 고쳐진다.
        /// </summary>
        public void Begin(GameSession gameSession, bool hostAuthority, IDictionary<string, object> header)
        {
            // **앞 판이 열려 있으면 여기서 닫는다.** `PlayEvents.Begin` 도 안 닫힌 것을
            // 닫아 주지만 그쪽은 층 상태도 걸린 시간도 모른다 — 마지막 줄이 비게 된다.
            End("restart");

            session = gameSession;
            authority = hostAuthority;

            // **직전에 본 값을 지운다.** 같은 관찰자로 두 번째 판을 열 수 있는데
            // (`SpaceBootstrap` 이 재사용한다), 지우지 않으면 앞 판의 기억이 남아
            // `cleared` 를 두 번째 판에서 안 내고 `fall` 을 엉뚱하게 센다
            // (`!471` AI 리뷰).
            floors = null;
            blocker = "";
            checkpoint = -1;
            falls = -1;
            cleared = false;
            next = 0f;
            assembly = null;

            PlayEvents.Begin(header);
        }

        void Update()
        {
            if (!PlayEvents.Active || Time.realtimeSinceStartup < next) return;
            next = Time.realtimeSinceStartup + Interval;

            if (session == null) session = GameSession.Instance;
            if (session == null) return;

            // 자기 것: 낙사 횟수는 그 사람 화면에서만 는다.
            if (session.Falls != falls)
            {
                if (falls >= 0) PlayEvents.Write("fall", One("count", session.Falls));
                falls = session.Falls;
            }

            if (!authority) return;

            if (session.CheckpointIndex != checkpoint)
            {
                if (checkpoint >= 0) PlayEvents.Write("checkpoint", One("index", session.CheckpointIndex));
                checkpoint = session.CheckpointIndex;
            }

            if (session.Cleared && !cleared)
            {
                cleared = true;
                PlayEvents.Write("cleared", One("seconds", session.ClearTime));
            }

            if (assembly == null) assembly = FindAnyObjectByType<ShipAssembly>();
            if (assembly == null) return;

            var now = Snapshot(assembly);
            if (!Same(now, floors))
            {
                floors = now;
                PlayEvents.Write("assembly", One("floors", now));
            }

            // **막힌 사유는 이름으로 싣는다** (S15P21B202-552). 한글 문구를 그대로 담으면
            // 문구가 바뀔 때마다 집계가 갈라진다.
            string code = assembly.IsBoardable ? "" : BlockerCode(assembly.Blocker);
            if (code != blocker)
            {
                blocker = code;
                if (code.Length > 0) PlayEvents.Write("blocked", One("code", code));
            }
        }

        /// <summary>
        /// 판을 닫는다. <b>마지막 층 상태를 함께 싣는다</b> — 그것이 곧 답이다.
        ///
        /// <para><b><see cref="PlayEvents.Open"/> 을 본다.</b> <c>Active</c> 는 상한에 닿으면
        /// 거짓이라, 그것을 보면 <see cref="PlayEvents.End"/> 가 "상한과 무관하게 마지막 한
        /// 줄은 남긴다" 고 일부러 만들어 둔 것을 여기서 무력화한다 — <b>가장 알고 싶은
        /// 판에서 그 줄이 사라진다</b>(<c>!471</c> AI 리뷰).
        ///
        /// <para>두 번 불려도 괜찮다. 파일이 닫히면 <c>Open</c> 이 거짓이 되고,
        /// <see cref="PlayEvents.End"/> 자체도 두 번 호출을 견딘다.
        /// </summary>
        /// <param name="sweep">
        /// 파일을 닫은 뒤 <b>보내기를 요청할지</b> (S15P21B202-607).
        ///
        /// <para><b>앱 종료 경로에서는 거짓이다</b>(<c>!514</c> AI 리뷰). 종료 훅에서
        /// 시작한 전송은 끝나지 못하는데, 이 인자가 없던 동안
        /// <see cref="OnApplicationQuit"/> → <c>End</c> → 요청 경로로 <b>그 일이 실제로
        /// 일어났다</b> — 문서와 코드가 어긋난 상태였다.</para>
        /// </param>
        public void End(string result, bool sweep = true)
        {
            if (!PlayEvents.Open) return;

            var fields = new Dictionary<string, object> { ["result"] = result };
            if (session != null)
            {
                fields["elapsed"] = session.Elapsed;
                fields["falls"] = session.Falls;
                // 마지막 줄 하나로 "이 판이 깨졌나" 가 답해져야 한다 — 따로 찾아
                // 맞춰 봐야 하면 그 줄을 마지막에 두는 뜻이 없다.
                fields["cleared"] = session.Cleared;
            }
            if (assembly == null) assembly = FindAnyObjectByType<ShipAssembly>();
            if (assembly != null)
            {
                fields["floors"] = Snapshot(assembly);
                string code = assembly.IsBoardable ? "" : BlockerCode(assembly.Blocker);
                if (code.Length > 0) fields["blocked"] = code;
            }
            PlayEvents.End(fields);

            // **판이 끝났으니 보낸다** (S15P21B202-607). 파일을 막 닫은 직후가 보낼 수
            // 있는 가장 이른 자리이고, 플레이 중이 아니다. 듣는 쪽이 없으면(로그인 전·
            // 세션 객체가 내려가는 중) 아무 일도 안 일어나고 다음 로그인이 잡는다.
            //
            // **앱 종료 경로에서는 부르지 않는다.** 거기서 시작한 전송은 끝나지 못한다 —
            // 그 결정은 `RunLogUpload` 주석에 있고, 이 `if` 가 그것을 코드로 지킨다.
            if (sweep) RunLogUpload.Request();
        }

        /// <summary>
        /// 앱이 꺼질 때 닫는다. <b>보내지는 않는다</b> — 종료 훅에서 시작한 전송은
        /// 끝나지 못한다(<see cref="RunLogUpload"/>). 못 보낸 것은 다음 실행이 잡는다.
        /// </summary>
        void OnApplicationQuit() => End("quit", sweep: false);

        /// <summary>
        /// 씬이 내려갈 때도 닫는다.
        ///
        /// <para><b>가장 흔한 끝이 여기다.</b> 클리어하고 로비로 돌아가는 길은 종료가
        /// 아니라 씬 교체라 <see cref="OnApplicationQuit"/> 가 오지 않는다. 그대로 두면
        /// 다음 판의 <see cref="PlayEvents.Begin"/> 이 파일을 그냥 닫아 버려, 결과도
        /// 걸린 시간도 마지막 층 상태도 없이 끊긴다(<c>!471</c> AI 리뷰).
        ///
        /// <para>종료할 때는 둘 다 오는데(먼저 <c>OnApplicationQuit</c>), 뒤에 오는 쪽은
        /// 파일이 이미 닫혀 아무 일도 하지 않는다 — 그래서 결과는 <c>quit</c> 로 남는다.
        /// </summary>
        void OnDestroy()
        {
            End("destroy");
        }

        static int[] Snapshot(ShipAssembly a)
        {
            var src = a.Floors;
            var outv = new int[src.Count];
            for (int i = 0; i < src.Count; i++) outv[i] = (int)src[i];
            return outv;
        }

        static bool Same(int[] a, int[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        /// <summary>
        /// 한글 사유를 이름으로. <see cref="ShipAssembly.Evaluate"/> 의 조기 탈출 순서와 같다.
        ///
        /// <para><b>문구를 그대로 싣지 않는 이유.</b> 그 값은 사람이 읽으라고 만든 것이라
        /// 고쳐질 수 있는데, 그때마다 집계가 갈라지면 옛 데이터와 비교할 수 없게 된다.
        ///
        /// <para><b>문구가 바뀌면 검사가 막는다</b> — <c>PlayEventsTests</c> 의
        /// <c>EveryBlockerMessageHasExactlyOneName</c> 이 여기의 조각과
        /// <see cref="ShipAssembly"/> 의 문구를 맞대 본다. 그것이 없으면 문구가 한 글자
        /// 바뀌는 순간 <b>조용히 <c>other</c> 로 떨어진다</b>(<c>!471</c> AI 리뷰).
        ///
        /// <para>문자열 맞추기 자체를 없애는 것(<see cref="ShipAssembly"/> 가 이름을 직접
        /// 내놓게 하는 것)이 더 낫고, <c>-552</c> 가 그 소스에 <c>// code:</c> 앵커를
        /// 넣어 두었다. 그 MR 이 머지된 뒤에 잇는다.
        /// </summary>
        public static string BlockerCode(string reason)
        {
            if (string.IsNullOrEmpty(reason)) return "";
            if (reason.Contains("조립대에 없다")) return "no_engine";
            if (reason.Contains("층만 쌓였다")) return "incomplete";
            if (reason.Contains("이 아니다")) return "wrong_order";
            if (reason.Contains("발사 구역")) return "off_pad";
            if (reason.Contains("기울어")) return "tilted";
            return "other";
        }

        static Dictionary<string, object> One(string key, object value)
        {
            return new Dictionary<string, object> { [key] = value };
        }
    }
}
