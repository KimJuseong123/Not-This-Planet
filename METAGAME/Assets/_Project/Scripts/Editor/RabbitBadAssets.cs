using UnityEditor;

namespace Metagame.EditorTools
{
    /// <summary>
    /// 블렌더가 만든 BAD 춤 FBX를 토끼 뼈대용 클립으로 굽는다 (S15P21B202-593).
    ///
    /// <para>굽는 절차는 <see cref="RabbitClipBaker"/> 에 있다 — 던지기(519)·
    /// 뮤직챌린지(563)·REDRED(576)가 쓰던 것 그대로다. 여기는 무엇을 굽는지만 적는다.</para>
    ///
    /// <para>원본은 <c>generate_rabbit_dance.py --data rabbit_dance_bad
    /// --action Rabbit_Bad</c> 가 만든다. 732프레임 · 24.40초이고 배속하지도
    /// 건너뛰지도 않는다 — 지금까지 중 가장 길다.</para>
    /// </summary>
    public static class RabbitBadAssets
    {
        public const string SourceFbx = RabbitClipBaker.Root + "/Source/Rabbit_Bad.fbx";
        public const string ClipName = "Rabbit_Bad";

        /// <summary>
        /// 굽는 뼈. <c>RabbitLocomotion.TossBoneNames</c> 와 같아야 한다.
        ///
        /// <para>에디터 어셈블리는 게임플레이 어셈블리를 참조하지 않으므로 목록을 따로
        /// 적는다. 어긋나면 굽힌 커브가 있어도 읽는 쪽이 그 뼈를 건너뛴다 — 그래서
        /// <see cref="RabbitClipBaker"/> 가 개수를 세고, 테스트가 두 목록을 맞대어 본다.</para>
        /// </summary>
        static readonly string[] BoneNames =
        {
            "Waist", "Spine01", "Spine02", "Head",
            "L_Clavicle", "L_Upperarm", "L_Forearm", "L_Hand",
            "R_Clavicle", "R_Upperarm", "R_Forearm", "R_Hand",
            "L_Thigh", "L_Calf", "L_Foot", "R_Thigh", "R_Calf", "R_Foot"
        };

        [MenuItem("Tools/Metagame/캐릭터/BAD 클립 굽기")]
        public static void BakeFromMenu() => Bake();

        /// <summary>배치에서 부르는 자리. <c>-executeMethod</c> 로 쓴다.</summary>
        public static void BakeBatch() => Bake();

        // 공차는 563·576 과 같은 0.0008 이다. 732프레임이라 줄여도 비-LFS 한도(5MB)에는
        // 못 들어가므로 더 깎을 이유가 없다 — 깎을수록 회전 오차만 커진다. 줄인 뒤
        // 1.5도를 넘게 틀어지면 굽기가 실패한다.
        public static void Bake() =>
            RabbitClipBaker.Bake(SourceFbx, ClipName, BoneNames, loop: false, keyTolerance: 0.0008f);
    }
}
