package com.metagame.backend;

import org.springframework.boot.SpringApplication;
import org.springframework.boot.autoconfigure.SpringBootApplication;
import org.springframework.scheduling.annotation.EnableScheduling;

// 플레이 로그 보관 회수가 @Scheduled 로 돈다 (S15P21B202-443).
// **이 한 줄이 없으면 @Scheduled 가 조용히 안 돈다** — 오류도 경고도 없이
// 디스크만 찬다.
@EnableScheduling
@SpringBootApplication
public class BackendApplication {

	public static void main(String[] args) {
		SpringApplication.run(BackendApplication.class, args);
	}

}
