using System.Collections;
using Metagame.Gameplay;
using Metagame.Gameplay.Parts;
using Mirror;
using UnityEngine;

namespace Metagame.AIExperiment
{
    /// <summary>Opt-in build validation (-aiSmoke); never runs in an ordinary play session.</summary>
    [DefaultExecutionOrder(10000)]
    public sealed partial class AITrialSmoke : MonoBehaviour
    {
        Camera _emoteCamera;
        NetworkCompanion _emoteSubject;

        void LateUpdate()
        {
            if (_emoteCamera == null || _emoteSubject == null) return;
            var target = _emoteSubject.EmoteAnchor - Vector3.up * .4f;
            var offset = _emoteSubject.CanControl ? new Vector3(3, 1, 1) : new Vector3(2.4f, 1, 2.5f);
            _emoteCamera.transform.SetPositionAndRotation(target + offset, Quaternion.LookRotation(-offset));
        }
        IEnumerator Start()
        {
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-aiToolsSmoke") >= 0)
            { yield return ToolsSmoke(); yield break; }
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-aiEmoteSmoke") >= 0)
            { yield return EmoteSmoke(); yield break; }
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
                var pin = FindTestPin();
                if (pin == null) { Debug.LogError("[AI_SMOKE] test pin missing"); Application.Quit(4); yield break; }
                var center = CompanionPinTarget.Center(pin);
                session.LocalPlayer.Teleport(new Vector3(center.x, 0.2f, center.z - 0.8f), Quaternion.identity);
                for (float end = Time.realtimeSinceStartup + 0.3f; Time.realtimeSinceStartup < end;)
                {
                    var angles = Quaternion.LookRotation(center - session.LocalPlayer.AimOrigin).eulerAngles;
                    session.LocalPlayer.Cam.Yaw = angles.y;
                    session.LocalPlayer.Cam.Pitch = Mathf.DeltaAngle(0, angles.x);
                    yield return null;
                }
                session.LocalPlayer.RefreshInteractionFocus();
                trial.SendText("이거 못 뽑아줘");
                while (trial.Busy) yield return null;
                Debug.Log("[AI_SMOKE] pin classification=" + trial.Decision + " receipt=" + trial.Receipt);
                session.LocalPlayer.Teleport(new Vector3(-1, 0.2f, 2), Quaternion.identity);
                if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-aiTraceScreenshot") >= 0)
                {
                    // Opt-in QA capture in the ignored build directory, not a production asset.
                    yield return null;
                    ScreenCapture.CaptureScreenshot(System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "ai-trace.png")));
                }
                for (float end = Time.realtimeSinceStartup + 32; pin != null && !pin.IsReleased && Time.realtimeSinceStartup < end;) yield return null;
                if (pin == null || !pin.IsReleased) { Debug.LogError("[AI_SMOKE] pin release failed"); Application.Quit(4); yield break; }
                Debug.Log("[AI_SMOKE] real pin released via voice-intent text path");
            }
            float finish = Time.realtimeSinceStartup + (authority ? 8 : 65);
            while (Time.realtimeSinceStartup < finish)
            {
                Debug.Log($"[AI_SMOKE] {(authority ? "host" : "observer")} id={companion.netId} order={companion.Order} position={companion.transform.position:F2}");
                var observedPin = FindTestPin();
                if (observedPin != null) Debug.Log($"[AI_SMOKE] pin {(authority ? "host" : "observer")} progress={observedPin.Progress:F3} released={observedPin.IsReleased}");
                for (float end = Time.realtimeSinceStartup + 1; Time.realtimeSinceStartup < end;) yield return null;
            }
            Application.Quit();
        }

        static PartClamp FindTestPin()
        {
            var pins = FindObjectsByType<PartClamp>();
            foreach (var pin in pins)
                if (pin.name == "PIN 03") return pin;
            return pins.Length == 1 ? pins[0] : null;
        }

        // Opt-in QA only: no model request, no gameplay change in ordinary launches.
        IEnumerator EmoteSmoke()
        {
            var until = Time.realtimeSinceStartup + 35;
            NetworkCompanion bot = null;
            while (Time.realtimeSinceStartup < until)
            {
                bot = FindAnyObjectByType<NetworkCompanion>();
                if (bot != null && bot.Rig != null) break;
                yield return null;
            }
            if (bot == null || bot.Rig == null) { Application.Quit(2); yield break; }
            var session = FindAnyObjectByType<GameSession>();
            session.ScriptedInput = true;
            _emoteCamera = session.LocalPlayer.Cam.Cam;
            _emoteSubject = bot;
            bool seen = false;
            bool expired = false;
            bool sent = false;
            double observedExpiry = 0;
            until = Time.realtimeSinceStartup + (bot.CanControl ? 30 : 24);
            float sendAt = Time.realtimeSinceStartup + 10;
            while (Time.realtimeSinceStartup < until)
            {
                if (bot.CanControl && !sent && Time.realtimeSinceStartup >= sendAt)
                {
                    FindAnyObjectByType<AICompanionTrial>().ReceiveResponseJson("{\"text\":\"용접해줘\",\"action\":\"none\"}");
                    sent = true;
                }
                bool active = Metagame.Core.EmoteState.IsActive(bot.EmoteId, bot.EmoteExpiresAt, bot.EmoteNow);
                if (active && !seen)
                {
                    seen = true;
                    observedExpiry = bot.EmoteExpiresAt;
                    Debug.Log($"[AI_EMOTE_SMOKE] visible role={(bot.CanControl ? "host" : "observer")} netId={bot.netId} id={bot.EmoteId} expiry={observedExpiry:F3}");
                    yield return new WaitForSeconds(.4f);
                    ScreenCapture.CaptureScreenshot(System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", bot.CanControl ? "emote-host.png" : "emote-observer.png")));
                }
                if (seen && !active && !expired)
                {
                    expired = true;
                    Debug.Log("[AI_EMOTE_SMOKE] expired normally");
                }
                yield return null;
            }
            Debug.Log($"[AI_EMOTE_SMOKE] result seen={seen} expired={expired} expiry={observedExpiry:F3}");
            Application.Quit(seen && expired ? 0 : 3);
        }
    }
}
