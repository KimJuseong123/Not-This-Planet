namespace Metagame.Network
{
    /// <summary>
    /// 이 실행이 어떤 자리로 매치에 들어갔는지 (S15P21B202-263).
    ///
    /// <b>왜 따로 두나.</b> 게임 씬은 "화면을 그릴 사람이 있는가" 를 알아야 한다 —
    /// 전용 서버라면 캐릭터·카메라·HUD 를 만들 이유가 없고, 방장이라면 반드시 만들어야 한다.
    /// 그런데 Mirror 의 상태로는 그 구분이 되지 않는 순간이 있다.
    ///
    /// <c>NetworkManager.StartHost()</c> 는 온라인 씬이 다르면 <c>finishStartHostPending</c> 로
    /// 표시해 두고 <b>씬을 먼저 바꾼 뒤</b>, 로딩이 끝나야 <c>FinishStartHost()</c> →
    /// <c>NetworkClient.ConnectHost()</c> 를 부른다(NetworkManager.cs 497·545·1038행).
    /// 그래서 게임 씬이 뜨는 동안 방장은 <c>NetworkServer.active == true</c> 이면서
    /// <c>NetworkClient.active == false</c> 다 — 전용 서버와 구별되지 않는다.
    ///
    /// 실제로 방장 화면이 캄캄한 채 인게임으로 넘어가지 않았다(2026-09-02 실측).
    /// 씬이 방장을 전용 서버로 오인해 카메라를 만들지 않았기 때문이다.
    ///
    /// 그래서 Mirror 를 켠 쪽이 직접 자리를 적어 둔다. 켜기 <b>전에</b> 정해지는 값이라
    /// 씬이 언제 뜨든 흔들리지 않는다.
    /// </summary>
    public static class MatchRole
    {
        /// <summary>
        /// 화면 없는 전용 서버로 도는가. 방장(호스트)은 <b>거짓</b>이다 — 사람이 보는 화면이 있다.
        /// 혼자 실행하는 경우도 거짓이다.
        /// </summary>
        public static bool DedicatedServer;
    }
}
