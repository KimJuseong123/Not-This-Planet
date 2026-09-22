using Metagame.Core;
using Metagame.UI;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// <b>키 이름은 태블릿과 같은 것을 쓴다</b> (S15P21B202-679). 예전에는 여기만
    /// 대문자로 적어 같은 키가 화면마다 "TAB" 과 "Tab" 으로 나왔다 — 이 검사도 그
    /// 대문자를 잠그고 있었다.
    /// </summary>
    public sealed class KeyGuideHolderTests
    {
        [Test]
        public void RebindingAndAvailabilityRefreshExistingGuide()
        {
            var holder = new KeyGuideHolder();
            var key = KeyCode.Tab;
            bool available = true;
            holder.Register("tablet", "태블릿", () => key, () => available);
            holder.Refresh();
            Assert.AreEqual("Tab", holder.Entries[0].KeyText);
            Assert.IsTrue(holder.Entries[0].Visible);
            key = KeyCode.Mouse4;
            available = false;
            holder.Refresh();
            Assert.AreEqual("Mouse4", holder.Entries[0].KeyText);
            Assert.IsFalse(holder.Entries[0].Visible);
            available = true;
            holder.Refresh();
            Assert.IsTrue(holder.Entries[0].Visible);
        }

        [Test]
        public void UnboundGuideIsHiddenAndNumberKeyHasReadableLabel()
        {
            var holder = new KeyGuideHolder();
            var key = KeyCode.None;
            holder.Register("extra", "추가", () => key, () => true);
            holder.Refresh();
            Assert.IsFalse(holder.Entries[0].Visible);
            key = KeyCode.Alpha1;
            holder.Refresh();
            Assert.AreEqual("1", holder.Entries[0].KeyText);
            Assert.IsTrue(holder.Entries[0].Visible);
        }

        [Test]
        public void RegisterReplacesInPlaceAndHoldersRemainIndependent()
        {
            var first = new KeyGuideHolder();
            var second = new KeyGuideHolder();
            first.Register("tablet", "태블릿", () => KeyCode.Tab, () => true);
            first.Register("throw", "던지기", () => KeyCode.Q, () => true);
            first.Register("tablet", "태블릿", () => KeyCode.F2, () => true);
            first.Refresh();
            Assert.AreEqual(2, first.Entries.Count);
            Assert.AreEqual("F2", first.Entries[0].KeyText);
            Assert.AreEqual("throw", first.Entries[1].Id);
            Assert.AreEqual(0, second.Entries.Count);
            first.Remove("tablet");
            Assert.AreEqual("throw", first.Entries[0].Id);
        }

        [Test]
        public void AppliedPlayerSettingsAreReadWithoutRecreatingHolder()
        {
            var oldTablet = Controls.Tablet;
            var oldThrow = Controls.ToolAction;
            try
            {
                var holder = new KeyGuideHolder();
                holder.Register("tablet", "태블릿", () => Controls.Tablet, () => true);
                holder.Register("throw", "던지기", () => Controls.ToolAction, () => true);
                Controls.Tablet = KeyCode.F3;
                Controls.ToolAction = KeyCode.Mouse3;
                holder.Refresh();
                Assert.AreEqual("F3", holder.Entries[0].KeyText);
                Assert.AreEqual("Mouse3", holder.Entries[1].KeyText);
            }
            finally
            {
                Controls.Tablet = oldTablet;
                Controls.ToolAction = oldThrow;
            }
        }
    }
}
