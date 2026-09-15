using UnityEngine;

namespace Metagame.Gameplay
{
    /// <summary>외형 선택만 저장한다. 네트워크 입력도 동일한 범위로 정규화한다.</summary>
    public struct CustomizationData
    {
        const string SaveKey = "appearance.v2";
        const string LegacySaveKey = "appearance.v1";
        const int LegacyTintCount = 4;
        public int Tint, Helmet, Badge;
        public static readonly string[] TintNames = { "기본", "노랑", "라벤더", "분홍", "하늘" };
        public static readonly string[] HelmetNames = { "없음", "탐사 안테나", "통신 헤드셋" };
        public static readonly string[] BadgeNames = { "없음", "탐사 배지", "쌍별 배지" };

        static readonly Color[] TintColors =
        {
            Color.white,
            new Color32(0xF2, 0xFF, 0x9F, 0xFF),
            new Color32(0xDE, 0xC6, 0xFF, 0xFF),
            new Color32(0xFF, 0xAC, 0xCD, 0xFF),
            new Color32(0xA1, 0xD9, 0xFF, 0xFF)
        };

        public static Color TintColor(int index)
        {
            return TintColors[Mathf.Clamp(index, 0, TintColors.Length - 1)];
        }

        /// <summary>Customizer Inspector에서 설정한 팔레트를 모든 캐릭터 표현에 공유한다.</summary>
        public static void ConfigureTintColors(Color[] colors)
        {
            if (colors == null || colors.Length != TintColors.Length)
            {
                Debug.LogWarning($"[CustomizationData] 슈트 컬러는 {TintColors.Length}개가 필요합니다.");
                return;
            }

            for (int i = 0; i < TintColors.Length; i++)
            {
                Color color = colors[i];
                color.a = 1f;
                TintColors[i] = color;
            }
        }
        public int Packed => Mathf.Clamp(Tint, 0, TintNames.Length - 1) | (Mathf.Clamp(Helmet, 0, 2) << 3)
            | (Mathf.Clamp(Badge, 0, 2) << 5);
        public static CustomizationData Decode(int value) => new CustomizationData
        {
            Tint = Mathf.Clamp(value & 7, 0, TintNames.Length - 1),
            Helmet = Mathf.Clamp((value >> 3) & 3, 0, 2),
            Badge = Mathf.Clamp((value >> 5) & 3, 0, 2)
        };
        public static CustomizationData Load()
        {
            if (PlayerPrefs.HasKey(SaveKey)) return Decode(PlayerPrefs.GetInt(SaveKey));
            if (!PlayerPrefs.HasKey(LegacySaveKey)) return Decode(0);

            int legacyValue = PlayerPrefs.GetInt(LegacySaveKey);
            var migrated = new CustomizationData
            {
                // v1은 노랑부터 시작했다. v2에서 앞에 추가한 흰색만큼 기존 인덱스를 옮긴다.
                Tint = Mathf.Clamp(legacyValue & 7, 0, LegacyTintCount - 1) + 1,
                Helmet = Mathf.Clamp((legacyValue >> 3) & 3, 0, 2),
                Badge = Mathf.Clamp((legacyValue >> 5) & 3, 0, 2)
            };
            migrated.Save();
            return migrated;
        }
        public void Save() { PlayerPrefs.SetInt(SaveKey, Packed); PlayerPrefs.Save(); }
    }
}
