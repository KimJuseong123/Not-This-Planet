using System.IO;
using System;
using System.Collections;
using System.Linq;
using System.Text;
using Metagame.Gameplay;
using Metagame.Gameplay.Parts;
using Metagame.Gameplay.Hazards;
using Metagame.Core;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Metagame.EditorTools
{
    // Editor-only diagnostics. Run mutates temporary Play state; it never saves scene data.
    [InitializeOnLoad]
    public static class Stage1ConceptInteractionCheck
    {
        static Stage1ConceptInteractionCheck()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (!SessionState.GetBool("ConceptManual.Batch", false)) return;
                if (state == PlayModeStateChange.EnteredPlayMode) Begin();
                if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool("ConceptManual.Finished", false))
                    EditorApplication.Exit(SessionState.GetInt("ConceptManual.Exit", 1));
            };
        }

        public static void ManualBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Batch only.");
            SessionState.SetBool("Metagame.URPSetupApplied", true);
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(Stage1ConceptPlayableBuilder.ScenePath);
            Stage1ConceptPlayableBuilder.EnableManualTransport();
            SessionState.SetBool("ConceptManual.Batch", true);
            SessionState.SetBool("ConceptManual.Finished", false);
            EditorApplication.EnterPlaymode();
        }
        private static IEnumerator steps;
        private static double resume;
        private static readonly StringBuilder results = new StringBuilder();
        private static PlayerRig testedPlayer;
        private static Vector3 aimPoint;
        private static bool aiming;
        private static Collider trackedCollider;
        private static bool manualScenario;

        public static void BeginManual()
        {
            manualScenario = true;
            Begin();
        }

        public static void Begin()
        {
            if (!EditorApplication.isPlaying || steps != null) throw new InvalidOperationException("Start a fresh concept Play session first.");
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != Stage1ConceptPlayableBuilder.ScenePath)
                throw new InvalidOperationException("Only the concept playable scene is in scope.");
            results.Clear();
            steps = (manualScenario || SessionState.GetBool("ConceptManual.Batch", false)) ? RunManual() : Run();
            resume = 0;
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            try
            {
                if (!EditorApplication.isPlaying) { Finish(); return; }
                if (aiming && testedPlayer != null)
                {
                    if (trackedCollider != null) aimPoint = trackedCollider.bounds.ClosestPoint(testedPlayer.AimOrigin);
                    Vector3 direction = (aimPoint - testedPlayer.AimOrigin).normalized;
                    testedPlayer.Cam.Yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
                    testedPlayer.Cam.Pitch = -Mathf.Asin(direction.y) * Mathf.Rad2Deg;
                    testedPlayer.RefreshInteractionFocus();
                }
                if (Time.timeAsDouble < resume) return;
                if (!steps.MoveNext()) { Finish(); return; }
                resume = Time.timeAsDouble + Convert.ToDouble(steps.Current);
            }
            catch (Exception e) { results.AppendLine("ERROR " + e); Finish(); }
        }

        private static void Finish()
        {
            aiming = false;
            trackedCollider = null;
            steps = null;
            EditorApplication.update -= Tick;
            bool batch = SessionState.GetBool("ConceptManual.Batch", false);
            bool manual = manualScenario || batch;
            File.WriteAllText(Path.GetFullPath(manual ? "../concept-playtest/manual-transport.txt" : "../concept-playtest/interaction-input.txt"), DateTime.Now.ToString("s") + "\n" + results);
            if (batch)
            {
                SessionState.SetBool("ConceptManual.Finished", true);
                SessionState.SetInt("ConceptManual.Exit", results.ToString().Contains("ERROR") ? 1 : 0);
            }
            manualScenario = false;
            EditorApplication.ExitPlaymode(); // never save changes made by the test
        }

        private static void Approach(Vector3 point, Vector3 outward, float distance)
        {
            aiming = true;
            aimPoint = point;
            Vector3 foot = point + outward * distance;
            if (Physics.Raycast(foot + Vector3.up, Vector3.down, out var hit, 30, GameLayers.GroundMask))
                foot.y = hit.point.y + .03f;
            testedPlayer.SetInput(default);
            testedPlayer.Teleport(foot, Quaternion.LookRotation(-outward));
        }

        private static IEnumerator Run()
        {
            yield return 1.0;
            testedPlayer = GameSession.Instance.LocalPlayer;
            GameSession.Instance.ScriptedInput = true;
            GameSession.Instance.SetCursor(false);
            Object.FindFirstObjectByType<MeteorSpawner>()?.SetPaused(true);
            foreach (var part in Object.FindObjectsByType<SpaceshipPart>(FindObjectsSortMode.None).OrderBy(p=>p.name))
            {
                foreach (var pin in part.GetComponentsInChildren<PartClamp>().OrderBy(c=>c.name))
                {
                    var pinHead = pin.GetComponentsInChildren<Collider>().First(c=>c.name == "Head_Collider");
                    Vector3 side = Vector3.ProjectOnPlane(pin.transform.position - part.transform.position, Vector3.up).normalized;
                    Approach(pinHead.bounds.center, side, .45f);
                    yield return 1.0;
                    results.AppendLine($"REACH {part.name}/{pin.name} allowed={testedPlayer.CurrentInteractionTarget.CanInteract} kind={testedPlayer.CurrentInteractionTarget.Kind}");
                }
                var clamp = part.GetComponentsInChildren<PartClamp>().OrderBy(c=>c.name).First();
                var head = clamp.GetComponentsInChildren<Collider>().First(c=>c.name == "Head_Collider");
                Vector3 outward = Vector3.ProjectOnPlane(clamp.transform.position - part.transform.position, Vector3.up).normalized;
                Approach(head.bounds.center, outward, .45f);
                yield return 1.0;
                results.AppendLine($"{part.name} before click foot={testedPlayer.FootPosition} target={testedPlayer.CurrentInteractionTarget.Kind} allowed={testedPlayer.CurrentInteractionTarget.CanInteract} pin={clamp.transform.position}");
                testedPlayer.SetInput(new InputFrame { GrabLeft = true });
                yield return .4;
                results.AppendLine($"{part.name} grabbed={testedPlayer.ClampGrabbed}");
                testedPlayer.SetInput(new InputFrame { GrabLeft = true, Move = Vector2.down });
                yield return 8.6;
                results.AppendLine($"{part.name} released={clamp.IsReleased} progress={clamp.Progress:0.000}");
                testedPlayer.SetInput(default);
                aiming = false;
                yield return .2;
            }
            foreach (var tool in Object.FindObjectsByType<ToolBase>(FindObjectsSortMode.None))
            {
                var bodyCollider = tool.GetComponentsInChildren<Collider>().First(c=>c.enabled && !c.isTrigger);
                Vector3 outward = Vector3.back;
                Vector3 near = bodyCollider.bounds.ClosestPoint(bodyCollider.bounds.center + outward * 10);
                Approach(near, outward, .22f);
                yield return 1.0;
                // Aim at the visible collider, not a pivot which may be outside the model.
                aimPoint = bodyCollider.ClosestPoint(testedPlayer.AimOrigin);
                yield return .3;
                results.AppendLine($"{tool.name} before click foot={testedPlayer.FootPosition} camera={testedPlayer.AimOrigin} target={testedPlayer.CurrentInteractionTarget.Kind} closest={aimPoint}");
                testedPlayer.SetInput(new InputFrame { GrabLeft = true });
                yield return .3;
                results.AppendLine($"{tool.name} equipped={tool.IsEquipped} owner={tool.Owner == testedPlayer}");
                testedPlayer.SetInput(default);
                aiming = false;
                // Teleport calls the real player's release/reset path between equipment cases.
                testedPlayer.Teleport(new Vector3(-22, .4f, -29), Quaternion.identity);
                yield return 3.0;
                near = bodyCollider.bounds.ClosestPoint(bodyCollider.bounds.center + outward * 10);
                Approach(near, outward, .25f);
                yield return 1.0;
                aimPoint = bodyCollider.bounds.center;
                yield return .3;
                testedPlayer.SetInput(new InputFrame { GrabLeft = true });
                yield return .3;
                results.AppendLine($"REPICK {tool.name} equipped={tool.IsEquipped} owner={tool.Owner == testedPlayer}");
                testedPlayer.SetInput(default);
                aiming = false;
                testedPlayer.Teleport(new Vector3(-22, .4f, -29), Quaternion.identity);
                yield return .2;
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            results.AppendLine("PASS " + message);
        }

        private static IEnumerator RunManual()
        {
            yield return 1.0;
            testedPlayer = GameSession.Instance.LocalPlayer;
            GameSession.Instance.ScriptedInput = true;
            GameSession.Instance.SetCursor(false);
            Object.FindFirstObjectByType<MeteorSpawner>()?.SetPaused(true);
            var part = Object.FindObjectsByType<SpaceshipPart>(FindObjectsSortMode.None).Single(p=>p.name == "Part_1");
            var body = part.GetComponent<Rigidbody>();
            var hover = part.GetComponent<PartGroundHover>();
            var push = part.GetComponent<PartPush>();
            Require(!hover.IsSupporting && part.State == PartState.Locked, "locked part does not hover");
            var pins = part.GetComponentsInChildren<PartClamp>().OrderBy(c=>c.name).ToArray();
            // Fixture: only the first three are released directly. The final pin uses real input.
            foreach (var pin in pins.Skip(1)) pin.Release();
            var last = pins[0];
            var head = last.GetComponentsInChildren<Collider>().First(c=>c.name == "Head_Collider");
            Approach(head.bounds.center, Vector3.ProjectOnPlane(last.transform.position-part.transform.position, Vector3.up).normalized, .45f);
            yield return 1.0;
            testedPlayer.SetInput(new InputFrame {GrabLeft=true, Move=Vector2.down});
            yield return 9.0;
            Require(last.IsReleased && part.State == PartState.Free, "last pin released by grab/backward input");
            testedPlayer.SetInput(default);
            aiming=false;
            testedPlayer.Teleport(new Vector3(-22,.4f,-29), Quaternion.identity);
            yield return 3.0;
            Require(hover.IsSupporting, "free part hovering above ground");
            var lower = part.GetComponentsInChildren<Collider>().Where(c=>!c.isTrigger && c.GetComponentInParent<PartClamp>()==null).Min(c=>c.bounds.min.y);
            Physics.Raycast(new Vector3(body.position.x, lower+.05f, body.position.z), Vector3.down, out var floor, 2f, GameLayers.GroundMask);
            results.AppendLine($"clearance={lower-floor.point.y:0.000}m");
            Require(lower-floor.point.y > .10f && lower-floor.point.y < .45f, "hover clearance near 0.25m");
            // Actual body surface, not the model pivot, is the manual interaction target.
            var collider = part.GetComponentsInChildren<Collider>().Where(c=>!c.isTrigger && c.GetComponentInParent<PartClamp>()==null)
                .OrderByDescending(c=>c.bounds.size.x).First();
            var point = collider.bounds.center;
            point.z = collider.bounds.min.z;
            Approach(point, Vector3.back, .32f);
            trackedCollider=collider;
            yield return 1.0;
            aimPoint=collider.bounds.center;
            var start=body.position;
            testedPlayer.SetInput(new InputFrame {Move=Vector2.up});
            yield return 3.0;
            results.AppendLine($"push delta={body.position-start} count={push.PusherCount}");
            Require(body.position.z > start.z+.2f && push.PusherCount > 0, "walking toward the compound body pushes it");
            testedPlayer.SetInput(new InputFrame {GrabLeft=true});
            yield return .5;
            Require(testedPlayer.LeftHand.HeldBody == body, "actual click grips the module");
            start=body.position;
            testedPlayer.SetInput(new InputFrame {GrabLeft=true,Move=Vector2.down});
            yield return 3.0;
            results.AppendLine($"pull delta={body.position-start} count={push.PusherCount}");
            Require(body.position.z < start.z-.2f, "holding and walking backward pulls it");
            testedPlayer.SetInput(default);
            aiming=false;
            testedPlayer.Teleport(new Vector3(-22,.4f,-29),Quaternion.identity);
            Require(part.TryTransitionTo(PartState.Carried), "tractor can take over");
            yield return .2;
            Require(!hover.IsSupporting && !push.CanPush, "hover and hand drive yield to tractor");
        }

        public static void Report()
        {
            var text = new StringBuilder();
            text.AppendLine("Playing=" + EditorApplication.isPlaying);
            var player = Object.FindFirstObjectByType<PlayerRig>();
            if (player != null)
                text.AppendLine($"Player foot={player.FootPosition} hips={player.Position} aim={player.AimOrigin}/{player.AimForward} target={player.CurrentInteractionTarget.Kind} allowed={player.CurrentInteractionTarget.CanInteract}");
            if (GameSession.Instance != null)
                text.AppendLine($"Input scripted={GameSession.Instance.ScriptedInput} suppressed={GameSession.Instance.InputSuppressed} cursor={GameSession.Instance.CursorLocked}");
            foreach (var part in Object.FindObjectsByType<SpaceshipPart>(FindObjectsSortMode.None).OrderBy(p=>p.name))
            {
                text.AppendLine($"PART {part.name} root={part.transform.position} scale={part.transform.lossyScale}");
                foreach (var c in part.GetComponentsInChildren<Collider>(true).Where(c=>c.GetComponentInParent<PartClamp>() == null))
                    text.AppendLine($" BODY {c.name} active={c.enabled} layer={c.gameObject.layer} bounds={c.bounds}");
                foreach (var clamp in part.GetComponentsInChildren<PartClamp>(true))
                {
                    text.AppendLine($" CLAMP {clamp.name} pos={clamp.transform.position}");
                    foreach (var c in clamp.GetComponentsInChildren<Collider>(true))
                        text.AppendLine($"  {c.name} layer={c.gameObject.layer} trigger={c.isTrigger} bounds={c.bounds}");
                }
            }
            foreach (var tool in Object.FindObjectsByType<ToolBase>(FindObjectsSortMode.None))
            {
                text.AppendLine($"TOOL {tool.name} type={tool.GetType().Name} root={tool.transform.position}");
                foreach (var c in tool.GetComponentsInChildren<Collider>(true))
                    text.AppendLine($" {c.name} layer={c.gameObject.layer} trigger={c.isTrigger} active={c.enabled} bounds={c.bounds}");
            }
            File.WriteAllText(Path.GetFullPath("../concept-playtest/interaction-placement.txt"), text.ToString());
        }
    }
}
