using System;

namespace Metagame.Gameplay
{
    /// <summary>개인 태블릿 안내 상태. 게임 진행·물리·다른 플레이어를 변경하지 않는다.</summary>
    public sealed class TabletGuideState
    {
        public const int PageCount = 6;
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

        /// <summary>
        /// 안내를 한 쪽 넘긴다. <b>마지막 쪽에서 더 넘기면 안내를 벗어난다</b>
        /// (S15P21B202-657) — 돌려주는 값이 그것이고, 그때 부르는 쪽이 진행도로 옮긴다.
        ///
        /// <para>예전에는 안내가 <b>막다른 길</b>이었다. 확인 키를 따로 누르기 전까지
        /// 진행도와 조작 안내가 그려지지도 않아서, 처음 하는 사람이 넷 중 아무것도
        /// 못 봤다. 넘기다 보면 빠져나오는 것이 이 이슈의 핵심이다.</para>
        /// </summary>
        /// <returns>안내 밖으로 나갔으면 <c>true</c>.</returns>
        public bool Move(int direction)
        {
            if (!Visible) return false;
            if (direction > 0 && Page >= PageCount - 1)
            {
                Completed = true;
                Visible = false;
                Confirmed?.Invoke();
                return true;
            }
            Page = Math.Max(0, Math.Min(PageCount - 1, Page + Math.Sign(direction)));
            return false;
        }

        /// <summary>
        /// 안내를 그 쪽에서 다시 연다 (S15P21B202-657). 진행도에서 앞으로 되돌아올 때
        /// 쓴다 — <b>다시 보려고 판을 새로 시작할 필요가 없어야 한다.</b>
        /// </summary>
        public void OpenAt(int page)
        {
            if (!Enabled) return;
            Visible = true;
            Page = Math.Max(0, Math.Min(PageCount - 1, page));
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
