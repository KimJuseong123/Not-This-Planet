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
        assertThat(RunLogStore.freeName(dir, base, ".log")).isEqualTo(base + ".log");

        Files.writeString(dir.resolve(base + ".log"), "먼저 받은 것");
        assertThat(RunLogStore.freeName(dir, base, ".log"))
                .as("이미 있는 이름을 다시 고른다 — 먼저 받은 로그를 덮어쓴다")
                .isEqualTo(base + "-2.log");

        Files.writeString(dir.resolve(base + "-2.log"), "두 번째");
        assertThat(RunLogStore.freeName(dir, base, ".log")).isEqualTo(base + "-3.log");

        // 먼저 받은 것이 그대로 있어야 한다.
        assertThat(Files.readString(dir.resolve(base + ".log"))).isEqualTo("먼저 받은 것");
    }

    @Test
    void whatIsInTheFileIsVisibleInItsName(@TempDir Path dir) throws IOException {
        // **파일 목록만으로 두 종류가 갈려야 한다** (S15P21B202-623). 예전에는 저장 이름
        // 뒤에 `.log` 를 무조건 붙여 이벤트가 `….ndjson.log` 로 남았고, 그러면
        // `*.log` 로 훑는 도구가 NDJSON 을 실행 로그로 읽는다.
        RunLogStore s = store(dir);
        byte[] body = "한 줄".getBytes(java.nio.charset.StandardCharsets.UTF_8);

        String event = s.save(7, "20260918-122304.ndjson", "qa-514-0b171dc9", "dev", body);
        assertThat(event)
                .as("이벤트 파일이 실행 로그 이름으로 저장됐다")
                .endsWith(".ndjson")
                .doesNotContain(".ndjson.log");

        String run = s.save(7, "20260918-122210.log", "qa-514-0b171dc9", "dev", body);
        assertThat(run).endsWith(".log").doesNotContain(".log.log");

        // 둘 다 실제로 놓였는지 본다 — 이름만 맞고 파일이 없으면 뜻이 없다.
        assertThat(Files.walk(dir).filter(Files::isRegularFile).map(p -> p.getFileName().toString()))
                .containsExactlyInAnyOrder(event, run);
    }

    @Test
    void theClientCannotChooseTheExtension() {
        // 확장자는 **우리가 정한 둘 중 하나**다. 클라이언트 이름에서 그대로 떼어 오면
        // 밖에서 온 값이 우리 폴더에 놓이는 파일의 확장자가 된다.
        assertThat(RunLogStore.extOf("payload.ndjson")).isEqualTo(".ndjson");
        assertThat(RunLogStore.extOf("PAYLOAD.NDJSON")).isEqualTo(".ndjson");
        assertThat(RunLogStore.extOf("run.log")).isEqualTo(".log");
        assertThat(RunLogStore.extOf("확장자없음")).isEqualTo(".log");
        assertThat(RunLogStore.extOf(null)).isEqualTo(".log");
        assertThat(RunLogStore.extOf("shell.jsp"))
                .as("모르는 확장자를 그대로 쓰면 밖에서 온 값이 파일 이름의 끝을 정한다")
                .isEqualTo(".log");

        // 몸통에서는 클라이언트가 준 확장자를 뺀다 — 그 자리에 위 값이 다시 붙는다.
        assertThat(RunLogStore.stem("20260918-122304.ndjson")).isEqualTo("20260918-122304");
        assertThat(RunLogStore.stem("20260918-122210.log")).isEqualTo("20260918-122210");
        assertThat(RunLogStore.stem("shell.jsp")).isEqualTo("shell.jsp");
        assertThat(RunLogStore.stem(".ndjson"))
                .as("이름이 확장자뿐이면 몸통이 빈다 — 빈 이름은 두지 않는다")
                .isEqualTo("none");
        assertThat(RunLogStore.stem(null)).isEqualTo("none");
    }

    @Test
    void theCollisionSuffixStaysBeforeTheExtension(@TempDir Path dir) throws IOException {
        // 뒤에 붙이면 `….ndjson-2` 가 되어 확장자로 가르는 것이 다시 깨진다.
        String base = "20260918-122718_u7_dev_20260918-122304";
        Files.writeString(dir.resolve(base + ".ndjson"), "먼저 받은 것");

        assertThat(RunLogStore.freeName(dir, base, ".ndjson")).isEqualTo(base + "-2.ndjson");
    }

    @Test
    void theOrderOfTheChecksIsDiskThenUserThenGlobal(@TempDir Path dir) {
        // 디스크가 없으면 누가 보냈든 받으면 안 된다. 그래서 디스크가 먼저다.
        // (여기서는 잴 수 있는 폴더라 통과한다 — 순서 자체는 아래 두 가지로 본다.)
        RunLogStore s = store(dir);

        // 이 검사만 호스트의 실제 여유 공간을 본다. 정말로 5 GB 밑이면 판정할 것이
        // 없으므로 건너뛴다 — 그때는 이 검사가 아니라 서버가 할 말이 있다.
        assumeTrue(s.hasDiskRoom(), "이 기계의 디스크 여유가 5 GB 밑이다");

        // 전역이 찬 상태에서는 **사용자별 상한이 남아 있어도** 전역 사유로 거절한다.
        for (int i = 0; i < RunLogStore.MAX_GLOBAL_PER_HOUR; i++) {
            s.allowGlobal(1);
        }
        assertThat(s.admit(1L, 1))
                .as("전역이 찼는데 사용자별 사유로 답한다 — 고칠 곳을 잘못 짚게 된다")
                .isEqualTo(RunLogStore.Admission.GLOBAL_RATE);
    }

    @Test
    void aUserOverTheirOwnLimitDoesNotBurnTheGlobalBudget(@TempDir Path dir) {
        // **사용자별을 전역보다 먼저 본다** (`!378` 리뷰). 전역을 먼저 보면
        // `allowGlobal` 이 세면서 소비하기 때문에, **어차피 거절될 요청이** 시간당
        // 예산을 먼저 태운다 — 한 사람이 계속 보내는 것만으로 자기는 429 를 받으면서
        // 다른 사람 전부를 막을 수 있다. 전역 상한이 막으려던 것과 정반대다.
        RunLogStore s = store(dir);
        assumeTrue(s.hasDiskRoom(), "이 기계의 디스크 여유가 5 GB 밑이다");

        for (int i = 0; i < RunLogStore.MAX_PER_WINDOW; i++) {
            assertThat(s.admit(7L, 1)).isEqualTo(RunLogStore.Admission.OK);
        }
        // 이 사람은 이제 막힌다.
        for (int i = 0; i < 50; i++) {
            assertThat(s.admit(7L, 1)).isEqualTo(RunLogStore.Admission.USER_RATE);
        }

        // **다른 사람은 그대로 받아야 한다.** 전역 예산이 위 50번에 타지 않았다면
        // 여기가 통과한다.
        for (int i = 0; i < RunLogStore.MAX_PER_WINDOW; i++) {
            assertThat(s.admit(8L, 1))
                    .as("한 사람의 거절된 요청이 전역 예산을 태웠다 (%d번째)", i)
                    .isEqualTo(RunLogStore.Admission.OK);
        }
    }

    @Test
    void twoRequestsInTheSameSecondDoNotShareATempFile(@TempDir Path dir) {
        // **덮어쓰기를 막았더니 문제가 임시 파일로 옮겨갔었다** (`!378` 2차 리뷰).
        // 같은 초·같은 userId·빈 clientName 인 두 요청은 아직 정식 파일이 없어 **둘 다**
        // 같은 이름을 고른다. 임시 파일까지 그 이름을 쓰면 같은 파일에 동시에 APPEND 해
        // 로그가 뒤섞이고, 한쪽이 옮긴 뒤 다른 쪽은 `NoSuchFile` 로 터진다.
        String base = "20260913-101500_u7_prod_none";

        assertThat(RunLogStore.tempFor(dir, base))
                .as("두 요청이 같은 임시 파일을 쓴다 — 로그가 뒤섞인다")
                .isNotEqualTo(RunLogStore.tempFor(dir, base));

        // 그래도 정식 이름과는 겹치지 않아야 한다 — `.log` 로 끝나면 정리·목록에 섞인다.
        assertThat(RunLogStore.tempFor(dir, base).getFileName().toString())
                .startsWith(base + ".")
                .endsWith(".part");
    }

    @Test
    void aNameRaceIsRetriedInsteadOfBecomingAServerError(@TempDir Path dir) {
        // `freeName` 과 `Files.move` 사이에 다른 요청이 그 이름을 채울 수 있다.
        // 덮어쓰면 먼저 받은 로그가 사라지고, 예외를 그냥 올리면 500 이 된다 —
        // 둘 다 답이 아니라 **이름을 다시 고른다**(`!378` 리뷰).
        assertThat(RunLogStore.MOVE_TRIES)
                .as("경합에서 다시 시도하지 않는다 — 이름 충돌이 그대로 500 이 된다")
                .isGreaterThan(1);
    }
}
