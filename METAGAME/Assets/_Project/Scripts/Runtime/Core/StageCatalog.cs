using System.Collections.Generic;
using UnityEngine;

namespace Metagame.Core
{
    /// <summary>
    /// 서버가 말하는 <c>stageId</c> 와 유니티 씬 이름을 잇는 <b>한 곳</b> (2맵 준비).
    ///
    /// <para><b>왜 만들었나.</b> 맵이 하나뿐인 동안 씬 이름 <c>"Stage1_Astra"</c> 는
    /// <see cref="Metagame.UI"/> 의 전환 코드, 개발자 단축키, 빌드 씬 목록, 빌드 설정 에셋에
    /// 각각 문자열로 박혀 있었다. 맵을 하나 더하면 <b>그 자리를 전부 찾아 고쳐야 하고</b>,
    /// 빌드 목록에 새 씬을 빠뜨리면 에디터에서는 멀쩡하다가 빌드에서만 검은 화면이 된다 —
    /// 컴파일은 통과하므로 아무도 미리 알려주지 않는다.</para>
    ///
    /// <para><b>서버가 정하고 여기서 옮긴다.</b> 어느 맵을 여는지는 방이 정하며
    /// (백엔드 <c>Room.stageId</c>), 클라이언트는 <c>GAME_STARTED</c>·<c>ROOM_STATE</c> 로
    /// 받은 <c>stageId</c> 를 이 표에 넣어 씬 이름을 얻는다. 클라이언트가 스스로 씬을
    /// 고르면 재입장한 사람이 다른 맵을 여는 날이 온다.</para>
    ///
    /// <para><b>맵을 더할 때 고칠 곳은 <see cref="Stages"/> 하나다.</b> 빌드 씬 목록
    /// (<c>SpaceBuild</c>)도 이 표에서 읽는다.</para>
    /// </summary>
    public static class StageCatalog
    {
        /// <summary>방이 맵을 고르지 않았을 때의 맵. 백엔드 <c>Room.DEFAULT_STAGE_ID</c> 와 같은 값이어야 한다.</summary>
        public const string DefaultStageId = "stage1";

        /// <summary>
        /// 기본 맵의 씬 이름.
        ///
        /// <para><c>const</c> 인 이유는 <b>선택적 매개변수의 기본값으로 쓰이기 때문</b>이다 —
        /// C# 은 기본값에 컴파일 타임 상수만 허용하므로 메서드 호출로는 바꿀 수 없다.</para>
        /// </summary>
        public const string DefaultStageScene = Stage1SceneName;

        public const string Stage1SceneName = "Stage1_Astra";
        public const string Stage1EndingSceneName = "Stage1_Ending";
        public const string Stage2SceneName = "Stage2_Earth";

        /// <summary>씬 파일이 사는 폴더. 빌드 목록은 이 경로로 씬을 찾는다.</summary>
        public const string SceneFolder = "Assets/Scenes/";

        public readonly struct Stage
        {
            public Stage(string id, string sceneName, string endingSceneName)
            {
                Id = id;
                SceneName = sceneName;
                EndingSceneName = endingSceneName;
            }

            /// <summary>서버가 쓰는 식별자. 씬 이름과 다르다 — 씬 이름은 바뀌어도 이 값은 유지한다.</summary>
            public string Id { get; }

            public string SceneName { get; }

            /// <summary>장 완료 연출 씬. 없는 맵이면 <c>null</c>.</summary>
            public string EndingSceneName { get; }

            public string ScenePath => SceneFolder + SceneName + ".unity";

            public string EndingScenePath =>
                string.IsNullOrEmpty(EndingSceneName) ? null : SceneFolder + EndingSceneName + ".unity";
        }

        /// <summary>
        /// 맵 목록. <b>맵을 더할 때 여기에 한 줄을 더한다.</b>
        /// </summary>
        static readonly Stage[] Stages =
        {
            new Stage(DefaultStageId, Stage1SceneName, Stage1EndingSceneName),
            new Stage("stage2", Stage2SceneName, null),
        };

        public static IReadOnlyList<Stage> All => Stages;

        public static bool TryFind(string stageId, out Stage stage)
        {
            if (!string.IsNullOrEmpty(stageId))
            {
                for (int i = 0; i < Stages.Length; i++)
                {
                    if (string.Equals(Stages[i].Id, stageId, System.StringComparison.OrdinalIgnoreCase))
                    {
                        stage = Stages[i];
                        return true;
                    }
                }
            }

            stage = Stages[0];
            return false;
        }

        /// <summary>
        /// <c>stageId</c> 에 해당하는 씬 이름. <b>모르는 맵이면 기본 맵으로 떨어진다.</b>
        ///
        /// <para>모르는 값에 빈 문자열을 돌려주면 씬 로딩이 조용히 실패해 로딩창에서 멈춘다.
        /// 그보다는 기본 맵이라도 열고 <b>경고를 남기는 쪽</b>이 낫다 — 서버가 우리보다
        /// 새 맵을 먼저 아는 경우(클라이언트가 구버전)가 실제로 생긴다.</para>
        /// </summary>
        public static string SceneNameOf(string stageId)
        {
            if (TryFind(stageId, out var stage))
            {
                return stage.SceneName;
            }

            Debug.LogWarning($"[Stage] 모르는 stageId → 기본 맵으로 연다: \"{stageId}\" → {stage.SceneName}");
            return stage.SceneName;
        }

        /// <summary>이 씬이 플레이 가능한 맵 씬인가. 로비·커스터마이저·엔딩 씬과 구분한다.</summary>
        public static bool IsStageScene(string sceneName)
        {
            for (int i = 0; i < Stages.Length; i++)
            {
                if (string.Equals(Stages[i].SceneName, sceneName, System.StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
