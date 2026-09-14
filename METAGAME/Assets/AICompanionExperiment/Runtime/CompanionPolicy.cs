using System;
using System.IO;
using UnityEngine;

namespace Metagame.AIExperiment
{
    public enum CompanionOrder : byte { Wait, Follow, Cancel, PullPin }

    /// <summary>The model may choose an intent, never code, transforms or game operations.</summary>
    public static class CompanionPolicy
    {
        public static bool TryParse(string action, out CompanionOrder order)
        {
            order = CompanionOrder.Wait;
            switch (action)
            {
                case "follow": order = CompanionOrder.Follow; return true;
                case "wait": return true;
                case "cancel": order = CompanionOrder.Cancel; return true;
                case "pull_pin": order = CompanionOrder.PullPin; return true;
                default: return false;
            }
        }

        public static string Label(string action) => action switch
        {
            "follow" => "따라오기", "wait" => "대기", "cancel" => "중단",
            "pull_pin" => "고정못 뽑기", "clarify" => "확인 필요", _ => "실행할 요청 없음"
        };

        public static byte[] EncodeWave(float[] samples, int channels, int sampleRate)
        {
            if (samples == null || channels < 1 || sampleRate < 1 || samples.Length % channels != 0)
                throw new ArgumentException("Invalid PCM audio.");
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            int bytes = samples.Length * 2;
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + bytes);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
            writer.Write((short)1); writer.Write((short)channels); writer.Write(sampleRate);
            writer.Write(sampleRate * channels * 2); writer.Write((short)(channels * 2)); writer.Write((short)16);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(bytes);
            foreach (float sample in samples) writer.Write((short)Mathf.RoundToInt(Mathf.Clamp(sample, -1, 1) * 32767));
            return stream.ToArray();
        }
    }
}
