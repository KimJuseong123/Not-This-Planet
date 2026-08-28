package com.metagame.backend.global.auth;

import static org.assertj.core.api.Assertions.assertThat;

import org.junit.jupiter.api.Test;

class JwtProviderTest {

    private static final String SECRET =
            "dGVzdC1zZWNyZXQta2V5LWZvci1qd3QtcHJvdmlkZXItdW5pdC10ZXN0LTMyKw==";
    private static final long ONE_HOUR = 3_600_000L;

    private final JwtProvider jwtProvider = new JwtProvider(SECRET, ONE_HOUR);

    @Test
    void issuedTokenResolvesBackToSameUserId() {
        String token = jwtProvider.createToken(42L);

        assertThat(jwtProvider.parseUserId(token)).isEqualTo(42L);
    }

    @Test
    void expiredTokenIsRejected() {
        JwtProvider alreadyExpired = new JwtProvider(SECRET, -1_000L);

        String token = alreadyExpired.createToken(42L);

        assertThat(alreadyExpired.parseUserId(token)).isNull();
    }

    @Test
    void tokenSignedWithAnotherSecretIsRejected() {
        JwtProvider attacker = new JwtProvider(
                "YW5vdGhlci1zZWNyZXQta2V5LXRoYXQtaXMtbG9uZy1lbm91Z2gtMzJieXRl", ONE_HOUR);

        String forged = attacker.createToken(42L);

        assertThat(jwtProvider.parseUserId(forged)).isNull();
    }

    @Test
    void tamperedTokenIsRejected() {
        String token = jwtProvider.createToken(42L);
        String tampered = token.substring(0, token.length() - 2) + "xy";

        assertThat(jwtProvider.parseUserId(tampered)).isNull();
    }

    @Test
    void malformedTokenIsRejected() {
        assertThat(jwtProvider.parseUserId("not-a-jwt")).isNull();
        assertThat(jwtProvider.parseUserId("")).isNull();
    }
}
