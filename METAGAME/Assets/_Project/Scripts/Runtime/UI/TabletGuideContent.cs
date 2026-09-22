using UnityEngine;

namespace Metagame.UI
{
    public static class TabletGuideContent
    {
        static readonly string[] Titles = { "함께 우주선을 조립하고 탈출하세요", "고정못을 잡고 뒤로 당기세요", "운석은 몽둥이로 쳐내세요", "쌓은 파츠는 용접으로 연결하세요",
            "견인 장치로 파츠를 옮기고 쌓으세요", "바람이 불면 낮게 엎드리세요" };
        static readonly string[] Captions = {
            "흩어진 파츠 찾기  →  순서대로 모아 조립하기  →  우주선 발사!",
            "고정못 잡기 → 클릭 유지 + 뒤로 이동(S) → 모두 뽑으면 파츠가 떠요!",
            "몽둥이를 들고  →  다가오는 운석을 타이밍에 맞춰 쳐내기!",
            "쌓기만 하면 흔들려요  →  맞닿은 곳을 용접하기  →  연결 완료!",
            "탑승 후 클릭으로 저중력장 켜기 → 파츠 운반 → 조립대에 쌓기",
            ""
        };
        // 기존 이미지 경로를 유지하면서 고정못 안내를 두 번째에 배치한다.
        static readonly string[] ArtPaths = { "TabletGuide/Page1", "TabletGuide/Page4", "TabletGuide/Page2", "TabletGuide/Page3",
            "TabletGuide/Page5", "TabletGuide/Page6" };
        static readonly Texture2D[] Art = new Texture2D[Metagame.Gameplay.TabletGuideState.PageCount];
        public static string Title(int page) => Titles[Mathf.Clamp(page, 0, Art.Length - 1)];
        public static string Caption(int page)
        {
            page = Mathf.Clamp(page, 0, Art.Length - 1);
            return page == 5
                ? "바람에 날아가기 전에 → " + TabletHelpContent.Cap(Metagame.Core.Controls.Prone) + " 엎드리기 → 바람 피하기"
                : Captions[page];
        }
        public static Texture2D Illustration(int page)
        {
            page = Mathf.Clamp(page, 0, Art.Length - 1);
            if (Art[page] == null) Art[page] = Resources.Load<Texture2D>(ArtPaths[page]);
            return Art[page];
        }
    }
}
