using UnityEngine;

namespace Metagame.Gameplay
{
    /// <summary>외형 선택만 저장한다. 네트워크 입력도 동일한 범위로 정규화한다.</summary>
    public struct CustomizationData
    {
        const string SaveKey = "appearance.v1";
        public int Tint, Helmet, Badge;
        public static readonly string[] TintNames = { "기본", "민트", "라벤더", "살구", "하늘" };
        public static readonly string[] HelmetNames = { "없음", "탐사 안테나", "통신 헤드셋" };
        public static readonly string[] BadgeNames = { "없음", "탐사 배지", "쌍별 배지" };
        public static Color TintColor(int index)
        {
            switch (index)
            {
                case 1: return new Color(0.65f, 1f, 0.84f);
                case 2: return new Color(0.84f, 0.72f, 1f);
                case 3: return new Color(1f, 0.79f, 0.63f);
                case 4: return new Color(0.65f, 0.86f, 1f);
                default: return Color.white;
            }
        }
        public int Packed => Mathf.Clamp(Tint, 0, 4) | (Mathf.Clamp(Helmet, 0, 2) << 3)
            | (Mathf.Clamp(Badge, 0, 2) << 5);
        public static CustomizationData Decode(int value) => new CustomizationData
        {
            Tint = Mathf.Clamp(value & 7, 0, 4),
            Helmet = Mathf.Clamp((value >> 3) & 3, 0, 2),
            Badge = Mathf.Clamp((value >> 5) & 3, 0, 2)
        };
        public static CustomizationData Load() => Decode(PlayerPrefs.GetInt(SaveKey, 0));
        public void Save() { PlayerPrefs.SetInt(SaveKey, Packed); PlayerPrefs.Save(); }
    }
}
