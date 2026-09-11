using UnityEngine;
using UnityEngine.UI;

namespace Metagame.UI
{
    /// <summary>
    /// 파츠 측면 외곽선을 UI 메시로 그린다(S15P21B202-345).
    ///
    /// <b>왜 <see cref="Image"/> 를 쓰지 않는가.</b> <see cref="Image"/> 는 사각형이다.
    /// 예전 태블릿 실루엣은 손으로 적은 사각형 2~4개를 겹쳐 놓은 것이었고, 그래서
    /// 접시 굽·돔·첨탑이 계단으로 보였다. 기획 §11.1 이 태블릿을 "우주선 조립 상태표" 로
    /// 정했으므로 <b>어느 파츠인지 형태로 알아볼 수 있어야</b> 한다.
    ///
    /// 형태는 <see cref="Metagame.Gameplay.Parts.PartSilhouette"/> 가 갖고 있다 —
    /// 프리팹 메시를 40구간으로 잘라 재서 굳힌 표다.
    ///
    /// <b>한 인스턴스는 한 가지만 그린다.</b> 채움과 외곽선을 <see cref="Mode"/> 로 나눈
    /// 이유는 <see cref="Graphic.color"/> 가 하나뿐이기 때문이다 — 둘을 한 메시에 담으면
    /// 색을 따로 바꿀 수 없다. 태블릿은 "올라감(외곽선)" 과 "용접 완료(채움)" 를 색으로
    /// 구분하므로 둘이 따로 필요하다.
    /// </summary>
    /// <remarks>
    /// <b><see cref="RequireComponent"/> 를 직접 붙여야 한다.</b> 그 어트리뷰트는
    /// <b>상속되지 않는다</b> — <see cref="Graphic"/> 에 붙은 것은 <c>Graphic</c> 을 직접
    /// 넣을 때만 적용된다. <see cref="Image"/> 와 <see cref="Text"/> 는 각자 자기 클래스에
    /// 다시 붙여 두었고, 그래서 런타임 <c>AddComponent</c> 로도 잘 그려진다.
    ///
    /// 없으면 <b>메시는 정상으로 만들어지는데 화면에는 아무것도 안 나온다</b>
    /// (S15P21B202-345 에서 태블릿 실루엣이 그렇게 사라졌다).
    /// </remarks>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class PartSilhouetteGraphic : MaskableGraphic
    {
        public enum DrawMode
        {
            /// <summary>안을 채운다.</summary>
            Fill,

            /// <summary>테두리만 그린다.</summary>
            Outline,
        }

        [SerializeField] DrawMode mode = DrawMode.Fill;

        [SerializeField] float outlineWidth = 4f;

        float[] _left;
        float[] _right;

        public DrawMode Mode
        {
            get => mode;
            set
            {
                if (mode == value) return;
                mode = value;
                SetVerticesDirty();
            }
        }

        /// <summary>테두리 두께(픽셀). <see cref="DrawMode.Fill"/> 에서는 쓰이지 않는다.</summary>
        public float OutlineWidth
        {
            get => outlineWidth;
            set
            {
                if (Mathf.Approximately(outlineWidth, value)) return;
                outlineWidth = value;
                SetVerticesDirty();
            }
        }

        /// <summary>
        /// 그릴 외곽선을 정한다. 두 배열은 길이가 같아야 하고 <b>바닥이 0번</b>이다.
        /// 값은 가장 넓은 폭을 1 로 정규화한 좌우 끝이다.
        ///
        /// 배열을 복사하지 않는다 — <see cref="Metagame.Gameplay.Parts.PartSilhouette"/> 의
        /// 표는 읽기만 하는 정적 데이터다.
        /// </summary>
        public void SetProfile(float[] left, float[] right)
        {
            _left = left;
            _right = right;
            SetVerticesDirty();
        }

        /// <summary>형태가 정해져 그릴 것이 있다.</summary>
        public bool HasProfile =>
            _left != null && _right != null
            && _left.Length >= 2 && _left.Length == _right.Length;

        protected override void OnPopulateMesh(VertexHelper vh) => BuildMesh(vh);

        /// <summary>
        /// 외곽선을 메시로 만든다.
        ///
        /// <see cref="OnPopulateMesh"/> 가 <c>protected</c> 라 밖에서 부를 수 없어 갈라 뒀다.
        /// <see cref="VertexHelper"/> 는 공개 형식이므로 이것을 공개하는 데 드는 값은 없다.
        /// </summary>
        public void BuildMesh(VertexHelper vh)
        {
            if (vh == null) return;
            vh.Clear();
            if (!HasProfile) return;

            var rect = GetPixelAdjustedRect();
            var bands = _left.Length;
            var rows = bands + 2;

            // 구간의 값은 그 구간 <b>가운데</b> 높이의 폭으로 본다. 그래야 이웃 구간과
            // 이어 붙일 때 계단이 아니라 기울어진 변이 된다. 바닥과 꼭대기에는 같은 폭의
            // 행을 하나 더 두어 실제 경계에서 끊는다.
            for (var r = 0; r < rows - 1; r++)
            {
                var (ax, bx, ay) = Row(r, bands, rect);
                var (cx, dx, by) = Row(r + 1, bands, rect);

                if (mode == DrawMode.Fill) Quad(vh, ax, ay, bx, ay, dx, by, cx, by);
                else
                {
                    Stroke(vh, ax, ay, cx, by);   // 왼쪽 변
                    Stroke(vh, bx, ay, dx, by);   // 오른쪽 변
                }
            }

            if (mode != DrawMode.Outline) return;

            // 바닥과 꼭대기를 막는다. 그러지 않으면 좌우 변만 남아 열린 도형이 된다.
            var bottom = Row(0, bands, rect);
            var top = Row(rows - 1, bands, rect);
            Stroke(vh, bottom.Item1, bottom.Item3, bottom.Item2, bottom.Item3);
            Stroke(vh, top.Item1, top.Item3, top.Item2, top.Item3);
        }

        /// <summary>행 하나의 왼쪽 x · 오른쪽 x · y 를 픽셀로 준다.</summary>
        (float, float, float) Row(int row, int bands, Rect rect)
        {
            int index;
            float t;

            if (row == 0)
            {
                index = 0;
                t = 0f;
            }
            else if (row == bands + 1)
            {
                index = bands - 1;
                t = 1f;
            }
            else
            {
                index = row - 1;
                t = (index + 0.5f) / bands;
            }

            // 정규화 좌표는 모델 원점을 0 으로 보므로 0.5 를 더해 0~1 로 옮긴다.
            return (rect.xMin + (_left[index] + 0.5f) * rect.width,
                    rect.xMin + (_right[index] + 0.5f) * rect.width,
                    rect.yMin + t * rect.height);
        }

        void Quad(VertexHelper vh,
                  float x0, float y0, float x1, float y1,
                  float x2, float y2, float x3, float y3)
        {
            var start = vh.currentVertCount;
            var c = color;
            vh.AddVert(new Vector3(x0, y0), c, Vector2.zero);
            vh.AddVert(new Vector3(x1, y1), c, Vector2.zero);
            vh.AddVert(new Vector3(x2, y2), c, Vector2.zero);
            vh.AddVert(new Vector3(x3, y3), c, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }

        /// <summary>
        /// 선분 하나를 두께 있는 사각형으로 그린다.
        ///
        /// 이음매를 따로 처리하지 않고 <b>양 끝을 두께의 절반만큼 늘린다.</b> 꺾이는 곳에
        /// 생기는 빈틈이 그만큼 겹쳐 메워진다. 태블릿 크기에서 이음매를 정확히 맞추는
        /// 값어치는 없다.
        /// </summary>
        void Stroke(VertexHelper vh, float x0, float y0, float x1, float y1)
        {
            var dx = x1 - x0;
            var dy = y1 - y0;
            var length = Mathf.Sqrt(dx * dx + dy * dy);
            if (length <= 1e-4f) return;

            var half = outlineWidth * 0.5f;
            var ux = dx / length;
            var uy = dy / length;

            // 길이 방향으로 늘리고, 수직 방향으로 두께를 준다.
            x0 -= ux * half;
            y0 -= uy * half;
            x1 += ux * half;
            y1 += uy * half;

            var nx = -uy * half;
            var ny = ux * half;

            Quad(vh,
                 x0 + nx, y0 + ny,
                 x1 + nx, y1 + ny,
                 x1 - nx, y1 - ny,
                 x0 - nx, y0 - ny);
        }
    }
}
