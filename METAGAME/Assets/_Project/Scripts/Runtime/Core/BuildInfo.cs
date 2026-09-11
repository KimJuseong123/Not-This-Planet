using System.IO;
using UnityEngine;

namespace Metagame.Core
{
    /// <summary>
    /// 이 실행 파일이 언제·어느 커밋으로 빌드됐는지 (S15P21B202-269).
    ///
    /// <b>왜 필요한가.</b> 테스트 빌드를 나눠 주면 받은 사람은 그게 어느 시점 것인지 알 수 없다.
    /// 압축 파일 이름에 날짜를 넣어도 압축을 푸는 순간 사라진다. 그래서 값을 산출물 안에 박고,
    /// 게임이 켜질 때 로그에 찍는다 — 팀원에게 <c>Player.log</c> 만 받아도 어느 빌드가 돌았는지
    /// 확정된다. 같은 날 두 번 빌드한 것을 두고 "새 거 받은 거 맞냐" 로 시간을 버린 적이 있다.
    ///
    /// 값은 빌드할 때 <c>BuildStamp</c>(에디터)가 StreamingAssets 에 써 넣는다. 저장소에는
    /// 아무것도 남기지 않으므로 빌드해도 git 이 더러워지지 않는다.
    /// </summary>
    public static class BuildInfo
    {
        /// <summary>에디터와 런타임이 같은 이름을 보게 한 곳에 둔다.</summary>
        public const string FileName = "build-stamp.txt";

        /// <summary>사람이 읽는 한 줄. 에디터에서 돌리면 그렇다고 나온다.</summary>
        public static string Stamp { get; private set; } = "에디터에서 실행";

        /// <summary>
        /// 이 빌드가 어느 <b>실험</b>인가 (S15P21B202-376). 평소 빌드는 빈 문자열이다.
        ///
        /// <para>빌드할 때 <c>SPACE_EXPERIMENT</c> 환경변수로 넣는다. 이 값이 있으면
        /// 진단 로그를 <b>인자 없이</b> 켠다 — 실험용 빌드를 받은 사람이 PowerShell 로
        /// 무엇을 붙일 필요가 없어야 하는 것이 이 이슈의 목적이다.</para>
        /// </summary>
        public static string Experiment { get; private set; } = string.Empty;

        /// <summary>실험용 빌드인가. 이 값으로 진단을 켤지 정한다.</summary>
        public static bool IsExperiment { get { return !string.IsNullOrEmpty(Experiment); } }

        /// <summary>
        /// 라벨 줄의 머리표. 에디터(<c>BuildStamp</c>)와 런타임이 같은 문자열을 봐야
        /// 하므로 한곳에 둔다 — 예전에 파일 이름을 양쪽에 적어 두고 어긋난 적이 있다.
        /// </summary>
        public const string ExperimentPrefix = "실험=";

        /// <summary>
        /// 백엔드 환경 줄의 머리표 — <c>백엔드=dev</c> 또는 <c>백엔드=prod</c>
        /// (S15P21B202-284). <see cref="BackendEndpoints"/> 가 이 값으로 주소를 정한다.
        /// </summary>
        public const string BackendPrefix = "백엔드=";

        /// <summary>
        /// 이 빌드가 가리키는 백엔드 — <c>dev</c> · <c>prod</c> · 빈 문자열(옛 빌드).
        ///
        /// <para>빈 값을 어떻게 볼지는 <see cref="BackendEndpoints"/> 가 정한다 — 이 클래스는
        /// 읽은 것을 그대로 전한다.</para>
        /// </summary>
        public static string BackendEnvironment { get; private set; } = string.Empty;

        /// <summary>
        /// 씬이 뜨기 전에 찍는다. 뒤에서 어떤 오류가 나든 이 줄은 로그 앞쪽에 남는다 —
        /// 문제가 난 로그를 받았을 때 가장 먼저 확인해야 하는 값이다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Announce()
        {
            Stamp = Read();
            Debug.Log("[빌드] " + Stamp);

            if (IsExperiment) Debug.Log("[빌드] 실험 " + Experiment);

            // 라벨을 읽은 **직후** 시작한다 (S15P21B202-380). 같은 초기화 단계의 순서는
            // 보장되지 않으므로 RunLog 가 스스로 훅을 달면 Experiment 가 아직 비어
            // 있을 수 있다. 읽은 쪽이 부르면 순서가 정해진다.
            RunLog.Start();
        }

        /// <summary>
        /// 값을 읽는다. 못 읽어도 게임은 그대로 굴러가야 한다 — 이건 참고용 표시일 뿐이다.
        /// </summary>
        static string Read()
        {
            // 에디터에서는 빌드를 거치지 않았으므로 파일이 없는 것이 정상이다.
            if (Application.isEditor) return "에디터에서 실행";

            try
            {
                string path = Path.Combine(Application.streamingAssetsPath, FileName);
                if (!File.Exists(path)) return "빌드 정보 없음 (이 표시가 없던 시절 빌드)";

                string text = File.ReadAllText(path).Trim();
                if (string.IsNullOrEmpty(text)) return "빌드 정보 비어 있음";

                // 첫 줄은 사람이 읽는 표시, 둘째 줄부터는 기계가 읽는 값이다.
                // 라벨이 없는 예전 빌드는 첫 줄만 있으므로 그대로 동작한다.
                var lines = text.Split('\n');
                for (int i = 1; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.StartsWith(ExperimentPrefix))
                        Experiment = line.Substring(ExperimentPrefix.Length).Trim();
                    else if (line.StartsWith(BackendPrefix))
                        BackendEnvironment = line.Substring(BackendPrefix.Length).Trim();
                }

                return lines[0].Trim();
            }
            catch (IOException e)
            {
                return "빌드 정보를 읽지 못함 · " + e.Message;
            }
        }
    }
}
