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
            string source = SourceLabel(Git("rev-parse --short HEAD"), worktree, staged, untracked);

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
            bool dev = (Environment.GetEnvironmentVariable("SPACE_DEV") ?? string.Empty).Trim() == "1";
            line.Append('\n').Append(BuildInfo.BackendPrefix).Append(dev ? "dev" : "prod");

            return line.ToString();
        }

        /// <summary>
        /// 첫 줄에 쓸 <b>기준</b>. 재현할 수 있으면 해시, 없으면 <b>왜 없는지</b>다.
        ///
        /// <para><b>순수 함수다</b> — git 을 부르지 않는다. 세 경우를 검사로 잠그기 위해서고,
        /// 검사가 git 인덱스를 실제로 흔들면 저장소를 더럽힌다.</para>
        ///
        /// <para><b>두 가지 더러움을 가른다</b> (S15P21B202-398). 예전에는 둘 다
        /// <c>+커밋안한변경</c> 한 문구였는데 뜻이 아주 다르다:</para>
        /// <list type="bullet">
        /// <item><b>덮어씀</b> — 작업 트리는 인덱스와 같은데 인덱스가 HEAD 와 다르다.
        /// <c>git checkout &lt;커밋&gt; -- METAGAME/</c> 를 한 모양이다. 빌드된 것은
        /// <b>HEAD 가 아닌 다른 커밋</b>이다</item>
        /// <item><b>수정</b> — 손으로 고친 것이 섞였다. 어느 커밋으로도 재현되지 않는다</item>
        /// </list>
        /// </summary>
        public static string SourceLabel(string headShort, string worktree, string staged, string untracked)
        {
            int edited = CountLines(worktree) + CountLines(untracked);
            int overwritten = CountLines(staged);

            // 손으로 고친 것이 하나라도 있으면 그것이 더 강한 이야기다 — 어느 커밋에도 없다.
            if (edited > 0) return "작업트리(재현불가·수정 " + edited + "개)";
            if (overwritten > 0) return "작업트리(재현불가·다른 커밋을 덮음 " + overwritten + "개)";

            return string.IsNullOrEmpty(headShort) ? "커밋 모름" : headShort;
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
        /// </summary>
        static string Git(string arguments)
        {
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

                    string output = git.StandardOutput.ReadToEnd();
                    // 표준오류를 비우지 않으면 출력이 많을 때 git 이 버퍼에서 멈춘다.
                    git.StandardError.ReadToEnd();

                    // 저장소가 잠겨 있거나 하면 끝나지 않을 수 있다. 빌드를 붙잡아 두지 않는다.
                    if (!git.WaitForExit(5000)) return string.Empty;

                    return git.ExitCode == 0 ? output.Trim() : string.Empty;
                }
            }
            catch (Exception)
            {
                return string.Empty;   // git 이 PATH 에 없는 환경
            }
        }
    }
}
