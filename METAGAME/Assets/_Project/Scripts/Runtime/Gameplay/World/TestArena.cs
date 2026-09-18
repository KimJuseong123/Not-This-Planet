using System.Collections.Generic;
using Metagame.Gameplay.Parts;
using UnityEngine;

namespace Metagame.Gameplay
{
    /// <summary>
    /// 조립을 손으로 하지 않고 바로 시험하게 해 주는 진입 (S15P21B202-572).
    ///
    /// <para><b>왜 있는가.</b> 조립·용접이 걸린 것을 고칠 때마다 혼자 하기로 들어가
    /// 파츠를 찾고 고정못을 풀고 견인으로 끌어와 쌓아야 했다. 한 번 확인하는 데 드는
    /// 시간이 고치는 시간보다 길었다.</para>
    ///
    /// <para><b>새 씬을 만들지 않는다.</b> <c>Stage1_Astra</c> 는 3.3MB 짜리 씬이라
    /// 사본을 뜨면 그때부터 둘이 갈라지고 고칠 때마다 양쪽을 맞춰야 한다. 그리고
    /// "혼자 하기와 똑같이" 라는 요구가 사본과 정면으로 어긋난다. 같은 씬을 그대로
    /// 쓰고 <b>다 세워진 뒤에 배치만 옮긴다.</b></para>
    ///
    /// <para><b>조립대를 옮기지 않고 사람을 데려간다.</b> <c>ShipAssembly.LaunchPad</c> 는
    /// <c>IsOnPad</c> · 유도등 · 도크가 모두 기준으로 삼는 자리다. 옮기면 그것들이 함께
    /// 어긋난다.</para>
    /// </summary>
    public static class TestArena
    {
        /// <summary>로비 버튼이 켠다. 한 번 쓰고 끈다 — 다음 판까지 남으면 안 된다.</summary>
        public static bool Requested { get; set; }

        /// <summary>조립대에서 사람이 물러설 거리(m). 눈앞에 파츠가 들어오는 정도.</summary>
        const float StandBack = 7f;

        /// <summary>파츠를 늘어놓을 반경(m)과 간격(도).</summary>
        const float PartRing = 4.5f;
        const float PartSpread = 26f;

        /// <summary>풀린 고정못을 치워 둘 거리(m). 발밑에 굴러다니면 걸린다.</summary>
        const float ClampDump = 60f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => Requested = false;

        /// <summary>
        /// 요청이 있으면 배치를 옮긴다. 없으면 아무 일도 하지 않는다 —
        /// 혼자 하기 경로에 값이 하나도 달라지지 않는다.
        /// </summary>
        public static void ArrangeIfRequested(PlayerRig player)
        {
            if (!Requested) return;
            Requested = false;
            Arrange(player);
        }

        /// <summary>
        /// 배치를 지금 옮긴다. 로비의 시험 진입과 치트 시험장(<c>CheatArenaConsole</c>)이
        /// 같은 코드를 쓴다 — 배치 규칙이 두 벌이 되지 않게 한다.
        /// </summary>
        public static void Arrange(PlayerRig player)
        {
            if (player == null || player.Doll == null)
            {
                Debug.LogWarning("[TestArena] 사람이 아직 없어 배치를 건너뛴다.");
                return;
            }

            var pad = FindPad();
            var center = pad != null ? pad.position : player.Position;

            // 사람은 조립대에서 한 걸음 물러나 그쪽을 본다.
            var back = Vector3.right;                 // 스폰 시 카메라가 +X 를 본다
            var stand = center - back * StandBack;
            player.Doll.Teleport(stand + Vector3.up * RagdollBuilder.HipsY,
                                 Quaternion.LookRotation(back, Vector3.up));

            var parts = CollectCoreParts();
            PlaceParts(parts, stand, back);
            PlaceTools(stand, back);

            Debug.Log($"[TestArena] 시험 배치 — 핵심 파츠 {parts.Count}개를 "
                      + $"{stand} 앞에 놓았다 (조립 구역 {center}).");
        }

        static Transform FindPad()
        {
            var assembly = Object.FindAnyObjectByType<ShipAssembly>(FindObjectsInactive.Exclude);
            return assembly != null ? assembly.LaunchPad : null;
        }

        static List<SpaceshipPart> CollectCoreParts()
        {
            var found = new List<SpaceshipPart>(PartSpec.All.Length);
            var all = Object.FindObjectsByType<SpaceshipPart>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);

            // PartSpec 순서대로 담는다 — 늘어놓는 차례가 매번 같아야 눈이 익는다.
            for (var i = 0; i < PartSpec.All.Length; i++)
                for (var j = 0; j < all.Length; j++)
                    if (all[j] != null && all[j].name == PartSpec.All[i].Name)
                    {
                        found.Add(all[j]);
                        break;
                    }

            return found;
        }

        /// <summary>
        /// 파츠를 사람 앞에 부채꼴로 늘어놓고 고정못을 푼다.
        ///
        /// <para><b>고정못을 푸는 이유.</b> 푸는 것 자체를 시험하려는 것이 아니고, 그
        /// 단계가 가장 오래 걸린다. 풀린 고정못은 멀리 치운다 — 발밑에 굴러다니면
        /// 잡기 판정에 걸린다.</para>
        /// </summary>
        static void PlaceParts(List<SpaceshipPart> parts, Vector3 stand, Vector3 forward)
        {
            for (var i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                if (part == null) continue;

                ReleaseClamps(part, i);

                // 가운데를 기준으로 좌우로 펼친다.
                var angle = (i - (parts.Count - 1) * 0.5f) * PartSpread;
                var direction = Quaternion.AngleAxis(angle, Vector3.up) * forward;
                var spot = stand + direction * PartRing;

                var body = part.GetComponent<Rigidbody>();
                if (body != null)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                    // MovePosition 은 다음 물리 스텝을 기다린다. 배치는 지금 끝나야 한다.
                    body.position = spot + Vector3.up * 0.4f;
                    body.rotation = Quaternion.LookRotation(-direction, Vector3.up);
                }
                part.transform.SetPositionAndRotation(spot + Vector3.up * 0.4f,
                                                      Quaternion.LookRotation(-direction, Vector3.up));
            }

            Physics.SyncTransforms();
        }

        /// <summary>파츠의 고정못을 전부 풀어 멀리 치운다. 치트 시험장도 이것을 쓴다.</summary>
        internal static void ReleaseClamps(SpaceshipPart part, int index)
        {
            var clamps = part.Clamps;
            if (clamps == null) return;

            // 목록을 그대로 돌면 안 된다 — Release 가 고정못을 파츠에서 떼어내며
            // 이 목록을 건드린다.
            var snapshot = new List<PartClamp>(clamps);
            for (var i = 0; i < snapshot.Count; i++)
            {
                var clamp = snapshot[i];
                if (clamp == null) continue;

                clamp.transform.position = new Vector3(ClampDump + index * 3f, -20f, ClampDump);
                clamp.Release();
            }
        }

        /// <summary>도구를 발밑에 모은다. 용접기와 방망이를 찾으러 다니지 않게 한다.</summary>
        static void PlaceTools(Vector3 stand, Vector3 forward)
        {
            var tools = Object.FindObjectsByType<ToolBase>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);

            var side = Vector3.Cross(Vector3.up, forward).normalized;
            for (var i = 0; i < tools.Length; i++)
            {
                var tool = tools[i];
                if (tool == null || tool.IsEquipped) continue;

                var spot = stand + forward * 1.4f + side * (i - (tools.Length - 1) * 0.5f) * 0.8f;
                var body = tool.GetComponent<Rigidbody>();
                if (body != null)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                    body.position = spot + Vector3.up * 0.3f;
                }
                tool.transform.position = spot + Vector3.up * 0.3f;
            }

            Physics.SyncTransforms();
        }
    }
}
