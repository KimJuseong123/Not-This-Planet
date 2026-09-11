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
            string commit = Git("rev-parse --short HEAD");
            string branch = Git("rev-parse --abbrev-ref HEAD");

            var line = new System.Text.StringBuilder(when);
            line.Append(" · ").Append(string.IsNullOrEmpty(commit) ? "커밋 모름" : commit);

            var key = IssueKey.Match(branch ?? string.Empty);
            if (key.Success) line.Append(" · ").Append(key.Value);
            else if (!string.IsNullOrEmpty(branch)) line.Append(" · ").Append(branch);

            // 커밋하지 않은 변경이 섞였으면 해시만으로는 이 빌드를 재현할 수 없다.
            // 그 사실을 숨기면 해시가 거짓말이 된다 — 실제로 커밋 전 코드를 빌드해 나눠 준 적이 있다.
            if (!string.IsNullOrEmpty(Git("status --porcelain"))) line.Append(" · +커밋안한변경");

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

            return line.ToString();
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
