using System.Collections;
using Metagame.Gameplay;
using Metagame.Gameplay.Hazards;
using Metagame.Network;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Metagame.Bootstrap
{
    /// <summary>Stage1은 계속 실행하고, 카메라 연출 씬만 가산 로드/언로드한다.</summary>
    public sealed class Stage1CutsceneFlow : MonoBehaviour
    {
        public const string StagePath = "Assets/Scenes/Stage1_Astra.unity";
        public const string EntryPath = "Assets/Scene/Stage1_EntryCutscene.unity";
        public const string AssaultPath = "Assets/Scene/Stage1_AssaultCutscene.unity";
        public Stage1Sequence Sequence { get; private set; }
        SpaceBootstrap _bootstrap;
        Stage1CutsceneScene _view;
        bool _loading;
        int _presentedRevision = -1;
        bool _networked;

        public void Configure(SpaceBootstrap bootstrap, MeteorSpawner spawner)
        {
            _bootstrap = bootstrap;
            _networked = NetworkServer.active || NetworkClient.active;
            Sequence = bootstrap.Session.gameObject.AddComponent<Stage1Sequence>();
            Sequence.Configure(bootstrap.Session, spawner,
                () => !_networked || NetworkServer.active,
                () => _networked ? NetworkTime.time : Time.unscaledTimeAsDouble);
        }

        IEnumerator Start()
        {
            if (Sequence == null) yield break;
            // 모든 서버 리그와 커마 Command가 구성될 시간을 확보한다.
            yield return null;
            while (!ReadyForEntry()) yield return null;
            if (_networked) yield return new WaitForSecondsRealtime(1f);
            if (Sequence.Phase != Stage1SequencePhase.Waiting || !Sequence.Authoritative) yield break;
            var preview = FindAnyObjectByType<Stage1CutsceneScene>();
            if (preview != null && preview.IsAssault) Sequence.TryBeginAssault(true);
            else Sequence.TryBeginEntry();
        }

        bool ReadyForEntry()
        {
            if (_networked)
            {
                if (!NetworkServer.active) return false;
                int count = 0;
                foreach (var connection in NetworkServer.connections.Values)
                {
                    if (!connection.isAuthenticated) continue;
                    var player = connection.identity != null ? connection.identity.GetComponent<NetPlayer>() : null;
                    if (!connection.isReady || player == null || !player.HasBody) return false;
                    count++;
                }
                return count > 0;
            }
            return _bootstrap.Session.LocalPlayer != null;
        }

        public bool PlayEntry() => CanTest() && Sequence.TryBeginEntry();
        public bool PlayAssault() => CanTest() && Sequence.TryBeginAssault(true);

        bool CanTest() => Sequence != null && !Sequence.BlocksInput && !_loading
            && !_bootstrap.Session.EndingActive && !_bootstrap.Session.InputSuppressed
            && !HostMigration.Pending && !HostMigration.Taking && !HostMigration.Settling;

        void Update()
        {
            if (Sequence == null || _loading || MatchRole.DedicatedServer) return;
            if (_bootstrap.Session.EndingActive || !Sequence.IsCinematic)
            {
                if (_view != null) StartCoroutine(CloseView());
                return;
            }
            if (_presentedRevision != Sequence.Revision) StartCoroutine(OpenView());
        }

        IEnumerator OpenView()
        {
            _loading = true;
            int revision = Sequence.Revision;
            string path = Sequence.Phase == Stage1SequencePhase.Assault ? AssaultPath : EntryPath;
            var scene = SceneManager.GetSceneByPath(path);
            if (!scene.isLoaded)
            {
                if (!Application.CanStreamedLevelBeLoaded(path))
                {
                    Debug.LogError("[Stage1Cutscene] Build Settings에 씬이 없습니다: " + path);
                    _presentedRevision = revision;
                    _loading = false;
                    yield break;
                }
                yield return SceneManager.LoadSceneAsync(path, LoadSceneMode.Additive);
                scene = SceneManager.GetSceneByPath(path);
            }
            foreach (var root in scene.GetRootGameObjects())
            {
                _view = root.GetComponentInChildren<Stage1CutsceneScene>();
                if (_view != null) break;
            }
            if (_view != null) _view.Play(_bootstrap, Sequence);
            _presentedRevision = revision;
            _loading = false;
        }

        IEnumerator CloseView()
        {
            _loading = true;
            var scene = _view.gameObject.scene;
            _view.Stop();
            _view = null;
            if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
            _loading = false;
        }

        void OnDestroy() { if (_view != null) _view.Stop(); }
    }
}
