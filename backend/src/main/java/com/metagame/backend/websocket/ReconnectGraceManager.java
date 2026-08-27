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

    /** 유예 타이머를 취소한다 (재접속 성공). */
    public void cancel(String roomId, Long userId) {
        ScheduledFuture<?> f = pending.remove(key(roomId, userId));
        if (f != null) {
            f.cancel(false);
            log.info("grace cancelled: room={} user={}", roomId, userId);
        }
    }

    private String key(String roomId, Long userId) {
        return roomId + ":" + userId;
    }

    @PreDestroy
    void shutdown() {
        scheduler.shutdownNow();
    }
}
