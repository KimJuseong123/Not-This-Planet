package com.metagame.backend.domain.leaderboard;

/**
 * 리더보드 판 구분 (S15P21B202-656).
 *
 * <p><b>인원 수 하나로 가른다.</b> 게임이 이미 인원으로 난이도를 조정하므로
 * (기획 §17.6 — 혼자면 운석 수가 45%), 같은 스테이지를 인원으로 가르는 것이
 * 곧 난이도로 가르는 것이다. 별도의 모드 선택 화면이 없어도 성립한다.</p>
 */
public enum RunMode {

    /** 혼자하기. 1인. */
    SOLO,

    /** 협동모드. 2~4인. */
    COOP;

    /** 게임에서 허용하는 최대 인원. 기획 §1 의 4인 협동. */
    public static final int MAX_CREW = 4;

    /**
     * 인원 수가 정하는 판.
     *
     * @throws IllegalArgumentException 1~{@link #MAX_CREW} 밖이면. 클라이언트가 보낸
     *     값이므로 믿지 않는다 — 0명이나 99명짜리 기록이 표에 오르면 안 된다.
     */
    public static RunMode ofCrew(int crewSize) {
        if (crewSize < 1 || crewSize > MAX_CREW) {
            throw new IllegalArgumentException(
                    "인원 수가 1~" + MAX_CREW + " 를 벗어납니다: " + crewSize);
        }
        return crewSize == 1 ? SOLO : COOP;
    }
}
