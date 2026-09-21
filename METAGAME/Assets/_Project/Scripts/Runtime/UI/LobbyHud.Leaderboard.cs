using Metagame.Core;
using UnityEngine;

namespace Metagame.UI
{
    /// <summary>
    /// 로비의 기록 표 (S15P21B202-656).
    ///
    /// <para><b>따로 둔 파일이다.</b> <c>LobbyHud</c> 가 이미 1,200줄이고, 여기에
    /// 더하면 메뉴·설정·멀티플레이가 한 파일에 네 화면으로 쌓인다.
    /// <c>GameHud.KeyGuides</c>·<c>GameHud.MeteorRhythm</c> 이 같은 방식으로 갈라져 있다.</para>
    /// </summary>
    public sealed partial class LobbyHud
    {
        /// <summary>표에 보여 주는 줄 수. 한 화면에 들어가는 정도다.</summary>
        const int BoardRows = 10;

        /// <summary>지금 보고 있는 표의 인원. 처음에는 4인을 연다.</summary>
        int boardCrew = Leaderboard.MaxCrew;
        LeaderboardRow[] boardRows = System.Array.Empty<LeaderboardRow>();
        bool boardLoading;

        /// <summary>
        /// 이미 받아 본 판인가. <b>빈 표와 아직 안 받은 표를 가른다</b> —
        /// 둘 다 줄이 0개라, 이것이 없으면 기록이 없는 판에서 "불러오는 중" 이
        /// 영원히 보인다.
        /// </summary>
        bool boardLoaded;

        void OpenLeaderboard()
        {
            error = string.Empty;
            notice = string.Empty;
            view = LobbyView.Leaderboard;
            RequestBoard(boardCrew);
        }

        void RequestBoard(int crewSize)
        {
            boardCrew = crewSize;
            boardRows = System.Array.Empty<LeaderboardRow>();
            boardLoaded = false;
            boardLoading = true;

            StartCoroutine(LeaderboardUpload.Fetch(
                BackendEndpoints.HttpBaseUrl, crewSize, BoardRows, rows =>
                {
                    // 탭을 빠르게 바꾸면 늦게 온 응답이 새 탭을 덮을 수 있다.
                    if (crewSize != boardCrew) return;

                    boardRows = rows;
                    boardLoading = false;
                    boardLoaded = true;
                }));
        }

        void DrawLeaderboard()
        {
            float scale = Mathf.Clamp(Mathf.Min(Screen.width / 1600f, Screen.height / 900f), 0.75f, 1.35f);
            float width = 620f * scale;
            float height = 520f * scale;

            Rect safe = UiScale.SafeArea;
            float x = Mathf.Clamp(UiScale.Width * 0.5f - width * 0.5f, safe.xMin + 20f, safe.xMax - width - 20f);
            float y = Mathf.Clamp(UiScale.Height * 0.5f - height * 0.5f, safe.yMin + 20f, safe.yMax - height - 20f);

            var panel = new Rect(x, y, width, height);
            UiTheme.DrawPanel(panel, true, true);

            float pad = 24f * scale;
            float cursor = y + pad;

            DrawLabel(new Rect(x + pad, cursor, width - pad * 2f, 30f * scale),
                "랭킹", titleStyle, UiTheme.Cyan);
            cursor += 42f * scale;

            DrawBoardTabs(new Rect(x + pad, cursor, width - pad * 2f, 40f * scale), scale);
            cursor += 54f * scale;

            DrawBoardRows(new Rect(x + pad, cursor, width - pad * 2f,
                height - (cursor - y) - pad - 60f * scale), scale);

            if (UiInteraction.Button(
                    new Rect(x + pad, y + height - pad - 46f * scale, 160f * scale, 46f * scale),
                    "돌아가기", buttonStyle))
                view = LobbyView.Main;
        }

        void DrawBoardTabs(Rect area, float scale)
        {
            var boards = Leaderboard.Boards;
            float gap = 8f * scale;
            float tabWidth = (area.width - gap * (boards.Length - 1)) / boards.Length;

            for (int i = 0; i < boards.Length; i++)
                DrawBoardTab(new Rect(area.x + (tabWidth + gap) * i, area.y, tabWidth, area.height),
                    boards[i]);
        }

        void DrawBoardTab(Rect rect, int crewSize)
        {
            bool active = boardCrew == crewSize;
            if (UiInteraction.Button(rect, Leaderboard.DisplayName(crewSize),
                    active ? primaryButtonStyle : buttonStyle) && !active)
                RequestBoard(crewSize);
        }

        void DrawBoardRows(Rect area, float scale)
        {
            if (boardLoading)
            {
                DrawLabel(area, "불러오는 중…", labelStyle, UiTheme.MutedText);
                return;
            }

            if (boardLoaded && boardRows.Length == 0)
            {
                // **아직 아무도 못 깼다는 것도 정보다.** "기록 없음" 으로 끝내면
                // 고장인지 비어 있는지 알 수 없다.
                DrawLabel(area,
                    boardCrew == 1
                        ? "아직 혼자 깬 사람이 없어요.\n첫 기록의 주인이 되어 보세요."
                        : $"아직 {boardCrew}인으로 깬 팀이 없어요.\n첫 기록의 주인이 되어 보세요.",
                    labelStyle, UiTheme.MutedText);
                return;
            }

            if (!boardLoaded)
            {
                DrawLabel(area, "랭킹을 불러오지 못했어요.", labelStyle, UiTheme.MutedText);
                return;
            }

            float rowHeight = 38f * scale;
            float rankWidth = 48f * scale;
            float timeWidth = 96f * scale;

            for (int i = 0; i < boardRows.Length; i++)
            {
                var row = boardRows[i];
                float rowY = area.y + i * rowHeight;
                if (rowY + rowHeight > area.yMax) break;

                var tint = row.rank == 1 ? UiTheme.Cyan : UiTheme.Text;

                DrawLabel(new Rect(area.x, rowY, rankWidth, rowHeight),
                    row.rank.ToString(), labelStyle, tint);

                // 협동은 닉네임이 여럿이라 칸을 넘길 수 있다. 자르는 것은 스타일의
                // Clipping 에 맡기고, 여기서 줄이지 않는다 — 누가 함께했는지가
                // 이 표의 내용이다.
                DrawLabel(new Rect(area.x + rankWidth, rowY,
                        area.width - rankWidth - timeWidth, rowHeight),
                    row.crewNames, labelStyle, tint);

                DrawLabel(new Rect(area.xMax - timeWidth, rowY, timeWidth, rowHeight),
                    Leaderboard.FormatTime(row.clearMillis), labelStyle, tint);
            }
        }
    }
}
