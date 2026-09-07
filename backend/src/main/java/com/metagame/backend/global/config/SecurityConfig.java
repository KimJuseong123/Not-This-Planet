package com.metagame.backend.global.config;

import jakarta.servlet.DispatcherType;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.security.config.annotation.web.builders.HttpSecurity;
import org.springframework.security.config.annotation.web.configuration.EnableWebSecurity;
import org.springframework.security.config.http.SessionCreationPolicy;
import org.springframework.security.web.SecurityFilterChain;

@Configuration
@EnableWebSecurity
public class SecurityConfig {

    @Bean
    public SecurityFilterChain securityFilterChain(HttpSecurity http) throws Exception {
        http
                .csrf(csrf -> csrf.disable())
                .formLogin(form -> form.disable())
                .httpBasic(basic -> basic.disable())
                .sessionManagement(session ->
                        session.sessionCreationPolicy(SessionCreationPolicy.STATELESS))
                .authorizeHttpRequests(auth -> auth
                        // 오류 응답을 만드는 내부 포워드는 통과시킨다 (S15P21B202-210).
                        //
                        // Spring Boot 는 오류를 /error 로 **포워드**해서 응답 본문을 만들고,
                        // 그 포워드도 이 필터를 다시 지난다. 아래 anyRequest().authenticated()
                        // 가 그것까지 막으면, 앱이 이미 만든 400·405 가 403 으로 덮여 나간다.
                        // 클라이언트는 "내 요청이 잘못됐다" 와 "권한이 없다" 를 구분할 수 없다.
                        //
                        // 경로(/error)가 아니라 **디스패치 종류**로 허용한다. 그래야 밖에서
                        // /error 를 직접 부르는 것은 그대로 막힌다 — 경로를 permitAll 하면
                        // 그 문까지 열린다.
                        .dispatcherTypeMatchers(DispatcherType.ERROR).permitAll()
                        .requestMatchers("/auth/**").permitAll()
                        .requestMatchers("/ws/**").permitAll()   // WebSocket은 핸드셰이크 인터셉터에서 JWT 검증
                        .anyRequest().authenticated());
        // TODO: 인증 필요한 REST API 추가 시 JWT 인증 필터 등록
        return http.build();
    }
}
