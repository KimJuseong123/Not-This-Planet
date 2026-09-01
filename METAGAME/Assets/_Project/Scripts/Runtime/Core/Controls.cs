using UnityEngine;

namespace Metagame.Core
{
    /// <summary>
    /// 한 프레임의 조작. 셀프테스트는 사람 대신 이 구조체를 채워 넣어
    /// 입력 없이도 걷기·점프·잡기를 검증한다.
    /// </summary>
    public struct InputFrame
    {
        public Vector2 Move;      // x=좌우, y=앞뒤
        public Vector2 Look;      // 마우스 델타
        public bool Jump;
        public bool Run;
        public bool GrabLeft;     // 좌클릭 — 왼팔
        public bool GrabRight;    // 우클릭 — 오른팔
        public bool ToolAction;   // Q — 도구를 놓거나 충전해서 던진다
        // 도구가 장착된 동안 사용하는 의미 입력. 물리 키 매핑은 이 파일에만 둔다.
        public bool ToolPrimary;  // 현재는 좌클릭 — 도구 주 기능
        public bool ToolSecondary;// 현재는 우클릭 — 도구 보조 기능
        public bool ToolUtility;  // 현재는 Q — 공통 장비 동작과 호환
        public bool TransportAction; // E — 운반 장치 탑승 / 하차
        public bool ArmsUp;       // 두 팔을 위로 (기어오를 때)
        public bool FreeLook;     // Alt — 시점은 자유롭게 돌리고 이동·조준 기준은 현재 시점을 따른다
        public bool TabletToggle; // Tab
        public bool FieldToggle;  // F — 광역 저중력장 켜기·끄기 (기획 §14.1)
        public bool TabletPreviousPage; // ← 또는 A — 태블릿 이전 페이지
        public bool TabletNextPage;     // → 또는 D — 태블릿 다음 페이지
        public bool Restart;
    }

    /// <summary>
    /// 장착된 도구에 전달하는 의미 입력. 도구는 Unity Input API를 직접 읽지 않고
    /// 이 구조체만 받아 장비별 동작을 구현한다.
    /// </summary>
    public struct ToolInputFrame
    {
        public bool PrimaryHeld;
        public bool SecondaryHeld;
        public bool UtilityHeld;

        public bool PrimaryPressed;
        public bool SecondaryPressed;
        public bool UtilityPressed;

        public bool PrimaryReleased;
        public bool SecondaryReleased;
        public bool UtilityReleased;
    }

    /// <summary>입력 매핑을 한 곳에 (Legacy Input Manager).</summary>
    public static class Controls
    {
        public static KeyCode Jump = KeyCode.Space;
        public static KeyCode Run = KeyCode.LeftShift;
        public static KeyCode ArmsUp = KeyCode.LeftControl;
        public static KeyCode FreeLook = KeyCode.LeftAlt;
        public static KeyCode Restart = KeyCode.R;
        public static KeyCode Respawn = KeyCode.T;      // 끼었을 때 탈출
        public static KeyCode ToolAction = KeyCode.Q;   // 도구 놓기 / 충전 투척
        public static KeyCode TransportAction = KeyCode.E; // 운반 장치 탑승 / 하차
        public static KeyCode Tablet = KeyCode.Tab;
        public static KeyCode FieldToggle = KeyCode.F;
        public static KeyCode Help = KeyCode.Tab;
        public static KeyCode TabletPreviousPage = KeyCode.LeftArrow;
        public static KeyCode TabletNextPage = KeyCode.RightArrow;
        public static KeyCode TabletPreviousPageAlternate = KeyCode.A;
        public static KeyCode TabletNextPageAlternate = KeyCode.D;
        public static KeyCode FreeCursor = KeyCode.Escape;

        public static InputFrame Read()
        {
            var f = new InputFrame();
            f.Move = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            f.Look = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
            f.Jump = Input.GetKey(Jump);
            f.Run = Input.GetKey(Run);
            f.GrabLeft = Input.GetMouseButton(0);
            f.GrabRight = Input.GetMouseButton(1);
            f.ToolAction = Input.GetKey(ToolAction);
            f.ToolPrimary = f.GrabLeft;
            f.ToolSecondary = f.GrabRight;
            f.ToolUtility = f.ToolAction;
            f.TransportAction = Input.GetKeyDown(TransportAction);
            f.ArmsUp = Input.GetKey(ArmsUp);
            f.FreeLook = Input.GetKey(FreeLook) || Input.GetKey(KeyCode.RightAlt);
            f.TabletToggle = Input.GetKeyDown(Tablet);
            f.FieldToggle = Input.GetKeyDown(FieldToggle);
            f.TabletPreviousPage = Input.GetKeyDown(TabletPreviousPage)
                                   || Input.GetKeyDown(TabletPreviousPageAlternate);
            f.TabletNextPage = Input.GetKeyDown(TabletNextPage)
                               || Input.GetKeyDown(TabletNextPageAlternate);
            f.Restart = Input.GetKeyDown(Restart);
            return f;
        }
    }
}
