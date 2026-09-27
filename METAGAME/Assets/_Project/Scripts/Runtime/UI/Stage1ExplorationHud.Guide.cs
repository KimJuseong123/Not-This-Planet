using Metagame.Core;
using UnityEngine;

namespace Metagame.UI
{
    public sealed partial class Stage1ExplorationHud
    {
        AudioSource guideAudio;
        AudioClip guideChirp;
        float guideNoticeTime;
        int guideChirps;
        int guideNoticeRunVersion = -1;

        bool GuideInputAllowed => _session != null && _session.LocalPlayer != null
            && !_session.InputSuppressed && !_session.ScriptedInput && !_session.EndingActive;
        bool GuideNoticeAllowed => GuideInputAllowed && _exploration != null
            && _exploration.Guide.Enabled && !_exploration.Guide.Completed
            && !_exploration.TabletOpen && !_session.LocalPlayer.Downed && !_session.Cleared;

        void InitializeGuide()
        {
            if (_exploration == null) return;
            // 안내 완료는 PC가 아니라 이번 게임에만 적용한다. 이전 PlayerPrefs는 읽거나 지우지 않는다.
            _exploration.Guide.Enable();
            ResetGuideNotice();
        }

        void ResetGuideNotice()
        {
            guideNoticeRunVersion = _exploration.Guide.RunVersion;
            guideNoticeTime = 0f;
            guideChirps = 0;
            if (guideAudio != null) guideAudio.Stop();
        }

        void TickGuideNotice()
        {
            if (_exploration != null && guideNoticeRunVersion != _exploration.Guide.RunVersion)
                ResetGuideNotice();
            if (!GuideNoticeAllowed)
            {
                if (guideAudio != null && guideAudio.isPlaying) guideAudio.Stop();
                return;
            }
            guideNoticeTime += Time.unscaledDeltaTime;
            // 시작 화면이 자리 잡은 뒤 알림. 소리는 최대 세 번, 표시는 확인 전까지 유지.
            if (guideChirps >= 3 || guideNoticeTime < 2f + guideChirps * 9f) return;
            guideChirps++;
            if (guideAudio == null)
            {
                guideAudio = gameObject.AddComponent<AudioSource>();
                guideAudio.playOnAwake = false;
                guideAudio.spatialBlend = 0f;
                guideAudio.loop = false;
                guideChirp = CreateGuideChirp();
                guideAudio.clip = guideChirp;
            }
            guideAudio.volume = 0.22f * (GameSettingsRuntime.Instance?.Current?.audio?.sfxVolume ?? 1f);
            guideAudio.Play();
        }

        static AudioClip CreateGuideChirp()
        {
            const int rate = 22050;
            // 짧은 3음 두 묶음: 삐비빅, 삐비빅. 시각 신호와 같은 9초 주기로 알린다.
            var samples = new float[(int)(rate * 1.2f)];
            for (int i = 0; i < samples.Length; i++)
            {
                float t = i / (float)rate;
                float groupTime = t % 0.65f;
                if (groupTime >= 0.45f) continue;
                float pulse = groupTime % 0.15f;
                float envelope = Mathf.Clamp01(pulse / 0.012f) * Mathf.Clamp01((0.11f - pulse) / 0.045f);
                samples[i] = Mathf.Sin(2f * Mathf.PI * (1150f + Mathf.Floor(groupTime / 0.15f) * 180f) * t)
                    * envelope * 0.35f;
            }
            var clip = AudioClip.Create("Tablet_IncomingSignal", samples.Length, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        void DrawGuideNotice()
        {
            if (!GuideNoticeAllowed || guideNoticeTime < 2f) return;
            float y = UiScale.Height * 0.40f;
            var color = new Color(1f, 0.18f, 0.22f, 0.95f);
            // 화면 왼쪽 가장자리에서 오른쪽으로 퍼지는 전파. 네모 패널 없이 TAB만 아래에 표시.
            Vector2 origin = new Vector2(12f, y);
            float cycle = (guideNoticeTime - 2f) % 9f;
            int lit = cycle < 1.2f ? 1 + Mathf.FloorToInt((cycle % 0.65f) / 0.15f) % 3
                : 1 + Mathf.FloorToInt(cycle * 2f) % 3;
            Box(new Rect(origin.x - 3f, origin.y - 3f, 6f, 6f), color);
            for (int arc = 1; arc <= 3; arc++)
            {
                var tint = color;
                if (arc > lit) tint.a *= 0.25f;
                float radius = arc * 23f;
                for (int j = 0; j < 24; j++)
                {
                    float a = (-65f + j * 130f / 24f) * Mathf.Deg2Rad;
                    float b = (-65f + (j + 1) * 130f / 24f) * Mathf.Deg2Rad;
                    var from = origin + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
                    var to = origin + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * radius;
                    DrawGuideLine(from, to, new Color(0.01f, 0.025f, 0.04f, 0.75f), 6f);
                    DrawGuideLine(from, to, tint, 3f);
                }
            }
            // **동사를 붙인다** (S15P21B202-657). 예전에는 "TAB" 글자만 띄웠는데,
            // 처음 하는 사람은 그것이 누르라는 뜻인 줄 몰랐다 — 끝까지 한 번도 안 눌렀다.
            string key = TabletHelpContent.Cap(Controls.Tablet) + " 태블릿 열기";
            L(new Rect(20f, y + 77f, 420f, 40f), key, _title, new Color(0.01f, 0.025f, 0.04f, 0.9f));
            L(new Rect(18f, y + 75f, 420f, 40f), key, _title, color);
        }

        static void DrawGuideLine(Vector2 from, Vector2 to, Color color, float width = 3f)
        {
            var matrix = GUI.matrix;
            var previous = GUI.color;
            GUI.color = color;
            var delta = to - from;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, from);
            GUI.DrawTexture(new Rect(from.x, from.y - width * 0.5f, delta.magnitude + 1f, width), Texture2D.whiteTexture);
            GUI.matrix = matrix;
            GUI.color = previous;
        }

        void DrawGuideFallback(Rect panel)
        {
            var guide = _exploration.Guide;
            Box(panel, new Color(0.035f, 0.085f, 0.115f, 0.99f));
            L(new Rect(panel.x + 30f, panel.y + 22f, panel.width - 60f, 44f),
                TabletGuideContent.Title(guide.Page), _title, Color.white);
            var art = TabletGuideContent.Illustration(guide.Page);
            if (art != null) GUI.DrawTexture(new Rect(panel.x + 30f, panel.y + 86f, panel.width - 60f, panel.height - 222f), art, ScaleMode.ScaleToFit);
            L(new Rect(panel.x + 30f, panel.yMax - 122f, panel.width - 60f, 48f),
                TabletGuideContent.Caption(guide.Page), _mid, Color.white);
            L(new Rect(panel.x + 30f, panel.yMax - 50f, panel.width * 0.62f, 34f),
                TabletHelpContent.PageStrip(true, guide.Page, 0), _mid, Color.white);
            bool enabled = GUI.enabled;
            GUI.enabled = enabled && GuideInputAllowed && guide.CanConfirm;
            if (UiInteraction.Button(new Rect(panel.xMax - 250f, panel.yMax - 55f, 220f, 38f), "진행도로 건너뛰기"))
                _exploration.ActivateTabletGuide();
            GUI.enabled = enabled;
        }

        void ReleaseGuide()
        {
            if (guideAudio != null) guideAudio.Stop();
        }

        void OnDisable()
        {
            if (guideAudio != null) guideAudio.Stop();
        }

        void OnDestroy()
        {
            ReleaseGuide();
            if (guideChirp != null) Destroy(guideChirp);
            if (_white != null) Destroy(_white);
            if (_arrowLeft != null) Destroy(_arrowLeft);
            if (_arrowRight != null) Destroy(_arrowRight);
        }
    }
}
