using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor.Build;
using UnityEngine;
using Metagame.Core;

namespace Metagame.EditorTools
{
    /// <summary>
    /// 빌드할 때 "언제·어느 커밋" 을 산출물에 박는다 (S15P21B202-269).
    /// 실행 시 그 값을 찍는 쪽은 <see cref="BuildInfo"/> 다.
    ///
    /// <b>왜 StreamingAssets 인가.</b> 값이 산출물 폴더 안에 들어가야 실행 파일과 함께 다닌다.
    /// 그렇다고 <c>Assets/</c> 아래에 파일을 만들면 빌드할 때마다 저장소가 더러워지고 팀원과
    /// 충돌한다. 이 방식은 임시 폴더에 써서 빌드에만 실어 보내므로 저장소에 흔적이 남지 않는다.
    ///
    /// <b>왜 BuildPlayerProcessor 인가.</b> CLI 빌드든 에디터의 Build 버튼이든 똑같이 지나간다.
    /// 팀원은 에디터에서 빌드하므로 한쪽에만 걸리면 의미가 없다.
    /// </summary>
    public sealed class BuildStamp : BuildPlayerProcessor
    {
        /// <summary>브랜치 이름에서 이슈키를 뽑는다. 팀 규칙상 브랜치는 키로 시작한다.</summary>
        static readonly Regex IssueKey = new Regex(@"S15P21B202-\d+", RegexOptions.Compiled);

        public override void PrepareForBuild(BuildPlayerContext context)
        {
            try
            {
                string folder = Path.Combine(Path.GetTempPath(), "metagame-build-stamp");
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, BuildInfo.FileName), Compose());
                context.AddAdditionalPathToStreamingAssets(folder, string.Empty);
            }
            catch (Exception e)
            {
                // 표시 하나 때문에 빌드가 깨지면 손해다. 못 박았다고 알리고 그대로 진행한다.
                Debug.LogWarning("[BuildStamp] 빌드 정보를 박지 못했다 · " + e.Message);
            }
        }

        /// <summary>
        /// 로그에 그대로 나갈 한 줄을 만든다.
        /// 예) <c>2026-09-02 15:13 · eab8595 · S15P21B202-269 · +수정</c>
        /// </summary>
        static string Compose()
        {
            string when = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            string branch = Git("rev-parse --abbrev-ref HEAD");

            // **빌드된 것이 무엇인지를 찍는다** (S15P21B202-398). 예전에는 `HEAD` 를 찍었다.
            // 그런데 우리는 작업 트리에 팀원의 커밋 안 된 문서 작업이 있어 `git switch` 를
            // 못 할 때 `git checkout <커밋> -- METAGAME/` 로 **게임 코드만 덮어** 빌드한다.
            // 그때 HEAD 는 움직이지 않으므로 **빌드된 코드와 아무 상관 없는 해시**가 박혔다 —
            // 2026-09-10 qa2 빌드가 그랬고, 팀이 곧바로 "옛날 걸로 빌드한 거 아니냐" 고 물었다.
            //
            // 해시는 그럴싸해서 사람이 믿는다. **재현할 수 없으면 찍지 않는 것**이 낫다.
            //
            // 범위는 `METAGAME/`(=`.`, 아래 `Git` 의 작업 폴더) 전체다. `Assets/` 만 보면
            // `ProjectSettings/`·`Packages/` 변경을 놓치는데 그것도 빌드 결과를 바꾼다.
            string worktree = Git("diff --name-only -- .");          // 작업 트리 ↔ 인덱스
            string staged = Git("diff --cached --name-only -- .");   // 인덱스 ↔ HEAD
            string untracked = Git("ls-files --others --exclude-standard -- .");
            // 손으로 고친 것이 없고 인덱스만 HEAD 와 다르면 **어느 커밋을 덮었는지 찾는다.**
            // 찾으면 그 빌드는 재현 가능하다 — `git checkout <그 커밋> -- METAGAME/` 이면 된다.
            string from = (string.IsNullOrEmpty(worktree) && string.IsNullOrEmpty(untracked)
                           && !string.IsNullOrEmpty(staged))
                          ? OverwrittenFrom()
                          : string.Empty;
            string source = SourceLabel(Git("rev-parse --short HEAD"), worktree, staged, untracked, from);

            var line = new System.Text.StringBuilder(when);
            line.Append(" · ").Append(source);

            var key = IssueKey.Match(branch ?? string.Empty);
            if (key.Success) line.Append(" · ").Append(key.Value);
            else if (!string.IsNullOrEmpty(branch)) line.Append(" · ").Append(branch);

            // **프로젝트 밖 변경은 빌드를 바꾸지 않는다.** 예전에는 저장소 전체를 보고
            // `+커밋안한변경` 을 붙였는데, 커밋 안 된 `docs/` 96개 때문에 **재현 가능한
            // 빌드에도 매번** 그 표시가 붙었다. 늘 붙는 표시는 아무도 안 읽는다.
            // 그래도 저장소가 깨끗하지 않다는 것은 알려 준다 — 뜻이 다른 표시로.
            bool projectClean = string.IsNullOrEmpty(worktree)
                                && string.IsNullOrEmpty(staged)
                                && string.IsNullOrEmpty(untracked);
            if (projectClean && !string.IsNullOrEmpty(Git("status --porcelain")))
                line.Append(" · +프로젝트밖수정");

            // 실험 라벨은 **둘째 줄**에 기계가 읽는 형식으로 박는다 (S15P21B202-376).
            // 첫 줄에 섞으면 사람이 읽는 표시를 파싱해야 하고, 그 형식이 바뀔 때마다
            // 런타임이 조용히 라벨을 놓친다.
            string experiment = (Environment.GetEnvironmentVariable("SPACE_EXPERIMENT") ?? string.Empty).Trim();
            if (!string.IsNullOrEmpty(experiment))
            {
                // 줄바꿈이 섞이면 둘째 줄 규칙이 깨진다.
                experiment = experiment.Replace("\r", " ").Replace("\n", " ");
                line.Append('\n').Append(BuildInfo.ExperimentPrefix).Append(experiment);
            }

            // **어느 백엔드를 가리키는지 박는다** (S15P21B202-284). 이 줄이 없으면
            // `BackendEndpoints` 가 "옛 빌드" 로 보고 배포 서버를 가리킨다 — 개발 빌드가
            // 실유저 서버에 붙는 것이 조용히 일어나는 길이다.
            //
            // 값은 `SpaceBuild` 가 확정해 둔 `SPACE_DEV` 를 본다(그쪽이 인자로 켠 dev 를
            // 환경변수로 되돌려 박는다). 여기서 다시 판단하지 않는다 — 두 곳이 각각
            // 판단하면 어긋난다.
            //
            // **다만 값이 없을 때의 기본을 바꾼다** (S15P21B202-652) — 아래 참고.
            string version = (Environment.GetEnvironmentVariable("SPACE_RELEASE_VERSION") ?? string.Empty).Trim();
            bool dev = BackendIsDev(Environment.GetEnvironmentVariable("SPACE_DEV"), version,
                                    Environment.GetEnvironmentVariable("CI_PIPELINE_ID"));
            line.Append('\n').Append(BuildInfo.BackendPrefix).Append(dev ? "dev" : "prod");

            // **릴리스 버전을 박는다** (S15P21B202-404). 서버가 이 값으로 구버전을 막는다.
            //
            // 값은 `release-windows` 가 빌드 **전에** `publish_release.py version` 으로
            // 정해 둔 것을 환경변수로 받는다. 여기서 git 태그를 다시 읽지 않는다 —
            // 두 곳이 각각 판단하면 어긋나고, 어긋난 버전은 **게이트를 조용히 무력화**한다.
            //
            // 릴리스가 아닌 빌드(로컬·개발·에디터)에는 이 줄이 없다. 그것을 어떻게 볼지는
            // **서버가** 정한다 — 클라이언트가 스스로 판단하면 그 판단째로 우회된다.
            if (!string.IsNullOrEmpty(version))
            {
                version = version.Replace("\r", " ").Replace("\n", " ");
                line.Append('\n').Append(BuildInfo.VersionPrefix).Append(version);
            }

            // **신원은 따로 적는다** (S15P21B202-597). 위 `버전=` 은 서버 게이트가 읽는
            // 값이라 `vX.Y.Z` 여야 하고, 그래서 개발·QA 빌드에는 **일부러 없다**(-404).
            // 그런데 "이 판이 어느 빌드였나" 는 모든 빌드가 말할 수 있어야 한다 —
            // 한 줄이 두 일을 겸하려 해서 개발 빌드 이벤트의 `build` 가 내내 빈칸이었다.
            //
            // **이 append 는 위 `if` 밖에 있어야 한다.** 안으로 들어가면 릴리스 빌드에만
            // 붙어 고치려던 상태로 그대로 돌아간다.
            string buildId = IdentityOf(Environment.GetEnvironmentVariable("SPACE_BUILD_ID"), version);
            if (!string.IsNullOrEmpty(buildId))
                line.Append('\n').Append(BuildInfo.BuildIdPrefix).Append(buildId);

            return line.ToString();
        }

        /// <summary>
        /// 스탬프에 적을 <b>신원</b> — <c>빌드=</c> 줄의 값 (S15P21B202-597).
        ///
        /// <para><b>릴리스 버전을 폴백으로 쓴다.</b> 릴리스 빌드에서는 파이프라인이
        /// <c>SPACE_BUILD_ID</c> 를 따로 주지 않아도 두 줄이 같은 값을 가리켜야 한다 —
        /// 같은 빌드를 두 이름으로 부르는 것이 가장 헷갈리는 결과다.</para>
        ///
        /// <para><b>순수 함수로 갈라 둔다</b>(<c>-501</c> 의 <c>BuildTag</c> 와 같은 이유).
        /// 에디터에서 도는 검사는 환경변수를 흔들 수 없다 — 흔들면 같은 프로세스의 다른
        /// 검사로 새어 나간다. 그래서 <b>무엇이 들어오면 무엇을 적는지</b>를 여기서 잠근다.</para>
        ///
        /// <para>둘 다 비면 빈 문자열이고, 그러면 <c>빌드=</c> 줄이 아예 없다 — 손으로 만든
        /// 로컬 빌드가 그렇다. <b>빈 값을 그럴듯한 것으로 메우지 않는다</b>: 읽는 쪽이
        /// "모른다" 를 알아야 한다.</para>
        /// </summary>
        public static string IdentityOf(string buildId, string version)
        {
            string id = (buildId ?? string.Empty).Trim();
            if (id.Length == 0) id = (version ?? string.Empty).Trim();

            // 줄바꿈이 섞이면 둘째 줄부터의 `머리표=값` 규칙이 깨진다 — 뒤쪽 줄이
            // 통째로 다른 값으로 읽힌다.
            //
            // **바꾼 뒤에 다시 다듬는다** (`!505` AI 리뷰). 앞의 `Trim()` 은 치환
            // 전이라 `"qa-1\n"` 이 `"qa-1 "` 로 남았다 — 읽는 쪽이 또 `Trim()` 해서
            // 살아나긴 하지만, 스탬프 파일에 꼬리 공백을 쓸 이유는 없다.
            //
            // 값이 이상하다고 **버리지는 않는다.** 중간에 공백이 섞인 이름은 이상한
            // 이름이지 없는 이름이 아니다 — 빈 값으로 떨어뜨리면 그 판이 어느 빌드였는지
            // 아예 사라진다. 이상한 값은 눈에 보이고, 없는 값은 안 보인다.
            return id.Replace("\r", " ").Replace("\n", " ").Trim();
        }

        /// <summary>
        /// 첫 줄에 쓸 <b>기준</b>. 재현할 수 있으면 해시, 없으면 <b>왜 없는지</b>다.
        ///
        /// <para><b>순수 함수다</b> — git 을 부르지 않는다. 세 경우를 검사로 잠그기 위해서고,
        /// 검사가 git 인덱스를 실제로 흔들면 저장소를 더럽힌다.</para>
        ///
        /// <para><b>세 가지를 가른다</b> (S15P21B202-398). 예전에는 전부
        /// <c>+커밋안한변경</c> 한 문구였는데 뜻이 아주 다르다:</para>
        /// <list type="bullet">
        /// <item><b>수정</b> — 손으로 고친 것이 섞였다. <b>어느 커밋으로도 재현되지 않는다.</b>
        /// 가장 강한 이야기이므로 겹치면 이쪽을 말한다</item>
        /// <item><b>덮어씀(커밋을 찾음)</b> — 인덱스가 어느 커밋의 <c>METAGAME/</c> 과 똑같다.
        /// <c>git checkout &lt;그 커밋&gt; -- METAGAME/</c> 로 <b>재현할 수 있다</b> —
        /// 그래서 그 해시를 찍는다. 라벨(<c>SPACE_EXPERIMENT</c>) 없이도 첫 줄만으로
        /// 기준을 알 수 있어야 한다는 것이 이 이슈의 완료 조건이다</item>
        /// <item><b>인덱스가 HEAD 와 다름(못 찾음)</b> — <c>git add</c> 만 해 둔 상태도
        /// 여기 들어온다. <b>단정하지 않고 관찰한 사실만</b> 적는다(<c>!384</c> 리뷰가
        /// 짚었다 — "덮어씀" 으로 단정하면 이 표시가 또 부정확해진다)</item>
        /// </list>
        /// </summary>
        /// <summary>
        /// 이 빌드가 <b>개발 백엔드</b>를 가리켜야 하는가 (S15P21B202-652).
        ///
        /// <para><b>순수 함수다</b> — 환경변수를 여기서 읽지 않는다. 세 경우를 검사로
        /// 잠그기 위해서고, 이 판단이 틀리면 **나간 게임이 아무도 못 노는** 쪽으로도
        /// 틀릴 수 있어서다.</para>
        ///
        /// <para><b>무엇이 문제였나.</b> 예전에는 <c>SPACE_DEV</c> 가 없으면 무조건
        /// <c>prod</c> 였다. 그래서 사람이 자기 PC 에서 <see cref="SpaceBuild.BuildWindows"/>
        /// 를 돌리면 <b>실유저 서버를 가리키는 바이너리</b>가 나왔다 — 2026-09-21 실측에서
        /// 배포 서버에 쌓인 판 6건 중 5건이 그런 빌드였다. 데이터가 섞이는 것으로 끝나지
        /// 않는다: 실유저 백엔드에 로그인하고, 실유저 방에 끼어들고,
        /// <c>MatchAuthenticator</c> 가 참가자 토큰을 prod 에 되묻는다 — <c>-284</c> 가
        /// 경고한 자리가 그것이다.</para>
        ///
        /// <para><c>BackendEndpoints</c> 의 방어 넷 중 ②(에디터는 개발)와 ④(스탬프 없는
        /// 개발 빌드는 개발)는 이 경우를 막는다. 그런데 <b>스탬프가 있고 그것이
        /// <c>prod</c> 라고 말하면</b>(③) 그대로 믿는다. 그 한 칸이 비어 있었다.</para>
        ///
        /// <para><b>양쪽으로 안전해야 한다.</b> 기본을 무조건 <c>dev</c> 로 뒤집으면
        /// 반대 사고가 생긴다 — CI 를 못 알아본 릴리스 빌드가 개발 서버를 가리키면
        /// <b>나간 게임을 아무도 못 논다.</b> 그래서 <c>prod</c> 신호를 <b>둘</b> 둔다:
        /// 명시적인 <c>SPACE_DEV=0</c> 과 <b>릴리스 버전의 존재</b>. 둘 중 하나라도
        /// 있으면 <c>prod</c> 이고, 거기에 CI 표시가 더해진다.</para>
        /// </summary>
        /// <param name="devFlag"><c>SPACE_DEV</c>. <c>"1"</c> 켬 · <c>"0"</c> 끔 · 그 밖은 "안 정함".</param>
        /// <param name="releaseVersion"><c>SPACE_RELEASE_VERSION</c>. 있으면 릴리스 빌드다.</param>
        /// <param name="ciPipelineId"><c>CI_PIPELINE_ID</c>. 있으면 CI 가 만든 빌드다.</param>
        public static bool BackendIsDev(string devFlag, string releaseVersion, string ciPipelineId)
        {
            string flag = (devFlag ?? string.Empty).Trim();
            if (flag == "1") return true;       // 일부러 켰다
            if (flag == "0") return false;      // 일부러 껐다 — 여기서 더 묻지 않는다

            // 안 정했다. **누가 만들었나**로 가른다. 릴리스 버전이 박히는 빌드는
            // `release-windows` 뿐이고, `CI_PIPELINE_ID` 는 모든 CI 잡에 있다.
            bool fromCI = !string.IsNullOrEmpty((releaseVersion ?? string.Empty).Trim())
                          || !string.IsNullOrEmpty((ciPipelineId ?? string.Empty).Trim());
            return !fromCI;
        }

        public static string SourceLabel(string headShort, string worktree, string staged,
                                         string untracked, string overwrittenFrom)
        {
            int edited = CountLines(worktree) + CountLines(untracked);
            int staged_ = CountLines(staged);

            // 손으로 고친 것이 하나라도 있으면 그것이 더 강한 이야기다 — 어느 커밋에도 없다.
            if (edited > 0) return "작업트리(재현불가·수정 " + edited + "개)";

            if (staged_ > 0)
            {
                // 어느 커밋을 덮었는지 찾았다 = **재현할 수 있다.** 해시를 찍는 것이 맞다.
                if (!string.IsNullOrEmpty(overwrittenFrom))
                    return overwrittenFrom + "(덮어씀)";

                return "작업트리(재현불가·인덱스가 HEAD 와 다름 " + staged_ + "개)";
            }

            return string.IsNullOrEmpty(headShort) ? "커밋 모름" : headShort;
        }

        /// <summary>몇 개의 커밋까지 거슬러 보며 찾을지. 2026-09-10 사례는 61커밋 뒤였다.</summary>
        const int SearchDepth = 120;

        /// <summary>
        /// 인덱스의 <c>METAGAME/</c> 이 <b>어느 커밋</b>의 것과 같은가. 못 찾으면 빈 문자열.
        ///
        /// <para><c>git checkout &lt;커밋&gt; -- METAGAME/</c> 는 <b>인덱스까지</b> 그 커밋으로
        /// 바꾼다. 그래서 최근 커밋을 훑으며 <c>git diff --cached &lt;커밋&gt; -- .</c> 가 비는
        /// 곳을 찾으면 그것이 덮어쓴 커밋이다.</para>
        ///
        /// <para><b>비용은 커밋 하나에 git 한 번</b>이다. 빌드는 몇 분짜리이고 이것은 한 번만
        /// 도므로 감당된다. 대신 <see cref="SearchDepth"/> 로 상한을 둔다 — 못 찾는 경우에
        /// 저장소 역사 전체를 훑으면 안 된다.</para>
        /// </summary>
        static string OverwrittenFrom()
        {
            // **빈 출력을 "같다" 로 읽지 않는다** (`!384` 2차 리뷰). `Git()` 은 실패해도
            // 빈 문자열을 준다 — 그래서 `--name-only` 의 빈 출력으로 판정하면, git 호출이
            // 한 번만 삐끗해도 **아무 상관 없는 커밋을 "덮어씀" 으로 찍는다.** 그것이
            // 바로 이 이슈가 고치려던 거짓 해시다. `--quiet` 의 **종료 코드**로 가른다:
            //   0 = 같다 · 1 = 다르다 · 그 밖 = 물어보지 못했다
            int rc;
            string log = Run("log --format=%h -n " + SearchDepth, out rc);
            if (rc != 0) return string.Empty;      // 역사를 못 읽었으면 찾지 않는다

            foreach (var raw in log.Split('\n'))
            {
                string sha = raw.Trim();
                if (sha.Length == 0) continue;

                Run("diff --cached --quiet " + sha + " -- .", out rc);
                if (rc == 0) return sha;           // 인덱스가 이 커밋의 METAGAME/ 과 같다
                if (rc != 1) return string.Empty;  // 0 도 1 도 아니면 **판정한 것이 아니다**
            }
            return string.Empty;
        }

        /// <summary>빈 줄을 뺀 줄 수. git 의 `--name-only` 출력은 파일 하나에 한 줄이다.</summary>
        public static int CountLines(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            int n = 0;
            foreach (var raw in text.Split('\n'))
                if (!string.IsNullOrEmpty(raw.Trim())) n++;
            return n;
        }

        /// <summary>
        /// git 한 번 돌리고 표준출력을 돌려준다. git 이 없거나 저장소가 아니면 빈 문자열이다 —
        /// 그때는 시각만으로도 빌드를 구분할 수 있으니 빌드를 막지 않는다.
        ///
        /// <para><b>실패와 "출력이 없다" 를 구별해야 하면 <see cref="Run"/> 를 쓴다.</b>
        /// 이쪽은 둘 다 빈 문자열이라, 빈 출력에 뜻을 싣는 자리에서 쓰면 거짓 판정이 된다
        /// (<c>!384</c> 2차 리뷰).</para>
        /// </summary>
        static string Git(string arguments)
        {
            int rc;
            string outText = Run(arguments, out rc);
            return rc == 0 ? outText : string.Empty;
        }

        /// <summary>
        /// git 한 번. <paramref name="exit"/> 에 종료 코드를 담는다 — 돌리지 못했으면 <c>-1</c>.
        /// </summary>
        static string Run(string arguments, out int exit)
        {
            exit = -1;
            try
            {
                var info = new System.Diagnostics.ProcessStartInfo("git", arguments)
                {
                    // Assets 의 부모(METAGAME)는 저장소 안이라 git 이 루트를 알아서 찾는다.
                    WorkingDirectory = Directory.GetParent(Application.dataPath).FullName,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                using (var git = System.Diagnostics.Process.Start(info))
                {
                    if (git == null) return string.Empty;

                    var output = git.StandardOutput.ReadToEndAsync();
                    var error = git.StandardError.ReadToEndAsync();

                    // 저장소가 잠겨 있거나 하면 끝나지 않을 수 있다. 빌드를 붙잡아 두지 않는다.
                    // **`exit` 는 -1 로 둔다** — 안 끝난 것은 판정이 아니다.
                    if (!git.WaitForExit(5000))
                    {
                        try { git.Kill(); } catch (Exception) { }
                        return string.Empty;
                    }
                    if (!System.Threading.Tasks.Task.WaitAll(new System.Threading.Tasks.Task[] { output, error }, 1000))
                        return string.Empty;

                    exit = git.ExitCode;
                    return output.Result.Trim();
                }
            }
            catch (Exception)
            {
                return string.Empty;   // git 이 PATH 에 없는 환경
            }
        }
    }
}
