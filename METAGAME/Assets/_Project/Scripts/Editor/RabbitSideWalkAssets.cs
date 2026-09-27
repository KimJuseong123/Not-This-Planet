using UnityEditor;

namespace Metagame.EditorTools
{
    /// <summary>
    /// 옆걸음(게걸음) 클립을 토끼 뼈대용으로 굽는다 (S15P21B202-670).
    ///
    /// <para>굽는 절차는 <see cref="RabbitClipBaker"/> 에 있다 — 던지기(519)·춤들·
    /// 엎드리기(616)·점프(628)가 쓰던 것 그대로다.</para>
    ///
    /// <para><b>순환 클립이다.</b> 걷기·달리기와 같은 <c>_phase</c> 로 찍어 쓰므로
    /// 끝이 처음으로 이어져야 한다.</para>
    ///
    /// <para><b>접지 위상을 앞걸음에 맞춰 만들었다.</b> 네 방향이 같은 시계를 쓰므로
    /// 같은 위상에서 같은 발이 땅에 닿아야 방향을 바꿀 때 다리가 안 미끄러진다.
    /// 기존 <c>Rabbit_Walk</c> 를 재서 얻은 값이다 — 왼 허벅지 앞끝 0.717, 오른쪽
    /// 0.183. 길이도 같은 1.167초다.</para>
    ///
    /// <para><b>좌우 두 벌이다.</b> 거울로 뒤집어 쓰면 접지 위상까지 뒤집혀
    /// (0.183 → 0.817) 앞걸음과 섞을 때 발이 미끄러진다. 두 벌이면 가는 쪽 발이
    /// 제 위상을 지킨다.</para>
    /// </summary>
    public static class RabbitSideWalkAssets
    {
        public const string RightFbx = RabbitClipBaker.Root + "/Source/Rabbit_SideWalk.fbx";
        public const string LeftFbx = RabbitClipBaker.Root + "/Source/Rabbit_SideWalkLeft.fbx";

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

        [MenuItem("Tools/Metagame/캐릭터/옆걸음 클립 굽기")]
        public static void BakeFromMenu() => Bake();

        /// <summary>배치에서 부르는 자리. <c>-executeMethod</c> 로 쓴다.</summary>
        public static void BakeBatch() => Bake();

        public static void Bake()
        {
            RabbitClipBaker.Bake(RightFbx, "Rabbit_SideWalk", BoneNames, loop: true);
            RabbitClipBaker.Bake(LeftFbx, "Rabbit_SideWalkLeft", BoneNames, loop: true);
        }
    }
}
