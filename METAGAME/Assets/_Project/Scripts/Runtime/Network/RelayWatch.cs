using System.Reflection;
using Mirror;
using UnityEngine;

namespace Metagame.Network
{
    /// <summary>
    /// 릴레이가 우리를 알아보고 있는지 로그에 남긴다 (S15P21B202-264).
    ///
    /// <b>왜 필요한가.</b> Edgegap 전송은 릴레이가 <b>답을 보내 줬을 때만</b> 상태를 찍는다
    /// (<c>EdgegapServer: state updated to: ...</c>). 그래서 답이 아예 오지 않으면 로그가
    /// 텅 비고, "시작을 안 한 것" 과 "보내는데 답이 없는 것" 이 구분되지 않는다.
    /// 실제로 방장 로그가 그렇게 비어 있어서 원인을 좁히지 못했다(2026-09-03).
    ///
    /// 상태 값은 전송 안쪽에 있어 밖에서 읽을 길이 없다. 진단 하나 때문에 Mirror 를
    /// 고치면 나중에 올릴 때 이 수정을 매번 다시 얹어야 하므로, 여기서 리플렉션으로 들여다본다.
    /// 못 읽어도 게임에는 영향이 없다.
    ///
    /// <list type="bullet">
    ///   <item><c>Disconnected</c> — 릴레이에 붙는 것 자체를 시작하지 않았다</item>
    ///   <item><c>Checking</c> — 보내고 있는데 답이 없다 (망이 막혔거나 릴레이가 우리를 모른다)</item>
    ///   <item><c>Valid</c> — 정상. 릴레이가 우리를 인정했다</item>
    ///   <item><c>SessionTimeout</c> — 세션 주인(방장)이 응답하지 않는다</item>
    /// </list>
    /// </summary>
    public sealed class RelayWatch : MonoBehaviour
    {
        /// <summary>몇 초마다 볼지. 핑이 0.5초 간격이라 이 정도면 변화를 놓치지 않는다.</summary>
        const float Interval = 2f;

        /// <summary>언제까지 볼지. 이 안에 자리를 잡지 못하면 그 뒤로도 잡지 못한다.</summary>
        const float Window = 30f;

        float elapsed;
        float nextLook;
        string lastReported;

        /// <summary>이 전송을 지켜본다. 이미 붙어 있으면 다시 붙이지 않는다.</summary>
        public static void Attach(Transport transport)
        {
            if (transport == null) return;
            if (transport.GetComponent<RelayWatch>() != null) return;

            transport.gameObject.AddComponent<RelayWatch>();
        }

        void Update()
        {
            elapsed += Time.deltaTime;
            if (elapsed > Window) { enabled = false; return; }
            if (elapsed < nextLook) return;
            nextLook = elapsed + Interval;

            string state = ReadState();
            if (state == null || state == lastReported) return;

            lastReported = state;
            Debug.Log($"[RelayWatch] 릴레이 상태 · {state} · {elapsed:0}초");
        }

        /// <summary>
        /// 전송 안쪽의 서버·클라이언트 상태를 읽는다. 어느 쪽으로 붙었는지에 따라 있는 것만 읽힌다.
        /// </summary>
        /// <returns>읽은 상태, 또는 읽을 수 없으면 null.</returns>
        string ReadState()
        {
            var transport = GetComponent<Transport>();
            if (transport == null) return null;

            string server = StateOf(transport, "server");
            string client = StateOf(transport, "client");

            if (server == null && client == null) return "읽을 수 없음";
            if (server != null && client != null) return $"서버={server} 클라={client}";
            return server != null ? $"서버={server}" : $"클라={client}";
        }

        /// <summary>전송의 <paramref name="fieldName"/> 안에 있는 state 값을 문자열로.</summary>
        static string StateOf(Transport transport, string fieldName)
        {
            const BindingFlags Where = BindingFlags.Instance | BindingFlags.Public
                                     | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;

            for (var type = transport.GetType(); type != null; type = type.BaseType)
            {
                var field = type.GetField(fieldName, Where);
                if (field == null) continue;

                object holder = field.GetValue(transport);
                if (holder == null) return null;              // 그쪽으로는 붙지 않았다

                // 파생 클래스부터 훑는다. KcpClient 에도 state 가 있어서 그냥 찾으면
                // 릴레이 상태 대신 KCP 접속 상태(Connected 등)가 잡힌다 — 우리가 볼 값이 아니다.
                for (var t = holder.GetType(); t != null; t = t.BaseType)
                {
                    var state = t.GetField("state", Where | BindingFlags.DeclaredOnly);
                    if (state != null) return state.GetValue(holder)?.ToString();
                }
                return null;
            }
            return null;
        }
    }
}
