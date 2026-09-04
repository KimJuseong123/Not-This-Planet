using UnityEngine;
using UnityEngine.Video;

namespace Metagame.UI
{
    /// <summary>
    /// 로비의 전체 화면 영상 배경과 정지 이미지 fallback을 그린다.
    ///
    /// 영상은 씬의 카메라나 Canvas에 의존하지 않고 RenderTexture에 출력한다.
    /// 그래서 기존 IMGUI 로비 위에 같은 화면 비율로 안정적으로 깔 수 있다.
    /// </summary>
    public sealed class LobbyBackground : MonoBehaviour
    {
        private Texture2D fallbackImage;
        private VideoPlayer videoPlayer;
        private RenderTexture videoTexture;
        private bool videoReady;
        private bool videoFailed;

        /// <summary>영상과 fallback을 연결하고 재생을 시작한다.</summary>
        public void Initialize(Texture2D image, VideoClip clip)
        {
            fallbackImage = image;
            videoReady = false;
            videoFailed = false;

            if (videoPlayer != null)
            {
                videoPlayer.prepareCompleted -= OnPrepared;
                videoPlayer.errorReceived -= OnError;
                videoPlayer.Stop();
                videoPlayer.clip = null;
            }

            ReleaseVideoTexture();

            if (clip == null)
                return;

            if (videoPlayer == null)
                videoPlayer = gameObject.AddComponent<VideoPlayer>();

            videoPlayer.playOnAwake = false;
            videoPlayer.waitForFirstFrame = true;
            videoPlayer.skipOnDrop = true;
            videoPlayer.isLooping = true;
            videoPlayer.source = VideoSource.VideoClip;
            videoPlayer.clip = clip;
            videoPlayer.renderMode = VideoRenderMode.RenderTexture;
            videoPlayer.audioOutputMode = VideoAudioOutputMode.None;
            videoPlayer.prepareCompleted += OnPrepared;
            videoPlayer.errorReceived += OnError;

            int width = clip.width > 0 ? Mathf.Min((int)clip.width, 1920) : 1920;
            int height = clip.height > 0 ? Mathf.Min((int)clip.height, 1080) : 1080;
            videoTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
            {
                name = "~로비 영상 출력",
                useMipMap = false,
                autoGenerateMips = false,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            videoTexture.Create();
            videoPlayer.targetTexture = videoTexture;
            videoPlayer.Prepare();
        }

        private void OnPrepared(VideoPlayer source)
        {
            if (source != videoPlayer || videoFailed)
                return;

            videoReady = true;
            source.Play();
        }

        private void OnError(VideoPlayer source, string message)
        {
            if (source != videoPlayer)
                return;

            videoFailed = true;
            videoReady = false;
            Debug.LogWarning("[LobbyBackground] 로비 영상 재생 실패: " + message, this);
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint)
                return;

            Texture background = videoReady && !videoFailed && videoTexture != null
                ? videoTexture
                : fallbackImage;

            int previousDepth = GUI.depth;
            GUI.depth = 1000;

            if (background != null)
            {
                GUI.color = Color.white;
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height),
                    background, ScaleMode.ScaleAndCrop, true);
            }
            else
            {
                GUI.color = new Color(0.02f, 0.04f, 0.10f, 1f);
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height),
                    Texture2D.whiteTexture);
            }

            // 글자와 버튼이 배경의 밝은 부분에 묻히지 않도록 얇은 남색 veil을 둔다.
            GUI.color = new Color(0.01f, 0.03f, 0.10f, 0.20f);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.depth = previousDepth;
        }

        private void ReleaseVideoTexture()
        {
            if (videoTexture == null)
                return;

            videoTexture.Release();
            Destroy(videoTexture);
            videoTexture = null;
        }

        private void OnDestroy()
        {
            if (videoPlayer != null)
            {
                videoPlayer.prepareCompleted -= OnPrepared;
                videoPlayer.errorReceived -= OnError;
                videoPlayer.Stop();
                videoPlayer.targetTexture = null;
            }

            ReleaseVideoTexture();
        }
    }
}
