using UnityEditor;

namespace Metagame.EditorTools
{
    /// <summary>
    /// 방망이 스윙과 대기 코일을 토끼 뼈대용으로 굽는다 (S15P21B202-694).
    ///
    /// <para>굽는 절차는 <see cref="RabbitClipBaker"/> 에 있다 — 던지기(519)·춤들·
    /// 엎드리기(616)·점프(628)·옆걸음(670)이 쓰던 것 그대로다.</para>
    ///
    /// <para><b>스윙은 한 번짜리, 대기는 고리다.</b> 대기 코일은 방망이를 들고 있는
    /// 동안 계속 보여야 한다 — 야구 게임이 로드를 대기 자세에 넣는 이유와 같다.
    /// 클릭 뒤에 감으면 판정 창(0.6초 ±0.08)을 통째로 먹는다.</para>
    ///
    /// <para><b>팔 뼈도 굽는다.</b> 읽는 쪽(<c>RabbitLocomotion</c>)은 스윙에서 팔을
    /// 건너뛰지만(손과 배트가 어긋나지 않게), 굽기는 <b>안 움직이는 뼈가 있으면
    /// 실패</b>하므로 커브는 다 들어 있어야 한다. 그리고 나중에 팔까지 쓰기로
    /// 정하면 다시 구울 필요가 없다.</para>
    /// </summary>
    public static class RabbitBatSwingAssets
    {
        public const string SwingFbx = RabbitClipBaker.Root + "/Source/Rabbit_BatSwing.fbx";
        public const string ReadyFbx = RabbitClipBaker.Root + "/Source/Rabbit_BatReady.fbx";

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

        [MenuItem("Tools/Metagame/캐릭터/방망이 스윙 클립 굽기")]
        public static void BakeFromMenu() => Bake();

        /// <summary>배치에서 부르는 자리. <c>-executeMethod</c> 로 쓴다.</summary>
        public static void BakeBatch() => Bake();

        public static void Bake()
        {
            RabbitClipBaker.Bake(SwingFbx, "Rabbit_BatSwing", BoneNames, loop: false);
            RabbitClipBaker.Bake(ReadyFbx, "Rabbit_BatReady", BoneNames, loop: true);
        }
    }
}
