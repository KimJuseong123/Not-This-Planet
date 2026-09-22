using System.Collections.Generic;
using Metagame.Core;
using Metagame.Gameplay;
using Metagame.Gameplay.Stage2;
using Metagame.Gameplay.Parts;
using UnityEngine;

namespace Metagame.UI
{
    /// <summary>Local, seekable presentation; puppets have no player or network behaviours.</summary>
    [DefaultExecutionOrder(250)]
    public sealed class Stage2DemoView : MonoBehaviour
    {
        Stage2Yard _yard;
        GameSession _session;
        OrbitCamera _orbit;
        bool _orbitEnabled, _firstPerson;
        float _fov;
        Behaviour[] _huds;
        readonly List<Behaviour> _hiddenHuds = new();
        readonly Dictionary<Renderer, bool> _hiddenPlayers = new();
        readonly List<Doll> _actors = new();
        readonly List<Transform> _wrenches = new();
        Transform _crew;
        Material _metal;
        Font _font;
        GUIStyle _title, _caption;
        float _nextRosterCheck;
        DemoEndingPlayer _solo;

        public bool Presenting => _orbit != null;
        public int ActorCount => _actors.Count;

        public void Configure(Stage2Yard yard, GameSession session, DemoEndingPlayer solo, params Behaviour[] huds)
        {
            _yard = yard; _session = session; _solo = solo; _huds = huds;
        }

        void LateUpdate() => Tick();

        public void Tick()
        {
            if (_yard == null || _session == null || !_yard.DemoMode) { Restore(); return; }
            // The demo has no playable puzzle or puzzle guide, including during the initial walk.
            if (_huds != null && _huds.Length > 0) Hide(_huds[0]);
            if (!_yard.DemoCinematic) return;
            var camera = _session.LocalPlayer != null ? _session.LocalPlayer.Cam : null;
            if (camera == null) return;
            if (_orbit != camera)
            {
                RestoreCamera();
                _orbit = camera; _orbitEnabled = camera.enabled;
                _firstPerson = camera.FirstPerson; _fov = camera.Cam.fieldOfView;
                camera.enabled = false; camera.FirstPerson = false;
            }
            if (_crew == null)
            {
                BuildCrew();
                foreach (var hud in _session.GetComponents<EmoteHud>()) Hide(hud);
                foreach (var hud in _session.GetComponents<DanceHud>()) Hide(hud);
            }
            if (Time.unscaledTime >= _nextRosterCheck)
            {
                _nextRosterCheck = Time.unscaledTime + .5f;
                foreach (var rig in PlayerRig.All)
                    if (rig != null)
                        foreach (var renderer in rig.GetComponentsInChildren<Renderer>(true))
                            if (!_hiddenPlayers.ContainsKey(renderer))
                            { _hiddenPlayers.Add(renderer, renderer.forceRenderingOff); renderer.forceRenderingOff = true; }
            }
            if (_huds != null) foreach (var hud in _huds) Hide(hud);
            float time = _yard.DemoElapsed - Stage2Yard.DemoMoveSeconds;
            PresentCamera(time);
            PresentCrew(time);
        }

        void Hide(Behaviour hud)
        {
            if (hud == null || !hud.enabled) return;
            _hiddenHuds.Add(hud); hud.enabled = false;
        }

        void BuildCrew()
        {
            _crew = new GameObject("Stage2 demo crew - presentation only").transform;
            _crew.SetParent(transform, false);
            _metal = Prim.Flat(new Color(.66f, .8f, .88f));
            if (_yard.State.DemoPlayers != null)
                foreach (var player in _yard.State.DemoPlayers)
                    AddActor(player.Name, player.Suit, player.Accent, player.Appearance);
            if (_actors.Count == 0)
                AddActor(_solo.Name, _solo.Suit, _solo.Accent, _solo.Appearance);
        }

        void AddActor(string label, Color suit, Color accent, int appearance)
        {
            var doll = RagdollBuilder.Build(label + " demo", _yard.DemoCenter, Quaternion.identity, suit, accent, !SpaceSuit.Available);
            doll.Root.SetParent(_crew, true);
            PlayerAppearance.Apply(doll, appearance);
            foreach (var body in doll.Bodies) body.isKinematic = true;
            foreach (var collider in doll.Root.GetComponentsInChildren<Collider>()) collider.enabled = false;
            foreach (var script in doll.Root.GetComponentsInChildren<MonoBehaviour>()) script.enabled = false;
            _actors.Add(doll);
            var wrench = new GameObject("Demo wrench").transform;
            wrench.SetParent(doll.Right.Hand.Body.transform, false);
            Shape(wrench, new Vector3(0, .18f, 0), new Vector3(.055f, .4f, .055f));
            Shape(wrench, new Vector3(-.055f, .4f, 0), new Vector3(.055f, .13f, .08f));
            Shape(wrench, new Vector3(.055f, .4f, 0), new Vector3(.055f, .13f, .08f));
            Shape(wrench, new Vector3(0, .34f, 0), new Vector3(.16f, .055f, .08f));
            _wrenches.Add(wrench);
        }

        void Shape(Transform parent, Vector3 position, Vector3 scale)
        {
            var shape = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shape.transform.SetParent(parent, false);
            shape.transform.localPosition = position; shape.transform.localScale = scale;
            shape.GetComponent<Collider>().enabled = false;
            Destroy(shape.GetComponent<Collider>());
            shape.GetComponent<Renderer>().sharedMaterial = _metal;
        }

        void PresentCamera(float time)
        {
            Vector3 focus, position;
            if (time < Stage2Yard.DemoOverviewSeconds)
            {
                float angle = Mathf.Lerp(-140f, -35f, Mathf.SmoothStep(0, 1, time / Stage2Yard.DemoOverviewSeconds));
                focus = _yard.DemoCenter + Vector3.up * 5f;
                position = focus + Quaternion.Euler(0, angle, 0) * new Vector3(0, 13f, 32f);
            }
            else
            {
                float assembly = time - Stage2Yard.DemoOverviewSeconds;
                int part = Mathf.Clamp((int)(assembly / Stage2Yard.DemoAssemblySeconds), 0, _yard.DemoPartCount - 1);
                float phase = assembly - part * Stage2Yard.DemoAssemblySeconds;
                focus = phase < 4.3f ? Vector3.Lerp(_yard.DemoHome(part), _yard.DemoSocket(part), Mathf.Clamp01(phase / 4.3f))
                    + Vector3.up * 1.5f : _yard.DemoSocket(part) + Vector3.up * .7f;
                position = focus + (phase < 4.3f ? new Vector3(-10, 7, -12) : new Vector3(-5, 1.6f, -7));
                if (assembly >= _yard.DemoPartCount * Stage2Yard.DemoAssemblySeconds)
                { focus = _yard.DemoCenter + Vector3.up * 4; position = focus + new Vector3(-12, 5, -18); }
            }
            _orbit.Cam.fieldOfView = 52;
            _orbit.transform.SetPositionAndRotation(position, Quaternion.LookRotation(focus - position));
        }

        void PresentCrew(float time)
        {
            float assembly = time - Stage2Yard.DemoOverviewSeconds;
            int part = Mathf.Clamp((int)(Mathf.Max(0, assembly) / Stage2Yard.DemoAssemblySeconds), 0, _yard.DemoPartCount - 1);
            float phase = assembly - part * Stage2Yard.DemoAssemblySeconds;
            for (int i = 0; i < _actors.Count; i++)
            {
                var doll = _actors[i];
                bool pilot = i == 1 && phase < 4.3f;
                bool working = assembly >= 0 && phase >= 4.3f && phase < 6;
                Vector3 socket = _yard.DemoSocket(part);
                Vector3 station = pilot ? _yard.DemoCrane : socket + new Vector3(-.9f + (i % 3) * .8f, -.7f, -2.3f - (i / 3) * .6f);
                // Higher joints are worked from the lift; first shot walks from the container to the joint.
                Vector3 start = _yard.DemoHome(part) + new Vector3(-2 + i, -.5f, -2);
                start.y = .35f;
                var ladderBase = new Vector3(station.x, .35f, station.z - 1.4f);
                Vector3 foot = phase < 2f ? Vector3.Lerp(start, ladderBase, Mathf.SmoothStep(0, 1, Mathf.Clamp01(phase / 2f)))
                    : Vector3.Lerp(ladderBase, station, Mathf.SmoothStep(0, 1, Mathf.Clamp01((phase - 2f) / 2.3f)));
                if (pilot) foot = station;
                Quaternion facing = Quaternion.LookRotation(Vector3.ProjectOnPlane(socket - foot, Vector3.up).normalized);
                doll.Teleport(foot + Vector3.up * RagdollBuilder.HipsY, facing);
                if (working || pilot)
                {
                    var shoulder = doll.Right.Upper.position + facing * Vector3.up * ((RagdollBuilder.UpperArmTop - RagdollBuilder.UpperArmBot) * .5f);
                    var rotation = Quaternion.AngleAxis(80f + Mathf.Sin(time * 10 + i) * 18f, facing * Vector3.forward);
                    Rotate(doll.Right.Upper.transform, shoulder, rotation);
                    Rotate(doll.Right.Lower.transform, shoulder, rotation);
                    Rotate(doll.Right.Hand.Body.transform, shoulder, rotation);
                }
                _wrenches[i].gameObject.SetActive(working);
                if (doll.Suit == null) continue;
                doll.Suit.Sync();
                if (!working && !pilot) doll.Suit.Locomotion?.Apply(new RabbitLocomotion.State
                { Speed = phase > 0 && phase < 4.3f ? 2f : 0, Grounded = true, Direction = Vector2.up }, Time.unscaledDeltaTime);
            }
        }

        static void Rotate(Transform limb, Vector3 pivot, Quaternion rotation) =>
            limb.SetPositionAndRotation(pivot + rotation * (limb.position - pivot), rotation * limb.rotation);

        void OnGUI()
        {
            if (_yard == null || !_yard.DemoMode) return;
            if (_font == null) _font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "Noto Sans CJK KR", "Arial" }, 48);
            _title ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, font = _font, fontSize = 48 };
            _caption ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, font = _font, fontSize = 22 };
            var matrix = GUI.matrix; var color = GUI.color; int depth = GUI.depth;
            GUI.depth = 100; // Existing Esc/settings menu stays usable above the final card.
            GUI.matrix = Matrix4x4.Scale(new Vector3(Screen.width / 1600f, Screen.height / 900f, 1));
            if (_yard.DemoCinematic)
            {
                GUI.color = Color.black;
                GUI.DrawTexture(new Rect(0, 0, 1600, 65), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(0, 800, 1600, 100), Texture2D.whiteTexture);
                float fade = Mathf.Clamp01((_yard.DemoElapsed - _yard.DemoEndAt + 1f));
                if (fade > 0)
                { GUI.color = new Color(.015f, .025f, .045f, fade); GUI.DrawTexture(new Rect(0, 0, 1600, 900), Texture2D.whiteTexture); }
                GUI.color = Color.white;
                if (_yard.DemoFinished)
                {
                    GUI.Label(new Rect(0, 330, 1600, 100), "-- 데모 완 --", _title);
                    GUI.Label(new Rect(0, 455, 1600, 60), "함께해 주셔서 감사합니다", _caption);
                    GUI.Label(new Rect(0, 735, 1600, 60), "ESC · 메뉴 / 로비로 돌아가기", _caption);
                }
                else
                {
                    float time = _yard.DemoElapsed - Stage2Yard.DemoMoveSeconds - Stage2Yard.DemoOverviewSeconds;
                    string caption = time < 0 ? "지구 · 다시 힘을 합칠 시간" : time < _yard.DemoPartCount * Stage2Yard.DemoAssemblySeconds
                        ? $"{Mathf.Clamp((int)(time / Stage2Yard.DemoAssemblySeconds) + 1, 1, _yard.DemoPartCount)} / {_yard.DemoPartCount}   크레인으로 옮기고 · 함께 단단히 체결" : "우리의 다음 모험은 계속됩니다";
                    GUI.Label(new Rect(0, 818, 1600, 60), caption, _caption);
                }
            }
            else
            {
                GUI.color = Color.white;
                GUI.Label(new Rect(0, 735, 1600, 70), _yard.State.DemoStarted
                    ? "잠시 주변을 둘러보세요 · 곧 마지막 이야기가 시작됩니다" : "동료들의 도착을 기다리는 중…", _caption);
            }
            GUI.matrix = matrix; GUI.color = color; GUI.depth = depth;
        }

        void RestoreCamera()
        {
            if (_orbit != null)
            { _orbit.enabled = _orbitEnabled; _orbit.FirstPerson = _firstPerson; if (_orbit.Cam != null) _orbit.Cam.fieldOfView = _fov; }
            _orbit = null;
        }

        void Restore()
        {
            RestoreCamera();
            foreach (var item in _hiddenPlayers) if (item.Key != null) item.Key.forceRenderingOff = item.Value;
            _hiddenPlayers.Clear();
            foreach (var hud in _hiddenHuds) if (hud != null) hud.enabled = true;
            _hiddenHuds.Clear();
            if (_crew != null) Destroy(_crew.gameObject);
            _crew = null; _actors.Clear(); _wrenches.Clear();
            if (_metal != null) Destroy(_metal);
            _metal = null;
        }

        void OnDisable() => Restore();
        void OnDestroy() { Restore(); if (_font != null) Destroy(_font); }
    }
}
