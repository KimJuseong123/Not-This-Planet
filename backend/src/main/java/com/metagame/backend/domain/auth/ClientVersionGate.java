package com.metagame.backend.domain.auth;

import org.springframework.beans.factory.annotation.Value;
import org.springframework.stereotype.Component;

/**
 * 구버전 클라이언트를 로그인에서 막는다 (S15P21B202-404).
 *
 * <p><b>왜 서버가 막나.</b> 런처로 막으면 우회된다 — 압축을 푼 폴더의 exe 를 직접
 * 더블클릭하는 경우가 가장 흔하고, 그게 정확히 막아야 하는 경우다. 게임에 들어오려면
 * 반드시 {@code POST /auth/guest} 를 지나므로, <b>여기서 보면 물리적으로 참가할 수 없다.</b>
 *
 * <p><b>허용 목록이 아니라 최소 버전이다.</b> 백엔드({@code deploy-*})와 클라이언트
 * ({@code release-windows})는 트리거·브랜치·시점이 달라 <b>동시에 나가지 않는다.</b>
 * 허용 목록이면 클라이언트가 먼저 나갔을 때 백엔드가 그 버전을 몰라 <b>전원 거절</b>한다.
 * 그래서 {@code 클라 < 최소} 일 때만 막고 <b>모르는 새 버전은 통과</b>시킨다.
 */
@Component
public class ClientVersionGate {

    /** 판정 결과. {@code OK} 가 아니면 로그인을 거절한다. */
    public enum Verdict {
        /** 통과 — 같거나 높은 버전, 또는 게이트가 꺼져 있다. */
        OK,
        /** 최소 허용 버전보다 낮다. */
        TOO_OLD,
        /** 버전을 읽을 수 없다 — 스탬프가 없는 옛 빌드이거나 모양이 다르다. */
        UNKNOWN
    }

    private final String minVersion;
    private final String downloadUrl;

    public ClientVersionGate(
            // **비우면 게이트가 꺼진다.** 이것이 기본값인 이유: 이 코드가 배포되는 순간
            // 이미 사람들 손에 있는 빌드가 전부 막히면 안 된다. 최소 버전을 올리는
            // **시점이 곧 구버전 차단 시점**이고, 그 판단은 사람이 한다.
            //
            // 개발 서버(`/dev`)는 계속 비워 둔다 — 에디터 실행과 dev·qa 빌드는
            // `vX.Y.Z` 모양이 아니라 켜면 팀이 전부 막힌다.
            @Value("${metagame.client.min-version:}") String minVersion,
            @Value("${metagame.client.download-url:https://j15b202.p.ssafy.io/play/}") String downloadUrl) {
        this.minVersion = minVersion == null ? "" : minVersion.trim();
        this.downloadUrl = downloadUrl == null ? "" : downloadUrl.trim();
    }

    /** 게이트가 켜져 있나. 최소 버전이 비어 있으면 아무도 막지 않는다. */
    public boolean enabled() {
        return parse(minVersion) != null;
    }

    public String minVersion() {
        return minVersion;
    }

    public String downloadUrl() {
        return downloadUrl;
    }

    /**
     * 이 클라이언트를 들여보낼 것인가.
     *
     * @param clientVersion 클라이언트가 보낸 릴리스 버전({@code v1.0.0}). 옛 빌드는 보내지 않는다.
     */
    public Verdict check(String clientVersion) {
        int[] min = parse(minVersion);
        if (min == null) return Verdict.OK;          // 게이트가 꺼져 있다

        int[] client = parse(clientVersion);
        // **모르는 것을 통과시키면 게이트가 뚫린다.** 스탬프가 없던 시절 빌드가 정확히
        // 막아야 하는 대상이다 — 그 빌드들은 버전을 보내지 않는다.
        if (client == null) return Verdict.UNKNOWN;

        return compare(client, min) < 0 ? Verdict.TOO_OLD : Verdict.OK;
    }

    /** 사람이 읽을 거절 사유. 화면에 그대로 띄운다 — 로그만 남기면 사용자는 이유를 모른다. */
    public String message(Verdict verdict) {
        if (verdict == Verdict.TOO_OLD) {
            return "게임 버전이 낮아 접속할 수 없습니다. 최소 " + minVersion + " 이 필요합니다.";
        }
        if (verdict == Verdict.UNKNOWN) {
            return "게임 버전을 확인할 수 없습니다. 최신 버전을 받아 주세요.";
        }
        return "";
    }

    /**
     * {@code v1.2.3} → {@code [1,2,3]}. 읽을 수 없으면 {@code null}.
     *
     * <p><b>문자열 비교를 쓰지 않는다.</b> {@code "v1.10.0" < "v1.9.0"} 이 되어 새 버전이
     * 낮다고 판정된다 — 두 자리 수가 나오는 순간 조용히 틀린다.
     */
    static int[] parse(String version) {
        if (version == null) return null;
        String s = version.trim();
        if (s.startsWith("v") || s.startsWith("V")) s = s.substring(1);
        if (s.isEmpty()) return null;

        String[] parts = s.split("\\.", -1);
        if (parts.length != 3) return null;

        int[] nums = new int[3];
        for (int i = 0; i < 3; i++) {
            // `1.0.0-dev` 같은 꼬리표는 받지 않는다. 배포 클라이언트는 순수 `vX.Y.Z` 이고,
            // 그 밖의 모양을 억지로 해석하면 **무엇을 통과시켰는지 아무도 모르게 된다.**
            if (parts[i].isEmpty() || parts[i].length() > 9) return null;
            for (int c = 0; c < parts[i].length(); c++) {
                if (parts[i].charAt(c) < '0' || parts[i].charAt(c) > '9') return null;
            }
            nums[i] = Integer.parseInt(parts[i]);
        }
        return nums;
    }

    static int compare(int[] a, int[] b) {
        for (int i = 0; i < 3; i++) {
            if (a[i] != b[i]) return a[i] < b[i] ? -1 : 1;
        }
        return 0;
    }
}
