using System;

namespace Metagame.Gameplay
{
    /// <summary>개인 태블릿 안내 상태. 게임 진행·물리·다른 플레이어를 변경하지 않는다.</summary>
    public sealed class TabletGuideState
    {
        public const int PageCount = 4;
        public int RunVersion { get; private set; }
        public bool Enabled { get; private set; }
        public bool Completed { get; private set; }
        public bool Visible { get; private set; }
        public int Page { get; private set; }
        public bool CanConfirm => Enabled && Visible;
        public event Action Confirmed;

        public void Enable()
        {
            if (Enabled) return;
            Enabled = true;
            ResetRun();
        }

        public void ResetRun()
        {
            Page = 0;
            Completed = false;
            RunVersion++;
            Visible = false;
        }

        public void OnTabletOpened()
        {
            if (!Enabled || Completed || Visible) return;
            Open();
        }

        public void Open()
        {
            if (!Enabled) return;
            Visible = true;
            Page = 0;
        }

        public void Move(int direction)
        {
            if (!Visible) return;
            Page = Math.Max(0, Math.Min(PageCount - 1, Page + Math.Sign(direction)));
        }

        public bool Confirm()
        {
            if (!CanConfirm) return false;
            Completed = true;
            Visible = false;
            Confirmed?.Invoke();
            return true;
        }
    }
}
