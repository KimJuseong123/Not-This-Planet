package com.metagame.backend.domain.runlog;

import static org.assertj.core.api.Assertions.assertThat;
import static org.junit.jupiter.api.Assumptions.assumeTrue;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

/**
 * 플레이 로그 저장의 상한 (S15P21B202-467).
 *
 * <p><b>무엇을 막는 검증인가.</b> 이 자리는 <b>로그인 없이도 열려 있는 업로드
 * 자리</b>다. {@code /auth/guest} 가 {@code permitAll} 이라 토큰을 새로 받을 때마다
 * userId 가 새로 생기고, 그러면 <b>사용자별 상한은 아무것도 막지 못한다.</b>
 * 막는 것은 전역 상한 하나뿐이라 그것이 조용히 꺼지면 디스크가 찬다 — 디스크가
 * 차면 백엔드·MySQL·CI 가 한꺼번에 죽는다.</p>
 */
class RunLogStoreTest {

    private RunLogStore store(Path dir) {
        return new RunLogStore(dir.toString(), 14);
    }

    @Test
    void theGlobalCapHoldsEvenWhenEveryUploadUsesANewUserId(@TempDir Path dir) {
        RunLogStore s = store(dir);

        // 매번 다른 userId — 게스트 토큰을 다시 받는 것과 같은 모양이다.
        for (int i = 0; i < RunLogStore.MAX_GLOBAL_PER_HOUR; i++) {
            assertThat(s.allowGlobal(1))
                    .as("전역 상한 안인데 막혔다 (%d번째)", i)
                    .isTrue();
        }
        assertThat(s.allowGlobal(1))
                .as("개수 상한을 넘겼는데 통과한다 — 사용자별 상한은 새 userId 로 우회된다")
                .isFalse();
    }

    @Test
    void theGlobalCapAlsoCountsBytesNotJustUploads(@TempDir Path dir) {
        RunLogStore s = store(dir);

        // 개수는 몇 개 안 되지만 바이트로는 상한을 넘는 경우. 8 MB 짜리를 40개면
        // 320 MB 다 — 개수만 세면 200개 전까지 그대로 받는다.
        int big = RunLogStore.MAX_BYTES;
        int fits = (int) (RunLogStore.MAX_GLOBAL_BYTES_PER_HOUR / big);
        for (int i = 0; i < fits; i++) {
            assertThat(s.allowGlobal(big)).as("바이트 상한 안인데 막혔다 (%d번째)", i).isTrue();
        }
        assertThat(s.allowGlobal(big))
                .as("바이트 상한을 넘겼는데 통과한다")
                .isFalse();
    }

    @Test
    void notBeingAbleToMeasureTheDiskDoesNotThrowAwayLogs(@TempDir Path dir) {
        // 폴더가 없으면 파일 시스템을 못 잰다. 그때 <b>받는 쪽</b>으로 기울어야 한다 —
        // 못 재는 것은 디스크가 찼다는 뜻이 아니고, 여기서 막으면 로그가 통째로 사라진다.
        RunLogStore s = store(dir.resolve("없는-폴더"));

        assertThat(s.hasDiskRoom())
                .as("디스크를 못 재는 것을 '가득 찼다' 로 읽는다 — 로그가 통째로 사라진다")
                .isTrue();
    }

    @Test
    void aSecondLogInTheSameSecondDoesNotOverwriteTheFirst(@TempDir Path dir) throws IOException {
        // 이름은 **초 단위**다. 같은 사람이 같은 초에 두 건을 올리고 클라이언트 이름이
        // 비면 이름이 그대로 겹친다 — 예전에는 `REPLACE_EXISTING` 이라 먼저 받은
        // 로그가 조용히 사라졌다. 사라지는 쪽이 보통 더 중요한 쪽(크래시)이다.
        String base = "20260913-101500_u7_prod_none";
        assertThat(RunLogStore.freeName(dir, base)).isEqualTo(base + ".log");

        Files.writeString(dir.resolve(base + ".log"), "먼저 받은 것");
        assertThat(RunLogStore.freeName(dir, base))
                .as("이미 있는 이름을 다시 고른다 — 먼저 받은 로그를 덮어쓴다")
                .isEqualTo(base + "-2.log");

        Files.writeString(dir.resolve(base + "-2.log"), "두 번째");
        assertThat(RunLogStore.freeName(dir, base)).isEqualTo(base + "-3.log");

        // 먼저 받은 것이 그대로 있어야 한다.
        assertThat(Files.readString(dir.resolve(base + ".log"))).isEqualTo("먼저 받은 것");
    }

    @Test
    void theOrderOfTheChecksIsDiskThenGlobalThenUser(@TempDir Path dir) {
        // 디스크가 없으면 누가 보냈든 받으면 안 된다. 그래서 디스크가 먼저다.
        // (여기서는 잴 수 있는 폴더라 통과한다 — 순서 자체는 아래 전역 상한으로 본다.)
        RunLogStore s = store(dir);

        // 이 검사만 호스트의 실제 여유 공간을 본다. 정말로 5 GB 밑이면 판정할 것이
        // 없으므로 건너뛴다 — 그때는 이 검사가 아니라 서버가 할 말이 있다.
        assumeTrue(s.hasDiskRoom(), "이 기계의 디스크 여유가 5 GB 밑이다");

        for (int i = 0; i < RunLogStore.MAX_GLOBAL_PER_HOUR; i++) {
            s.allowGlobal(1);
        }
        // 전역이 찬 상태에서는 **사용자별 상한이 남아 있어도** 전역 사유로 거절한다.
        assertThat(s.admit(1L, 1))
                .as("전역이 찼는데 사용자별 사유로 답한다 — 고칠 곳을 잘못 짚게 된다")
                .isEqualTo(RunLogStore.Admission.GLOBAL_RATE);
    }
}
