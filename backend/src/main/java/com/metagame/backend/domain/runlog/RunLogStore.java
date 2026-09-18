package com.metagame.backend.domain.runlog;

import jakarta.annotation.PostConstruct;
import java.io.IOException;
import java.nio.file.FileAlreadyExistsException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.StandardCopyOption;
import java.time.Duration;
import java.time.Instant;
import java.time.LocalDateTime;
import java.time.format.DateTimeFormatter;
import java.util.Comparator;
import java.util.Locale;
import java.util.Map;
import java.util.UUID;
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

    /** 이름 경합이 났을 때 다시 고를 횟수. 이 이상이면 사람이 볼 만한 일이 벌어진 것이다. */
    static final int MOVE_TRIES = 3;

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
     * 받으면 안 된다. 그다음 <b>사용자별</b>, 마지막이 전역이다.</p>
     *
     * <p><b>전역을 먼저 보면 안 된다</b> (`!378` 리뷰). {@link #allowGlobal} 은 세면서
     * 소비한다. 전역을 먼저 보면 <b>사용자별 상한에 걸려 어차피 거절될 요청이</b>
     * 시간당 200개 예산을 먼저 태운다 — 한 사람이 계속 보내는 것만으로 자기는 429 를
     * 받으면서 <b>다른 사람 전부를 막을 수 있다.</b> 그러면 전역 상한이 막으려던 것과
     * 정반대가 된다.</p>
     */
    public Admission admit(long userId, int bytes) {
        if (!hasDiskRoom()) return Admission.DISK_FULL;
        if (!allow(userId)) return Admission.USER_RATE;
        if (!allowGlobal(bytes)) return Admission.GLOBAL_RATE;
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
                now.format(STAMP), userId, env, stem(clientName));
        // **무엇이 담긴 파일인지 이름으로 말한다** (S15P21B202-623).
        String ext = extOf(clientName);

        // **덮어쓰지 않는다** (S15P21B202-467). 이름이 **초 단위**라, 같은 사람이 같은
        // 초에 두 건을 올리고 클라이언트 이름이 비면(`stem()=="none"`) 이름이 겹친다.
        // 예전에는 `REPLACE_EXISTING` 이라 **먼저 받은 로그가 조용히 사라졌다.**
        String name = freeName(dayDir, base, ext);
        Path target = dayDir.resolve(name);   // 아래 경합 재시도에서 다시 고를 수 있다

        // **임시 파일 이름은 요청마다 다르다** (`!378` 2차 리뷰). 예전에는 `name + ".part"`
        // 였는데, 같은 초·같은 userId·빈 clientName 인 두 요청은 **아직 정식 파일이 없으므로
        // 둘 다 같은 `base.log` 를 고르고**, 그러면 같은 `.part` 에 동시에 APPEND 한다 —
        // 로그가 뒤섞이고, 한쪽이 옮긴 뒤 다른 쪽은 `NoSuchFile` 로 터져 500 이 된다.
        // 덮어쓰기를 막으려던 수정이 문제를 임시 파일로 옮겼던 셈이다.
        Path tmp = tempFor(dayDir, base);

        // 머리에 무엇으로 만든 빌드인지 적는다. 파일 하나만 열어도 알 수 있어야 한다.
        StringBuilder head = new StringBuilder();
        head.append("# user=").append(userId).append('\n');
        head.append("# backend=").append(env).append('\n');
        head.append("# build=").append(token(build)).append('\n');
        head.append("# client-file=").append(token(clientName)).append('\n');
        head.append("# received=").append(now).append('\n');
        head.append("# ---\n");

        // **끝까지 못 가면 임시 파일을 치운다** (`!378` 3차 리뷰). 이름이 요청마다 다르니
        // 덮어쓰이지도, 다음 요청에 쓰이지도 않는다 — 그래서 실패하면 **그냥 남는다.**
        // 회수는 날짜 폴더 단위(14일)라 그때까지 쌓인 채로 있고, 목록에 섞여 사람이
        // "받다 만 로그" 를 열어 보게 된다.
        boolean placed = false;
        try {
            Files.write(tmp, head.toString().getBytes(java.nio.charset.StandardCharsets.UTF_8));
            Files.write(tmp, body, java.nio.file.StandardOpenOption.APPEND);
            // 원자적으로 놓는다 — 반쯤 쓰인 파일을 사람이 열어 보면 안 된다.
            // `REPLACE_EXISTING` 을 주지 않는다 — 그것이 **먼저 받은 로그를 지우던** 원인이다.
            //
            // 대신 **이름을 다시 골라 다시 시도한다** (`!378` 리뷰). `freeName` 과 이 줄
            // 사이에 다른 요청이 그 이름을 채울 수 있는데, 그때 예외를 그냥 올리면
            // 클라이언트가 500 을 받는다 — 덮어쓰기도 500 도 답이 아니다.
            for (int attempt = 1; ; attempt++) {
                try {
                    Files.move(tmp, target, StandardCopyOption.ATOMIC_MOVE);
                    break;
                } catch (FileAlreadyExistsException e) {
                    if (attempt >= MOVE_TRIES) throw e;
                    name = freeName(dayDir, base, ext);
                    target = dayDir.resolve(name);
                }
            }
            placed = true;
        } finally {
            if (!placed) {
                // 치우다 또 터지면 **원래 실패를 덮지 않는다.** 사람이 봐야 할 것은 그쪽이다.
                try {
                    Files.deleteIfExists(tmp);
                } catch (IOException e) {
                    log.warn("[runlog] 임시 파일을 치우지 못했다 {} : {}", tmp, e.toString());
                }
            }
        }
        return name;
    }

    /**
     * 반쯤 쓰인 파일이 놓일 자리. <b>요청마다 다르다.</b>
     *
     * <p>이름을 {@code base + ".part"} 로 하면, 같은 초·같은 userId·빈 clientName 인 두
     * 요청이 <b>같은 파일에 동시에 APPEND</b> 한다 — 아직 정식 파일이 없어 둘 다 같은
     * {@code base.log} 를 고르기 때문이다. 로그가 뒤섞이고 한쪽은 500 으로 끝난다.</p>
     */
    static Path tempFor(Path dayDir, String base) {
        return dayDir.resolve(base + "." + UUID.randomUUID() + ".part");
    }

    /** 판마다 남는 실행 로그. */
    static final String LOG_EXT = ".log";

    /** 판 안에서 일어난 일을 한 줄에 하나씩 적은 것 (S15P21B202-498). */
    static final String EVENT_EXT = ".ndjson";

    /**
     * 저장할 이름의 확장자. <b>우리가 정한 둘 중 하나다.</b>
     *
     * <p><b>왜 클라이언트 이름에서 그대로 떼어 오지 않는가.</b> 그러면 밖에서 온 값이
     * 우리 폴더에 놓이는 파일의 확장자가 된다 — {@code .html}·{@code .jsp} 로 끝나는
     * 이름을 만들 수 있고, 그 폴더가 언젠가 무엇에든 노출되면 확장자가 곧 실행 경로다.
     * {@code token()} 이 글자를 거르는 것과 같은 이유로 여기서는 <b>고르기만</b> 한다.</p>
     *
     * <p>이름이 {@code .ndjson} 인지만 본다. 그 밖에는 전부 실행 로그로 본다 —
     * 옛 빌드가 보내는 것도, 우리가 모르는 이름도 그쪽이 맞다.</p>
     */
    static String extOf(String clientName) {
        if (clientName == null) return LOG_EXT;
        return clientName.trim().toLowerCase(Locale.ROOT).endsWith(EVENT_EXT) ? EVENT_EXT : LOG_EXT;
    }

    /**
     * 이름의 몸통 — 클라이언트가 준 확장자를 뺀 것 (S15P21B202-623).
     *
     * <p>빼지 않으면 {@code ….ndjson.log} 처럼 확장자가 두 번 붙고, 그러면
     * <b>{@code *.log} 로 훑는 도구가 이벤트 파일을 실행 로그로 읽는다.</b>
     * 무엇이 담겼는지는 파일 목록만으로 갈라져야 한다.</p>
     */
    static String stem(String clientName) {
        String t = token(clientName);
        String lower = t.toLowerCase(Locale.ROOT);
        for (String ext : new String[] {EVENT_EXT, LOG_EXT}) {
            if (lower.endsWith(ext)) {
                String cut = t.substring(0, t.length() - ext.length());
                // 이름이 확장자뿐이면(`.ndjson`) 몸통이 빈다 — 빈 이름은 두지 않는다.
                return cut.isEmpty() ? "none" : cut;
            }
        }
        return t;
    }

    /**
     * 비어 있는 이름을 고른다 — {@code base.log}, 있으면 {@code base-2.log}, …
     *
     * <p>50까지만 본다. 같은 초에 같은 사람이 50개를 올리는 것은 빈도 상한이 이미
     * 막았어야 하는 상황이라, 여기서 더 세는 것보다 {@code Files.move} 가
     * 실패해서 눈에 띄는 편이 낫다.</p>
     *
     * <p><b>겹침을 피하는 {@code -2} 는 확장자 앞에 온다</b> — 뒤에 붙이면
     * {@code ….ndjson-2} 가 되어 확장자로 가르는 것이 다시 깨진다.</p>
     */
    static String freeName(Path dayDir, String base, String ext) {
        String name = base + ext;
        for (int i = 2; Files.exists(dayDir.resolve(name)) && i <= 50; i++) {
            name = base + "-" + i + ext;
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
