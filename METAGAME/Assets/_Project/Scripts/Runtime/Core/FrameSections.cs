using System;
using System.Diagnostics;

namespace Metagame.Core
{
    /// <summary>
    /// FixedUpdate 를 쓰는 무거운 스크립트가 각각 몇 ms 를 쓰는지 (S15P21B202-746).
    ///
    /// <para>2026-09-26 QA 의 <c>[FRAME]</c> 로그에서 FixedUpdate 스크립트가 프레임당 3.7ms 로 CPU 에서 가장 컸다.
    /// 누가 쓰는지 가르려고 메서드 첫 줄에 <c>using var _ = FrameSections.Scope(FrameSections.PlayerRig);</c> 한 줄만
    /// 둔다 — 모든 return 에서 저절로 닫힌다. 배포용 빌드에서도 잰다(프로파일러가 필요 없다).
    /// <see cref="FrameBudget"/> 이 10초마다 합을 로그에 붙인다.</para>
    /// </summary>
    public static class FrameSections
    {
        public const int PlayerRig = 0, NetPlayer = 1, WorldSync = 2, PartPush = 3, PartLink = 4,
                         Transport = 5, RemoteAvatar = 6, LowGravity = 7, Meteors = 8, Count = 9;

        public static readonly string[] Names =
            { "PlayerRig", "NetPlayer", "WorldSync", "PartPush", "PartLink", "견인", "RemoteAvatar", "저중력", "운석" };

        static readonly long[] Ticks = new long[Count];

        public static Section Scope(int id) => new Section(id, Stopwatch.GetTimestamp());

        public readonly struct Section : IDisposable
        {
            readonly int id; readonly long started;
            public Section(int id, long started) { this.id = id; this.started = started; }
            public void Dispose() => Ticks[id] += Stopwatch.GetTimestamp() - started;
        }

        /// <summary>쌓인 합(ms)을 돌려주고 비운다.</summary>
        public static double Take(int id)
        {
            double ms = Ticks[id] * 1000.0 / Stopwatch.Frequency;
            Ticks[id] = 0;
            return ms;
        }
    }
}
