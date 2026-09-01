namespace Metagame.Gameplay
{
    /// <summary>
    /// ProjectSettings/TagManager.asset 에 정의된 게임플레이 Layer 인덱스와
    /// 자주 사용하는 LayerMask 조합.
    ///
    /// 레이어 인덱스를 바꾸면 이미 배치된 모든 프리팹의 Layer 설정이 어긋난다.
    /// 값을 수정할 때는 TagManager.asset 도 함께 수정해야 하며,
    /// GameLayersTests 가 두 곳의 불일치를 검출한다.
    ///
    /// 폴더는 Physics/ 지만 네임스페이스는 Metagame.Gameplay 를 사용한다.
    /// Metagame.Gameplay.Physics 로 두면 하위 네임스페이스에서 UnityEngine.Physics 가
    /// 가려져 Physics.Raycast 같은 호출이 깨지기 때문이다.
    /// </summary>
    public static class GameLayers
    {
        public const int Player = 6;
        public const int PlayerDowned = 7;
        public const int Ground = 8;
        public const int Part = 9;
        public const int PartCarried = 10;
        public const int Meteor = 11;
        public const int Interactable = 12;
        public const int Prop = 13;
        public const int BeamQuery = 14;

        /// 원격 플레이어의 몸. 보여 주기 위한 것이라 어떤 것과도 부딪히지 않는다 —
        /// 충돌 판정은 게임 서버가 하고, 클라이언트는 결과를 받아 그리기만 한다.
        public const int RemotePlayer = 15;

        public const int PlayerMask = (1 << Player) | (1 << PlayerDowned);
        public const int GroundMask = 1 << Ground;
        public const int PartMask = (1 << Part) | (1 << PartCarried);
        public const int MeteorMask = 1 << Meteor;
        public const int InteractableMask = 1 << Interactable;
        public const int PropMask = 1 << Prop;
        public const int BeamQueryMask = 1 << BeamQuery;
        public const int RemotePlayerMask = 1 << RemotePlayer;

        /// <summary>
        /// (인덱스, TagManager 에 등록된 이름) 쌍.
        /// 이름은 nameof 로 상수명과 강제로 일치시킨다.
        /// </summary>
        public static readonly (int Index, string Name)[] All =
        {
            (Player, nameof(Player)),
            (PlayerDowned, nameof(PlayerDowned)),
            (Ground, nameof(Ground)),
            (Part, nameof(Part)),
            (PartCarried, nameof(PartCarried)),
            (Meteor, nameof(Meteor)),
            (Interactable, nameof(Interactable)),
            (Prop, nameof(Prop)),
            (BeamQuery, nameof(BeamQuery)),
            (RemotePlayer, nameof(RemotePlayer)),
        };
    }
}
