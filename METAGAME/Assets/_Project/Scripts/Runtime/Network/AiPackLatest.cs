using System;
using System.Collections;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Metagame.Network
{
    /// <summary>
    /// 서버의 <b>최신</b> AI 추가팩을 찾는다 (S15P21B202-744).
    ///
    /// <para><b>왜.</b> 빌드가 manifest 주소와 해시를 박아 두면(<c>-690</c>) 팩을 새로 올릴 때마다 CI
    /// 변수를 고치고 빌드를 다시 해야 했다. 이제 빌드에는 <b>고정 주소</b>(<c>latest_url</c>)만 두고,
    /// 서버가 그 주소에 최신 manifest 를 둔다 — 서버의 <c>latest-companion-v4</c> 가 최신 팩 폴더를
    /// 가리키는 링크라, 팩을 올릴 때 링크 하나만 옮기면 된다.</para>
    ///
    /// <para><b>무엇을 믿나.</b> 받은 manifest 가 지금의 규칙(<see cref="AiPackValidation.Manifest"/> —
    /// 스키마 · 플랫폼 · 프로토콜 · 승인 · zip 두 개의 크기와 해시)을 통과할 때만 쓰고, 그 바이트의
    /// 해시를 배포 정보로 삼는다. zip 은 지금처럼 manifest 의 해시로 확인한다. 달라진 것은 manifest
    /// 해시의 출처가 빌드에서 같은 서버로 옮겨 간 것뿐이다 — 게임 빌드도 같은 서버에서 받는다.</para>
    ///
    /// <para><b>주소에 프로토콜이 들어 있다.</b> 워커 프로토콜이 바뀐 팩을 올려도 옛 빌드는 제 프로토콜의
    /// 주소를 보므로 맞지 않는 팩을 받지 않는다.</para>
    ///
    /// <para><b>못 받으면 빌드에 박힌 값을 쓴다</b> — 지금과 같다. 세션마다 한 번, 짧게 받는다.</para>
    /// </summary>
    public static class AiPackLatest
    {
        public const int TimeoutSeconds = 6;
        public const int MaxManifestBytes = 64 * 1024;

        static volatile AiPackSource resolved;
        static volatile bool fetching;

        /// <summary>받아서 검증을 통과한 최신 배포 정보. 아직 없으면 null. 작업 스레드에서 읽어도 된다.</summary>
        public static AiPackSource Resolved => resolved;
        public static bool Fetching => fetching;

        /// <summary>최신 주소가 쓸 만한가 — manifest 주소와 같은 규칙(https · 사용자정보 · 질의 · 조각 없음).</summary>
        public static bool Url(string value)
            => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https"
               && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);

        /// <summary>
        /// 받은 manifest 바이트 → 배포 정보. 규칙을 못 넘으면 null. <b>순수 함수</b>다 — 검사가 값으로 부른다.
        /// </summary>
        public static AiPackSource FromManifest(string url, byte[] body)
        {
            if (!Url(url) || body == null || body.Length == 0 || body.Length > MaxManifestBytes) return null;
            AiPackManifest manifest;
            try { manifest = JsonUtility.FromJson<AiPackManifest>(Encoding.UTF8.GetString(body)); }
            catch (Exception) { return null; }
            if (!AiPackValidation.Manifest(manifest)) return null;
            using (var sha = SHA256.Create())
                return new AiPackSource { manifest_url = url, manifest_sha256 = Hex(sha.ComputeHash(body)) };
        }

        /// <summary>
        /// 빌드에 박힌 값과 받은 최신 중 무엇을 쓸까. 최신이 규칙을 통과하면 최신, 아니면 빌드의 값.
        /// </summary>
        public static AiPackSource Choose(AiPackSource built, AiPackSource latest)
        {
            if (!AiPackValidation.Source(latest)) return built;
            return new AiPackSource { manifest_url = latest.manifest_url, manifest_sha256 = latest.manifest_sha256,
                                      latest_url = built != null ? built.latest_url : latest.manifest_url };
        }

        /// <summary>최신 manifest 를 한 번 받는다. 이미 받는 중이거나 주소가 틀리면 아무것도 안 한다.</summary>
        public static IEnumerator Fetch(string url)
        {
            if (fetching || !Url(url)) yield break;
            fetching = true;
            using (var request = UnityWebRequest.Get(url))
            {
                request.timeout = TimeoutSeconds;
                yield return request.SendWebRequest();
                var found = request.result == UnityWebRequest.Result.Success
                    ? FromManifest(url, request.downloadHandler.data) : null;
                if (found != null)
                {
                    resolved = found;
                    Debug.Log("[AI 팩] 서버의 최신 팩을 쓴다 · manifest " + found.manifest_sha256.Substring(0, 12));
                }
                else
                    Debug.Log("[AI 팩] 최신 팩을 확인하지 못해 빌드에 들어 있는 값을 쓴다 (" + request.result + ")");
            }
            fetching = false;
        }

        static string Hex(byte[] bytes)
        {
            var text = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) text.Append(b.ToString("x2"));
            return text.ToString();
        }
    }
}
