using System;
using System.Collections.Generic;
using UnityEngine;

namespace Metagame.UI
{
    /// <summary>부채꼴의 배경과 잔량을 한 이미지에 합성한다. 회전 GUI 사각형끼리 덮어쓰지 않는다.</summary>
    public sealed class WeldingGaugeTexture : IDisposable
    {
        const int Size = 256;
        public const float OuterRadiusRatio = 1.095f;
        static readonly Color32 Filled = new Color32(255, 217, 20, 255);
        static readonly Color32 Empty = new Color32(16, 13, 5, 210);
        readonly Color32[] pixels = new Color32[Size * Size];
        readonly int[] indices;
        readonly float[] thresholds;
        int lastStep = -1;
        public Texture2D Texture { get; }

        public WeldingGaugeTexture()
        {
            Texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                name = "Welding material sector",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            var active = new List<int>();
            var levels = new List<float>();
            float inner = 0.905f / OuterRadiusRatio;
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float px = (x + 0.5f) * 2f / Size - 1f;
                    float py = (y + 0.5f) * 2f / Size - 1f;
                    float radius = Mathf.Sqrt(px * px + py * py);
                    // HUD의 왼쪽 볼록 호와 같은 방향이 되도록 원본 부채꼴을 수평 반전한다.
                    float angle = Mathf.Atan2(py, -px) * Mathf.Rad2Deg;
                    if (radius < inner || radius > 1f || angle < -18f || angle > 48f) continue;
                    active.Add(y * Size + x);
                    levels.Add((angle + 18f) / 66f);
                }
            indices = active.ToArray();
            thresholds = levels.ToArray();
            SetFraction(1f);
        }

        public void SetFraction(float fraction)
        {
            int step = Mathf.RoundToInt(Mathf.Clamp01(fraction) * 1024f);
            if (step == lastStep) return;
            lastStep = step;
            for (int i = 0; i < indices.Length; i++)
                pixels[indices[i]] = thresholds[i] <= step / 1024f ? Filled : Empty;
            Texture.SetPixels32(pixels);
            Texture.Apply(false, false);
        }

        public void Dispose()
        {
            if (Texture == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(Texture);
            else UnityEngine.Object.DestroyImmediate(Texture);
        }
    }
}
