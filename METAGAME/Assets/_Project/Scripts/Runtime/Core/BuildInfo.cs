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
        /// 씬이 뜨기 전에 찍는다. 뒤에서 어떤 오류가 나든 이 줄은 로그 앞쪽에 남는다 —
        /// 문제가 난 로그를 받았을 때 가장 먼저 확인해야 하는 값이다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Announce()
        {
            Stamp = Read();
            Debug.Log("[빌드] " + Stamp);
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
                return string.IsNullOrEmpty(text) ? "빌드 정보 비어 있음" : text;
            }
            catch (IOException e)
            {
                return "빌드 정보를 읽지 못함 · " + e.Message;
            }
        }
    }
}
