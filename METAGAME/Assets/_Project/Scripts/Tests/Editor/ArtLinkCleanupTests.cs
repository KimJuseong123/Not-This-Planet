using Metagame.EditorTools;
using NUnit.Framework;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 빌드가 남의 프리팹을 지우지 않는지 (S15P21B202-351).
    ///
    /// <para><b>무엇이 있었나.</b> <c>ArtLink.CleanStale</c> 이
    /// <c>Assets/_Project/Prefabs/Resources</c> 에서 자기가 FBX 로 만드는 셋만 남기고
    /// <b>나머지 전부를 지웠다.</b> 그런데 그 폴더는 ArtLink 것이 아니다 — 런타임이
    /// <c>Resources.Load</c> 로 집는 프리팹은 모두 거기 있어야 하므로 손으로 만든 것도
    /// 함께 산다. 그래서 <c>PF_Stage1_Tablet.prefab</c> 이 <b>빌드마다 지워졌다.</b></para>
    ///
    /// <para><b>왜 아무도 몰랐나.</b> ① <c>SpaceBuild.Build</c> 가 <c>ArtLink.BuildPrefabs</c> 를
    /// 맨 처음 부르므로 <b>지워진 상태로 플레이어가 빌드된다</b> ② <c>Resources</c> 아래는
    /// <c>Resources.Load</c> 로만 쓰여 <b>컴파일 검사에 안 걸린다</b> ③ CI 워크스페이스는
    /// 매번 <c>git clean</c> 되므로 <b>삭제가 커밋되지 않아 흔적이 없다.</b>
    /// 종료 코드는 0이고 빌드는 "성공" 이다. 2026-09-08 측정 빌드에서
    /// 367.0MB → 375.6MB(그 한 줄만 막고 다시 빌드) 로 차이를 확인했다.</para>
    ///
    /// <para><c>CleanStale</c> 자체를 테스트에서 돌리면 <b>프로젝트 에셋을 건드린다</b> —
    /// 그래서 판단만 <see cref="ArtLink.ShouldDelete"/> 로 빼고 그것을 검사한다.</para>
    /// </summary>
    public sealed class ArtLinkCleanupTests
    {
        /// <summary>
        /// <b>이 테스트가 그 사고를 막는다.</b> 손으로 만든 프리팹 이름은 ArtLink 가
        /// 모르는 이름이고, 모르는 이름은 지우지 않아야 한다.
        /// </summary>
        [Test]
        public void 모르는_이름은_지우지_않는다()
        {
            Assert.IsFalse(ArtLink.ShouldDelete("PF_Stage1_Tablet"),
                "손으로 만든 프리팹이다 — 빌드가 지우면 그 에셋 없는 빌드가 나간다(-351).");

            Assert.IsFalse(ArtLink.ShouldDelete("PF_Anything_Someone_Adds_Later"),
                "ArtLink 가 모르는 이름은 남겨 두고 사람이 판단해야 한다.");
        }

        /// <summary>ArtLink 가 지금 만드는 것은 당연히 지우지 않는다.</summary>
        [Test]
        public void 지금_만드는_것은_지우지_않는다()
        {
            Assert.IsFalse(ArtLink.ShouldDelete(ArtLink.RabbitKey));
            Assert.IsFalse(ArtLink.ShouldDelete(ArtLink.CapsuleKey));
            Assert.IsFalse(ArtLink.ShouldDelete(ArtLink.HandKey));
        }

        /// <summary>
        /// 빈 이름·null 에 걸려 넘어지지 않는다. 빌드 도중 도는 코드이므로 여기서 예외가
        /// 나면 빌드가 죽는다.
        /// </summary>
        [Test]
        public void 빈_이름에도_터지지_않는다()
        {
            Assert.IsFalse(ArtLink.ShouldDelete(null));
            Assert.IsFalse(ArtLink.ShouldDelete(string.Empty));
        }
    }
}
