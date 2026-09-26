using UnityEngine;

namespace Metagame.UI
{
    /// <summary>
    /// 화면에 그리는 작은 아이콘 — 원 · 테두리 · 마이크 (S15P21B202-746). 그림 파일을 두지 않고 처음 쓸 때 한 번 만든다.
    /// 가장자리는 4×4 표본으로 부드럽게 한다.
    /// </summary>
    public static class HudIcons
    {
        const int Size = 128;
        static Texture2D disc, ring, mic;

        public static Texture2D Disc => disc ??= Make("HUD disc", (x, y) => x * x + y * y <= 1f);

        /// <summary>테두리 두께는 반지름의 12%.</summary>
        public static Texture2D Ring => ring ??= Make("HUD ring", (x, y) => { float r = x * x + y * y; return r <= 1f && r >= .88f * .88f; });

        /// <summary>마이크 — 둥근 머리 · 받침 반원 · 대 · 밑판.</summary>
        public static Texture2D Mic => mic ??= Make("HUD mic", (x, y) =>
        {
            // 머리: 세로로 긴 캡슐(가로 반지름 .28, 위아래 .15 ~ .75)
            float cx = Mathf.Abs(x);
            bool head = cx <= .28f && y >= .15f && y <= .75f
                        || (x * x + (y - .75f) * (y - .75f)) <= .28f * .28f
                        || (x * x + (y - .15f) * (y - .15f)) <= .28f * .28f;
            // 받침 반원: 반지름 .45 의 아래쪽 반, 두께 .08
            float d = Mathf.Sqrt(x * x + (y - .15f) * (y - .15f));
            bool cradle = y <= .15f && d >= .41f && d <= .49f;
            bool stem = cx <= .05f && y <= -.30f && y >= -.62f;
            bool foot = cx <= .30f && y <= -.62f && y >= -.70f;
            return head || cradle || stem || foot;
        });

        static Texture2D Make(string name, System.Func<float, float, bool> inside)
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            { name = name, hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[Size * Size];
            for (int py = 0; py < Size; py++)
                for (int px = 0; px < Size; px++)
                {
                    int covered = 0;
                    for (int sy = 0; sy < 4; sy++)
                        for (int sx = 0; sx < 4; sx++)
                        {
                            float x = (px + (sx + .5f) / 4f) / Size * 2f - 1f;
                            float y = (py + (sy + .5f) / 4f) / Size * 2f - 1f;
                            if (inside(x, y)) covered++;
                        }
                    // 텍스처의 0행이 아래다 — y 도 아래에서 위로 커지므로 그대로 담는다.
                    pixels[py * Size + px] = new Color32(255, 255, 255, (byte)(covered * 255 / 16));
                }
            texture.SetPixels32(pixels); texture.Apply(false, true);
            return texture;
        }
    }
}
