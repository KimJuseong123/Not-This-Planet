package com.metagame.backend.global.config;

import com.metagame.backend.websocket.GameWebSocketHandler;
import com.metagame.backend.websocket.JwtHandshakeInterceptor;
import lombok.RequiredArgsConstructor;
import org.springframework.context.annotation.Configuration;
import org.springframework.web.socket.config.annotation.EnableWebSocket;
import org.springframework.web.socket.config.annotation.WebSocketConfigurer;
import org.springframework.web.socket.config.annotation.WebSocketHandlerRegistry;

/**
 * raw WebSocket 설정. STOMP는 Unity 클라이언트가 없어 사용하지 않는다 (지침 문서 2-1).
 * 접속: ws://localhost:8080/ws/game?token={JWT}
 */
@Configuration
@EnableWebSocket
@RequiredArgsConstructor
public class WebSocketConfig implements WebSocketConfigurer {

    private final GameWebSocketHandler gameWebSocketHandler;
    private final JwtHandshakeInterceptor jwtHandshakeInterceptor;

    @Override
    public void registerWebSocketHandlers(WebSocketHandlerRegistry registry) {
        registry.addHandler(gameWebSocketHandler, "/ws/game")
                .addInterceptors(jwtHandshakeInterceptor)
                .setAllowedOrigins("*");   // TODO: 배포 시 Unity 클라이언트 도메인으로 제한
    }
}
