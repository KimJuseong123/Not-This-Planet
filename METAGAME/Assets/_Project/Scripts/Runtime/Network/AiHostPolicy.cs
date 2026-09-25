using Mirror;
using UnityEngine;

namespace Metagame.Network
{
    public enum AiRuntimeState : byte { Missing, Loading, Ready, Failed }
    public enum AiRoomState : byte { Unknown, Off, Missing, Loading, Active, Failed, IdleTest }

    /// <summary>추후 로컬 실행기가 제공한다. 파일 존재가 아니라 실제 행동 실행 준비 상태다.</summary>
    public interface IAiRuntimeStatus
    {
        AiRuntimeState State { get; }
    }

    public static class AiHostPolicy
    {
        public static AiRoomState Evaluate(bool requested, AiRuntimeState runtime, bool idleTest)
        {
            if (idleTest) return AiRoomState.IdleTest;
            if (!requested) return AiRoomState.Off;
            switch (runtime)
            {
                case AiRuntimeState.Missing: return AiRoomState.Missing;
                case AiRuntimeState.Loading: return AiRoomState.Loading;
                case AiRuntimeState.Ready: return AiRoomState.Active;
                default: return AiRoomState.Failed;
            }
        }

        public static bool CanFill(AiRoomState state) => state == AiRoomState.Active || state == AiRoomState.IdleTest;

        public static string Label(AiRoomState state)
        {
            switch (state)
            {
                case AiRoomState.Off: return "AI 동료 사용 안 함";
                case AiRoomState.Missing: return "AI 실행 기능 미연결 · 사람만 플레이";
                case AiRoomState.Loading: return "방장 AI 준비 중 · 사람만 플레이";
                case AiRoomState.Active: return "방장 AI 사용 중";
                case AiRoomState.Failed: return "방장 AI 실행 실패 · 사람만 플레이";
                case AiRoomState.IdleTest: return "대기 AI 테스트 · 명령 수행 없음";
                default: return "방장의 AI 상태 확인 중";
            }
        }
    }

    /// <summary>준비 상태는 이 PC에만 존재한다. 선택과 달리 저장하거나 다른 PC에서 복사하지 않는다.</summary>
    public static class AiHostSettings
    {
        static IAiRuntimeStatus runtime;

        /// <summary>
        /// 방이 정한 AI 자리 수 (S15P21B202-713). <b>이 PC 가 정하지 않는다</b> — 방장이
        /// <see cref="TrySetRequestedCount"/> 로 서버에 요청하고, 서버가 <c>ROOM_STATE</c> 로
        /// 돌려준 값을 모든 PC 가 <see cref="AdoptRoom"/> 로 받아 둔다.
        ///
        /// <para>그래서 판 중에 방장이 바뀌어도 새 방장이 이미 같은 수를 안다 — 로비에서 방을
        /// 만들지 않은 사람(참가자)도 받아 두기 때문이다. 예전의 bool 은 방을 만든 사람에게만
        /// 있어서, 참가자가 방장을 이어받으면 지난 방의 값이 남아 있었다.</para>
        /// </summary>
        public static int RequestedCount { get; private set; }

        /// <summary>AI 를 하나라도 원하나. 실행기(<c>AiLocalRuntime</c>)를 켤지 정하는 데 쓴다.</summary>
        public static bool Requested => RequestedCount > 0;
        public static bool IdleTest { get; private set; }
        public static AiRuntimeState RuntimeState => runtime != null ? runtime.State : AiRuntimeState.Missing;
        public static bool TestModeAvailable
        {
            get
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                return true;
#else
                return false;
#endif
            }
        }
        // 게임 중/접속 중에는 방장도 변경하지 않는다. 방장 이전은 별도 승계 경로를 사용한다.
        public static bool CanConfigure => !NetworkServer.active && !NetworkClient.active
            && NetworkSession.Instance != null && NetworkSession.Instance.IsRoomHost
            && NetworkSession.Instance.CurrentRoom.IsWaiting;
        public static AiRoomState LocalState => AiHostPolicy.Evaluate(Requested, RuntimeState, IdleTest && TestModeAvailable);
        public static AiRoomState ServerState => NetworkServer.active ? LocalState : AiRoomState.Unknown;

        // 로컬 실행기의 수명주기에서 연결/해제한다. 네트워크 Command나 UI의 Ready 조작 경로는 없다.
        public static void BindRuntime(IAiRuntimeStatus provider) => runtime = provider;
        public static void UnbindRuntime(IAiRuntimeStatus provider)
        {
            if (ReferenceEquals(runtime, provider)) runtime = null;
        }

        /// <summary>
        /// 방장이 AI 자리 수를 서버에 요청한다 (S15P21B202-713). 요청할 수 있는 때였으면 true 다 —
        /// 소켓이 끊겨 못 보낸 것은 <see cref="NetworkSession"/> 이 오류 문구로 따로 알린다.
        ///
        /// <para><b>여기서 <see cref="RequestedCount"/> 를 바꾸지 않는다.</b> 답(<c>ROOM_STATE</c>)이
        /// 와야 바뀐다. 미리 바꾸면 서버가 거절하거나 줄였을 때(사람이 막 들어와 자리가 모자란
        /// 경우) 이 PC 만 다른 수를 믿는다 — 조용히 틀리는 쪽이다.</para>
        ///
        /// <para>옛 서버(<see cref="RoomSnapshot.SupportsAi"/> 가 거짓)에는 보내지 않는다. 모르는
        /// 메시지라 오류만 돌아온다. 절대값을 보내므로 두 번 눌려도 결과가 같다.</para>
        /// </summary>
        public static bool TrySetRequestedCount(int count)
        {
            if (!CanConfigure) return false;
            RoomSnapshot room = NetworkSession.Instance.CurrentRoom;
            if (!room.SupportsAi) return false;
            NetworkSession.Instance.SetAiCount(AiSeatMath.ClampRequested(count, room.Players.Count));
            IdleTest = false;
            return true;
        }

        /// <summary>
        /// 방 상태의 AI 수를 받아 둔다 (S15P21B202-713). <c>ROOM_STATE</c> 를 받을 때마다 모든 PC 가
        /// 부른다 — 방장만 받으면 방장이 바뀌었을 때 새 방장이 수를 모른다. 방이 없으면 0 이다.
        /// </summary>
        public static void AdoptRoom(RoomSnapshot room) => RequestedCount = room != null ? room.AiCount : 0;

        public static bool TrySetIdleTest(bool value)
        {
            if (!CanConfigure || !TestModeAvailable) return false;
            IdleTest = value;
            return true;
        }

        public static void BeginRoom()
        {
            RequestedCount = 0;
            IdleTest = false;
        }

        public static void InheritRoomChoice(AiRoomState previous)
        {
            // 이전 방장의 Ready와 개발용 테스트 모드는 물려받지 않는다.
            //
            // AI 수는 여기서 정하지 않는다(S15P21B202-713). 모든 PC 가 ROOM_STATE 로 이미 받아
            // 두었고, 그 값이 유일한 출처다. 옛 방장의 상태(previous)에서 다시 짐작하면 두 사본이
            // 어긋날 수 있고, 2맵처럼 WorldSync 가 없는 곳에서는 이 함수가 불리지도 않는다.
            IdleTest = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            runtime = null;
            BeginRoom();
        }
    }
}
