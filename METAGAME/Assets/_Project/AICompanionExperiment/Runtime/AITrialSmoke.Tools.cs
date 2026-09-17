using System.Collections;
using Metagame.Gameplay;
using UnityEngine;

namespace Metagame.AIExperiment
{
    public sealed partial class AITrialSmoke
    {
        // Explicit two-process QA only. Uses deterministic intents, not microphone or model calls.
        IEnumerator ToolsSmoke()
        {
            float deadline = Time.realtimeSinceStartup + 40;
            NetworkCompanion bot = null;
            while (Time.realtimeSinceStartup < deadline)
            {
                bot = FindAnyObjectByType<NetworkCompanion>();
                if (bot != null && bot.Rig != null) break;
                yield return null;
            }
            if (bot == null || bot.Rig == null) { Debug.LogError("[AI_TOOLS_QA] spawn timeout"); Application.Quit(2); yield break; }
            var session = FindAnyObjectByType<GameSession>();
            session.ScriptedInput = true;
            _emoteCamera = session.LocalPlayer.Cam.Cam;
            _emoteSubject = bot;
            var bat = FindAnyObjectByType<MeteorBat>();
            if (!bot.CanControl)
            {
                bool held = false, swung = false, dropped = false;
                deadline = Time.realtimeSinceStartup + 105;
                while (Time.realtimeSinceStartup < deadline)
                {
                    bool bound = bat.IsHeldForView && bat.ViewOwner == bot.Rig;
                    if (bound && !held)
                    {
                        held = true;
                        Debug.Log($"[AI_TOOLS_QA] observer held owner={bot.netId}");
                        yield return new WaitForSeconds(.5f);
                        ScreenCapture.CaptureScreenshot(System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "tools-observer.png")));
                    }
                    if (bound && bat.ViewAction != 0 && !swung)
                    { swung = true; Debug.Log("[AI_TOOLS_QA] observer swing replicated"); }
                    if (held && !bat.IsHeldForView)
                    { dropped = true; Debug.Log("[AI_TOOLS_QA] observer drop replicated"); break; }
                    yield return null;
                }
                Debug.Log($"[AI_TOOLS_QA] observer result held={held} swing={swung} drop={dropped}");
                Application.Quit(held && swung && dropped ? 0 : 3);
                yield break;
            }
            yield return new WaitForSeconds(12);
            var trial = FindAnyObjectByType<AICompanionTrial>();
            session.LocalPlayer.Teleport(new Vector3(1, .2f, -3), Quaternion.identity);
            bool accepted = trial.ReceiveIntent("몽둥이 주워줘", "pickup_bat");
            foreach (var player in PlayerRig.All)
                if (player != bot.Rig) player.Teleport(new Vector3(7, .2f, 3), Quaternion.identity);
            deadline = Time.realtimeSinceStartup + 36;
            while (bot.Order == CompanionOrder.PickUpBat && Time.realtimeSinceStartup < deadline) yield return null;
            if (!accepted || bot.Rig.ActiveTool != bat)
            { Debug.LogError("[AI_TOOLS_QA] pickup failed " + bot.Status); Application.Quit(3); yield break; }
            Debug.Log("[AI_TOOLS_QA] host pickup without firing=" + (bat.RhythmSequence == 0));
            yield return new WaitForSeconds(3);
            session.LocalPlayer.Teleport(new Vector3(1, .2f, -3), Quaternion.identity);
            accepted = trial.ReceiveIntent("공 때려줘", "hit_target");
            session.LocalPlayer.Teleport(new Vector3(7, .2f, 3), Quaternion.identity);
            deadline = Time.realtimeSinceStartup + 36;
            while (bot.Order == CompanionOrder.HitTarget && Time.realtimeSinceStartup < deadline) yield return null;
            var target = FindAnyObjectByType<PracticeMeteorTarget>();
            if (!accepted || target.HitCount == 0)
            { Debug.LogError("[AI_TOOLS_QA] hit failed " + bot.Status); Application.Quit(3); yield break; }
            Debug.Log("[AI_TOOLS_QA] host real ball hit=" + target.HitCount);
            yield return new WaitForSeconds(3);
            trial.ReceiveIntent("내려놔", "drop_tool");
            yield return new WaitForSeconds(2);
            bool droppedHost = bot.Rig.HeldTool == null && !bat.IsEquipped;
            Debug.Log("[AI_TOOLS_QA] host drop=" + droppedHost);
            yield return new WaitForSeconds(5);
            Application.Quit(droppedHost ? 0 : 3);
        }
    }
}
