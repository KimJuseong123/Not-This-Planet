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

        // 지금 어디를 보고 있는가. Look 은 "얼마나 돌렸나" 라서 이것만으로는 방향을
        // 알 수 없다. 카메라는 각자 PC 에만 있으므로(S15P21B202-147), 서버가 "앞" 이
        // 어느 쪽인지 알려면 결과인 이 두 각도를 함께 받아야 한다.
        // 혼자 돌릴 때는 카메라가 옆에 있으니 쓰이지 않는다.
        public float AimYaw;
        public float AimPitch;

        /// <summary>
        /// 조준이 시작되는 지점 — 내 화면의 카메라 위치다.
        ///
        /// 각도만으로는 모자란다. 내 화면은 카메라에서 재고 서버는 몸에서 재면 기준점이
        /// 몇 미터 어긋나, 화면에는 "잡을 수 있다" 고 나오는데 눌러도 안 잡힌다.
        /// 방장은 서버에 선 몸이 곧 내 몸이라 카메라가 있어 안 겪고, 참가자만 겪었다
        /// (2026-09-03 실측). 시작점을 함께 보내면 양쪽이 같은 것을 본다.
        /// </summary>
        public Vector3 AimFrom;
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

        /// <summary>
        /// T — 끼임 탈출(기획 §21). <b>누른 순간이 아니라 누르고 있는 상태</b>다.
        /// §21 이 "3초 동안 유지하면" 이라고 정하므로 유지 여부를 매 프레임 알아야 한다.
        /// </summary>
        public bool StuckEscapeHeld;
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
        public static KeyCode StuckEscape = KeyCode.T;  // 끼었을 때 탈출 (기획 §21)
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
            f.StuckEscapeHeld = Input.GetKey(StuckEscape);
            return f;
        }
    }
}
