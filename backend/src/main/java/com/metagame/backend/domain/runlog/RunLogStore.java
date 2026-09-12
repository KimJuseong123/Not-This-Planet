package com.metagame.backend.domain.runlog;

import jakarta.annotation.PostConstruct;
import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.StandardCopyOption;
import java.time.Duration;
import java.time.Instant;
import java.time.LocalDateTime;
import java.time.format.DateTimeFormatter;
import java.util.Comparator;
import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;
import java.util.stream.Stream;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.scheduling.annotation.Scheduled;
import org.springframework.stereotype.Component;

/**
 * 받은 로그를 디스크에 두고, 오래된 것을 회수한다 (S15P21B202-443).
 *
 * <p><b>왜 DB 가 아닌가.</b> 로그는 한 판에 수 MB 이고 우리가 검색하지 않는다 —
 * 문제가 난 판을 열어서 읽을 뿐이다. MySQL 에 넣으면 백업과 덤프가 무거워지기만 한다.
 * 디스크에는 여유가 251 GB 있다.</p>
 *
 * <p><b>보관 기간을 두는 이유.</b> 상한 없는 업로드 자리는 디스크를 채우는 길이다.
 * 알파 테스트 기간을 생각하면 <b>14일</b>이면 충분하다 — 그보다 오래된 로그를 다시
 * 꺼내 본 적이 없다.</p>
 */
@Component
public class RunLogStore {

    private static final Logger log = LoggerFactory.getLogger(RunLogStore.class);

    /** 한 판의 상한. 클라이언트(`RunLog.MaxBytes`)와 같은 값이다. */
    public static final int MAX_BYTES = 8 * 1024 * 1024;

    /** 한 사람이 이 시간 안에 올릴 수 있는 개수. 시작할 때 최대 3개를 보내므로 여유를 둔다. */
    public static final int MAX_PER_WINDOW = 6;
    public static final Duration WINDOW = Duration.ofMinutes(10);

    /**
     * <b>전역 상한</b> (S15P21B202-467).
     *
     * <p><b>사용자별 상한만으로는 아무것도 막지 못한다.</b> {@code /auth/guest} 가
     * {@code permitAll} 이라 <b>토큰을 새로 받을 때마다 새 userId</b> 가 되고, 그러면
     * 10분에 6개라는 선을 원하는 만큼 넘을 수 있다. 공개 서버에서 이것은
     * <b>디스크를 채우는 길</b>이다 — `-443` 이 그 말을 적어 놓고 정작 이 구멍을
     * 남겼다(리뷰가 잡았다).</p>
     *
     * <p>값은 정상 사용의 몇 배로 잡았다. 팀원 6명이 하루에 여러 번 켠다고 해도
     * 시간당 수십 개다. 200개·300 MB 에 닿는다면 그것은 사람이 봐야 할 상황이다.</p>
     */
    public static final int MAX_GLOBAL_PER_HOUR = 200;
    public static final long MAX_GLOBAL_BYTES_PER_HOUR = 300L * 1024 * 1024;
    public static final Duration GLOBAL_WINDOW = Duration.ofHours(1);

    /**
     * 이 아래로 내려가면 <b>받지 않는다</b>. 로그를 못 받는 것이 서버가 멈추는 것보다
     * 낫다 — 디스크가 차면 백엔드·MySQL·CI 가 한꺼번에 죽는다.
     */
    public static final long MIN_FREE_BYTES = 5L * 1024 * 1024 * 1024;

    /** 왜 받지 않았는지. 컨트롤러가 이것으로 상태 코드를 고른다. */
    public enum Admission { OK, USER_RATE, GLOBAL_RATE, DISK_FULL }

    private static final DateTimeFormatter DAY = DateTimeFormatter.ofPattern("yyyyMMdd");
    private static final DateTimeFormatter STAMP = DateTimeFormatter.ofPattern("yyyyMMdd-HHmmss");

    private final Path root;
    private final int keepDays;

    /** userId → (창 시작, 그 창에서 받은 개수). 메모리에만 둔다 — 재시작하면 풀린다. */
    private final Map<Long, long[]> recent = new ConcurrentHashMap<>();

    /** 전역 창. 같은 이유로 메모리에만 둔다. */
    private long globalWindowStart;
    private long globalCount;
    private long globalBytes;

    public RunLogStore(
            @Value("${metagame.runlog.dir:/var/log/metagame-runlogs}") String dir,
            @Value("${metagame.runlog.keep-days:14}") int keepDays) {
        this.root = Path.of(dir);
        this.keepDays = keepDays;
    }

    @PostConstruct
    void ensureDir() {
        try {
            Files.createDirectories(root);
        } catch (IOException e) {
            // 여기서 죽이지 않는다. 로그를 못 받는 것이 게임 서버를 못 뜨게 할 이유는
            // 아니다 — 대신 업로드마다 실패가 보인다.
            log.warn("[runlog] 저장 폴더를 만들지 못했다 {} : {}", root, e.toString());
        }
    }

    /**
     * 빈도 상한. 넘으면 {@code false}.
     *
     * <p><b>판정을 {@code compute} 안에서 끝낸다.</b> 밖에서 세어 둔 배열을 다시 읽으면
     * 그 사이 <b>다른 요청이 올린 값</b>을 보고 판정한다 — 같은 사람이 연달아 올릴 때
     * 엉뚱하게 막히거나 통과한다. {@code compute} 는 키 단위로 직렬화되므로 그 안에서
     * 낸 답이 곧 그 요청의 답이다.</p>
     */
    /**
     * 받을지 말지. {@link Admission#OK} 가 아니면 그 사유가 이유다.
     *
     * <p><b>순서가 뜻을 가진다.</b> 디스크를 먼저 본다 — 여유가 없으면 누가 보냈든
     * 받으면 안 된다. 그다음 전역, 마지막이 사용자별이다.</p>
     */
    public Admission admit(long userId, int bytes) {
        if (!hasDiskRoom()) return Admission.DISK_FULL;
        if (!allowGlobal(bytes)) return Admission.GLOBAL_RATE;
        if (!allow(userId)) return Admission.USER_RATE;
        return Admission.OK;
    }

    /** 디스크 여유가 {@link #MIN_FREE_BYTES} 위인가. 못 재면 <b>받는 쪽</b>으로 둔다. */
    boolean hasDiskRoom() {
        try {
            return Files.getFileStore(root).getUsableSpace() >= MIN_FREE_BYTES;
        } catch (IOException e) {
            // 못 재는 것이 로그를 버릴 이유는 아니다. 다만 우리가 알아야 한다.
            log.warn("[runlog] 디스크 여유를 재지 못했다 : {}", e.toString());
            return true;
        }
    }

    /**
     * 전역 창 안의 개수·바이트 상한. <b>누가 보냈는지 보지 않는다</b> — 그것이
     * 게스트 토큰으로 우회되기 때문이다.
     */
    synchronized boolean allowGlobal(int bytes) {
        long now = Instant.now().toEpochMilli();
        if (now - globalWindowStart > GLOBAL_WINDOW.toMillis()) {
            globalWindowStart = now;
            globalCount = 0;
            globalBytes = 0;
        }
        if (globalCount + 1 > MAX_GLOBAL_PER_HOUR
                || globalBytes + bytes > MAX_GLOBAL_BYTES_PER_HOUR) {
            return false;
        }
        globalCount++;
        globalBytes += bytes;
        return true;
    }

    public boolean allow(long userId) {
        long now = Instant.now().toEpochMilli();
        long windowMs = WINDOW.toMillis();

        boolean[] ok = new boolean[1];
        recent.compute(userId, (k, v) -> {
            long[] slot = (v == null || now - v[0] > windowMs) ? new long[] {now, 0} : v;
            slot[1]++;
            ok[0] = slot[1] <= MAX_PER_WINDOW;
            return slot;
        });
        return ok[0];
    }

    /**
     * 지난 창의 빈도 기록을 버린다.
     *
     * <p>버리지 않으면 <b>접속한 사람 수만큼 계속 쌓인다.</b> 한 항목이 작아 당장
     * 문제는 아니지만, 오래 뜬 서버에서 줄어들 길이 없는 자료 구조는 두지 않는다.</p>
     */
    private void forgetOldRates() {
        long stale = Instant.now().toEpochMilli() - WINDOW.toMillis();
        recent.entrySet().removeIf(e -> e.getValue()[0] < stale);
    }

    /**
     * 저장한다. 돌려주는 것은 저장한 이름이다.
     *
     * <p><b>파일 이름은 우리가 만든다.</b> 클라이언트가 준 이름을 그대로 쓰면
     * {@code ../} 하나로 폴더 밖에 쓸 수 있다 — 그 이름은 <b>참고용으로만</b> 쓰고
     * 안전한 글자만 남긴다.</p>
     */
    public String save(long userId, String clientName, String build, String backend, byte[] body)
            throws IOException {
        LocalDateTime now = LocalDateTime.now();
        Path dayDir = root.resolve(now.format(DAY));
        Files.createDirectories(dayDir);

        String env = "dev".equalsIgnoreCase(backend) ? "dev" : "prod";
        String base = String.format("%s_u%d_%s_%s",
                now.format(STAMP), userId, env, token(clientName));

        // **덮어쓰지 않는다** (S15P21B202-467). 이름이 **초 단위**라, 같은 사람이 같은
        // 초에 두 건을 올리고 클라이언트 이름이 비면(`token()=="none"`) 이름이 겹친다.
        // 예전에는 `REPLACE_EXISTING` 이라 **먼저 받은 로그가 조용히 사라졌다.**
        String name = freeName(dayDir, base);
        Path target = dayDir.resolve(name);
        Path tmp = dayDir.resolve(name + ".part");

        // 머리에 무엇으로 만든 빌드인지 적는다. 파일 하나만 열어도 알 수 있어야 한다.
        StringBuilder head = new StringBuilder();
        head.append("# user=").append(userId).append('\n');
        head.append("# backend=").append(env).append('\n');
        head.append("# build=").append(token(build)).append('\n');
        head.append("# client-file=").append(token(clientName)).append('\n');
        head.append("# received=").append(now).append('\n');
        head.append("# ---\n");

        Files.write(tmp, head.toString().getBytes(java.nio.charset.StandardCharsets.UTF_8));
        Files.write(tmp, body, java.nio.file.StandardOpenOption.APPEND);
        // 원자적으로 놓는다 — 반쯤 쓰인 파일을 사람이 열어 보면 안 된다.
        // `REPLACE_EXISTING` 을 주지 않는다 — 위에서 빈 이름을 골랐고, 그 사이에 누가
        // 채웠다면 덮어쓰는 것보다 실패하는 편이 낫다(클라이언트가 다음 실행에 다시 보낸다).
        Files.move(tmp, target, StandardCopyOption.ATOMIC_MOVE);
        return name;
    }

    /**
     * 비어 있는 이름을 고른다 — {@code base.log}, 있으면 {@code base-2.log}, …
     *
     * <p>50까지만 본다. 같은 초에 같은 사람이 50개를 올리는 것은 빈도 상한이 이미
     * 막았어야 하는 상황이라, 여기서 더 세는 것보다 {@code Files.move} 가
     * 실패해서 눈에 띄는 편이 낫다.</p>
     */
    static String freeName(Path dayDir, String base) {
        String name = base + ".log";
        for (int i = 2; Files.exists(dayDir.resolve(name)) && i <= 50; i++) {
            name = base + "-" + i + ".log";
        }
        return name;
    }

    /** 파일 이름과 헤더에 넣어도 되는 글자만 남긴다. */
    static String token(String s) {
        if (s == null || s.isBlank()) return "none";
        StringBuilder sb = new StringBuilder();
        for (char c : s.toCharArray()) {
            if (Character.isLetterOrDigit(c) || c == '-' || c == '.' || c == '_') sb.append(c);
            else if (sb.length() > 0 && sb.charAt(sb.length() - 1) != '-') sb.append('-');
            if (sb.length() >= 48) break;
        }
        String t = sb.toString().replaceAll("^-+|-+$", "");
        return t.isEmpty() ? "none" : t;
    }

    /**
     * 보관 기간이 지난 날짜 폴더를 지운다. 매일 새벽에 돈다.
     *
     * <p>날짜 폴더 단위로 지우므로 파일을 하나씩 보지 않아도 된다.</p>
     */
    @Scheduled(cron = "0 30 4 * * *")
    public void sweep() {
        forgetOldRates();

        if (!Files.isDirectory(root)) return;
        String cutoff = LocalDateTime.now().minusDays(keepDays).format(DAY);
        try (Stream<Path> dirs = Files.list(root)) {
            dirs.filter(Files::isDirectory)
                .filter(p -> p.getFileName().toString().compareTo(cutoff) < 0)
                .forEach(this::deleteTree);
        } catch (IOException e) {
            log.warn("[runlog] 회수 실패 : {}", e.toString());
        }
    }

    private void deleteTree(Path dir) {
        try (Stream<Path> walk = Files.walk(dir)) {
            walk.sorted(Comparator.reverseOrder()).forEach(p -> {
                try {
                    Files.deleteIfExists(p);
                } catch (IOException ignored) {
                    // 다음 회수에서 다시 시도된다
                }
            });
            log.info("[runlog] 보관 기간이 지난 폴더 회수 {}", dir.getFileName());
        } catch (IOException e) {
            log.warn("[runlog] 폴더를 지우지 못했다 {} : {}", dir, e.toString());
        }
    }
}
