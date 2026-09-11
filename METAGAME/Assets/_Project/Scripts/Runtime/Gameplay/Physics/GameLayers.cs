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
        /// <b>발을 딛거나 무엇이 얹힐 수 있는 것.</b>
        ///
        /// <see cref="GroundMask"/> 하나만 보면 안 된다. 씬의 지면
        /// (<c>ENV_Ground_Arena_50m</c>)은 <b>레이어 0(Default)</b> 에 있고
        /// <see cref="Ground"/>(8) 레이어 오브젝트가 <b>씬에 하나도 없다</b>
        /// (2026-09-02 실측, S15P21B202-254). 그래서 <c>GroundMask</c> 로 지면을 찾던
        /// <see cref="SafeGround"/> 의 끼임 탈출이 조용히 죽어 있었다.
        ///
        /// 플레이어의 접지 판정(<see cref="RagdollBuilder.WorldMask"/>)은 Default 를
        /// 포함해서 잘 동작했다 — <b>저장소에 지면 정의가 둘 있었고 서로 달랐던 것</b>이
        /// 원인이다. 이 마스크로 합친다.
        ///
        /// 빼는 것과 이유:
        /// <code>
        /// Player · PlayerDowned   사람 위에 착탄 지점을 잡거나 사람을 밟고 서지 않는다
        /// Interactable · BeamQuery 물리 충돌이 없는 질의 전용 레이어다
        /// Meteor                  이미 박힌 운석 위에 예고를 다시 그리지 않는다(§19)
        /// PartCarried             견인 중인 파츠는 곧 자리를 뜬다
        /// </code>
        /// </summary>
        /// <summary>
        /// 방망이가 밀칠 것을 찾는 범위(기획 §18).
        ///
        /// <b>예전에는 <see cref="PropMask"/> 하나만 봤다.</b> Prop 레이어 밖에 있는
        /// 움직이는 물체는 찾지 못했고, 그 레이어 안에 있던 것은 용접기 · 방망이 자신 ·
        /// 견인 운반체뿐이라 인게임에서 <b>아무것도 안 맞았다</b>
        /// (2026-09-02 확인, S15P21B202-257).
        ///
        /// 빼는 것과 이유:
        /// <code>
        /// Player · PlayerDowned    동료 타격은 별 경로다(§18.3) — MeteorBat.FindPlayer
        /// Meteor                   운석 반사도 별 경로다(§18.1) — MeteorBat.FindTarget
        /// Part · PartCarried       §18.3 "파츠와 용접부에 피해를 주지는 않는다".
        ///                          비드는 파츠와 같은 레이어라 함께 빠진다
        /// Interactable · BeamQuery 물리 충돌이 없는 질의 전용 레이어다
        /// </code>
        ///
        /// 남는 것(지면 · 자연물 포함)은 <c>MeteorBat.CanPush</c> 가 Rigidbody 로 다시
        /// 거른다 — 조준한 쪽에 움직일 것이 없으면 헛스윙이 되는 것이 맞다.
        /// </summary>
        public const int BatStrikeMask =
            ~((1 << Player) | (1 << PlayerDowned) | (1 << Meteor)
              | (1 << Part) | (1 << PartCarried)
              | (1 << Interactable) | (1 << BeamQuery));

        public const int SurfaceMask =
            ~((1 << Player) | (1 << PlayerDowned) | (1 << Interactable)
              | (1 << BeamQuery) | (1 << Meteor) | (1 << PartCarried));

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
