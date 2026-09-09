using Metagame.Network;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 끊긴 자리 복구의 규칙을 고정한다 (S15P21B202-150).
    ///
    /// <b>왜 이 규칙이 필요한가.</b> Mirror 는 연결이 끊기면 플레이어 오브젝트를 지운다.
    /// 그래서 다시 붙으면 새 몸이 스폰 지점에 서고, 멀리까지 걸어간 사람이 처음으로
    /// 되돌아간다. 서버가 자리를 적어 두고 돌아올 때 쓴다.
    ///
    /// 시간에 기대는 규칙(유예 초과)은 <see cref="Time.timeAsDouble"/> 을 EditMode 에서
    /// 앞으로 돌릴 수 없어 여기서 검증하지 않는다 — 그 항은 실측(-151)의 몫이다.
    /// </summary>
    public sealed class RejoinMemoryTests
    {
        const long UserId = 42L;

        [SetUp]
        public void SetUp() => RejoinMemory.Clear();

        [TearDown]
        public void TearDown() => RejoinMemory.Clear();

        [Test]
        public void NothingRememberedForANewcomer()
        {
            Assert.IsFalse(RejoinMemory.TryTake(UserId, out _, out _),
                "첫 입장이면 스폰 지점에 서야 한다");
        }

        [Test]
        public void RememberedSpotComesBack()
        {
            var foot = new Vector3(12.5f, 3f, -40f);
            var forward = Vector3.left;

            RejoinMemory.Remember(UserId, foot, forward);

            Assert.IsTrue(RejoinMemory.TryTake(UserId, out Vector3 takenFoot, out Vector3 takenForward));
            Assert.That(takenFoot, Is.EqualTo(foot));
            Assert.That(takenForward, Is.EqualTo(forward));
        }

        /// <summary>
        /// 꺼내면 지운다. 남겨 두면 그 사람이 다음에 죽거나 나갔다 들어올 때 옛 자리로
        /// 끌려간다.
        /// </summary>
        [Test]
        public void TakingASpotConsumesIt()
        {
            RejoinMemory.Remember(UserId, Vector3.one, Vector3.forward);

            Assert.IsTrue(RejoinMemory.TryTake(UserId, out _, out _));
            Assert.IsFalse(RejoinMemory.TryTake(UserId, out _, out _), "두 번 쓰이면 안 된다");
        }

        [Test]
        public void SpotsAreKeptPerPerson()
        {
            RejoinMemory.Remember(1L, new Vector3(1f, 0f, 0f), Vector3.forward);
            RejoinMemory.Remember(2L, new Vector3(2f, 0f, 0f), Vector3.back);

            Assert.IsTrue(RejoinMemory.TryTake(2L, out Vector3 second, out _));
            Assert.That(second.x, Is.EqualTo(2f).Within(0.001f), "남의 자리를 주면 안 된다");

            Assert.IsTrue(RejoinMemory.TryTake(1L, out Vector3 first, out _));
            Assert.That(first.x, Is.EqualTo(1f).Within(0.001f));
        }

        /// <summary>새 판이 시작되면 지난 판의 자리는 버린다.</summary>
        [Test]
        public void ANewMatchForgetsEverything()
        {
            RejoinMemory.Remember(UserId, Vector3.one, Vector3.forward);
            Assert.That(RejoinMemory.Count, Is.EqualTo(1));

            RejoinMemory.Clear();

            Assert.That(RejoinMemory.Count, Is.EqualTo(0));
            Assert.IsFalse(RejoinMemory.TryTake(UserId, out _, out _));
        }

        /// <summary>
        /// 신원이 없는 연결(userId 0)은 적지 않는다 — 누구 자리인지 알 수 없다.
        /// </summary>
        [Test]
        public void AnUnknownUserIsNotRemembered()
        {
            RejoinMemory.Remember(0L, Vector3.one, Vector3.forward);

            Assert.That(RejoinMemory.Count, Is.EqualTo(0));
        }

        /// <summary>백엔드의 유예(30초)와 같은 값이어야 한다 — 완료 조건이 요구한다.</summary>
        [Test]
        public void GraceMatchesTheBackend()
        {
            Assert.That(RejoinMemory.GraceSeconds, Is.EqualTo(30f).Within(0.001f));
        }
    }
}
