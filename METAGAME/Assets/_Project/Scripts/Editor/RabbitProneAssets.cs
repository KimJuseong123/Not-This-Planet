using UnityEditor;

namespace Metagame.EditorTools
{
    /// <summary>
    /// 엎드리기 클립 네 개를 토끼 뼈대용으로 굽는다 (S15P21B202-616).
    ///
    /// <para>굽는 절차는 <see cref="RabbitClipBaker"/> 에 있다 — 던지기(519)와
    /// 춤들(563·576·593)이 쓰던 것 그대로다. 여기는 무엇을 굽는지만 적는다.</para>
    ///
    /// <para><b>대기와 기어가기는 순환(loop)이고 전환 둘은 아니다.</b> 자세는 뗄
    /// 때까지 유지되므로 그 두 개가 안 돌면 첫 프레임에서 굳는다.</para>
    ///
    /// <para><b>몸이 눕는 것은 이 클립들에 없다.</b> 그것은 물리가 한다
    /// (<c>PlayerRig.Balance</c> 의 목표 회전). 클립은 그 위에 얹히는 팔다리
    /// 모양만 맡는다 — 둘 다 눕히면 두 번 눕는다.</para>
    /// </summary>
    public static class RabbitProneAssets
    {
        public const string IdleFbx = RabbitClipBaker.Root + "/Source/Rabbit_ProneIdle.fbx";
        public const string CrawlFbx = RabbitClipBaker.Root + "/Source/Rabbit_Crawl.fbx";
        public const string LieDownFbx = RabbitClipBaker.Root + "/Source/Rabbit_LieDown.fbx";
        public const string StandUpFbx = RabbitClipBaker.Root + "/Source/Rabbit_StandUp.fbx";

        /// <summary>
        /// 굽는 뼈. <c>RabbitLocomotion.TossBoneNames</c> 와 같아야 한다.
        ///
        /// <para>에디터 어셈블리는 게임플레이 어셈블리를 참조하지 않으므로 목록을 따로
        /// 적는다. 어긋나면 굽힌 커브가 있어도 읽는 쪽이 그 뼈를 건너뛴다.</para>
        /// </summary>
        static readonly string[] BoneNames =
        {
            "Waist", "Spine01", "Spine02", "Head",
            "L_Clavicle", "L_Upperarm", "L_Forearm", "L_Hand",
            "R_Clavicle", "R_Upperarm", "R_Forearm", "R_Hand",
            "L_Thigh", "L_Calf", "L_Foot", "R_Thigh", "R_Calf", "R_Foot"
        };

        [MenuItem("Tools/Metagame/캐릭터/엎드리기 클립 굽기")]
        public static void BakeFromMenu() => Bake();

        /// <summary>배치에서 부르는 자리. <c>-executeMethod</c> 로 쓴다.</summary>
        public static void BakeBatch() => Bake();

        // 전부 2초 이하라 솎을 이유가 없다 — 춤(14~24초)과 달리 용량이 문제가 안 된다.
        // 솎으면 회전 오차만 생긴다.
        public static void Bake()
        {
            RabbitClipBaker.Bake(IdleFbx, "Rabbit_ProneIdle", BoneNames, loop: true);
            RabbitClipBaker.Bake(CrawlFbx, "Rabbit_Crawl", BoneNames, loop: true);
            RabbitClipBaker.Bake(LieDownFbx, "Rabbit_LieDown", BoneNames, loop: false);
            RabbitClipBaker.Bake(StandUpFbx, "Rabbit_StandUp", BoneNames, loop: false);
        }
    }
}
