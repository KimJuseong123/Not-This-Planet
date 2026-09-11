using Metagame.Network;
using NUnit.Framework;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 재접속할 때 <b>같은 사람의 낡은 접속을 걷어내는 규칙</b>을 검증한다
    /// (S15P21B202-373).
    ///
    /// <para><b>왜 필요했나.</b> 2026-09-10 4인 QA 에서 "재접속하면 그 사람의 이전
    /// 캐릭터가 유령으로 남는다" 가 나왔다. 참가자 로그에서 <c>userId=19</c> 에 대해
    /// "다른 플레이어가 보인다" 가 <b>두 번</b> 찍혔다 — 클라이언트 로그이므로 서버가
    /// 같은 사람을 두 번 들였다는 뜻이다. Mirror 는 연결이 죽어도 전송 타임아웃까지 그
    /// 플레이어 오브젝트를 들고 있고, 그 사이에 재접속이 성공하면(백엔드가 자리를 지켜
    /// 준다, S15P21B202-150) 한 사람에 대해 <c>NetPlayer</c> 가 둘이 된다.</para>
    ///
    /// <para>걷어내는 <b>동작</b>(오브젝트 파괴·연결 끊기)은 <c>NetPlayer</c> 가 하고
    /// Mirror 서버가 떠 있어야 하므로 여기서 돌리지 않는다. 대신 <b>누구를 걷어낼지
    /// 고르는 판정</b>이 <see cref="RejoinMemory"/> 에 값만 보는 순수 함수로 있고, 이
    /// 스위트가 그 규칙을 지킨다 — 잘못 고르면 멀쩡한 접속이나 호스트를 끊게 되고,
    /// <b>그쪽이 유령보다 나쁘다.</b></para>
    ///
    /// <para>판정을 <c>NetPlayer</c> 에 두었다가 옮겼다. 그 클래스는
    /// <c>NetworkBehaviour</c> 라 여기서 부르면 테스트 어셈블리가 Mirror 를 참조해야
    /// 한다 — 2026-09-10 에 <c>error CS0012</c> 5건으로 실측했다.</para>
    /// </summary>
    public sealed class RejoinDuplicateTests
    {
        /// <summary>같은 사람의 다른 접속은 걷어낸다 — 유령을 없애는 본론이다.</summary>
        [Test]
        public void SameUserIsEvicted()
        {
            Assert.That(RejoinMemory.IsStaleDuplicate(19, 19, false), Is.True,
                "같은 userId 의 낡은 접속을 걷어내지 않으면 이전 캐릭터가 유령으로 남는다");
        }

        /// <summary>다른 사람은 건드리지 않는다.</summary>
        [Test]
        public void OtherUsersAreLeftAlone()
        {
            Assert.That(RejoinMemory.IsStaleDuplicate(19, 23, false), Is.False,
                "남의 접속을 끊는 것은 유령보다 나쁘다");
        }

        /// <summary>
        /// <b><c>userId == 0</c> 은 걷어내지 않는다.</b>
        ///
        /// <para>인증자 없이 띄운 검증용 실행에서는 신원이 없어 <c>userId</c> 가 0 이
        /// 된다. <b>서로 다른 사람이 모두 0</b> 이 될 수 있으므로 그때 걷어내면 멀쩡한
        /// 접속을 끊는다. <see cref="RejoinMemory"/> 도 같은 이유로 0 을 건너뛴다.</para>
        /// </summary>
        [Test]
        public void UnknownIdentityIsNeverEvicted()
        {
            Assert.That(RejoinMemory.IsStaleDuplicate(0, 0, false), Is.False,
                "userId 0 은 신원이 없다는 뜻이다 — 서로 다른 사람일 수 있으므로 끊지 않는다");
        }

        /// <summary>
        /// <b>호스트는 걷어내지 않는다.</b>
        ///
        /// <para>방장의 몸은 게임 씬이 만든 것을 넘겨받은 것이고(<c>adoptedBody</c>),
        /// 호스트가 나가면 판 자체가 끝난다(<c>docs/network-sync-policy.md</c> §5).
        /// 여기서 끊을 수 있으면 <b>남의 재접속이 판을 없애는 길</b>이 열린다.</para>
        /// </summary>
        [Test]
        public void HostIsNeverEvicted()
        {
            Assert.That(RejoinMemory.IsStaleDuplicate(19, 19, true), Is.False,
                "호스트를 끊으면 남의 재접속이 판을 없앨 수 있다");
        }

        /// <summary>
        /// 신원이 있는 사람이 붙는데 신원 없는(0) 접속이 남아 있으면 건드리지 않는다.
        /// <c>0 != 19</c> 이므로 같은 사람이 아니다 — 판정이 한쪽만 보고 넘어가지 않는지 본다.
        /// </summary>
        [Test]
        public void KnownUserDoesNotEvictUnknownOne()
        {
            Assert.That(RejoinMemory.IsStaleDuplicate(19, 0, false), Is.False);
        }
    }
}
