using UnityEditor;

namespace Metagame.EditorTools
{
    /// <summary>
    /// 블렌더가 만든 던지기 FBX를 토끼 뼈대용 클립으로 굽는다 (S15P21B202-519).
    ///
    /// <para>굽는 절차는 <see cref="RabbitClipBaker"/> 에 있다 — 춤(S15P21B202-563)이
    /// 같은 절차를 쓰게 되면서 옮겼다. 절차 안의 안전장치들은 전부 여기서 한 번씩
    /// 당한 뒤에 생긴 것이라, 한쪽에만 두면 다른 쪽이 같은 사고를 다시 당한다.</para>
    ///
    /// <para>원본은 <c>METAGAME/Tools/Blender/generate_rabbit_toss.py</c> 가 만든다.</para>
    /// </summary>
    public static class RabbitTossAssets
    {
        public const string SourceFbx = RabbitClipBaker.Root + "/Source/Rabbit_TossUp.fbx";
        public const string ClipName = "Rabbit_TossUp";

        /// <summary>
        /// 굽는 뼈. <c>RabbitLocomotion.TossBoneNames</c> 와 같아야 한다.
        ///
        /// <para>에디터 어셈블리는 게임플레이 어셈블리를 참조하지 않으므로 목록을 따로
        /// 적는다. 어긋나면 굽힌 커브가 있어도 읽는 쪽이 그 뼈를 건너뛴다 — 그래서
        /// <see cref="RabbitClipBaker"/> 가 개수를 세고, 테스트가 두 목록을 맞대어 본다
        /// (<c>TheBakeListAndTheRuntimeListAreTheSame</c>).</para>
        /// </summary>
        static readonly string[] BoneNames =
        {
            "Waist", "Spine01", "Spine02", "Head",
            "L_Clavicle", "L_Upperarm", "L_Forearm", "L_Hand",
            "R_Clavicle", "R_Upperarm", "R_Forearm", "R_Hand",
            "L_Thigh", "L_Calf", "L_Foot", "R_Thigh", "R_Calf", "R_Foot"
        };

        [MenuItem("Tools/Metagame/캐릭터/던지기 클립 굽기")]
        public static void BakeFromMenu() => Bake();

        /// <summary>배치에서 부르는 자리. <c>-executeMethod</c> 로 쓴다.</summary>
        public static void BakeBatch() => Bake();

        // 한 번짜리 동작이다.
        public static void Bake() => RabbitClipBaker.Bake(SourceFbx, ClipName, BoneNames, loop: false);
    }
}
