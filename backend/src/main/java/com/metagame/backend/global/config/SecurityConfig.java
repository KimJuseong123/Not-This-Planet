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

                        // 플레이 로그 (S15P21B202-443). **여기서 permitAll 이지만 열려
                        // 있는 것이 아니다** — 컨트롤러가 Authorization 헤더의 게스트
                        // 토큰을 직접 검증하고, 없거나 틀리면 401 을 돌려준다.
                        //
                        // 왜 필터가 아니라 이 방식인가: 이 프로젝트에는 아직 **JWT 인증
                        // 필터가 없다**(아래 TODO). `authenticated()` 로 두면 SecurityContext
                        // 를 채우는 것이 없어서 **토큰이 맞아도 403** 이 된다 — 조용히
                        // 안 되는 종류의 고장이다. `/ws/**` 가 이미 같은 방식으로 돈다.
                        //
                        // 인증이 필요한 REST 엔드포인트가 하나 더 생기면 그때는 필터를
                        // 만드는 편이 싸다. 지금 만들면 모든 경로의 거동이 바뀌는데
                        // 그것을 확인할 통합 테스트가 없다.
                        .requestMatchers("/logs").permitAll()

                        .anyRequest().authenticated());
        // TODO: 인증 필요한 REST API 가 둘 이상이 되면 JWT 인증 필터를 등록하고
        //       위 /logs · /ws 의 자체 검증을 그쪽으로 옮긴다
        return http.build();
    }
}
