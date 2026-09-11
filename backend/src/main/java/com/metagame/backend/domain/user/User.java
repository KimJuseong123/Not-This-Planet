package com.metagame.backend.domain.user;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.GeneratedValue;
import jakarta.persistence.GenerationType;
import jakarta.persistence.Id;
import jakarta.persistence.Table;
import java.time.LocalDateTime;
import lombok.AccessLevel;
import lombok.Builder;
import lombok.Getter;
import lombok.NoArgsConstructor;

@Entity
@Table(name = "users")
@Getter
@NoArgsConstructor(access = AccessLevel.PROTECTED)
public class User {

    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    @Column(nullable = false, length = 20)
    private String nickname;

    // 게스트 로그인: 기기 재접속 시 같은 유저를 찾는 키
    @Column(nullable = false, unique = true, length = 64)
    private String deviceId;

    // Steam 배포 대비 자리. 계정 승격 전까지 항상 null
    @Column(unique = true)
    private Long steamId;

    @Column(nullable = false)
    private LocalDateTime createdAt;

    private LocalDateTime lastLoginAt;

    @Builder
    private User(String nickname, String deviceId) {
        this.nickname = nickname;
        this.deviceId = deviceId;
        this.createdAt = LocalDateTime.now();
        this.lastLoginAt = LocalDateTime.now();
    }

    public void login(String nickname) {
        this.nickname = nickname;
        this.lastLoginAt = LocalDateTime.now();
    }
}
