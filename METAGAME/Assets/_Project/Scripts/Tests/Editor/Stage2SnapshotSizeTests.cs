using System;
using System.Linq;
using System.Text;
using Metagame.Gameplay.Parts;
using Metagame.Gameplay.Stage2;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Network.Tests
{
    /// <summary>
    /// 2스테이지 스냅샷의 크기와 채널 (S15P21B202-725).
    ///
    /// <para>이 스냅샷은 Unreliable 로 보낼 수 있는 크기가 아니다. 한도를 넘은 통은
    /// KcpPeer 가 보내지 않고 버리므로, 참가자에게는 아무것도 도착하지 않는다. 컷신이
    /// 호스트에게만 보이던 것이 그 증상이었다.</para>
    ///
    /// <para>채널을 Reliable 로 바꾼 것은 S15P21B202-723 이다. 여기서 재는 것은 "무엇이
    /// 옳은 채널인가" 가 아니라 "Unreliable 이 가능한 크기인가" 다. 나중에 스냅샷을 줄여
    /// 다시 Unreliable 로 돌리고 싶어지면, 넘어야 할 선이 얼마인지 이 테스트가 알려 준다.</para>
    /// </summary>
    public sealed class Stage2SnapshotSizeTests
    {
        /// <summary>KcpPeer.UnreliableMaxMessageSize(1200) = 1200 - METADATA_SIZE(5) - 1.</summary>
        const int KcpUnreliableMax = 1194;

        /// <summary>
        /// 실제 한 판의 스냅샷. 파츠 3개에 인원 4명은 이 게임이 허용하는 최대 구성이다.
        /// 좌표는 2의 거듭제곱 분수라 JSON 으로 짧게 적힌다 — 실제 플레이 중의 값은
        /// 자릿수가 길어 이보다 크다. 즉 여기 수치는 낙관적인 하한이다.
        /// </summary>
        static Stage2State Snapshot(string[] demoCast)
        {
            var state = new Stage2State
            {
                Time = 1234.5678,
                Elapsed = 942.25f,
                Parts = Enumerable.Range(0, 3).Select(i => new YardPartState
                {
                    Position = new Vector3(1.25f + i, 0.5f, -3.75f),
                    Rotation = Quaternion.Euler(12.5f, 47.25f, 3.125f),
                    A = 0.5f, B = 0.25f, C = 0.125f, Open = true, Secured = i == 0,
                }).ToArray(),
                Players = Enumerable.Range(0, 4).Select(i => new YardPlayerState
                {
                    Id = (uint)(i + 1),
                    Contamination = 0.375f,
                    Position = new Vector3(-2.5f + i, 1.125f, 4.625f),
                }).ToArray(),
            };

            if (demoCast.Length == 0) return state;

            state.DemoStarted = true;
            state.DemoPlayers = demoCast.Select((name, i) => new DemoEndingPlayer
            {
                Name = name,
                Suit = new Color(0.25f, 0.5f, 0.75f),
                Accent = new Color(0.125f, 0.875f, 0.375f),
                Appearance = i,
            }).ToArray();
            return state;
        }

        static int Bytes(Stage2State state) =>
            Encoding.UTF8.GetByteCount(JsonUtility.ToJson(state));

        /// <summary>
        /// **평상시 스냅샷부터 이미 한도를 넘는다.** 컷신이 문제가 아니었다 — 참가자는
        /// 2스테이지 스냅샷을 한 번도 받은 적이 없다. 이 사실이 채널 선택의 근거다.
        /// </summary>
        [Test]
        public void OrdinaryPlaySnapshotAlreadyExceedsTheUnreliableLimit()
        {
            var bytes = Bytes(Snapshot(Array.Empty<string>()));

            Assert.That(bytes, Is.GreaterThan(KcpUnreliableMax),
                $"평상시 스냅샷 {bytes}B. 이 값이 {KcpUnreliableMax}B 아래로 내려왔다면 "
                + "Unreliable 로 되돌릴 수 있다 — Stage2Bridge 의 채널 선택을 다시 보라.");
        }

        /// <summary>
        /// 데모가 시작되면 닉네임과 색이 실려 더 커진다. 한글 닉네임은 UTF-8 에서 한 글자가
        /// 3바이트라, 글자 수로 재면 실제보다 작게 보인다.
        /// </summary>
        [Test]
        public void DemoStartMakesTheSnapshotLargerStill()
        {
            var ordinary = Bytes(Snapshot(Array.Empty<string>()));
            var demo = Snapshot(new[] { "김수호", "최재현", "박주성", "이영희" });
            var json = JsonUtility.ToJson(demo);

            Assert.That(Bytes(demo), Is.GreaterThan(ordinary));
            Assert.That(Encoding.UTF8.GetByteCount(json), Is.GreaterThan(json.Length),
                "한글 닉네임이 있으면 바이트 수가 글자 수보다 크다 — 크기는 바이트로 재야 한다.");
        }
    }
}
