using System.Collections;
using UnityEngine;
using Metagame.Core;
using Metagame.Gameplay;
using Metagame.Gameplay.Parts;

namespace Metagame.Bootstrap
{
    /// <summary>
    /// 매치에 붙은 채로 사람 없이 조작해 보는 점검 (S15P21B202-149).
    ///
    /// <b>왜 필요한가.</b> 동기화 문제는 "둘이 붙어서 눌러 봐야" 드러난다. 그래서 고칠 때마다
    /// 사람에게 테스트를 부탁했고, 그 왕복이 원인 찾는 시간보다 길었다. 조작을 스크립트로
    /// 넣으면 혼자서 끝까지 확인할 수 있다.
    ///
    /// <b>어떻게 통하나.</b> <see cref="GameSession.ScriptedInput"/> 을 켜면 키보드를 읽지
    /// 않으므로, 여기서 넣은 입력이 그대로 남는다. 그 값을 <c>NetPlayer</c> 가 읽어 서버로
    /// 보내므로(실제 사람과 같은 경로), 서버 판정까지 전부 지나간다.
    ///
    /// 조준은 카메라 각도로 전달된다. 그래서 방향을 바꿀 때 프레임이 아니라 카메라를 돌린다.
    ///
    /// 실행: <c>MetaGame.exe -match-drive</c> (참가자로 붙는 창에 붙인다)
    /// </summary>
    public sealed class MatchDriveTest : MonoBehaviour
    {
        const string Flag = "-match-drive";

        /// <summary>이 거리 안에 들어오면 탑승을 시도한다.</summary>
        const float BoardRange = 2.5f;

        /// <summary>걸어가기를 포기하는 시간. 못 닿으면 그 사실 자체가 결과다.</summary>
        const float ApproachTimeout = 25f;

        static bool Requested =>
            System.Array.IndexOf(System.Environment.GetCommandLineArgs(), Flag) >= 0;

        /// <summary>
        /// 스스로 붙는다. 네트워크 계층에서 붙이려 했으나 그쪽은 Bootstrap 을 참조할 수 없다
        /// (참조 방향이 Bootstrap → Network 라 반대로 걸면 순환이 된다).
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (!Requested) return;

            var host = new GameObject("~자동 조작 점검");
            DontDestroyOnLoad(host);
            host.AddComponent<MatchDriveTest>();
        }

        GameSession session;
        PlayerRig me;

        IEnumerator Start()
        {
            // 게임 씬과 내 몸이 설 때까지 기다린다.
            float waited = 0f;
            while (waited < 60f)
            {
                session = FindAnyObjectByType<GameSession>();
                me = session != null ? session.LocalPlayer : null;
                if (me != null && me.Cam != null) break;

                waited += Time.deltaTime;
                yield return null;
            }

            if (me == null)
            {
                Debug.LogError("[MatchDrive] 내 몸이 서지 않았다 — 매치에 못 들어갔다.");
                yield break;
            }

            session.ScriptedInput = true;
            Debug.Log("[MatchDrive] 시작 — 키보드 대신 스크립트로 조작한다.");

            yield return new WaitForSeconds(2f);   // 월드 동기화가 자리를 잡을 틈

            yield return Approach();
            yield return Board();
            yield return Drive();
            yield return Interact();

            yield return Clamp();
            yield return Weld();

            Debug.Log("[MatchDrive] 끝.");
        }

        /// <summary>가장 가까운 탈것까지 걸어간다.</summary>
        IEnumerator Approach()
        {
            var vehicle = Nearest();
            if (vehicle == null)
            {
                Debug.LogError("[MatchDrive] 탈것을 찾지 못했다.");
                yield break;
            }

            Debug.Log($"[MatchDrive] 탈것 {vehicle.name} · 거리 {Distance(vehicle):0.0}m");

            float spent = 0f;
            while (spent < ApproachTimeout && Distance(vehicle) > BoardRange)
            {
                FaceToward(vehicle.transform.position);
                Push(new InputFrame { Move = new Vector2(0f, 1f) });

                spent += Time.deltaTime;
                yield return null;
            }

            Push(new InputFrame());
            Debug.Log($"[MatchDrive] 접근 끝 · 거리 {Distance(vehicle):0.0}m · {spent:0.0}초 걸림");
        }

        /// <summary>탑승 버튼을 누른다. 눌린 순간만 세므로 몇 프레임 유지한다.</summary>
        IEnumerator Board()
        {
            var vehicle = Nearest();
            Vector3 before = vehicle != null ? vehicle.transform.position : Vector3.zero;

            Debug.Log("[MatchDrive] 탑승 시도");
            for (int i = 0; i < 8; i++)
            {
                Push(new InputFrame { TransportAction = true });
                yield return null;
            }
            Push(new InputFrame());

            yield return new WaitForSeconds(2f);
            Debug.Log($"[MatchDrive] 탑승 뒤 · 내 높이 {me.Position.y:0.00} · "
                      + $"탈것 이동 {(vehicle != null ? Vector3.Distance(before, vehicle.transform.position) : 0f):0.00}m");
        }

        /// <summary>앞으로 몰아 본다. 탈것이 실제로 움직이는지가 이 점검의 핵심이다.</summary>
        IEnumerator Drive()
        {
            var vehicle = Nearest();
            if (vehicle == null) yield break;

            Vector3 start = vehicle.transform.position;
            Vector3 mineStart = me.Position;

            Debug.Log("[MatchDrive] 전진 3초");
            float spent = 0f;
            while (spent < 3f)
            {
                Push(new InputFrame { Move = new Vector2(0f, 1f) });
                spent += Time.deltaTime;
                yield return null;
            }
            Push(new InputFrame());
            yield return new WaitForSeconds(1f);

            float moved = Vector3.Distance(start, vehicle.transform.position);
            float mineMoved = Vector3.Distance(mineStart, me.Position);

            Debug.Log($"[MatchDrive] 전진 결과 · 탈것 {moved:0.00}m · 나 {mineMoved:0.00}m");

            // 둘이 함께 움직여야 정상이다. 나만 움직였으면 탈것이 안 따라온 것이고,
            // 둘 다 안 움직였으면 애초에 타지 못한 것이다.
            if (moved < 0.5f && mineMoved > 1f)
                Debug.LogError("[MatchDrive] 나만 움직였다 — 탈것이 동기화되지 않는다.");
            else if (moved < 0.5f)
                Debug.LogWarning("[MatchDrive] 둘 다 안 움직였다 — 탑승 자체가 되지 않았을 수 있다.");
            else
                Debug.Log("[MatchDrive] 탈것이 함께 움직였다 — 정상.");
        }

        /// <summary>내려서 잡기와 고정쇠를 눌러 본다.</summary>
        IEnumerator Interact()
        {
            Debug.Log("[MatchDrive] 하차");
            for (int i = 0; i < 8; i++)
            {
                Push(new InputFrame { TransportAction = true });
                yield return null;
            }
            Push(new InputFrame());
            yield return new WaitForSeconds(3f);

            // 부품 앞으로 걸어간다 — 그냥 누르면 그 자리에 잡을 것이 없어 None 이 나온다.
            var part = NearestPart();
            if (part == null)
            {
                Debug.LogError("[MatchDrive] 부품을 찾지 못했다.");
                yield break;
            }

            Debug.Log($"[MatchDrive] 부품 {part.name} 으로 · 거리 {Vector3.Distance(me.Position, part.transform.position):0.0}m");

            float spent = 0f;
            while (spent < ApproachTimeout
                   && Vector3.Distance(me.Position, part.transform.position) > 1.6f)
            {
                FaceToward(part.transform.position);
                Push(new InputFrame { Move = new Vector2(0f, 1f) });
                spent += Time.deltaTime;
                yield return null;
            }
            Push(new InputFrame());

            // 부품을 바라본 채 잠깐 두고 조준이 잡히는지 본다.
            for (int i = 0; i < 30; i++)
            {
                FaceToward(part.transform.position);
                Push(new InputFrame());
                yield return null;
            }

            Debug.Log($"[MatchDrive] 부품 앞 · 거리 {Vector3.Distance(me.Position, part.transform.position):0.0}m · "
                      + $"내 화면이 본 대상={me.CurrentInteractionTarget.Kind}");

            for (int i = 0; i < 90; i++)
            {
                FaceToward(part.transform.position);
                Push(new InputFrame { GrabRight = true });
                yield return null;
            }
            Push(new InputFrame());
            yield return new WaitForSeconds(1f);

            // 조준만 되고 실제로 들리지 않으면 뜻이 없다. 들고 뒤로 걸어가 부품이 따라오는지 본다.
            Vector3 partStart = part.transform.position;
            Vector3 meStart = me.Position;
            for (int i = 0; i < 120; i++)
            {
                Push(new InputFrame { GrabRight = true, Move = new Vector2(0f, -1f) });
                yield return null;
            }
            Push(new InputFrame());
            yield return new WaitForSeconds(1f);

            float partMoved = Vector3.Distance(partStart, part.transform.position);
            float meMoved = Vector3.Distance(meStart, me.Position);
            Debug.Log($"[MatchDrive] 들고 이동 · 나 {meMoved:0.00}m · 부품 {partMoved:0.00}m 따라옴 · "
                      + $"대상={me.CurrentInteractionTarget.Kind}");
            if (partMoved < 0.3f)
                Debug.LogError("[MatchDrive] 부품이 따라오지 않았다 — 잡기가 서버에서 안 걸린다.");
            else
                Debug.Log("[MatchDrive] 부품이 따라왔다 — 정상.");
        }

        /// <summary>
        /// 고정쇠를 뽑아 본다. 진행도는 서버가 정하고 내려 주므로(-149),
        /// 이 화면에서 값이 올라가면 그 전달이 실제로 되는 것이다.
        /// </summary>
        IEnumerator Clamp()
        {
            var clamp = NearestClamp();
            if (clamp == null)
            {
                Debug.LogWarning("[MatchDrive] 고정쇠를 찾지 못했다.");
                yield break;
            }

            Debug.Log($"[MatchDrive] 고정쇠 {clamp.name} 으로 · 거리 {Vector3.Distance(me.Position, clamp.transform.position):0.0}m");

            float spent = 0f;
            while (spent < ApproachTimeout
                   && Vector3.Distance(me.Position, clamp.transform.position) > 1.5f)
            {
                FaceToward(clamp.transform.position);
                Push(new InputFrame { Move = new Vector2(0f, 1f) });
                spent += Time.deltaTime;
                yield return null;
            }
            Push(new InputFrame());

            float before = clamp.Progress;
            Debug.Log($"[MatchDrive] 고정쇠 앞 · 대상={me.CurrentInteractionTarget.Kind} · 진행도 {before:0.00}");

            // 잡아 당긴다. 서버가 진행도를 올려 내려 주기를 기다린다.
            for (int i = 0; i < 300; i++)
            {
                FaceToward(clamp.transform.position);
                Push(new InputFrame { GrabRight = true });
                yield return null;
            }
            Push(new InputFrame());
            yield return new WaitForSeconds(1f);

            Debug.Log($"[MatchDrive] 고정쇠 결과 · 진행도 {before:0.00} → {clamp.Progress:0.00} · "
                      + $"풀림={(clamp.IsReleased ? "O" : "X")}");

            if (clamp.Progress <= before + 0.01f && !clamp.IsReleased)
                Debug.LogError("[MatchDrive] 진행도가 오르지 않았다 — 서버가 안 올렸거나 전달이 안 된다.");
            else
                Debug.Log("[MatchDrive] 진행도가 내려왔다 — 정상.");
        }

        /// <summary>
        /// 용접건을 들고 쏴 본다. 비드는 서버가 만들고 그 <b>행동</b>이 내려온다(-149).
        /// 이 화면에서 비드가 늘어나면 그 전달이 실제로 되는 것이다.
        /// </summary>
        IEnumerator Weld()
        {
            var tool = FindAnyObjectByType<PlasmaWeldingTool>();
            if (tool == null)
            {
                Debug.LogWarning("[MatchDrive] 용접건을 찾지 못했다.");
                yield break;
            }

            Debug.Log($"[MatchDrive] 용접건으로 · 거리 {Vector3.Distance(me.Position, tool.transform.position):0.0}m");

            float spent = 0f;
            while (spent < ApproachTimeout
                   && Vector3.Distance(me.Position, tool.transform.position) > 1.4f)
            {
                FaceToward(tool.transform.position);
                Push(new InputFrame { Move = new Vector2(0f, 1f) });
                spent += Time.deltaTime;
                yield return null;
            }
            Push(new InputFrame());

            for (int i = 0; i < 30; i++)
            {
                FaceToward(tool.transform.position);
                Push(new InputFrame());
                yield return null;
            }
            Debug.Log($"[MatchDrive] 용접건 앞 · 대상={me.CurrentInteractionTarget.Kind}");

            // 집는다 — 도구는 양손 그립이라 잠시 눌러 준다.
            for (int i = 0; i < 90; i++)
            {
                FaceToward(tool.transform.position);
                Push(new InputFrame { GrabRight = true });
                yield return null;
            }
            Push(new InputFrame());
            yield return new WaitForSeconds(1f);

            bool held = me.RightHand != null && me.RightHand.Held != null;
            Debug.Log($"[MatchDrive] 용접건 집기 · 내 화면 기준 {(held ? "들었다" : "못 들었다")}");

            // 부품 쪽을 보고 쏜다.
            var part = NearestPart();
            int before = WeldBead.All.Count;

            for (int i = 0; i < 240; i++)
            {
                if (part != null) FaceToward(part.transform.position);
                Push(new InputFrame { ToolPrimary = true });
                yield return null;
            }
            Push(new InputFrame());
            yield return new WaitForSeconds(1f);

            int after = WeldBead.All.Count;
            Debug.Log($"[MatchDrive] 용접 결과 · 비드 {before} → {after}");

            if (after > before)
                Debug.Log("[MatchDrive] 비드가 내려왔다 — 용접 동기화 정상.");
            else
                Debug.LogWarning("[MatchDrive] 비드가 늘지 않았다 — 도구를 못 들었거나 전달이 안 된다.");
        }

        PartClamp NearestClamp()
        {
            PartClamp best = null;
            float bestDistance = float.MaxValue;

            foreach (var clamp in FindObjectsByType<PartClamp>(FindObjectsSortMode.None))
            {
                if (clamp == null || clamp.IsReleased) continue;

                float distance = Vector3.Distance(me.Position, clamp.transform.position);
                if (distance >= bestDistance) continue;

                bestDistance = distance;
                best = clamp;
            }
            return best;
        }

        // ── 도구 ────────────────────────────────────────────────

        void Push(InputFrame frame) => me.SetInput(frame);

        /// <summary>카메라를 그쪽으로 돌린다. 조준은 카메라 각도로 서버에 전달된다.</summary>
        void FaceToward(Vector3 point)
        {
            Vector3 flat = point - me.Position;
            flat.y = 0f;
            if (flat.sqrMagnitude < 1e-4f) return;

            me.Cam.Yaw = Quaternion.LookRotation(flat.normalized, Vector3.up).eulerAngles.y;
        }

        float Distance(TransportVehicle vehicle) =>
            vehicle == null ? float.MaxValue : Vector3.Distance(me.Position, vehicle.transform.position);

        SpaceshipPart NearestPart()
        {
            SpaceshipPart best = null;
            float bestDistance = float.MaxValue;

            foreach (var part in FindObjectsByType<SpaceshipPart>(FindObjectsSortMode.None))
            {
                if (part == null) continue;

                float distance = Vector3.Distance(me.Position, part.transform.position);
                if (distance >= bestDistance) continue;

                bestDistance = distance;
                best = part;
            }
            return best;
        }

        TransportVehicle Nearest()
        {
            TransportVehicle best = null;
            float bestDistance = float.MaxValue;

            foreach (var vehicle in FindObjectsByType<TransportVehicle>(FindObjectsSortMode.None))
            {
                if (vehicle == null) continue;

                float distance = Vector3.Distance(me.Position, vehicle.transform.position);
                if (distance >= bestDistance) continue;

                bestDistance = distance;
                best = vehicle;
            }
            return best;
        }
    }
}
