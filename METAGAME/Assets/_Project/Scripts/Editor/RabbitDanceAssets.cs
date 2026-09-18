using UnityEditor;

namespace Metagame.EditorTools
{
    /// <summary>
    /// 블렌더가 만든 춤 FBX를 토끼 뼈대용 클립으로 굽는다 (S15P21B202-563).
    ///
    /// <para>굽는 절차는 던지기와 같아 <see cref="RabbitClipBaker"/> 에 있다.
    /// 여기는 무엇을 굽는지만 적는다.</para>
    ///
    /// <para>원본은 <c>METAGAME/Tools/Blender/generate_rabbit_dance.py</c> 가 만든다.
    /// 참조 영상에서 뽑은 동작이며 14.23초 · 427프레임이다 — 배속하지도 건너뛰지도
    /// 않는다.</para>
    /// </summary>
    public static class RabbitDanceAssets
    {
        public const string SourceFbx = RabbitClipBaker.Root + "/Source/Rabbit_Dance.fbx";
        public const string ClipName = "Rabbit_Dance";

        /// <summary>
        /// 굽는 뼈. <c>RabbitLocomotion.TossBoneNames</c> 와 같아야 한다.
        ///
        /// <para>에디터 어셈블리는 게임플레이 어셈블리를 참조하지 않으므로 목록을 따로
        /// 적는다. 어긋나면 굽힌 커브가 있어도 읽는 쪽이 그 뼈를 건너뛴다 — 그래서
        /// <see cref="RabbitClipBaker"/> 가 개수를 센다.</para>
        /// </summary>
        static readonly string[] BoneNames =
        {
            "Waist", "Spine01", "Spine02", "Head",
            "L_Clavicle", "L_Upperarm", "L_Forearm", "L_Hand",
            "R_Clavicle", "R_Upperarm", "R_Forearm", "R_Hand",
            "L_Thigh", "L_Calf", "L_Foot", "R_Thigh", "R_Calf", "R_Foot"
        };

        [MenuItem("Tools/Metagame/캐릭터/춤 클립 굽기")]
        public static void BakeFromMenu() => Bake();

        /// <summary>배치에서 부르는 자리. <c>-executeMethod</c> 로 쓴다.</summary>
        public static void BakeBatch() => Bake();

        // 한 번짜리 감정표현이다. 끝나면 서 있는 자세로 돌아간다.
        // 키를 줄인다. 30fps · 14.2초 · 18뼈면 키가 3만 개가 넘어 텍스트로 16MB 가 되고,
        // 저장소 위생 검사(비-LFS 한도)에 걸린다. 줄인 뒤 회전이 0.5도 넘게 틀어지면
        // 굽기가 실패한다 — 용량을 줄이려다 동작을 바꾸면 안 된다.
        // 던지기(26프레임)는 작아서 줄이지 않는다.
        public static void Bake() =>
            RabbitClipBaker.Bake(SourceFbx, ClipName, BoneNames, loop: false, keyTolerance: 0.0008f);
    }
}
