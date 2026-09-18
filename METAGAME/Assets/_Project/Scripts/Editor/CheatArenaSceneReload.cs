using Metagame.Gameplay.Cheats;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Metagame.EditorTools
{
    /// <summary>
    /// 치트 시험장의 "씬 다시 불러오기"(F9)를 에디터 플레이 중에 가능하게 한다.
    ///
    /// <para>시험장 씬은 빌드 목록에 없다. <c>SceneManager.LoadScene</c> 은 플레이 모드에서도
    /// 빌드 목록에 있는 씬만 열지만, 에디터에는 <see cref="EditorSceneManager.LoadSceneInPlayMode"/>
    /// 가 따로 있다. 런타임 코드는 <c>UnityEditor</c> 를 참조하지 않으므로(작성 규칙 §1) 이쪽이
    /// <see cref="CheatArenaConsole.ReloadHook"/> 에 함수를 꽂아 준다.</para>
    ///
    /// <para><c>InitializeOnLoad</c> 라 에디터가 뜰 때와 도메인 리로드 뒤마다 다시 꽂힌다.
    /// 도메인 리로드를 끈 설정에서도 정적 필드가 남으므로 그대로 유지된다.</para>
    /// </summary>
    [InitializeOnLoad]
    public static class CheatArenaSceneReload
    {
        static CheatArenaSceneReload()
        {
            CheatArenaConsole.ReloadHook = Reload;
        }

        static bool Reload(string scenePath)
        {
            if (!Application.isPlaying || string.IsNullOrEmpty(scenePath)) return false;
            EditorSceneManager.LoadSceneInPlayMode(scenePath, new LoadSceneParameters(LoadSceneMode.Single));
            return true;
        }
    }
}
