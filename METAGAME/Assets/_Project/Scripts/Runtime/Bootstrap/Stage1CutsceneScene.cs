using System;
using System.Collections;
using System.Collections.Generic;
using Metagame.Core;
using Metagame.Gameplay;
using Metagame.Gameplay.Audio;
using Metagame.Gameplay.Parts;
using Metagame.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Metagame.Bootstrap
{
    /// <summary>씬에 저장된 샷을 실제 플레이어/월드에 바인딩한다. 대역 캐릭터를 만들지 않는다.</summary>
    [DefaultExecutionOrder(250)]
    public sealed class Stage1CutsceneScene : MonoBehaviour
    {
        public enum Anchor { Crew, Engine, Assembly, Impact }
        [Serializable]
        public struct Shot
        {
            public string label;
            public float start;
            public float duration;
            public Anchor anchor;
            public Vector3 from;
            public Vector3 to;
            public Vector3 look;
            public float fieldOfView;
        }

        [SerializeField] bool _assault;
        [SerializeField] GameObject _meteorVisual;
        [SerializeField] Shot[] _shots;
        public bool IsAssault => _assault;
        public bool Playing => _sequence != null;
        public Shot[] Shots => _shots;

        SpaceBootstrap _bootstrap;
        Stage1Sequence _sequence;
        OrbitCamera _orbit;
        bool _savedOrbitEnabled, _savedFirstPerson, _savedTablet;
        float _savedFov;
        Vector3 _crew, _engine, _assembly, _impact;
        readonly List<Behaviour> _hidden = new();
        readonly List<Light> _lights = new();
        readonly List<float> _intensities = new();
        readonly List<Light> _guides = new();
        AtmosphereMeteorShower _cutsceneShower;
        GameObject _meteor;
        Light _flash;
        Material _trailMaterial;
        bool _impacted, _warned;
        readonly List<Transform> _assaultMeteors = new();
        readonly List<LineRenderer> _assaultTrails = new();

        public void Author(bool assault, GameObject meteorVisual, Shot[] shots)
        { _assault = assault; _meteorVisual = meteorVisual; _shots = shots; }

        IEnumerator Start()
        {
            // 이 씬만 열고 Play해도 원본 Stage1을 그대로 구성한다.
            if (FindAnyObjectByType<SpaceBootstrap>() != null) yield break;
            yield return SceneManager.LoadSceneAsync(Stage1CutsceneFlow.StagePath, LoadSceneMode.Additive);
            var stage = SceneManager.GetSceneByPath(Stage1CutsceneFlow.StagePath);
            if (stage.IsValid()) SceneManager.SetActiveScene(stage);
        }

        public void Play(SpaceBootstrap bootstrap, Stage1Sequence sequence)
        {
            if (Playing) return;
            _bootstrap = bootstrap;
            _sequence = sequence;
            _orbit = bootstrap.Session.LocalPlayer != null ? bootstrap.Session.LocalPlayer.Cam : null;
            if (_orbit == null) { _sequence = null; return; }
            _savedOrbitEnabled = _orbit.enabled;
            _savedFirstPerson = _orbit.FirstPerson;
            _savedFov = _orbit.Cam.fieldOfView;
            _savedTablet = bootstrap.Exploration.TabletOpen;
            bootstrap.Exploration.SetTabletOpen(false);
            _orbit.enabled = false;
            _orbit.FirstPerson = false;
            _crew = CrewCenter(bootstrap.Session.LocalPlayer.Hips.position);
            _engine = _crew;
            foreach (var part in FindObjectsByType<SpaceshipPart>(FindObjectsSortMode.None))
                if (part.name == PartSpec.All[0].Name) _engine = part.transform.position;
            var assembly = FindAnyObjectByType<ShipAssembly>();
            _assembly = assembly != null ? assembly.LaunchPad.position : _engine + Vector3.right * 15f;
            _impact = _engine + new Vector3(-9f, 0f, 7f);
            if (Physics.Raycast(_impact + Vector3.up * 80f, Vector3.down, out var hit, 160f,
                ~0, QueryTriggerInteraction.Ignore)) _impact = hit.point;

            Hide(bootstrap.Hud); Hide(bootstrap.PingHud);
            Hide(bootstrap.NameplateHud); Hide(bootstrap.RescueHud);
            foreach (var hud in bootstrap.Session.GetComponents<EmoteHud>()) Hide(hud);
            foreach (var hud in bootstrap.Session.GetComponents<DanceHud>()) Hide(hud);
            foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (light.type == LightType.Directional || !light.enabled) continue;
                _lights.Add(light); _intensities.Add(light.intensity);
            }
            // 컷씬 전용 유성우만 소유한다. 기존 월드의 효과/강도는 변경하지 않는다.
            var showerRoot = new GameObject("Cutscene meteor shower");
            showerRoot.transform.SetParent(transform, false);
            _cutsceneShower = showerRoot.AddComponent<AtmosphereMeteorShower>();
            _cutsceneShower.Apply(bootstrap.AtmosphereProfile);
            _cutsceneShower.BindCamera(_orbit.Cam);
            _cutsceneShower.SetIntensity(1f);
            _cutsceneShower.SetAlertIntensity(1f);
            _warned = false;
            if (_assault) BuildAssaultEffects(); else BuildEntryEffects();
            Debug.Log("[Stage1Cutscene] " + (_assault ? "Assault" : "Entry") + " · live world bound");
        }

        static Vector3 CrewCenter(Vector3 fallback)
        {
            var total = Vector3.zero;
            int count = 0;
            foreach (var rig in PlayerRig.All)
                if (rig != null && rig.Hips != null) { total += rig.Hips.position; count++; }
            return count > 0 ? total / count : fallback;
        }

        void Hide(Behaviour component)
        {
            if (component == null || !component.enabled) return;
            _hidden.Add(component);
            component.enabled = false;
        }

        void BuildEntryEffects()
        {
            if (_meteorVisual != null)
            {
                _meteor = Instantiate(_meteorVisual, transform);
                _meteor.name = "Entry meteor · visual only";
                _meteor.transform.localScale = Vector3.one * 0.7f;
                var trail = _meteor.AddComponent<TrailRenderer>();
                _trailMaterial = Prim.Flat(new Color(3f, 1.3f, 0.35f));
                trail.sharedMaterial = _trailMaterial;
                trail.time = 0.45f; trail.startWidth = 0.55f; trail.endWidth = 0f;
                _meteor.SetActive(false);
            }
            _flash = NewLight("Impact flash", _impact + Vector3.up, new Color(1f, 0.45f, 0.12f), 18f);
            foreach (var clamp in FindObjectsByType<PartClamp>(FindObjectsSortMode.None))
            {
                var light = NewLight("Clamp reveal", clamp.transform.position + Vector3.up * 0.25f, Color.cyan, 2.5f);
                _guides.Add(light);
            }
            _guides.Add(NewLight("Assembly reveal", _assembly + Vector3.up * 2f, Color.cyan, 12f));
        }

        Light NewLight(string label, Vector3 position, Color color, float range)
        {
            var go = new GameObject(label);
            go.transform.SetParent(transform, false);
            go.transform.position = position;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point; light.color = color; light.range = range;
            light.intensity = 0f; light.shadows = LightShadows.None;
            return light;
        }

        void BuildAssaultEffects()
        {
            if (_meteorVisual == null) return;
            if (_trailMaterial == null) _trailMaterial = Prim.Flat(new Color(4f, 1.5f, 0.3f));
            // A fixed budget of local visual models; no Meteor components or gameplay spawns.
            for (int i = 0; i < 12; i++)
            {
                var meteor = Instantiate(_meteorVisual, transform);
                meteor.name = "Assault meteor visual " + (i + 1);
                meteor.SetActive(false);
                var trail = meteor.AddComponent<LineRenderer>();
                trail.sharedMaterial = _trailMaterial;
                trail.useWorldSpace = true;
                trail.positionCount = 2;
                trail.startWidth = 0.48f + (i % 3) * 0.12f;
                trail.endWidth = 0f;
                trail.numCapVertices = 3;
                trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                trail.receiveShadows = false;
                _assaultMeteors.Add(meteor.transform);
                _assaultTrails.Add(trail);
            }
        }

        void TickAssaultMeteors(float t)
        {
            for (int i = 0; i < _assaultMeteors.Count; i++)
            {
                var meteor = _assaultMeteors[i];
                bool visible = t >= 1f && t < Stage1Sequence.AssaultSeconds;
                meteor.gameObject.SetActive(visible);
                if (!visible) continue;
                // Shared sequence age gives late joiners the same staggered falling formation.
                float u = Mathf.Repeat(t - 1f + i * 0.23f, 3f) / 3f;
                var from = _assembly + new Vector3(-30f + i * 3f, 48f, 8f + (i % 3) * 8f);
                var to = _assembly + new Vector3(-12f + i * 3f, 4f, 8f + (i % 3) * 8f);
                float travel = u * (0.65f + 0.35f * u);
                meteor.position = Vector3.Lerp(from, to, travel);
                meteor.rotation = Quaternion.Euler(t * (90f + i * 7f), i * 31f + t * 65f, t * 35f);
                float fade = Mathf.Clamp01(u / 0.08f) * Mathf.Clamp01((1f - u) / 0.15f);
                meteor.localScale = Vector3.one * ((0.65f + (i % 4) * 0.18f) * fade);
                var trail = _assaultTrails[i];
                trail.widthMultiplier = fade;
                trail.SetPosition(0, meteor.position);
                trail.SetPosition(1, meteor.position + (from - to).normalized * ((5f + i % 4) * fade));
            }
        }

        void LateUpdate()
        {
            if (!Playing || _orbit == null) return;
            float t = _sequence.Age;
            if (!_assault && t >= 15f)
            {
                _orbit.FirstPerson = true;
                _orbit.SnapToTarget();
                _orbit.Cam.fieldOfView = _savedFov;
                _bootstrap.Exploration.SetTabletOpen(true);
            }
            else
            {
                _orbit.FirstPerson = false;
                PoseCamera(t);
            }
            if (_assault) TickAssault(t); else TickEntry(t);
        }

        Vector3 Resolve(Anchor anchor) => anchor switch
        { Anchor.Engine => _engine, Anchor.Assembly => _assembly, Anchor.Impact => _impact, _ => _crew };

        void PoseCamera(float t)
        {
            if (_shots == null || _shots.Length == 0) return;
            Shot shot = _shots[0];
            foreach (var candidate in _shots) if (t >= candidate.start) shot = candidate;
            float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - shot.start) / Mathf.Max(0.01f, shot.duration)));
            Vector3 anchor = Resolve(shot.anchor);
            Vector3 look = anchor + shot.look;
            Vector3 position = anchor + Vector3.Lerp(shot.from, shot.to, u);
            if (!_assault && shot.anchor == Anchor.Impact && t < 9f)
                look = EntryMeteorPosition(t);
            // 실내/이동한 파츠가 카메라 사이에 들어왔을 때 벽 너머를 촬영하지 않는다.
            Vector3 ray = position - look;
            if (ray.sqrMagnitude > 0.1f && Physics.SphereCast(look, 0.18f, ray.normalized,
                out var hit, ray.magnitude, ~0, QueryTriggerInteraction.Ignore))
                position = look + ray.normalized * Mathf.Max(0.4f, hit.distance - 0.2f);
            _orbit.transform.SetPositionAndRotation(position, Quaternion.LookRotation(look - position));
            _orbit.Cam.fieldOfView = shot.fieldOfView;
        }

        void TickEntry(float t)
        {
            if (_meteor != null)
            {
                bool flying = t >= 6.8f && t < 9f;
                _meteor.SetActive(flying);
                if (flying)
                {
                    _meteor.transform.position = EntryMeteorPosition(t);
                    _meteor.transform.Rotate(new Vector3(130f, 70f, 30f) * Time.unscaledDeltaTime);
                }
            }
            if (t >= 9f && !_impacted)
            {
                _impacted = true;
                _bootstrap.Sound?.EmitLocalOneShot(SoundEventId.MeteorImpactSmall, _impact);
                _bootstrap.Sound?.EmitLocalOneShot(SoundEventId.EnvironmentBasePowerDown, _engine);
                EmitImpact();
            }
            if (_flash != null) _flash.intensity = t >= 9f ? 18f * Mathf.Clamp01(1f - (t - 9f) / 0.8f) : 0f;
            float lightScale = t < 9f ? 1f : t < 10.5f ? 0.05f : Mathf.Lerp(0.05f, 1f, Mathf.Clamp01((t - 10.5f) / 4.5f));
            SetLights(lightScale);
            foreach (var light in _guides)
                if (light != null) light.intensity = t >= 10f && t < 15f ? 5f * (0.7f + 0.3f * Mathf.Sin(t * 5f)) : 0f;
        }

        void TickAssault(float t)
        {
            TickAssaultMeteors(t);
            if (!_warned)
            {
                _warned = true;
                _bootstrap.Sound?.EmitLocalOneShot(SoundEventId.MeteorWaveWarning, _orbit.transform.position);
            }
            SetLights(t < Stage1Sequence.WarningSeconds ? (Mathf.Sin(t * 12f) > 0f ? 1f : 0.15f) : 1f);
        }

        Vector3 EntryMeteorPosition(float t)
        {
            float u = Mathf.Clamp01((t - 6.8f) / 2.2f);
            return Vector3.Lerp(_impact + new Vector3(-32f, 48f, 12f), _impact, u * u);
        }

        void EmitImpact()
        {
            var go = new GameObject("Entry impact sparks");
            go.transform.SetParent(transform, false);
            go.transform.position = _impact + Vector3.up * 0.2f;
            var particles = go.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.loop = false; main.startLifetime = 1.1f; main.startSpeed = 6f;
            main.startSize = 0.22f; main.startColor = new Color(1f, 0.55f, 0.12f);
            main.gravityModifier = 0.5f; main.maxParticles = 80;
            var emission = particles.emission; emission.enabled = false;
            var shape = particles.shape; shape.shapeType = ParticleSystemShapeType.Hemisphere; shape.radius = 0.6f;
            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = _trailMaterial;
            particles.Play(); particles.Emit(64);
        }

        void SetLights(float scale)
        {
            for (int i = 0; i < _lights.Count; i++)
                if (_lights[i] != null) _lights[i].intensity = _intensities[i] * scale;
        }

        public void Stop()
        {
            if (!Playing) return;
            SetLights(1f);
            foreach (var component in _hidden) if (component != null) component.enabled = true;
            _hidden.Clear();
            foreach (var meteor in _assaultMeteors)
                if (meteor != null) { meteor.gameObject.SetActive(false); Destroy(meteor.gameObject); }
            _assaultMeteors.Clear();
            _assaultTrails.Clear();
            _lights.Clear();
            _intensities.Clear();
            if (_cutsceneShower != null)
            {
                _cutsceneShower.Clear();
                Destroy(_cutsceneShower.gameObject);
                _cutsceneShower = null;
            }
            if (_orbit != null)
            {
                _orbit.FirstPerson = _savedFirstPerson;
                _orbit.Cam.fieldOfView = _savedFov;
                _orbit.enabled = _savedOrbitEnabled;
                _orbit.SnapToTarget();
            }
            if (_bootstrap != null && _bootstrap.Exploration != null)
            {
                // Entry hands the tablet to the player, including an early skip.
                // Interrupted entry and assault still restore their previous state.
                bool entryCompleted = !_assault && !_sequence.BlocksInput
                    && _bootstrap.Session != null && !_bootstrap.Session.EndingActive;
                _bootstrap.Exploration.SetTabletOpen(entryCompleted || _savedTablet);
                if (entryCompleted) _bootstrap.Session.SetCursor(false);
            }
            _sequence = null;
        }

        void OnGUI()
        {
            if (!Playing) return;
            float duration = _assault ? Stage1Sequence.AssaultSeconds : Stage1Sequence.EntrySeconds;
            float t = _sequence.Age;
            float bars = Mathf.Clamp01(Mathf.Min(t / 0.5f, (duration - t) / 0.5f));
            var color = GUI.color;
            var depth = GUI.depth;
            GUI.depth = -7000;
            GUI.color = Color.black;
            float height = Screen.height * 0.1f * bars;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(0, Screen.height - height, Screen.width, height), Texture2D.whiteTexture);
            GUI.color = color; GUI.depth = depth;
        }

        void OnDisable() => Stop();
        void OnDestroy() { Stop(); if (_trailMaterial != null) Destroy(_trailMaterial); }
    }
}
