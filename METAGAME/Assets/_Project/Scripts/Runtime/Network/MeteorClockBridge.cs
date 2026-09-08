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
    /// (<c>MeteorSpawner.cs:764</c>), <c>WorkAreaPoint</c> 는 <c>clamp.Pullers</c>·
    /// <c>bead.BlobCount</c> 로 만든 목록이 비면 다른 갈래로 간다(<c>:809</c>). 참가자 쪽
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
            if (!configured || spawner == null) return;

            if (!HasAuthority())
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
        /// </summary>
        static bool HasAuthority()
        {
            return MatchRole.DedicatedServer || NetworkServer.active || !NetworkClient.active;
        }
    }
}
