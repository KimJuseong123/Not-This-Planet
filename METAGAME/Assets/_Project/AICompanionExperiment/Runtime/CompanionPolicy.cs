using System;
using System.IO;
using UnityEngine;

namespace Metagame.AIExperiment
{
    public enum CompanionOrder : byte { Wait, Follow, Cancel, PullPin, MoveToPin, PullNearbyPins,
        PickUpTool, PickUpBat, PickUpWelder, DropTool, HitTarget, DefendMeteors }

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
                case "move_to_pin": order = CompanionOrder.MoveToPin; return true;
                case "pull_nearby_pins": order = CompanionOrder.PullNearbyPins; return true;
                case "pickup_tool": order = CompanionOrder.PickUpTool; return true;
                case "pickup_bat": order = CompanionOrder.PickUpBat; return true;
                case "pickup_welder": order = CompanionOrder.PickUpWelder; return true;
                case "drop_tool": order = CompanionOrder.DropTool; return true;
                case "hit_target": order = CompanionOrder.HitTarget; return true;
                case "defend_meteors": order = CompanionOrder.DefendMeteors; return true;
                default: return false;
            }
        }

        public static string Label(string action) => action switch
        {
            "follow" => "따라오기", "wait" => "대기", "cancel" => "중단",
            "pull_pin" => "고정못 뽑기", "move_to_pin" => "고정못으로 이동",
            "pull_nearby_pins" => "주변 고정못 모두 뽑기",
            "pickup_tool" => "도구 줍기", "pickup_bat" => "몽둥이 줍기", "pickup_welder" => "용접건 줍기",
            "drop_tool" => "도구 내려놓기", "hit_target" => "연습용 공 타격", "defend_meteors" => "근거리 운석 방어",
            "clarify" => "확인 필요", _ => "실행할 요청 없음"
        };

        public static string ServiceError(long code) => code switch
        {
            404 => "서버 버전 불일치 · v4 서버를 다시 실행하세요",
            409 => "AI 서버가 다른 요청 처리 중 · 잠시 후 다시 요청하세요",
            0 => "AI 서버 연결 실패/시간 초과 · START.bat와 서버 로그를 확인하세요",
            503 => "로컬 모델 처리 실패 · AI 서버 로그를 확인하세요",
            _ => "AI 서버 오류 · HTTP " + code
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
