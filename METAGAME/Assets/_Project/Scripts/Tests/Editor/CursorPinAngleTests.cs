using Metagame.UI;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 커서 핀을 눕힌 것을 검증한다 (S15P21B202-535).
    ///
    /// <para><b>무엇이 문제였나.</b> 커서가 지도 핀이라 hotspot 이 <b>아래쪽 뾰족한 끝</b>
    /// 이었다 — 손이 가리키는 점 <b>위로</b> 그림이 40px 서 있어서, 목록이나 버튼을 고를 때
    /// 무엇을 누르는지 가렸다. 토끼 귀 때문에 세로로 더 길다.</para>
    ///
    /// <para><b>PNG 는 건드리지 않는다.</b> 같은 그림을 <c>TeamPingHud</c> 가 쓰는데, 그쪽은
    /// 화면 밖 대상을 가리킬 때 <b>스스로 회전</b>한다(<c>GUIUtility.RotateAroundPivot</c>).
    /// 핀의 끝이 방향 지시자라, 원본을 돌리면 가리키는 각도가 전부 어긋난다.</para>
    /// </summary>
    public sealed class CursorPinAngleTests
    {
        const int Size = LocalCursorFeedback.CursorSize;
        const float Length = 35f;
        const float Width = 21f;

        static Texture2D Marker()
        {
            var source = Resources.Load<Texture2D>("PingMarker");
            Assert.IsNotNull(source, "PingMarker 를 Resources 에서 찾지 못했다.");
            Assert.IsTrue(source.isReadable, "픽셀을 읽을 수 있어야 커서를 만든다.");
            return source;
        }

        /// <summary>그려진 픽셀의 테두리 상자. 없으면 전부 -1 이다.</summary>
        static RectInt Opaque(Texture2D texture)
        {
            int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
            var pixels = texture.GetPixels32();
            for (int y = 0; y < texture.height; y++)
            for (int x = 0; x < texture.width; x++)
            {
                if (pixels[y * texture.width + x].a < 40) continue;
                minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
                minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
            }
            return maxX < 0 ? new RectInt(-1, -1, 0, 0)
                            : new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }

        // ── 방향 ────────────────────────────────────────────────

        /// <summary><b>이 검증이 이 이슈의 핵심이다.</b> 끝이 왼쪽 위로 가야 한다.</summary>
        [Test]
        public void ThePinLeansSoItsTipPointsUpAndLeft()
        {
            var axis = LocalCursorFeedback.PinAxis(LocalCursorFeedback.PinAngleDegrees);

            // 텍스처 좌표는 y 가 위쪽이다. 몸이 오른쪽 아래로 눕는다 = (+x, -y).
            Assert.Greater(axis.x, 0.3f, "몸이 오른쪽으로 뻗어야 한다.");
            Assert.Less(axis.y, -0.3f, "몸이 화면 아래쪽으로 뻗어야 한다.");
        }

        [Test]
        public void ZeroDegreesIsTheOldUprightPin()
        {
            var axis = LocalCursorFeedback.PinAxis(0f);

            Assert.AreEqual(0f, axis.x, 1e-4f);
            Assert.AreEqual(1f, axis.y, 1e-4f, "0° 면 끝에서 위로 — 예전과 같다.");
        }

        /// <summary>
        /// <b>축·수직축이 회전이어야 한다. 좌우 반전이면 안 된다</b> (!449 AI 리뷰).
        ///
        /// <para>수직축을 <c>(-axis.y, axis.x)</c> 로 두면 행렬식이 <c>-1</c> 이라
        /// <b>회전이 아니라 거울</b>이 된다. 지금 <c>PingMarker</c> 는 좌우가 완벽
        /// 대칭이라(실측: 비대칭 픽셀 0/88763) <b>눈으로는 절대 못 잡는다.</b></para>
        /// </summary>
        [Test]
        public void TheTransformIsARotationNotAMirror()
        {
            foreach (var degrees in new[] { 0f, 45f, 135f, 200f })
            {
                var axis = LocalCursorFeedback.PinAxis(degrees);
                var cross = LocalCursorFeedback.PinCross(axis);

                float determinant = cross.x * axis.y - cross.y * axis.x;
                Assert.AreEqual(1f, determinant, 1e-4f,
                    $"{degrees}° 에서 행렬식이 {determinant:F2} 다 — -1 이면 좌우가 뒤집힌다.");
            }
        }

        /// <summary>
        /// <b>말로 두지 않고 픽셀로 잰다.</b> "각도 0 이면 예전 식과 같다" 는 주석을
        /// 달아 놓고 실제로는 좌우가 뒤집혀 있었다 — 벡터만 재는 검증은 그것을
        /// 통과시켰다 (!449 AI 리뷰).
        ///
        /// <para>예전 식을 여기서 그대로 다시 계산해 0° 결과와 <b>한 픽셀씩</b> 견준다.</para>
        /// </summary>
        [Test]
        public void ZeroDegreesMatchesTheOldMappingPixelForPixel()
        {
            const int oldSize = 40;
            var source = Marker();
            var texture = LocalCursorFeedback.CreateCursorTexture(source, Color.white, false,
                                                                  0f, oldSize, out var hotspot);
            try
            {
                Assert.AreEqual(oldSize / 2f, hotspot.x, 0.51f, "예전 hotspot 은 가로 절반이었다.");
                Assert.AreEqual(oldSize - 2f, hotspot.y, 1.01f, "예전 hotspot 은 세로 맨 아래였다.");

                int raw = Diff(texture, OldMapping(source, oldSize, oldSize * 0.5f), oldSize);
                int centered = Diff(texture, OldMapping(source, oldSize, (oldSize - 1) * 0.5f), oldSize);
                Debug.Log($"[프로브] 예전 식 그대로 {raw} 픽셀 · 반 픽셀 보정 {centered} 픽셀");

                Assert.LessOrEqual(centered, oldSize * oldSize / 100,
                    $"0° 결과가 예전 식과 {centered} 픽셀 다르다 — 좌우가 뒤집혔는지 본다.");
            }
            finally { Object.DestroyImmediate(texture); }
        }

        /// <summary>이 이슈 이전의 UV 계산. 견주기 위해서만 둔다.</summary>
        /// <summary>
        /// 실루엣만 견준다. 테두리 픽셀(어두운 색)은 <b>예전 식 재현본에 없는 과정</b>이라
        /// 빼야 뜻이 있다 — 흰색으로 구우므로 <c>r</c> 로 가른다.
        /// </summary>
        static int Diff(Texture2D texture, float[] expected, int size)
        {
            int n = 0;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var pixel = texture.GetPixel(x, y);
                bool drawn = pixel.r > 0.5f && pixel.a > 0.5f;   // 테두리가 아니라 그림
                if (drawn != expected[y * size + x] > 0.5f) n++;
            }
            return n;
        }

        static float[] OldMapping(Texture2D source, int size, float centerX)
        {
            int minX = source.width, minY = source.height, maxX = 0, maxY = 0;
            var pixels = source.GetPixels32();
            for (int y = 0; y < source.height; y++)
            for (int x = 0; x < source.width; x++)
            {
                if (pixels[y * source.width + x].a < 128) continue;
                minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
                minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
            }
            var alpha = new float[size * size];
            float height = 35f;
            float width = height * (maxX - minX + 1f) / Mathf.Max(1f, maxY - minY + 1f);
            for (int y = 2; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x - centerX) / width + 0.5f;
                float v = (y - 2f) / height;
                if (u < 0f || u > 1f || v > 1f) continue;
                alpha[y * size + x] = source.GetPixelBilinear(
                    (minX + u * (maxX - minX)) / source.width,
                    (minY + v * (maxY - minY)) / source.height).a;
            }
            return alpha;
        }

        [Test]
        public void TheCrossAxisIsPerpendicularAndUnitLength()
        {
            foreach (var degrees in new[] { 0f, 45f, 135f, 200f })
            {
                var axis = LocalCursorFeedback.PinAxis(degrees);
                var cross = LocalCursorFeedback.PinCross(axis);

                Assert.AreEqual(0f, Vector2.Dot(axis, cross), 1e-4f, $"{degrees}° 에서 수직이 아니다.");
                Assert.AreEqual(1f, cross.magnitude, 1e-3f);
                Assert.AreEqual(1f, axis.magnitude, 1e-3f);
            }
        }

        // ── 클릭점 ──────────────────────────────────────────────

        /// <summary>
        /// <b>hotspot 은 왼쪽 위 기준이고 텍스처는 아래가 0 이다.</b> 뒤집기를 빠뜨리면
        /// 클릭점이 위아래로 뒤집힌다 — 눈으로는 알아채기 어렵다.
        /// </summary>
        [Test]
        public void TheHotspotFlipsFromTextureToScreenCoordinates()
        {
            var hotspot = LocalCursorFeedback.Hotspot(new Vector2(6f, 40f), 48);

            Assert.AreEqual(6f, hotspot.x, 1e-4f, "가로는 그대로다.");
            Assert.AreEqual(7f, hotspot.y, 1e-4f, "세로는 뒤집힌다 — 48-1-40 = 7.");
        }

        /// <summary>클릭점은 그림의 <b>왼쪽 위</b>에 있어야 한다 — 보통 커서와 같은 배치.</summary>
        [Test]
        public void TheClickPointSitsAtTheTopLeftOfTheDrawing()
        {
            var texture = LocalCursorFeedback.CreateCursorTexture(Marker(), Color.white, false,
                                                                  out var hotspot);
            try
            {
                var box = Opaque(texture);
                Assert.Greater(box.width, 0, "아무것도 안 그려졌다.");

                // 텍스처 좌표로 되돌려 견준다.
                float tipY = Size - 1f - hotspot.y;
                Assert.Less(hotspot.x, box.xMin + box.width * 0.4f,
                    "클릭점이 그림의 왼쪽 편에 있어야 한다.");
                Assert.Greater(tipY, box.yMin + box.height * 0.6f,
                    "클릭점이 그림의 위쪽에 있어야 한다.");
            }
            finally { Object.DestroyImmediate(texture); }
        }

        /// <summary>
        /// <b>클릭하는 그 픽셀에 그림이 있어야 한다.</b> 끝이 비어 있으면 "어디를 누르는지"가
        /// 다시 짐작이 된다 — 이 이슈가 고치려는 것이 정확히 그것이다.
        /// </summary>
        [Test]
        public void TheClickedPixelIsActuallyDrawn()
        {
            var texture = LocalCursorFeedback.CreateCursorTexture(Marker(), Color.white, false,
                                                                  out var hotspot);
            try
            {
                int x = Mathf.RoundToInt(hotspot.x);
                int y = Mathf.RoundToInt(Size - 1f - hotspot.y);

                float best = 0f;
                for (int dy = -2; dy <= 2; dy++)
                for (int dx = -2; dx <= 2; dx++)
                {
                    int sx = Mathf.Clamp(x + dx, 0, Size - 1), sy = Mathf.Clamp(y + dy, 0, Size - 1);
                    best = Mathf.Max(best, texture.GetPixel(sx, sy).a);
                }
                Assert.Greater(best, 0.25f, "클릭점 둘레에 그림이 없다 — 끝이 그 자리에 없다.");
            }
            finally { Object.DestroyImmediate(texture); }
        }

        // ── 잘리지 않는가 ───────────────────────────────────────

        /// <summary>
        /// 눕히면 대각선으로 서서 <b>40px 에는 안 들어간다</b>. 잘리면 귀나 끝이 사라진다.
        /// </summary>
        [Test]
        public void TheDrawingDoesNotTouchTheEdge()
        {
            foreach (var pressed in new[] { false, true })
            {
                var texture = LocalCursorFeedback.CreateCursorTexture(Marker(), Color.white, pressed);
                try
                {
                    var box = Opaque(texture);
                    Assert.Greater(box.xMin, 0, $"pressed={pressed}: 왼쪽이 잘렸다.");
                    Assert.Greater(box.yMin, 0, $"pressed={pressed}: 아래가 잘렸다.");
                    Assert.Less(box.xMax, Size, $"pressed={pressed}: 오른쪽이 잘렸다.");
                    Assert.Less(box.yMax, Size, $"pressed={pressed}: 위가 잘렸다.");
                }
                finally { Object.DestroyImmediate(texture); }
            }
        }

        /// <summary>
        /// 눕힌 핀이 <b>정말 대각선으로</b> 서는가. 세로로 길쭉하면 안 돌아간 것이다.
        /// </summary>
        [Test]
        public void TheDrawingIsDiagonalNotUpright()
        {
            var texture = LocalCursorFeedback.CreateCursorTexture(Marker(), Color.white, false);
            try
            {
                var box = Opaque(texture);
                float aspect = box.width / (float)box.height;

                Assert.Greater(aspect, 0.7f,
                    $"세로로 길쭉하다({box.width}x{box.height}) — 눕지 않았다.");
            }
            finally { Object.DestroyImmediate(texture); }
        }

        [Test]
        public void TheTipIsPushedInByTheMarginOnly()
        {
            var tip = LocalCursorFeedback.PinTip(LocalCursorFeedback.PinAngleDegrees,
                                                 Length, Width, Size);

            Assert.GreaterOrEqual(tip.x, 0f);
            Assert.LessOrEqual(tip.x, Size - 1f);
            Assert.GreaterOrEqual(tip.y, 0f);
            Assert.LessOrEqual(tip.y, Size - 1f);
        }

        /// <summary>각도를 바꿔도 칸 안에 들어가야 한다 — 135 만 맞춰 놓은 것이 아니다.</summary>
        [Test]
        public void AnyAngleStillFitsInsideTheCursor()
        {
            foreach (var degrees in new[] { 0f, 45f, 90f, 135f, 180f, 225f, 315f })
            {
                var axis = LocalCursorFeedback.PinAxis(degrees);
                var cross = LocalCursorFeedback.PinCross(axis);
                var tip = LocalCursorFeedback.PinTip(degrees, Length, Width, Size);

                foreach (var along in new[] { 0f, Length })
                foreach (var side in new[] { -Width * 0.5f, Width * 0.5f })
                {
                    var corner = tip + axis * along + cross * side;
                    Assert.GreaterOrEqual(corner.x, -0.01f, $"{degrees}° 에서 왼쪽으로 넘쳤다.");
                    Assert.GreaterOrEqual(corner.y, -0.01f, $"{degrees}° 에서 아래로 넘쳤다.");
                    Assert.LessOrEqual(corner.x, Size - 0.99f, $"{degrees}° 에서 오른쪽으로 넘쳤다.");
                    Assert.LessOrEqual(corner.y, Size - 0.99f, $"{degrees}° 에서 위로 넘쳤다.");
                }
            }
        }

        // ── 남의 기능을 건드리지 않았는가 ───────────────────────

        /// <summary>
        /// <b>월드 핑은 원본을 그대로 쓴다.</b> 커서를 만드는 쪽에서만 돌려야,
        /// 화면 밖 핑이 가리키는 각도가 어긋나지 않는다.
        /// </summary>
        [Test]
        public void TheWorldPingStillReadsTheUnrotatedSource()
        {
            var hud = TestSource.Blank(TestSource.Read("Runtime/UI/TeamPingHud.cs"));

            Assert.That(hud, Does.Contain("Resources.Load<Texture2D>"),
                "월드 핑은 원본을 직접 읽는다.");
            Assert.That(hud, Does.Not.Contain("PinAngleDegrees"),
                "핑 쪽에 각도가 새어 들어가면 방향 지시가 어긋난다.");
            Assert.That(hud, Does.Contain("RotateAroundPivot"),
                "화면 밖 핑이 스스로 도는 것은 그대로 남아야 한다.");
        }

        /// <summary>
        /// <b>그린 hotspot 을 그대로 넘겨야 한다.</b> 예전에는 좌표가
        /// <c>SetCursor</c> 자리에 <b>따로 적혀</b> 있었다 — 각도를 바꾸면 끝이 가는 자리도
        /// 달라지므로, 그렇게 두면 그림과 클릭점이 조용히 어긋난다.
        /// </summary>
        [Test]
        public void TheCursorIsSetWithTheHotspotItWasDrawnWith()
        {
            var source = TestSource.Blank(TestSource.Read("Runtime/UI/LocalCursorFeedback.cs"));
            var method = TestSource.Method(source, "void LateUpdate()");

            Assert.That(method, Does.Contain("_normalHotspot"),
                "그린 쪽이 돌려준 hotspot 을 써야 한다.");
            Assert.That(method, Does.Not.Contain("CursorSize - 2f"),
                "좌표를 여기 따로 적으면 각도와 어긋난다.");
        }

        /// <summary>
        /// <b>호스트만이 아니라 모두에게 적용된다</b> (사용자 규칙 · 멀티 전제).
        ///
        /// <para>커서는 각자 기계의 OS 호출(<c>Cursor.SetCursor</c>)이라 <b>보낼 값이
        /// 없다</b> — 이번 주의 <c>-505</c>·<c>-511</c>·<c>-513</c>·<c>-519</c> 와 다른
        /// 종류다. 대신 <b>설치에 호스트 조건이 붙지 않는 것</b>이 지켜야 할 전부다.</para>
        ///
        /// <para>색은 각 기계가 <b>자기 NetPlayer</b>에서 읽으므로 참가자도 제 색을 받는다.
        /// 여기 쓰이는 <c>isLocalPlayer</c> 는 "여럿 중 내 것을 고른다" 는 뜻이고,
        /// 권한 게이트가 아니다.</para>
        /// </summary>
        [Test]
        public void EveryMachineGetsItsOwnCursorNotJustTheHost()
        {
            var source = TestSource.Blank(TestSource.Read("Runtime/UI/LocalCursorFeedback.cs"));
            var install = TestSource.Method(source, "void Install()");

            Assert.That(install, Does.Not.Contain("isServer"),
                "설치에 서버 조건이 붙으면 참가자 화면은 기본 커서로 남는다.");
            Assert.That(install, Does.Not.Contain("NetworkServer"),
                "설치는 네트워크 상태를 보지 않는다.");

            var update = TestSource.Method(source, "void LateUpdate()");
            Assert.That(update, Does.Not.Contain("isServer"),
                "커서를 세우는 자리에도 서버 조건이 없어야 한다.");
        }

        [Category("Heavy")]
        [Test]
        public void MakingTheCursorNeverChangesTheSource()
        {
            var source = Marker();
            var before = source.GetPixels32();

            var texture = LocalCursorFeedback.CreateCursorTexture(source, Color.cyan, false);
            try { CollectionAssert.AreEqual(before, source.GetPixels32()); }
            finally { Object.DestroyImmediate(texture); }
        }
    }
}
