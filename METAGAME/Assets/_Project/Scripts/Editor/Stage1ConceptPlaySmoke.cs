using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Metagame.Bootstrap;
using Metagame.Core;
using Metagame.Gameplay;
using Metagame.Gameplay.Parts;
using Metagame.Gameplay.Hazards;
using Metagame.Gameplay.Interaction;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Metagame.EditorTools
{
    /// <summary>Play-mode integration checks. Mutates only the unsaved running concept scene.</summary>
    [InitializeOnLoad]
    public static class Stage1ConceptPlaySmoke
    {
        static Stage1ConceptPlaySmoke()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (!SessionState.GetBool("ConceptPlaySmoke.Batch", false)) return;
                if (state == PlayModeStateChange.EnteredPlayMode) Begin();
                if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool("ConceptPlaySmoke.Finished", false))
                    EditorApplication.Exit(SessionState.GetInt("ConceptPlaySmoke.Exit", 1));
            };
        }

        public static void Batch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Batch entry only.");
            SessionState.SetBool("Metagame.URPSetupApplied", true);
            SessionState.SetBool("ConceptPlaySmoke.Batch", true);
            SessionState.SetBool("ConceptPlaySmoke.Finished", false);
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(Stage1ConceptPlayableBuilder.ScenePath);
            EditorApplication.EnterPlaymode();
        }
        private static IEnumerator _steps;
        private static double _resume;
        private static readonly List<string> Results = new List<string>();
        private static readonly List<string> Errors = new List<string>();
        private static readonly string Report = Path.GetFullPath("../concept-playtest/report.txt");

        public static void Begin()
        {
            if (!EditorApplication.isPlaying || UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != Stage1ConceptPlayableBuilder.ScenePath)
                throw new InvalidOperationException("Run only in the playable concept scene.");
            if (_steps != null) throw new InvalidOperationException("Smoke check already running.");
            Results.Clear();
            Errors.Clear();
            Directory.CreateDirectory(Path.GetDirectoryName(Report));
            Application.logMessageReceived += Log;
            _steps = Steps();
            _resume = 0;
            EditorApplication.update += Update;
        }

        private static void Log(string condition, string trace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) Errors.Add(condition);
        }

        private static void Update()
        {
            if (Time.timeAsDouble < _resume) return;
            try
            {
                if (!EditorApplication.isPlaying) throw new InvalidOperationException("Play stopped during check.");
                if (!_steps.MoveNext()) { Finish(true); return; }
                _resume = Time.timeAsDouble + Convert.ToDouble(_steps.Current);
            }
            catch (Exception ex) { Results.Add("FAIL: " + ex); Finish(false); }
        }

        private static void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException(description);
            Results.Add("PASS: " + description);
        }

        private static void Finish(bool success)
        {
            Application.logMessageReceived -= Log;
            EditorApplication.update -= Update;
            _steps = null;
            Results.AddRange(Errors.Select(e => "RUNTIME ERROR: " + e));
            File.WriteAllText(Report, DateTime.Now.ToString("s") + "\n" + (success && Errors.Count == 0 ? "PASS" : "FAIL") + "\n" + string.Join("\n", Results));
            Debug.Log("[ConceptPlaySmoke] " + (success && Errors.Count == 0 ? "PASS" : "FAIL") + " — " + Report);
            SessionState.SetBool("ConceptPlaySmoke.Finished", true);
            SessionState.SetInt("ConceptPlaySmoke.Exit", success && Errors.Count == 0 ? 0 : 1);
            EditorApplication.ExitPlaymode();
        }

        private static IEnumerator Steps()
        {
            yield return 1.0;
            var bootstrap = Object.FindFirstObjectByType<SpaceBootstrap>();
            Check(bootstrap != null && bootstrap.Player != null && bootstrap.Session != null, "Existing player/bootstrap created");
            var player = bootstrap.Player;
            var session = bootstrap.Session;
            session.ScriptedInput = true;
            session.SetCursor(false);
            player.SetInput(default);
            Check(bootstrap.ExplorationHud != null && bootstrap.Hud != null, "Tablet and gameplay HUD created");
            var parts = Object.FindObjectsByType<SpaceshipPart>(FindObjectsSortMode.None).OrderBy(p => p.name).ToArray();
            Check(parts.Length == 4 && parts.All(p => p.enabled), "All four real parts active");
            Check(parts.All(p => p.Sockets.Count == 2 && p.RemainingRequiredClamps == 4), "Eight assembly sockets and sixteen clamps wired");
            var plasma = Object.FindFirstObjectByType<PlasmaWeldingTool>();
            Check(plasma != null, "Real plasma welding/cutting tool present");
            Check(Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).Any(b => b != null && b.GetType().Name == "MeteorBat"), "Real meteor bat present");
            var meteor = Object.FindFirstObjectByType<MeteorSpawner>();
            Check(meteor != null && meteor.Elapsed > 0, "Meteor wave clock running");
            meteor.SetPaused(true);
            player.Teleport(bootstrap.playerSpawn.position, bootstrap.playerSpawn.rotation);
            yield return 1.0;
            Check(player.FootPosition.y > -.5f && player.FootPosition.y < 1.5f, "Player settles on camp ground");
            Vector3 start = player.Position;
            player.SetInput(new InputFrame { Move = Vector2.up });
            yield return 1.5;
            player.SetInput(default);
            Check(Vector3.Distance(start, player.Position) > .5f, "Movement input moves physical player");

            // Land beside the pod, not in its clamp/instrument collision envelope.
            Vector3 hillFoot = new Vector3(24.5f, 8.35f, 19);
            Check(Physics.Raycast(hillFoot + Vector3.up, Vector3.down, out var hillGround, 2f,
                1 << GameLayers.Ground) && Mathf.Abs(hillGround.point.y - 8.02f) < .15f,
                "Hill landing point has authored deck beneath it: " + (hillGround.collider != null ? hillGround.collider.name + " " + hillGround.point : "no ground hit"));
            player.Teleport(hillFoot, Quaternion.identity);
            yield return 1.5;
            Check(player.FootPosition.y > 7.7f && player.FootPosition.y < 8.8f, "Hill deck supports player: " + player.FootPosition);
            player.Teleport(new Vector3(-29, 5.65f, 23), Quaternion.identity);
            yield return 1.5;
            Check(player.FootPosition.y > 5.0f && player.FootPosition.y < 6.2f, "Hangar floor supports player under solid roof: " + player.FootPosition);

            foreach (var part in parts)
                foreach (var clamp in part.Clamps.ToArray()) clamp.Release();
            Check(parts.All(p => p.State == PartState.Free && p.RemainingRequiredClamps == 0), "Clamp release frees all four parts without consuming engine");
            var beam = Object.FindFirstObjectByType<TractorBeam>();
            Check(beam != null && beam.TryGetComponent<TransportVehicle>(out _), "Reusable carrier registered");
            var vehicle = beam.GetComponent<TransportVehicle>();
            vehicle.enabled = false; // isolate beam query from flight movement for this check only
            beam.SetWorldPosition(parts[1].transform.position + Vector3.up * 4f);
            beam.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            Physics.SyncTransforms();
            Check(beam.TryGrab(parts[1]) == BeamGrabResult.Grabbed && parts[1].State == PartState.Carried, "Tractor beam grabs hill module at new map coordinates");
            beam.Tick(Time.fixedDeltaTime);
            beam.Release();
            Check(!beam.IsHolding && parts[1].State == PartState.Placed, "Beam releases module for placement");
            beam.SetWorldPosition(new Vector3(25, 5, -18));

            plasma.Equip(player, plasma.GetComponent<Rigidbody>());
            plasma.SetInput(new ToolInputFrame { PrimaryHeld = true }, new ToolAimContext(null, null, Vector3.forward));
            Check(plasma.BeamMode == PlasmaBeamMode.Weld, "Left-click semantic input selects welding");
            plasma.SetInput(new ToolInputFrame { SecondaryHeld = true }, new ToolAimContext(null, null, Vector3.forward));
            Check(plasma.BeamMode == PlasmaBeamMode.Cut, "Right-click semantic input selects cutting");
            plasma.SetInput(default, new ToolAimContext(null, null, Vector3.forward));
            plasma.Unequip();

            var assembly = Object.FindFirstObjectByType<ShipAssembly>();
            var links = new List<PartAssemblyLink>();
            foreach (var part in parts)
            {
                int i = Array.IndexOf(parts, part);
                var rb = part.GetComponent<Rigidbody>();
                rb.rotation = Quaternion.identity;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                part.transform.SetPositionAndRotation(rb.position, rb.rotation);
                // Use the actual scene sockets, not nominal spec heights: model import scales
                // and authored contact offsets need not equal PartSpec.Height / 2.
                var baseSocket = part.Sockets.Single(s => s.Role == SocketRole.Base);
                Vector3 target = i == 0 ? assembly.LaunchPad.position + Vector3.up * .04f
                    : parts[i - 1].Sockets.Single(s => s.Role == SocketRole.Top).transform.position;
                rb.position += target - baseSocket.transform.position;
                part.transform.position = rb.position;
                part.TryTransitionTo(PartState.Carried);
                if (i == 0) part.TryTransitionTo(PartState.Placed);
            }
            Physics.SyncTransforms();
            for (int i = 1; i < parts.Length; i++)
            {
                var match = PartAssembler.FindMatch(parts[i]);
                Check(match.HasValue, "Assembly match found for floor " + (i + 1) +
                    " (registered sockets=" + PartSocket.All.Count + ", previous=" + parts[i - 1].State + ")");
                var link = PartAssembler.Assemble(match);
                Check(link != null, "Assembly joint created for floor " + (i + 1));
                link.ForceWeldComplete();
                link.Tick();
                links.Add(link);
            }
            player.Teleport(new Vector3(5, 1.2f, -4), Quaternion.identity);
            yield return 1.0;
            assembly.Evaluate();
            Check(assembly.IsBoardable, "Full tower is boardable on actual central pad: " + assembly.Blocker);
            var hatch = Object.FindFirstObjectByType<ShipHatch>();
            // Uses the existing boarding interaction, not a replacement launch sequence.
            hatch.OnCompleted(player.GetComponentInChildren<Interactor>());
            Check(hatch.State == BoardingState.CountingDown, "Existing hatch starts boarding countdown");
            Check(player.IsRiding, "Actual player attaches to the ship through existing hatch");
            var launch = Object.FindFirstObjectByType<LaunchTest>();
            yield return 5.5;
            Check(launch.State == LaunchState.Ramping || launch.State == LaunchState.Ascending || launch.State == LaunchState.Success,
                "Launch starts without immediate breakup: " + launch.State + " " + launch.FailureReason);
            yield return 12.0;
            Check(launch.State == LaunchState.Success, "Full launch reaches success: " + launch.State + " " + launch.FailureReason);
        }
    }
}
