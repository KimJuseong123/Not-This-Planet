package com.metagame.backend.websocket;

import java.util.LinkedHashMap;

/**
 * 릴레이 세션을 열고 닫는 주체 (S15P21B202-299).
 *
 * <p>구현이 둘이다. 어느 것을 쓸지는 {@code relay.provider} 속성이 정한다.
 *
 * <ul>
 *   <li>{@code local} (기본) — {@link LocalRelayService}. 우리 EC2 의 릴레이. 채택된 방식이다.
 *   <li>{@code edgegap} — {@link EdgegapRelayService}. 예전 방식. 되돌릴 길로만 남겨 둔다.
 * </ul>
 *
 * <p><b>왜 우리 릴레이로 바꿨나.</b> {@code -298} 에서 Edgegap 의 서버 검증이 우리 세션에서
 * 동작하지 않고 <b>우리가 고칠 수 없다</b>는 것을 실측으로 확정했다(300초·260회 응답 내내
 * {@code Checking}, 30초 주기 재시도, 25세션 중 1회만 성공). 그리고 {@code -145} 의 원래 결정이
 * EC2 릴레이였다 — Edgegap 은 나중에 그 자리에 들어온 것이다.
 *
 * <p><b>릴레이를 우리가 돌려도 netcode 를 새로 만드는 것이 아니다.</b> 재전송·순서 보장·혼잡
 * 제어(KCP)는 Mirror 가 양쪽 끝에서 그대로 한다. 릴레이는 내용을 모르는 채 바이트를 옮긴다.
 */
public interface RelayProvider {

    /** 릴레이를 쓸 수 있는 상태인가. 아니면 같은 네트워크 직접 접속으로 간다. */
    boolean isEnabled();

    /**
     * 이 방을 위한 릴레이 세션을 연다. 실패하면 {@code null} — <b>릴레이 하나 때문에 매치가
     * 아예 안 열리는 것보다 낫다.</b>
     *
     * @param userIps    userId → 공인 IP. 우리 릴레이는 IP 를 쓰지 않지만(토큰으로 가른다)
     *                   Edgegap 구현이 필요로 하므로 인터페이스에 남긴다.
     * @param hostUserId 방장. <b>순서에 기대지 않고 명시로 받는다</b> — 우리 릴레이는 누가 서버인지를
     *                   알아야 서버 역할의 핑을 받아 줄 수 있다.
     */
    RelaySession open(LinkedHashMap<Long, String> userIps, Long hostUserId);

    /**
     * 세션을 닫는다. 방이 사라질 때 부른다.
     *
     * <p>예전에는 이것을 부르는 곳이 없어 세션이 계속 쌓였다({@code -298} §5-①). 그때는
     * {@link RelaySession} 이 식별자를 들고 있지 않아 부를 수도 없었다.
     */
    void close(String sessionId);
}
