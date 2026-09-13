package com.metagame.backend.domain.auth;

import static org.assertj.core.api.Assertions.assertThat;

import com.metagame.backend.domain.auth.ClientVersionGate.Verdict;
import org.junit.jupiter.api.Test;

/**
 * 구버전 차단 판정 (S15P21B202-404).
 *
 * <p>Spring 을 띄우지 않는다 — 순수 계산이라 빠르게 많이 돌린다.
 */
class ClientVersionGateTest {

    private static ClientVersionGate gate(String min) {
        return new ClientVersionGate(min, "https://example.test/play/");
    }

    // ── 이슈가 요구한 네 경우 ────────────────────────────────────────────

    @Test
    void sameVersionPasses() {
        assertThat(gate("v1.2.0").check("v1.2.0")).isEqualTo(Verdict.OK);
    }

    @Test
    void olderVersionIsRejected() {
        assertThat(gate("v1.2.0").check("v1.1.9")).isEqualTo(Verdict.TOO_OLD);
    }

    /**
     * **모르는 새 버전은 통과시킨다.** 백엔드와 클라이언트가 동시에 나가지 않으므로,
     * 허용 목록이었다면 클라이언트가 먼저 나간 순간 전원이 막혔을 것이다.
     */
    @Test
    void newerVersionPasses() {
        assertThat(gate("v1.2.0").check("v1.3.0")).isEqualTo(Verdict.OK);
        assertThat(gate("v1.2.0").check("v2.0.0")).isEqualTo(Verdict.OK);
    }

    /** 버전을 못 읽는 옛 빌드가 **정확히 막아야 하는 대상**이다. 통과시키면 게이트가 뚫린다. */
    @Test
    void missingOrUnreadableVersionIsRejected() {
        ClientVersionGate g = gate("v1.2.0");
        assertThat(g.check(null)).isEqualTo(Verdict.UNKNOWN);
        assertThat(g.check("")).isEqualTo(Verdict.UNKNOWN);
        assertThat(g.check("   ")).isEqualTo(Verdict.UNKNOWN);
        assertThat(g.check("v1.2")).isEqualTo(Verdict.UNKNOWN);        // 자리 수가 다르다
        assertThat(g.check("1.2.0.4")).isEqualTo(Verdict.UNKNOWN);
        assertThat(g.check("v1.2.0-dev")).isEqualTo(Verdict.UNKNOWN);  // 꼬리표는 받지 않는다
        assertThat(g.check("최신")).isEqualTo(Verdict.UNKNOWN);
    }

    // ── 켜고 끄는 것 ────────────────────────────────────────────────────

    /**
     * **비어 있으면 아무도 막지 않는다.** 이 코드가 배포되는 순간 이미 사람들 손에 있는
     * 빌드가 전부 막히면 안 된다. 개발 서버도 이 상태로 둔다 — 에디터 실행과 dev·qa
     * 빌드는 `vX.Y.Z` 모양이 아니라 켜면 팀이 전부 막힌다.
     */
    @Test
    void emptyMinVersionDisablesTheGateEntirely() {
        for (String min : new String[] {"", "   ", null}) {
            ClientVersionGate g = gate(min);
            assertThat(g.enabled()).isFalse();
            assertThat(g.check(null)).isEqualTo(Verdict.OK);
            assertThat(g.check("v0.0.1")).isEqualTo(Verdict.OK);
        }
    }

    /** 최소 버전 자체가 이상하면 **막지 않는다** — 오타 하나로 전원이 못 들어오면 더 나쁘다. */
    @Test
    void unreadableMinVersionDisablesTheGate() {
        ClientVersionGate g = gate("최신으로");
        assertThat(g.enabled()).isFalse();
        assertThat(g.check("v0.0.1")).isEqualTo(Verdict.OK);
    }

    // ── 조용히 틀리는 자리 ──────────────────────────────────────────────

    /**
     * **문자열 비교였다면 여기서 뒤집힌다.** `"v1.10.0" < "v1.9.0"` 이 참이라 새 버전이
     * 낮다고 판정된다. 두 자리 수가 나오는 순간이라 한참 뒤에야 드러난다.
     */
    @Test
    void twoDigitPartsCompareAsNumbersNotText() {
        assertThat(gate("v1.9.0").check("v1.10.0")).isEqualTo(Verdict.OK);
        assertThat(gate("v1.10.0").check("v1.9.0")).isEqualTo(Verdict.TOO_OLD);
        assertThat(gate("v2.0.0").check("v10.0.0")).isEqualTo(Verdict.OK);
    }

    @Test
    void leadingVIsOptionalOnBothSides() {
        assertThat(gate("1.2.0").check("v1.2.0")).isEqualTo(Verdict.OK);
        assertThat(gate("v1.2.0").check("1.2.0")).isEqualTo(Verdict.OK);
        assertThat(gate("V1.2.0").check("V1.2.0")).isEqualTo(Verdict.OK);
    }

    @Test
    void patchAndMinorAreBothCompared() {
        assertThat(gate("v1.2.3").check("v1.2.2")).isEqualTo(Verdict.TOO_OLD);
        assertThat(gate("v1.2.3").check("v1.2.4")).isEqualTo(Verdict.OK);
        assertThat(gate("v1.2.3").check("v0.9.9")).isEqualTo(Verdict.TOO_OLD);
    }

    // ── 사용자가 보는 것 ────────────────────────────────────────────────

    /** 거절 사유에 **무엇이 필요한지**가 들어 있어야 한다. 로그만 남기면 사용자는 이유를 모른다. */
    @Test
    void rejectionMessageSaysWhatIsNeeded() {
        ClientVersionGate g = gate("v1.2.0");
        assertThat(g.message(Verdict.TOO_OLD)).contains("v1.2.0");
        assertThat(g.message(Verdict.UNKNOWN)).isNotEmpty();
        assertThat(g.message(Verdict.OK)).isEmpty();
        assertThat(g.downloadUrl()).isEqualTo("https://example.test/play/");
    }
}
