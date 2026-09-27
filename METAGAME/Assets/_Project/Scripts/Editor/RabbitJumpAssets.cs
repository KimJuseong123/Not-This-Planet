using UnityEditor;

namespace Metagame.EditorTools
{
    /// <summary>
    /// 점프·낙하 클립 네 개를 토끼 뼈대용으로 굽는다 (S15P21B202-628).
    ///
    /// <para>굽는 절차는 <see cref="RabbitClipBaker"/> 에 있다 — 던지기(519)·
    /// 춤들(563·576·593)·엎드리기(616)가 쓰던 것 그대로다.</para>
    ///
    /// <para><b><c>Rabbit_Air</c> 는 시간축이 시간이 아니다.</b> 0프레임이 최대
    /// 하강, 가운데가 중립, 끝이 최대 상승이다 — 런타임이
    /// <c>SetTime(세로속도를 되매핑한 값)</c> 으로 찍어 쓴다. 그래서 순환도
    /// 아니고 흐르지도 않는다. 걷기/달리기가 <c>_phase</c> 로 하는 것과 같은
    /// 방식이라 새 블렌딩 코드가 필요 없다.</para>
    ///
    /// <para><b>몸이 뜨고 떨어지는 것은 이 클립들에 없다.</b> 그건 물리가 한다
    /// (<c>PlayerRig</c> 가 속도를 한 번 얹고 중력이 나머지를 한다). 클립은 그
    /// 위에 얹히는 팔다리 모양만 맡는다.</para>
    /// </summary>
    public static class RabbitJumpAssets
    {
        public const string AirFbx = RabbitClipBaker.Root + "/Source/Rabbit_Air.fbx";
        public const string LaunchFbx = RabbitClipBaker.Root + "/Source/Rabbit_JumpLaunch.fbx";
        public const string LandFbx = RabbitClipBaker.Root + "/Source/Rabbit_JumpLand.fbx";
        public const string HeelFbx = RabbitClipBaker.Root + "/Source/Rabbit_JumpHeel.fbx";

        /// <summary>
        /// 굽는 뼈. <c>RabbitLocomotion.TossBoneNames</c> 와 같아야 한다.
        ///
        /// <para>에디터 어셈블리는 게임플레이 어셈블리를 참조하지 않으므로 목록을
        /// 따로 적는다. 어긋나면 굽힌 커브가 있어도 읽는 쪽이 그 뼈를 건너뛴다.</para>
        /// </summary>
        static readonly string[] BoneNames =
        {
            "Waist", "Spine01", "Spine02", "Head",
            "L_Clavicle", "L_Upperarm", "L_Forearm", "L_Hand",
            "R_Clavicle", "R_Upperarm", "R_Forearm", "R_Hand",
            "L_Thigh", "L_Calf", "L_Foot", "R_Thigh", "R_Calf", "R_Foot"
        };

        [MenuItem("Tools/Metagame/캐릭터/점프 클립 굽기")]
        public static void BakeFromMenu() => Bake();

        /// <summary>배치에서 부르는 자리. <c>-executeMethod</c> 로 쓴다.</summary>
        public static void BakeBatch() => Bake();

        // 전부 1.4초 이하라 솎지 않는다 — 춤(14~24초)과 달리 용량이 문제가 안 되고,
        // 솎으면 회전 오차만 생긴다. 넷 다 순환이 아니다: 공중은 속도로 찍어 쓰고
        // 나머지 셋은 한 번짜리다.
        public static void Bake()
        {
            RabbitClipBaker.Bake(AirFbx, "Rabbit_Air", BoneNames, loop: false);
            RabbitClipBaker.Bake(LaunchFbx, "Rabbit_JumpLaunch", BoneNames, loop: false);
            RabbitClipBaker.Bake(LandFbx, "Rabbit_JumpLand", BoneNames, loop: false);
            RabbitClipBaker.Bake(HeelFbx, "Rabbit_JumpHeel", BoneNames, loop: false);
        }
    }
}
