using System.Collections;
using Metagame.Gameplay;
using Metagame.Gameplay.Parts;
using Mirror;
using UnityEngine;

namespace Metagame.AIExperiment
{
    /// <summary>Opt-in build validation (-aiSmoke); never runs in an ordinary play session.</summary>
    public sealed class AITrialSmoke : MonoBehaviour
    {
        IEnumerator Start()
        {
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-aiSmoke") < 0) yield break;
            float deadline = Time.realtimeSinceStartup + 35;
            NetworkCompanion companion = null;
            while (Time.realtimeSinceStartup < deadline)
            {
                companion = FindAnyObjectByType<NetworkCompanion>();
                if (companion != null && companion.Rig != null) break;
                yield return null;
            }
            if (companion == null || companion.Rig == null) { Debug.LogError("[AI_SMOKE] spawn timeout"); Application.Quit(2); yield break; }
            bool authority = companion.CanControl;
            Debug.Log($"[AI_SMOKE] role={(authority ? "host" : "observer")} count={FindObjectsByType<NetworkCompanion>(FindObjectsSortMode.None).Length} netId={companion.netId}");
            if (authority)
            {
                // Leave enough time for the second build process to join before moving.
                for (float end = Time.realtimeSinceStartup + 12; Time.realtimeSinceStartup < end;) yield return null;
                var session = FindAnyObjectByType<GameSession>();
                session.ScriptedInput = true;
                session.LocalPlayer.Teleport(new Vector3(-2, 0.2f, -2.5f), Quaternion.identity);
                var trial = FindAnyObjectByType<AICompanionTrial>();
                trial.SendText("나 좀 따라와");
                while (trial.Busy) yield return null;
                Debug.Log("[AI_SMOKE] actual local LLM result=" + trial.Decision);
                if (trial.Decision != "follow") { Debug.LogError("[AI_SMOKE] classifier failed"); Application.Quit(3); yield break; }
                for (float end = Time.realtimeSinceStartup + 8; Time.realtimeSinceStartup < end;) yield return null;
                Debug.Log($"[AI_SMOKE] arrived distance={Vector3.Distance(companion.Rig.Hips.position, session.LocalPlayer.Hips.position):F2} clients={NetworkServer.connections.Count}");
                trial.SendText("따라오지 마");
                while (trial.Busy) yield return null;
                Debug.Log("[AI_SMOKE] cancellation=" + companion.Order);
                trial.SendText("이거 못 뽑아줘");
                while (trial.Busy) yield return null;
                Debug.Log("[AI_SMOKE] pin classification=" + trial.Decision + " receipt=" + trial.Receipt);
                var pin = FindAnyObjectByType<PartClamp>();
                for (float end = Time.realtimeSinceStartup + 32; pin != null && !pin.IsReleased && Time.realtimeSinceStartup < end;) yield return null;
                if (pin == null || !pin.IsReleased) { Debug.LogError("[AI_SMOKE] pin release failed"); Application.Quit(4); yield break; }
                Debug.Log("[AI_SMOKE] real pin released via voice-intent text path");
            }
            float finish = Time.realtimeSinceStartup + (authority ? 8 : 65);
            while (Time.realtimeSinceStartup < finish)
            {
                Debug.Log($"[AI_SMOKE] {(authority ? "host" : "observer")} id={companion.netId} order={companion.Order} position={companion.transform.position:F2}");
                var observedPin = FindAnyObjectByType<PartClamp>();
                if (observedPin != null) Debug.Log($"[AI_SMOKE] pin {(authority ? "host" : "observer")} progress={observedPin.Progress:F3} released={observedPin.IsReleased}");
                for (float end = Time.realtimeSinceStartup + 1; Time.realtimeSinceStartup < end;) yield return null;
            }
            Application.Quit();
        }
    }
}
