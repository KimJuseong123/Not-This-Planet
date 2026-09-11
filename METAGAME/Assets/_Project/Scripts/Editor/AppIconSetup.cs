using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Metagame.EditorTools
{
    /// <summary>
    /// 앱 아이콘을 <c>PlayerSettings</c> 에 지정한다(S15P21B202-431).
    ///
    /// <b>왜 도구로 남기는가.</b> 아이콘은 한 번 지정하면 끝인 설정이라 손으로 눌러도
    /// 된다. 그런데 <c>ProjectSettings.asset</c> 의 아이콘 항목은 <b>텍스처 GUID 를
    /// 크기별 슬롯에 적는 구조</b>라, 파일을 직접 고치면 GUID 한 글자만 틀려도 조용히
    /// 빈 슬롯이 된다 — 그리고 그 결과는 빌드해 보기 전에는 보이지 않는다.
    /// 여기를 거치면 유니티가 슬롯을 채우므로 그 실수가 없고, 아이콘을 바꿀 때
    /// 같은 방법을 다시 쓸 수 있다.
    ///
    /// <code>
    /// Unity.exe -batchmode -quit -projectPath METAGAME \
    ///   -executeMethod Metagame.EditorTools.AppIconSetup.ApplyBatch
    /// </code>
    /// </summary>
    public static class AppIconSetup
    {
        /// <summary>아이콘 원본. 정사각형 PNG 여야 한다 — 아니면 유니티가 늘린다.</summary>
        public const string IconPath = "Assets/_Project/UI/AppIcon.png";

        /// <summary>
        /// 배치 실행용. 실패하면 <b>종료 코드 1</b> 로 끝낸다 — 그래야 CI 나 스크립트가
        /// 실패를 알아챈다.
        ///
        /// <b>메뉴에서 부르는 <see cref="Apply"/> 와 분리한 이유.</b> 예전에는 하나였고
        /// 원본을 못 찾으면 <c>EditorApplication.Exit(1)</c> 을 불렀다. 그러면 메뉴에서
        /// 누른 사람의 <b>에디터가 통째로 닫혀 저장하지 않은 작업이 날아간다</b>
        /// (!339 AI 리뷰가 짚었다). 배치에서만 종료하고, 사람이 누를 때는 로그만 남긴다.
        /// </summary>
        public static void ApplyBatch()
        {
            if (!Apply())
            {
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// 아이콘을 지정한다. 성공하면 참.
        /// <b>여기서는 에디터를 종료하지 않는다</b> — 위 <see cref="ApplyBatch"/> 참고.
        /// </summary>
        [MenuItem("Tools/Space/앱 아이콘 지정")]
        public static bool Apply()
        {
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            if (icon == null)
            {
                Debug.LogError($"[AppIcon] 원본을 찾지 못했다: {IconPath}");
                return false;
            }

            if (icon.width != icon.height)
                Debug.LogWarning($"[AppIcon] 정사각형이 아니다({icon.width}x{icon.height}) — " +
                                 "유니티가 늘려 찌그러진다.");

            // 기본 아이콘. 플랫폼별로 따로 지정하지 않으면 모든 대상이 이것을 쓴다.
            // 지금 빌드 대상은 StandaloneWindows64 하나뿐이다(SpaceBuild).
            SetAll(NamedBuildTarget.Unknown, icon);

            // 윈도우 칸도 같은 그림으로 채운다. 기본 아이콘만 두면 유니티 버전에 따라
            // exe 에 굽는 단계에서 비어 보이는 경우가 있어, 명시해 두는 편이 안전하다.
            SetAll(NamedBuildTarget.Standalone, icon);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Report();
            return true;
        }

        /// <summary>
        /// 그 대상이 요구하는 모든 크기 슬롯을 같은 그림으로 채운다.
        ///
        /// <c>IconKind.Any</c> 하나만 쓴다. 여러 종류(Settings · Notification · Store 등)는
        /// 모바일 쪽 개념이고 그것은 <c>SetPlatformIcons</c> 가 다룬다 — 데스크톱에서
        /// <c>GetSupportedIconKinds</c> 를 돌려 보면 <c>PlatformIconKind</c> 라
        /// 여기 <c>IconKind</c> 와 타입부터 다르다.
        /// </summary>
        static void SetAll(NamedBuildTarget target, Texture2D icon)
        {
            var sizes = PlayerSettings.GetIconSizes(target, IconKind.Any);
            if (sizes == null || sizes.Length == 0)
            {
                Debug.LogWarning($"[AppIcon] {target.TargetName} 에는 아이콘 슬롯이 없다.");
                return;
            }

            var textures = new Texture2D[sizes.Length];
            for (var i = 0; i < textures.Length; i++) textures[i] = icon;

            PlayerSettings.SetIcons(target, textures, IconKind.Any);
            Debug.Log($"[AppIcon] {target.TargetName} · 슬롯 {sizes.Length}칸 · " +
                      $"크기 {string.Join("/", sizes)}");
        }

        /// <summary>지금 무엇이 들어가 있는지 찍는다. 배치 로그로 확인하는 용도다.</summary>
        public static void Report()
        {
            var lines = new List<string>();

            foreach (var target in new[] { NamedBuildTarget.Unknown, NamedBuildTarget.Standalone })
            {
                var icons = PlayerSettings.GetIcons(target, IconKind.Any);
                var filled = 0;
                for (var i = 0; i < icons.Length; i++)
                    if (icons[i] != null) filled++;

                lines.Add($"  {target.TargetName,-12} {filled}/{icons.Length} 칸" +
                          (filled > 0 ? $" · {icons[0].name}" : " · 비어 있음"));
            }

            Debug.Log("[AppIcon] 현재 설정\n" + string.Join("\n", lines));
        }
    }
}
