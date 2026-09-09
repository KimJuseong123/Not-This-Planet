using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 진행도를 호스트 하나가 정한다는 규칙을 검증한다 (S15P21B202-54).
    ///
    /// <b>왜 이 규칙이 필요한가.</b> 경과 시간·클리어·체크포인트를 각 기계가 따로
    /// 세고 있었다. 그래서 같은 판인데 사람마다 기록이 다르고, "도착했다" 는 순간이
    /// 공유되지 않았다.
    ///
    /// <b>여기서 확인하는 것은 <see cref="GameSession"/> 의 계약뿐이다.</b> 값이 실제로
    /// 선을 타고 가는지(<c>ProgressSync</c> 의 SyncVar)는 서버가 떠 있어야 하므로
    /// EditMode 로 확인할 수 없다 — 그것은 2대 이상으로 하는 플레이 검증의 몫이다.
    /// 대신 <b>참가자가 자기 값을 덮어쓰지 못한다</b>는 불변식은 전부 여기서 막는다.
    /// </summary>
    public sealed class HostProgressTests
    {
        /// <summary>
        /// 원점에서 멀리 떨어뜨려 만든다.
        ///
        /// <para><see cref="TowerCollapseTests.Collapse_IsDeterministic"/> 은 같은 배치를 두 번
        /// 돌려 비교하므로 <b>이웃 픽스처가 남긴 물체까지 같아야 한다</b>. 그 테스트가
        /// 자기 무대를 (400,0,400) 으로 옮긴 이유이고(S15P21B202-213 에서 실제로 깨졌다),
        /// 여기도 원점을 비워 둔다. 이 픽스처에는 콜라이더가 없어 지금은 무해하지만,
        /// 나중에 콜라이더가 붙으면 그때 원인을 찾기 어려운 실패가 된다.</para>
        /// </summary>
        static readonly Vector3 FarFromOrigin = new(600f, 0f, 600f);

        GameObject holder;
        GameSession session;

        [SetUp]
        public void SetUp()
        {
            // 정적 목록을 비운 상태에서 시작한다 — 앞 픽스처가 남긴 것에 기대지 않는다.
            Checkpoint.All.Clear();

            holder = new GameObject("~진행도 테스트");
            holder.transform.position = FarFromOrigin;
            session = holder.AddComponent<GameSession>();
        }

        /// <summary>
        /// 정적 상태를 원래대로 돌린다.
        ///
        /// <para><see cref="GameSession.Instance"/> 는 <c>Awake</c> 가 채우는 <b>정적</b>
        /// 필드다. 오브젝트만 지우면 파괴된 참조가 남아, 뒤에 도는 테스트가 보는 상태가
        /// 이 픽스처를 돌렸는지에 따라 달라진다.</para>
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            if (holder != null) Object.DestroyImmediate(holder);

            Checkpoint.All.Clear();
            GameSession.Instance = null;
        }

        [Test]
        public void ProgressIsLocalUntilHostSendsAnything()
        {
            Assert.That(session.ProgressFromHost, Is.False,
                "네트워크가 없으면 로컬이 진행도를 가져야 한다 — 혼자 하기가 예전처럼 동작해야 한다");
        }

        [Test]
        public void HostProgressIsTakenAsIs()
        {
            session.ApplyHostProgress(checkpointIndex: 2, cleared: false, clearTime: 0f,
                                      elapsed: 12.5f, falls: 3);

            Assert.That(session.ProgressFromHost, Is.True);
            Assert.That(session.CheckpointIndex, Is.EqualTo(2));
            Assert.That(session.Cleared, Is.False);
            Assert.That(session.Elapsed, Is.EqualTo(12.5f).Within(0.001f));
            Assert.That(session.Falls, Is.EqualTo(3));
        }

        [Test]
        public void HostClearIsTakenAsIs()
        {
            session.ApplyHostProgress(4, cleared: true, clearTime: 31.4f, elapsed: 31.4f, falls: 1);

            Assert.That(session.Cleared, Is.True);
            Assert.That(session.ClearTime, Is.EqualTo(31.4f).Within(0.001f));
        }

        /// <summary>
        /// <see cref="Goal"/> 의 트리거는 참가자 화면에서도 돈다. 막지 않으면 참가자가
        /// 자기 시계로 <c>ClearTime</c> 을 정해 버려 사람마다 다른 기록이 남는다.
        /// </summary>
        [Test]
        public void ParticipantCannotClearOnItsOwn()
        {
            session.ApplyHostProgress(0, cleared: false, clearTime: 0f, elapsed: 8f, falls: 0);

            session.Clear();

            Assert.That(session.Cleared, Is.False, "호스트가 아직 클리어라고 하지 않았다");
            Assert.That(session.ClearTime, Is.EqualTo(0f).Within(0.001f));
        }

        /// <summary><see cref="Checkpoint"/> 트리거도 참가자 화면에서 돈다 — 같은 이유로 막는다.</summary>
        [Test]
        public void ParticipantCannotAdvanceCheckpointOnItsOwn()
        {
            session.ApplyHostProgress(1, cleared: false, clearTime: 0f, elapsed: 5f, falls: 0);

            var cpHolder = new GameObject("~체크포인트");
            cpHolder.transform.position = FarFromOrigin;
            var cp = cpHolder.AddComponent<Checkpoint>();
            cp.Index = 3;
            try
            {
                session.ReachCheckpoint(cp);

                Assert.That(session.CheckpointIndex, Is.EqualTo(1),
                    "체크포인트도 호스트가 정한다");
            }
            finally
            {
                Object.DestroyImmediate(cp.gameObject);
            }
        }

        /// <summary>
        /// 호스트에게서 값이 더 오지 않게 되면 로컬로 되돌아가야 한다. 그러지 않으면
        /// 마지막으로 받은 값에 시계가 멈춘 채로 남아, 혼자 하기로 넘어가도 시간이
        /// 흐르지 않는다.
        /// </summary>
        [Test]
        public void LocalTakesOverAgainWhenHostStops()
        {
            session.ApplyHostProgress(2, cleared: false, clearTime: 0f, elapsed: 9f, falls: 2);

            session.ClearHostProgress();

            Assert.That(session.ProgressFromHost, Is.False);

            session.Clear();
            Assert.That(session.Cleared, Is.True, "로컬로 돌아왔으면 다시 스스로 판정한다");
        }

        /// <summary>
        /// 재시작도 진행도를 바꾸는 일이다. 참가자가 <c>R</c> 을 눌러 판을 되돌리면
        /// 다음 수신에 덮여, 눌린 것도 아니고 안 눌린 것도 아닌 상태가 된다.
        /// </summary>
        [Test]
        public void ParticipantCannotRestartTheRunOnItsOwn()
        {
            session.ApplyHostProgress(3, cleared: false, clearTime: 0f, elapsed: 20f, falls: 4);

            session.Restart();

            Assert.That(session.CheckpointIndex, Is.EqualTo(3));
            Assert.That(session.Elapsed, Is.EqualTo(20f).Within(0.001f));
            Assert.That(session.Falls, Is.EqualTo(4));
        }
    }
}
