using UnityEngine;
using Metagame.Gameplay;
using Metagame.Core;
using Metagame.Gameplay.Hazards;

namespace Metagame.UI
{
    public partial class GameHud
    {
        MeteorSpawner rhythmSpawner;
        MeteorBat rhythmBat;
        Meteor rhythmTarget;
        uint rhythmSequence;
        float rhythmResultUntil;
        byte rhythmResult;
        float nextRhythmSearch;
        bool rhythmPending;

        bool CanShowRhythm => Local != null && !Local.Downed && !Local.IsRiding
            && Cursor.lockState == CursorLockMode.Locked
            && (Exploration == null || !Exploration.TabletOpen)
            && (GameSession.Instance == null || !GameSession.Instance.EndingActive);

        void UpdateMeteorRhythm()
        {
            var bat = CanShowRhythm ? Local.PresentationTool as MeteorBat : null;
            if (bat != null && !bat.IsHeldForView) bat = null;
            if (bat != rhythmBat)
            {
                rhythmBat = bat;
                rhythmSequence = bat != null ? bat.RhythmSequence : 0;
                rhythmTarget = null;
                rhythmResultUntil = 0f;
            }
            rhythmPending = false;
            if (bat == null) return;
            // Observe only this player's confirmed result. Joining/picking up never replays it.
            if (rhythmSequence != bat.RhythmSequence)
            {
                rhythmSequence = bat.RhythmSequence;
                if (rhythmTarget != null || bat.RhythmGrade > 0)
                {
                    rhythmResult = bat.RhythmGrade;
                    rhythmResultUntil = Time.unscaledTime + 0.8f;
                    if (rhythmResult > 0 && Local.Cam != null) Local.Cam.PlayBatImpact(rhythmResult);
                }
            }
            rhythmTarget = MeteorBat.FindGuideTarget(Local.Position, Meteor.All);
            // Remote meteors are disabled visuals, deliberately absent from Meteor.All.
            if (rhythmSpawner == null && Time.unscaledTime >= nextRhythmSearch)
            {
                nextRhythmSearch = Time.unscaledTime + 1f;
                rhythmSpawner = FindAnyObjectByType<MeteorSpawner>();
            }
            if (rhythmTarget == null && rhythmSpawner != null)
                rhythmTarget = MeteorBat.FindGuideTarget(Local.Position, rhythmSpawner.Ghosts);
            if (rhythmTarget == null && rhythmSpawner != null)
            {
                float radius = rhythmSpawner.Tuning != null
                    ? rhythmSpawner.Tuning.BatAssistRadius : PhysicsScale.BatAssistRadius;
                foreach (var drop in rhythmSpawner.Pending)
                {
                    var offset = drop.ImpactPoint - Local.Position;
                    offset.y = 0f;
                    if (offset.sqrMagnitude <= radius * radius)
                    { rhythmPending = true; break; }
                }
            }
        }

        void DrawMeteorRhythm(float width, float height)
        {
            if (rhythmBat == null || !CanShowRhythm) return;
            float panelWidth = Mathf.Min(400f, width - 32f);
            float x = (width - panelWidth) * 0.5f;
            float y = height * 0.66f;
            if (Time.unscaledTime < rhythmResultUntil)
            {
                var grade = rhythmResult;
                string label = grade == 3 ? "PERFECT" : grade == 2 ? "GREAT" : grade == 1 ? "GOOD" : "MISS";
                L(new Rect(x, y - 45f, panelWidth, 38f), label, big,
                    grade > 0 ? new Color(1f, 0.86f, 0.25f) : new Color(1f, 0.35f, 0.3f));
            }
            if (rhythmPending)
            {
                Box(new Rect(x, y, panelWidth, 50f), new Color(0.025f, 0.035f, 0.07f, 0.88f));
                L(new Rect(x, y + 10f, panelWidth, 30f), "운석 타격 준비", center, Color.white);
            }
            if (rhythmTarget == null || !rhythmTarget.TryPredictImpact(out float seconds, out _)) return;
            float window = rhythmTarget.BatSwingWindowSeconds;
            float span = Mathf.Max(MeteorBat.GuideSeconds, window * 1.5f);
            Box(new Rect(x, y, panelWidth, 112f), new Color(0.025f, 0.035f, 0.07f, 0.88f));
            bool ready = seconds <= window && rhythmBat.CanSwing;
            L(new Rect(x, y + 7f, panelWidth, 25f),
                ready ? "지금 클릭!" : "운석 타격 준비", center, Color.white);
            float left = x + 20f, barWidth = panelWidth - 40f;
            float barY = y + 43f;
            Box(new Rect(left, barY, barWidth, 18f), new Color(0.16f, 0.21f, 0.3f));
            Box(new Rect(left + barWidth * (1f - window / span), barY,
                barWidth * window / span, 18f), new Color(0.16f, 0.55f, 0.55f));
            Box(new Rect(left + barWidth * (1f - window * 0.8f / span), barY,
                barWidth * window * 0.4f / span, 18f), new Color(0.3f, 0.85f, 0.7f));
            Box(new Rect(left + barWidth * (1f - window * 0.68f / span), barY - 3f,
                barWidth * window * 0.16f / span, 24f), new Color(1f, 0.82f, 0.15f));
            float marker = left + barWidth * Mathf.Clamp01(1f - seconds / span);
            Box(new Rect(marker - 2f, barY - 7f, 4f, 32f), Color.white);
            L(new Rect(x, y + 70f, panelWidth, 28f), rhythmBat.CanSwing
                ? "좌클릭 · 노란 구간에 맞춰 휘두르기" : "다시 준비...", center, Color.white);
        }
    }
}
