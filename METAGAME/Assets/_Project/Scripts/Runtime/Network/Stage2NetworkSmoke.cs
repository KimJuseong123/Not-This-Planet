using System.Collections;
using System.Linq;
using Metagame.Core;
using Metagame.Gameplay;
using Metagame.Gameplay.Stage2;
using Mirror;
using UnityEngine;

namespace Metagame.Network
{
    /// <summary>Only attached to the generated local test launcher, never to production scenes.</summary>
    public sealed class Stage2NetworkSmoke : MonoBehaviour
    {
        [SerializeField] GameObject _playerPrefab;
        public void Configure(GameObject prefab) => _playerPrefab = prefab;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        bool _host, _observer;
        float _started;
        IEnumerator Start()
        {
            string[] args = System.Environment.GetCommandLineArgs();
            _host = args.Contains("-stage2-smoke-host");
            _observer = args.Contains("-stage2-smoke-observer");
            if (!_host && !_observer && !args.Contains("-stage2-smoke-client")) yield break;
            DontDestroyOnLoad(gameObject);
            _started = Time.realtimeSinceStartup;
            var root = new GameObject("Local Stage2 smoke network"); root.SetActive(false);
            var transport = root.AddComponent<TelepathyTransport>(); transport.port = 27863;
            var manager = root.AddComponent<NetworkManager>();
            manager.transport = transport; manager.playerPrefab = _playerPrefab;
            manager.networkAddress = "127.0.0.1"; manager.onlineScene = StageCatalog.SceneFolder + StageCatalog.Stage2SceneName + ".unity";
            manager.offlineScene = ""; manager.autoCreatePlayer = true; manager.headlessStartMode = HeadlessStartOptions.DoNothing;
            root.SetActive(true); MatchRole.DedicatedServer = false;
            if (_host) manager.StartHost(); else manager.StartClient();
            while (GameSession.Instance == null || Object.FindAnyObjectByType<Stage2Bridge>() == null) yield return null;
            GameSession.Instance.ScriptedInput = true;
            GameSession.Instance.LocalPlayer.SetInput(default);
            var yard = Object.FindAnyObjectByType<Stage2Yard>();
            var bridge = Object.FindAnyObjectByType<Stage2Bridge>();
            if (_host) yield return Host(yard, bridge);
            else yield return Client(yard, bridge);
        }

        IEnumerator Host(Stage2Yard yard, Stage2Bridge bridge)
        {
            while (NetworkServer.connections.Count < 2 || NetworkServer.connections.Values.Any(c=>c.identity == null || c.identity.GetComponent<NetPlayer>().VisualRig == null)) yield return null;
            var remote = NetworkServer.connections.Values.First(c=>c!=NetworkServer.localConnection);
            var rig = remote.identity.GetComponent<NetPlayer>().VisualRig;
            uint remoteId = remote.identity.netId;
            var first = yard.Targets.First(t=>t.Kind==YardTargetKind.Container&&t.Index==0);
            var second = yard.Targets.First(t=>t.Kind==YardTargetKind.Container&&t.Index==1);
            GameSession.Instance.LocalPlayer.Teleport(first.transform.position+Vector3.back-Vector3.up,Quaternion.identity);
            rig.Teleport(second.transform.position+Vector3.back-Vector3.up,Quaternion.identity);
            Physics.SyncTransforms();
            bridge.Send(new YardCommand { Action=YardAction.Interact,Target=first.Id });
            while (!yard.State.Parts[0].Open || !yard.State.Parts[1].Open) yield return null;
            Debug.Log("[Stage2Smoke] HOST containers from both authenticated peers PASS");
            var controls = yard.Targets.First(t=>t.Kind==YardTargetKind.Crane);
            rig.Teleport(controls.transform.position+Vector3.back-Vector3.up*.8f,Quaternion.identity);
            Physics.SyncTransforms();
            while (yard.State.Pilot != remoteId) yield return null;
            float yaw = yard.State.Yaw;
            while (Mathf.Abs(yard.State.Yaw-yaw)<3f) yield return null;
            Debug.Log("[Stage2Smoke] HOST remote pilot motion PASS; late observer may join");
            while (NetworkServer.connections.ContainsKey(remote.connectionId)) yield return null;
            yield return new WaitForSeconds(.5f);
            if (yard.State.Pilot != 0) { Debug.LogError("[Stage2Smoke] FAIL pilot lease remained after disconnect"); Application.Quit(2); yield break; }
            Debug.Log("[Stage2Smoke] HOST disconnect cleanup PASS");
            yield return new WaitForSeconds(12);
            Application.Quit(0);
        }

        IEnumerator Client(Stage2Yard yard, Stage2Bridge bridge)
        {
            if (_observer)
            {
                while (!yard.State.Parts[0].Open || !yard.State.Parts[1].Open || Mathf.Abs(yard.State.Yaw)<3f) yield return null;
                Debug.Log("[Stage2Smoke] LATE OBSERVER full state PASS"); Application.Quit(0); yield break;
            }
            while (!yard.State.Parts[0].Open) yield return null;
            var second = yard.Targets.First(t=>t.Kind==YardTargetKind.Container&&t.Index==1);
            while (!yard.State.Parts[1].Open)
            { bridge.Send(new YardCommand{Action=YardAction.Interact,Target=second.Id}); yield return new WaitForSeconds(.3f); }
            var controls = yard.Targets.First(t=>t.Kind==YardTargetKind.Crane);
            while (yard.State.Pilot!=bridge.LocalId)
            { bridge.Send(new YardCommand{Action=YardAction.Interact,Target=controls.Id}); yield return new WaitForSeconds(.3f); }
            float end=Time.time+10f;
            while(Time.time<end)
            { bridge.Send(new YardCommand{Axes=Vector3.right,Slow=true}); yield return new WaitForSeconds(.05f); }
            if(Mathf.Abs(yard.State.Yaw)<3f) { Debug.LogError("[Stage2Smoke] FAIL no replicated crane movement"); Application.Quit(2); yield break; }
            Debug.Log("[Stage2Smoke] CLIENT input and replicated crane PASS");
            Application.Quit(0);
        }
        void Update()
        {
            if (_started>0 && Time.realtimeSinceStartup-_started>100)
            { Debug.LogError("[Stage2Smoke] FAIL timeout"); Application.Quit(2); }
        }
#endif
    }
}
