using Mirror;
using UnityEngine;
using Metagame.Gameplay;
using Metagame.Gameplay.Hazards;

namespace Metagame.Network
{
    /// <summary>
    /// 운석 공세를 <b>호스트 하나가 정하게</b> 한다 (S15P21B202-352).
    ///
    /// <para><see cref="DustStormClockBridge"/> 와 같은 모양이다 — <c>Metagame.Gameplay</c> 는
    /// Mirror 를 모른 채로 두고(어셈블리도 참조하지 않는다), 네트워크 판단은 이 어댑터가
    /// 소유한다. <c>MeteorSpawner</c> 는 한 줄도 고치지 않는다.</para>
    ///
    /// <para><b>왜 필요한가.</b> <c>DemoMeteorDriver</c> 가 권위 검사 없이 양쪽에서
    /// <c>FixedUpdate</c> 로 <see cref="MeteorSpawner.Tick"/> 을 돌리고 있었다. 그 파일 주석이
    /// "게임플레이 코드에서는 스포너의 Tick 을 상위 시스템이 명시적으로 호출한다 … <b>그 상위
    /// 시스템이 아직 없으므로</b> 씬에서 확인할 수 있게 여기서 대신 돌린다" 고 적어 둔 자리를
    /// 이 어댑터가 맡는다.</para>
    ///
    /// <para><b>시드가 같아도 재현되지 않는다 — 그래서 참가자는 아예 돌리지 않는다.</b>
    /// <c>MeteorSpawner</c> 는 <c>System.Random(_seed)</c> 로 결정적 스케줄을 만들지만,
    /// <b>난수를 몇 번 뽑는지가 로컬 세계 상태에 달렸다</b>:
    /// <c>NearPlayerPoint</c> 는 <c>PlayerRig.All</c> 로 센 후보가 0이면 뽑기 전에 반환하고
    /// (<c>MeteorSpawner.NearPlayerPoint</c>), <c>WorkAreaPoint</c> 는 <c>clamp.Pullers</c>·
    /// <c>bead.BlobCount</c> 로 만든 목록이 비면 다른 갈래로 간다(<c>WorkAreaPoint</c>). 참가자 쪽
    /// 리그는 그림뿐이고 고정못 진행도는 호스트가 정하므로 그 값들이 다르다 →
    /// <b>첫 공세 안에 난수 스트림이 영구히 갈린다.</b> 공유 시계(<c>NetworkTime.time</c>)로도
    /// 못 고치는 종류다.</para>
    ///
    /// <para>그래서 참가자 화면에는 <b>운석 실물이 생기지 않는다.</b> 대신 <b>예고는 호스트에서
    /// 받는다</b>(<c>WorldSync.RpcApplyDrops</c> → <c>MeteorSpawner.ApplyNetworkPending</c>) —
    /// 참가자가 붉은 원을 보고 피하고 방망이 타이밍을 맞출 수 있다. 남은 시간은 이 어댑터가
    /// 로컬에서 센다(<c>AgeNetworkPending</c>).</para>
    ///
    /// <para>남은 것은 <b>낙하하는 돌덩이의 그림</b>이다. <c>Meteor</c> 컴포넌트를 끈 채 띄우면
    /// 충돌 콜백이 안 오므로(그 클래스에는 자체 <c>Update</c> 가 없다) 로컬 판정 없이 보이게만
    /// 할 수 있다 — <c>Meteor.cs</c> 를 열지 않아 열린 MR 과 충돌하지 않는다.</para>
    /// </summary>
    public sealed class MeteorClockBridge : MonoBehaviour
    {
        MeteorSpawner spawner;
        bool configured;
        bool loggedRole;

        /// <summary>직전 틱의 권위. 참 → 거짓으로 바뀌는 순간을 잡는다(FixedUpdate 주석).</summary>
        bool wasAuthority = true;

        public void Configure(MeteorSpawner meteorSpawner)
        {
            spawner = meteorSpawner;
            configured = spawner != null;
            if (!configured) return;

            // 씬의 임시 드라이버를 물러나게 한다. SpaceBootstrap 이 고정쇠·견인빔 데모
            // 드라이버를 끄는 것과 같은 처리다(DisableDemoClampReleasers·DisableDemoBeamDrivers).
            // 컴포넌트를 지우지 않고 끄기만 하는 이유: 씬을 고치지 않아도 되고, 혼자
            // 실행할 때 이 어댑터가 그대로 대신 돌기 때문이다.
            var demo = spawner.GetComponent<Gameplay.Demo.DemoMeteorDriver>();
            if (demo != null) demo.enabled = false;

            // 샌드박스 드라이버도 같은 스포너를 돌린다(Gameplay/Physics/SandboxMeteorDriver).
            // 지금은 어느 씬에도 없지만, 남아 있으면 권위 쪽에서 **두 번 틱** 이 돌아
            // 공세가 두 배로 빨라진다. 같이 물러나게 한다.
            var sandbox = spawner.GetComponent<Gameplay.SandboxMeteorDriver>();
            if (sandbox != null) sandbox.enabled = false;
        }

        void FixedUpdate()
        {

            using var frameSection = Metagame.Core.FrameSections.Scope(Metagame.Core.FrameSections.Meteors);   // S15P21B202-746 프레임 기록
            if (!configured || spawner == null) return;

            var authority = HasAuthority();

            // **권위를 잃는 순간 로컬 계산 결과를 버린다** (AI 리뷰 지적, 2026-09-08).
            //
            // 접속이 끝나기 전에는 NetworkClient.active 가 아직 거짓이라 참가자 기계도
            // 위 식에서 권위로 나온다(혼자 실행과 구별할 수 없다 — 그래서 그 식은
            // 옳다). 그동안 Tick 이 돌아 **진짜 운석과 예고가 이 기계에만 생긴다.**
            // 접속이 끝나 Tick 이 멈춰도 그것들은 남아, 호스트에 없는 돌과 착탄 판정이
            // 참가자 쪽에만 존재한다.
            //
            // ResetRun 을 쓴다 — 예고를 비우고 살아 있는 운석을 지우고 흔적까지
            // 정리한다. 참가자 쪽의 그 셋은 전부 **이 기계가 혼자 만든 것**이므로
            // 남길 이유가 하나도 없다.
            // 반대 전이도 치운다. 지금 구조에서는 호스트가 넘어오는 길이 없지만, 그림과
            // 진짜 운석이 겹쳐 뜨는 것은 눈에 바로 보이는 종류라 한 줄로 막아 둔다.
            if (!wasAuthority && authority)
            {
                spawner.ClearNetworkMeteors();

                // 흔적 그림도 버린다 (S15P21B202-355). 이 기계가 이제 착탄을 판정하므로
                // 진짜 흔적이 생긴다 — 그림을 남기면 돌이 두 벌로 보인다.
                spawner.ClearNetworkScars();
            }

            if (wasAuthority && !authority)
            {
                Debug.Log("[MeteorClockBridge] 권위를 넘긴다 — 접속 전에 이 기계가 만든 "
                          + "운석·예고·흔적을 버린다(호스트가 보낸 것으로 다시 채워진다)");
                spawner.ResetRun();
            }
            wasAuthority = authority;

            if (!authority)
            {
                if (!loggedRole)
                {
                    loggedRole = true;
                    Debug.Log("[MeteorClockBridge] 참가자다 — 운석 공세를 돌리지 않는다(호스트가 정한다)");
                }

                // 호스트가 보낸 예고(WorldSync.RpcApplyDrops)의 남은 시간만 센다. 목록은
                // 달라질 때만 오므로 그 사이를 여기서 메워 원이 매끄럽게 줄어들게 한다.
                // 스폰·난수·스케줄 전진은 하지 않는다 — 갈림의 원인이 그 셋이다.
                spawner.AgeNetworkPending(Time.fixedDeltaTime);

                // 굴러가는 흔적 그림을 다음 묶음까지 이어서 밟는다 (S15P21B202-658).
                //
                // `-658` 에서 흔적 그림을 **키네마틱**으로 바꿨다(콜라이더를 켜면서 —
                // 동적으로 두면 참가자가 돌을 밀어 버린다). 키네마틱 강체는
                // `linearVelocity` 를 무시하므로 예전처럼 속도만 넣어 두면 돌이 묶음과
                // 묶음 사이에 멈춰, `-434` 가 고친 "뚝뚝 끊김" 이 그대로 돌아온다.
                //
                // **여기가 맞는 자리다.** 물리 스텝마다 한 번 돌고, 참가자 갈래 안이라
                // 호스트에서는 불리지 않는다(호스트에는 그림이 없다).
                spawner.AdvanceScarGhosts(Time.fixedDeltaTime);
                return;
            }

            if (!loggedRole)
            {
                loggedRole = true;
                Debug.Log("[MeteorClockBridge] 권위가 있다 — 운석 공세를 여기서 돌린다");
            }

            // 인원수 보정(기획 §17.6). **권위 쪽에서만 부르므로** 이 목록이 곧 접속 인원이다 —
            // 참가자 기계에서 세면 원격 리그가 그림뿐이라 값이 갈렸던 것이 원인 중 하나였다.
            spawner.SetCrew(Mathf.Max(1, PlayerRig.All.Count));
            spawner.Tick(Time.fixedDeltaTime);
        }

        /// <summary>
        /// <see cref="DustStormClockBridge"/> 와 <b>같은 식</b>이어야 한다 — 두 위협이 서로 다른
        /// 기준으로 권위를 판단하면 한쪽만 도는 상태가 생긴다.
        ///
        /// <para><c>NetworkClient.active</c> 만 보면 안 된다: 방장은 게임 씬이 뜨는 동안 그 값이
        /// 거짓이라 전용 서버로 오인된다(<c>MatchRole</c>·<c>SpaceBootstrap</c> 주석이 같은
        /// 함정을 두 번 적어 두었다). 혼자 실행(<c>!NetworkClient.active</c>)은 권위가 있다.</para>
        ///
        /// <para><b>"접속 확정 전까지 틱을 보류하라" 는 지적을 받았고 그렇게 하지 않았다</b>
        /// (AI 리뷰 8차, 2026-09-08). 참가자가 그 창에 걸리지 않는다는 것을 확인했다 —
        /// <c>StartClient()</c> 는 <c>LobbyBootstrap</c>(<c>:185</c>) 또는
        /// <c>MirrorLaunchArgs</c>(<c>:117</c>)에서 <b>게임 씬이 뜨기 전에</b> 불리고,
        /// 그 순간 <c>NetworkClient.active</c> 가 참이 된다. 그러므로 이 어댑터가 도는
        /// 시점에 참가자는 이미 비권위로 판정된다.</para>
        ///
        /// <para>반대로 <c>NetworkClient.active</c> 가 거짓인 채 게임 씬이 도는 경우는
        /// <b>정말로 혼자 실행</b>일 때뿐이고, 그때는 <b>틱이 돌아야 맞다</b> — 보류하면
        /// 1인 플레이에서 운석이 아예 안 떨어진다. 접속 의사를 적어 둔 플래그도 없다
        /// (<c>MatchRole</c> 에는 <c>DedicatedServer</c> 하나뿐이다). 남는 잔재는
        /// <see cref="FixedUpdate"/> 의 권위 전이 정리가 치운다.</para>
        /// </summary>
        static bool HasAuthority()
        {
            return MatchRole.DedicatedServer || NetworkServer.active || !NetworkClient.active;
        }
    }
}
