package com.metagame.backend.websocket;

import static org.assertj.core.api.Assertions.assertThat;

import java.util.Arrays;
import java.util.List;
import org.junit.jupiter.api.DisplayName;
import org.junit.jupiter.api.Test;

/**
 * 릴레이를 붙일지 직접 접속할지 (S15P21B202-570).
 *
 * <p><b>왜 이 검사가 있나.</b> 이 판정에 검사가 하나도 없었고, 그래서 <b>서로 다른 망을
 * 같은 망으로 보는 결함이 2주 동안 남아 있었다.</b> 2026-09-16 에 팀원 둘이 입장하지 못했다 —
 * 백엔드 로그는 {@code distinctIps=2} 인데 {@code decision=direct(same network)} 였다.
 *
 * <p><b>틀렸을 때의 대가가 한쪽으로 기울어 있다.</b> 릴레이는 느릴 뿐 항상 되고, 직접 접속은
 * 안 될 때 아예 못 들어간다. 그래서 애매한 경우는 전부 릴레이 쪽으로 못 박는다.
 */
class SameNetworkTest {

    private static List<String> ips(String... values) {
        return Arrays.asList(values);
    }

    @Test
    @DisplayName("혼자면 붙을 상대가 없다")
    void aloneIsAlwaysSame() {
        assertThat(GameWebSocketHandler.sameNetwork(ips("1.2.3.4"), ips("192.168.0.2"))).isTrue();
    }

    @Test
    @DisplayName("공인 IP 가 하나면 같은 공유기 뒤다")
    void oneIpIsSameRouter() {
        assertThat(GameWebSocketHandler.sameNetwork(
                ips("1.2.3.4", "1.2.3.4"), ips("192.168.0.2", "192.168.0.7"))).isTrue();
    }

    @Test
    @DisplayName("교육장 — 공인 IP 를 돌려쓰지만 한 /24 이고 랜 대역도 같다")
    void classroomPoolStaysDirect() {
        // 2026-09-02 회귀: 이것이 없어서 같은 WiFi 인데 릴레이를 탔다.
        assertThat(GameWebSocketHandler.sameNetwork(
                ips("210.11.22.33", "210.11.22.57"),
                ips("192.168.31.10", "192.168.31.66"))).isTrue();
    }

    @Test
    @DisplayName("서로 다른 집 — 사설 대역이 우연히 같아도 다른 망이다")
    void collidingPrivateSubnetIsNotSame() {
        // **이것이 이번 사고다.** `192.168.31.x` 는 어떤 공유기의 기본값이라 흔히 겹친다.
        assertThat(GameWebSocketHandler.sameNetwork(
                ips("1.2.3.4", "121.130.55.9"),
                ips("192.168.31.35", "192.168.31.77"))).isFalse();
    }

    @Test
    @DisplayName("아이폰 핫스팟 대역도 마찬가지로 겹친다")
    void collidingHotspotSubnetIsNotSame() {
        assertThat(GameWebSocketHandler.sameNetwork(
                ips("1.2.3.4", "121.130.55.9"),
                ips("172.16.10.2", "172.16.10.3"))).isFalse();
    }

    @Test
    @DisplayName("공인 IP 가 한 /24 라도 랜 대역이 다르면 직접 닿는다고 볼 수 없다")
    void samePublicBlockButDifferentLan() {
        assertThat(GameWebSocketHandler.sameNetwork(
                ips("210.11.22.33", "210.11.22.57"),
                ips("192.168.0.10", "192.168.1.10"))).isFalse();
    }

    @Test
    @DisplayName("랜 주소를 안 알려 준 사람이 있으면 릴레이로 간다")
    void missingLocalIpFallsBackToRelay() {
        assertThat(GameWebSocketHandler.sameNetwork(
                ips("210.11.22.33", "210.11.22.57"),
                ips("192.168.31.10", null))).isFalse();
        assertThat(GameWebSocketHandler.sameNetwork(
                ips("210.11.22.33", "210.11.22.57"),
                ips("192.168.31.10", ""))).isFalse();
    }

    @Test
    @DisplayName("랜 주소라고 했는데 공인 주소면 믿지 않는다")
    void publicAddressIsNotALocalSubnet() {
        assertThat(GameWebSocketHandler.sameNetwork(
                ips("210.11.22.33", "210.11.22.57"),
                ips("8.8.8.8", "8.8.8.9"))).isFalse();
    }

    @Test
    @DisplayName("IPv6 등 IPv4 가 아닌 공인 주소는 판단하지 않고 릴레이로 간다")
    void nonIpv4PublicAddressFallsBackToRelay() {
        assertThat(GameWebSocketHandler.sameNetwork(
                ips("2001:db8::1", "2001:db8::2"),
                ips("192.168.31.10", "192.168.31.66"))).isFalse();
    }

    @Test
    @DisplayName("네 명이 모두 같은 망")
    void fourPlayersSameNetwork() {
        assertThat(GameWebSocketHandler.sameNetwork(
                ips("210.11.22.1", "210.11.22.2", "210.11.22.3", "210.11.22.4"),
                ips("192.168.31.2", "192.168.31.3", "192.168.31.4", "192.168.31.5"))).isTrue();
    }

    @Test
    @DisplayName("네 명 중 한 명만 다른 망이어도 릴레이")
    void oneOutsiderMeansRelay() {
        assertThat(GameWebSocketHandler.sameNetwork(
                ips("210.11.22.1", "210.11.22.2", "210.11.22.3", "121.130.55.9"),
                ips("192.168.31.2", "192.168.31.3", "192.168.31.4", "192.168.31.5"))).isFalse();
    }
}
