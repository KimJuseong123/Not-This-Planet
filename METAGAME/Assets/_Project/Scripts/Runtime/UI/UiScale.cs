using System;
using Metagame.Core;
using UnityEngine;

namespace Metagame.UI
{
    /// <summary>로컬 설정으로 텍스트와 컨트롤을 함께 확대하고 화면 앵커를 유지한다.</summary>
    public static class UiScale
    {
        static float drawingScale = 1f;

        public static float Value => GameSettingsRuntime.Instance?.Current?.display?.uiTextScale ?? 1f;
        public static float Width => Screen.width / drawingScale;
        public static float Height => Screen.height / drawingScale;
        public static Rect SafeArea
        {
            get
            {
                Rect area = Screen.safeArea;
                return new Rect(area.position / drawingScale, area.size / drawingScale);
            }
        }

        public static Vector3 ScreenPoint(Vector3 point) =>
            new Vector3(point.x / drawingScale, point.y / drawingScale, point.z);

        // 한 GUI 이벤트 도중 슬라이더가 변경되어도 좌표와 행렬은 같은 배율을 쓴다.
        public readonly struct Scope : IDisposable
        {
            readonly Matrix4x4 previousMatrix;
            readonly float previousScale;

            public Scope(float scale)
            {
                previousMatrix = GUI.matrix;
                previousScale = drawingScale;
                drawingScale = scale;
                GUI.matrix = previousMatrix * Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            }

            public void Dispose()
            {
                GUI.matrix = previousMatrix;
                drawingScale = previousScale;
            }
        }
    }
}
