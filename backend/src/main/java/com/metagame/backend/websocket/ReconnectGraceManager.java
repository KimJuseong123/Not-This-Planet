package com.metagame.backend.websocket;

import jakarta.annotation.PreDestroy;
import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.Executors;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.ScheduledFuture;
import java.util.concurrent.TimeUnit;
import lombok.extern.slf4j.Slf4j;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.stereotype.Component;

/**
 * 연결이 끊긴 플레이어의 유예 시간을 관리한다.
 * 유예 시간이 지나면 등록된 동작(참가자 목록에서 제외)을 실행하고,
 * 그 전에 재접속하면 취소된다 (지침 문서 6장).
 */
@Slf4j
@Component
public class ReconnectGraceManager {

    private final long graceMs;
    private final ScheduledExecutorService scheduler = Executors.newSingleThreadScheduledExecutor(r -> {
        Thread t = new Thread(r, "reconnect-grace");
        t.setDaemon(true);
        return t;
    });
    private final Map<String, ScheduledFuture<?>> pending = new ConcurrentHashMap<>();

    public ReconnectGraceManager(@Value("${game.reconnect-grace-ms}") long graceMs) {
        this.graceMs = graceMs;
    }

    public long getGraceMs() {
        return graceMs;
    }

    /** 유예 타이머를 건다. 같은 대상의 기존 타이머는 교체한다. */
    public void schedule(String roomId, Long userId, Runnable onExpire) {
        String key = key(roomId, userId);
        cancel(roomId, userId);
        pending.put(key, scheduler.schedule(() -> {
            pending.remove(key);
            try {
                onExpire.run();
            } catch (RuntimeException e) {
                log.warn("grace expiry failed: room={} user={} : {}", roomId, userId, e.getMessage());
            }
        }, graceMs, TimeUnit.MILLISECONDS));
        log.info("grace started: room={} user={} ({}ms)", roomId, userId, graceMs);
    }

    /**
     * 유예 타이머를 취소한다 (재접속 성공). <b>실제로 취소한 것이 있으면 true.</b>
     *
     * <p>반환값이 있는 이유(S15P21B202-313 ②) — 예전에는 호출부가 <b>정규화되지 않은</b> 방
     * 코드를 넘겨서 취소가 조용히 빗나갔다. 아무 일도 일어나지 않는 것과 구분이 안 됐다.
     * 이제 호출부가 "취소할 것이 없었다" 를 알아채고 경고할 수 있다.
     *
     * <p><b>이미 실행이 시작된 태스크는 이 함수로 멈출 수 없다.</b> 아래 schedule 의 태스크가
     * 먼저 {@code pending} 에서 자기를 지우기 때문에 여기서 키를 찾지 못하고, 찾았더라도
     * {@code cancel(false)} 는 실행 중인 것을 건드리지 않는다. 그래서 만료 동작 쪽에서
     * <b>다시 확인</b>해야 한다 — {@code GameWebSocketHandler.onGraceExpired} 참고.
     */
    public boolean cancel(String roomId, Long userId) {
        ScheduledFuture<?> f = pending.remove(key(roomId, userId));
        if (f == null) {
            return false;
        }
        f.cancel(false);
        log.info("grace cancelled: room={} user={}", roomId, userId);
        return true;
    }

    private String key(String roomId, Long userId) {
        return roomId + ":" + userId;
    }

    @PreDestroy
    void shutdown() {
        scheduler.shutdownNow();
    }
}
