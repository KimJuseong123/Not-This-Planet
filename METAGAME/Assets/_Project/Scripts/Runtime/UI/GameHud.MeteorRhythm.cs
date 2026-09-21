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
        readonly System.Collections.Generic.List<MeteorBat.GuideTarget> rhythmTargets = new(8);
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
                rhythmTargets.Clear();
                rhythmResultUntil = 0f;
            }
            rhythmPending = false;
            if (bat == null) return;
            // Observe only this player's confirmed result. Joining/picking up never replays it.
            if (rhythmSequence != bat.RhythmSequence)
            {
                rhythmSequence = bat.RhythmSequence;
                if (rhythmTargets.Count > 0 || bat.RhythmGrade > 0)
                {
                    rhythmResult = bat.RhythmGrade;
                    rhythmResultUntil = Time.unscaledTime + 0.8f;
                    if (rhythmResult > 0 && Local.Cam != null) Local.Cam.PlayBatImpact(rhythmResult);
                }
            }
            // Remote meteors are disabled visuals, deliberately absent from Meteor.All.
            if (rhythmSpawner == null && Time.unscaledTime >= nextRhythmSearch)
            {
                nextRhythmSearch = Time.unscaledTime + 1f;
                rhythmSpawner = FindAnyObjectByType<MeteorSpawner>();
            }
            MeteorBat.CollectGuideTargets(Local.Position, Meteor.All, rhythmTargets,
                rhythmSpawner != null ? rhythmSpawner.Ghosts : null);
            if (rhythmTargets.Count == 0 && rhythmSpawner != null)
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

        const float RhythmRowHeight = 62f;
        const float RhythmRowPitch = 68f;

        // Overflow uses additional columns rather than hiding incoming timing bars.
        public static Rect MeteorRhythmRowRect(int index, int count, float width, float height)
        {
            int maxRows = Mathf.Max(1, Mathf.FloorToInt((height - 180f) / RhythmRowPitch));
            int columns = Mathf.Max(1, Mathf.CeilToInt(count / (float)maxRows));
            int rows = Mathf.Max(1, Mathf.CeilToInt(count / (float)columns));
            float columnWidth = Mathf.Min(400f, (width - 32f - (columns - 1) * 12f) / columns);
            float totalWidth = columns * columnWidth + (columns - 1) * 12f;
            float top = Mathf.Max(80f, Mathf.Min(height * 0.66f, height - 98f - rows * RhythmRowPitch));
            return new Rect((width - totalWidth) * 0.5f + (index / rows) * (columnWidth + 12f),
                top + (index % rows) * RhythmRowPitch, columnWidth, RhythmRowHeight);
        }

        void DrawMeteorRhythm(float width, float height)
        {
            if (rhythmBat == null || !CanShowRhythm) return;
            int count = rhythmTargets.Count;
            var firstRow = MeteorRhythmRowRect(0, Mathf.Max(1, count), width, height);
            float panelWidth = Mathf.Min(400f, width - 32f);
            float x = (width - panelWidth) * 0.5f;
            float y = firstRow.y;
            if (Time.unscaledTime < rhythmResultUntil)
            {
                var grade = rhythmResult;
                string label = grade == 3 ? "PERFECT" : grade == 2 ? "GREAT" : grade == 1 ? "GOOD" : "MISS";
                L(new Rect(x, y - 58f, panelWidth, 38f), label, big,
                    grade > 0 ? BatSwingLook.ImpactColor(grade) : new Color(1f, 0.35f, 0.3f));
                if (grade == 3)
                    L(new Rect(x, y - 23f, panelWidth, 24f), "반사 속도 ×1.8", center,
                        BatSwingLook.ImpactColor(grade));
            }
            if (rhythmPending)
            {
                UiTheme.DrawPanel(new Rect(x, y, panelWidth, 50f), true, false);
                L(new Rect(x, y + 10f, panelWidth, 30f), "운석 타격 준비", center, UiTheme.Text);
            }

            bool strikeChosen = false;
            float bottom = y;
            for (int i = 0; i < count; i++)
            {
                var target = rhythmTargets[i];
                if (target.Meteor == null || target.Meteor.Harmless || target.Meteor.GroundProp) continue;
                bool inWindow = target.Seconds <= target.Window;
                bool selected = !strikeChosen && inWindow;
                if (selected) strikeChosen = true;
                bool ready = selected && rhythmBat.CanSwing;
                var row = MeteorRhythmRowRect(i, count, width, height);
                bottom = Mathf.Max(bottom, row.yMax);
                UiTheme.DrawPanel(row, true, false);
                if (ready) Box(new Rect(row.x, row.y, 3f, row.height), UiTheme.Lime);
                string status = ready ? "지금 클릭!" : inWindow ? "다음 타격" : "타격 대기";
                L(new Rect(row.x + 8f, row.y + 3f, row.width - 16f, 25f),
                    $"{i + 1} · {status} · {target.Seconds:0.0}초", row.width < 320f ? small : center,
                    ready ? UiTheme.Lime : UiTheme.Text);
                DrawMeteorTimingBar(row, target);
            }
            if (count > 0)
                L(new Rect(16f, bottom + 4f, width - 32f, 28f), rhythmBat.CanSwing
                    ? "좌클릭 · 먼저 떨어지는 순서 · 노란 구간에 맞추기"
                    : "다시 준비...", center, UiTheme.MutedText);
        }

        void DrawMeteorTimingBar(Rect row, MeteorBat.GuideTarget target)
        {
            float window = target.Window;
            float span = target.Span;
            float left = row.x + 20f, barWidth = row.width - 40f;
            float barY = row.y + 36f;
            Box(new Rect(left, barY, barWidth, 14f), new Color(0.16f, 0.21f, 0.3f));
            Box(new Rect(left + barWidth * (1f - window / span), barY,
                barWidth * window / span, 14f), new Color(0.16f, 0.55f, 0.55f));
            Box(new Rect(left + barWidth * (1f - window * 0.8f / span), barY,
                barWidth * window * 0.4f / span, 14f), new Color(0.3f, 0.85f, 0.7f));
            Box(new Rect(left + barWidth * (1f - window * 0.68f / span), barY - 3f,
                barWidth * window * 0.16f / span, 20f), new Color(1f, 0.82f, 0.15f));
            float marker = left + barWidth * target.Progress01;
            Box(new Rect(marker - 2f, barY - 5f, 4f, 24f), Color.white);
        }
    }
}
