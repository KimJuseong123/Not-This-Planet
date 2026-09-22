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
        public static bool Requested { get; private set; }
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

        public static bool TrySetRequested(bool value)
        {
            if (!CanConfigure) return false;
            Requested = value;
            IdleTest = false;
            return true;
        }

        public static bool TrySetIdleTest(bool value)
        {
            if (!CanConfigure || !TestModeAvailable) return false;
            IdleTest = value;
            return true;
        }

        public static void BeginRoom()
        {
            Requested = false;
            IdleTest = false;
        }

        public static void InheritRoomChoice(AiRoomState previous)
        {
            // 선택만 승계한다. 이전 방장의 Ready와 개발용 테스트 모드는 물려받지 않는다.
            Requested = previous == AiRoomState.Active || previous == AiRoomState.Missing
                || previous == AiRoomState.Loading || previous == AiRoomState.Failed;
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
