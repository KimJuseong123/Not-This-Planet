using System;
using System.Collections.Generic;
using Metagame.Core;
using Metagame.Gameplay;
using Metagame.Gameplay.Parts;
using UnityEngine;

namespace Metagame.UI
{
    /// <summary>실제 파츠 메시와 서버 스냅샷을 이용한 표시 전용 엔딩 무대.</summary>
    public sealed class DemoEndingView : MonoBehaviour
    {
        // 씬의 물리/네트워크 오브젝트를 복제하지 않고 메시만 복사한다.
        const int StageLayer = 31;
        static readonly Vector3 StageOrigin = new(10000f, 10000f, 10000f);
        DemoEnding _ending;
        Func<bool> _returnToLobby;
        Transform _stage, _ship, _flames, _ground, _planet;
        Camera _camera;
        readonly List<Doll> _actors = new();
        readonly List<Transform> _jets = new();
        readonly List<Camera> _previousCameras = new();
        readonly List<Canvas> _previousCanvases = new();
        readonly List<(Light Light, int Mask)> _previousLights = new();
        readonly List<Material> _glowMaterials = new();
        Bounds _bounds;
        RenderTexture _photo;
        GUIStyle _title;
        Font _font;
        bool _returning, _savedFog;

        public void Configure(DemoEnding ending, Func<bool> returnToLobby)
        {
            _ending = ending;
            _returnToLobby = returnToLobby;
        }

        void LateUpdate()
        {
            if (_ending == null || !_ending.Active) return;
            if (_stage == null) BuildStage();
            var stage = _ending.Stage;
            if (stage == DemoEndingStage.Lobby)
            {
                if (!_returning) _returning = _returnToLobby();
                return;
            }
            if (stage == DemoEndingStage.Boarding) ShowBoarding(_ending.Elapsed);
            else if (stage == DemoEndingStage.Launching)
                ShowLaunch(_ending.Elapsed - DemoEnding.BoardingSeconds);
            else if (_photo == null) TakePhotograph();
        }

        void BuildStage()
        {
            _stage = new GameObject("Demo Ending Stage").transform;
            _stage.SetParent(transform, false);
            _stage.position = StageOrigin;
            _ship = NewRoot("Completed Ship", _stage);
            _flames = NewRoot("Exhaust", _ship);
            _savedFog = RenderSettings.fog;
            RenderSettings.fog = false;

            foreach (var camera in FindObjectsByType<Camera>())
                if (camera.enabled) { _previousCameras.Add(camera); camera.enabled = false; }
            foreach (var canvas in FindObjectsByType<Canvas>())
                if (canvas.enabled) { _previousCanvases.Add(canvas); canvas.enabled = false; }
            // HUD, 태블릿, 이름표와 열린 설정 창이 컷씬 위에 남지 않게 한다.
            foreach (var view in GetComponents<MonoBehaviour>())
                if (view != this && view.GetType().Namespace == "Metagame.UI") view.enabled = false;

            _camera = NewRoot("Ending Camera", _stage).gameObject.AddComponent<Camera>();
            _camera.cullingMask = 1 << StageLayer;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.009f, 0.015f, 0.045f);
            _camera.fieldOfView = 42f;
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 600f;
            _camera.depth = 100;

            foreach (var light in FindObjectsByType<Light>())
            { _previousLights.Add((light, light.cullingMask)); light.cullingMask &= ~(1 << StageLayer); }
            var key = NewRoot("Ending Key Light", _stage).gameObject.AddComponent<Light>();
            key.type = LightType.Directional;
            key.color = new Color(0.88f, 0.93f, 1f);
            key.intensity = 0.85f;
            key.cullingMask = 1 << StageLayer;
            key.transform.rotation = Quaternion.Euler(35f, -35f, 0f);

            var snapshot = _ending.Snapshot;
            var origin = snapshot.Parts[0].Position;
            var first = true;
            foreach (var pose in snapshot.Parts)
            {
                SpaceshipPart source = null;
                foreach (var part in _ending.Parts)
                    if (part != null && part.name == pose.Name) { source = part; break; }
                if (source == null) continue;
                var root = NewRoot(pose.Name, _ship);
                root.localPosition = pose.Position - origin;
                root.localRotation = pose.Rotation;
                root.localScale = pose.Scale;
                CopyMeshes(source.transform, root);
                var beads = NewRoot("Weld Beads", root);
                beads.localScale = new Vector3(1f / pose.Scale.x, 1f / pose.Scale.y, 1f / pose.Scale.z);
                for (var c = 0; c < pose.Beads.Length; c++)
                {
                    var chain = pose.Beads[c];
                    var helpful = pose.HelpfulBeads != null && c < pose.HelpfulBeads.Length && pose.HelpfulBeads[c];
                    for (var b = 0; b < chain.BlobCount; b++)
                    {
                        var radius = b == chain.BlobCount - 1 ? chain.TipRadius : PhysicsScale.WeldBeadMaxRadius;
                        MakeShape(PrimitiveType.Sphere, beads,
                            chain.Origin + chain.Direction * (b * PhysicsScale.WeldBeadStep),
                            Vector3.one * (radius * 2f), helpful
                                ? new Color(0.12f, 0.78f, 0.72f) : new Color(0.42f, 0.36f, 0.32f));
                    }
                }
                foreach (var renderer in root.GetComponentsInChildren<Renderer>())
                {
                    var bounds = renderer.bounds;
                    bounds.center -= StageOrigin;
                    if (first) { _bounds = bounds; first = false; }
                    else _bounds.Encapsulate(bounds);
                }
            }
            if (first) _bounds = new Bounds(Vector3.zero, Vector3.one * 4f);
            // 비뚤어진/가로 배치도 그대로 보존하면서 무대 바닥에 놓는다.
            var offset = new Vector3(-_bounds.center.x, -_bounds.min.y, -_bounds.center.z);
            _ship.localPosition = offset;
            _launchBase = offset;
            _bounds.center += offset;

            _ground = MakeShape(PrimitiveType.Cylinder, _stage, new Vector3(0f, -0.15f, 0f),
                new Vector3(Mathf.Max(16f, _bounds.size.x + 8f), 0.15f, Mathf.Max(16f, _bounds.size.z + 8f)),
                new Color(0.13f, 0.19f, 0.26f));
            _planet = MakeShape(PrimitiveType.Sphere, _stage, new Vector3(0f, -45f, 40f),
                Vector3.one * 75f, new Color(0.20f, 0.36f, 0.68f));
            _planet.gameObject.SetActive(false);
            var random = new System.Random(117);
            for (var i = 0; i < 110; i++)
                MakeShape(PrimitiveType.Sphere, _stage,
                    new Vector3((float)random.NextDouble() * 240f - 120f,
                        (float)random.NextDouble() * 160f - 25f, 90f + (float)random.NextDouble() * 40f),
                    Vector3.one * (0.09f + (float)random.NextDouble() * 0.18f), Color.white);

            var fireMaterial = GlowMaterial(new Color(1f, 0.28f, 0.025f));
            var coreMaterial = GlowMaterial(new Color(1f, 0.93f, 0.55f));
            // 연결된 각 파츠 아래로 불꽃을 배치한다. 파츠의 회전은 그대로 유지한다.
            foreach (var pose in snapshot.Parts)
            {
                var spec = PartSpec.Find(pose.Name);
                var nozzle = pose.Position - origin + pose.Rotation * Vector3.down * (spec != null ? spec.Height * 0.45f : 0.5f);
                var jet = NewRoot("Thruster", _flames);
                jet.localPosition = nozzle;
                var fire = MakeShape(PrimitiveType.Sphere, jet, new Vector3(0f, -0.9f, 0f),
                    new Vector3(0.5f, 2.2f, 0.5f), new Color(1f, 0.30f, 0.035f));
                var core = MakeShape(PrimitiveType.Sphere, jet, new Vector3(0f, -0.45f, -0.19f),
                    new Vector3(0.24f, 1.2f, 0.24f), new Color(1f, 0.9f, 0.4f));
                if (fireMaterial != null) fire.GetComponent<Renderer>().sharedMaterial = fireMaterial;
                if (coreMaterial != null) core.GetComponent<Renderer>().sharedMaterial = coreMaterial;
                _jets.Add(jet);
            }
            _flames.gameObject.SetActive(false);
            foreach (var player in snapshot.Players)
            {
                var doll = RagdollBuilder.Build(player.Name, StageOrigin, Quaternion.identity,
                    player.Suit, player.Accent, !SpaceSuit.Available);
                doll.Root.SetParent(_stage, true);
                foreach (var body in doll.Bodies) body.isKinematic = true;
                foreach (var collider in doll.Root.GetComponentsInChildren<Collider>()) collider.enabled = false;
                foreach (var script in doll.Root.GetComponentsInChildren<MonoBehaviour>()) script.enabled = false;
                foreach (var t in doll.Root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = StageLayer;
                _actors.Add(doll);
            }
        }

        void CopyMeshes(Transform source, Transform destination)
        {
            foreach (var renderer in source.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (renderer.GetComponentInParent<WeldBead>() != null
                    && IsBead(renderer.transform, source)) continue;
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null || !renderer.gameObject.activeInHierarchy) continue;
                // 고정못·소켓·상호작용 안내는 완성 우주선 메시에서 제외한다.
                if (renderer.GetComponentInParent<PartClamp>() != null
                    || renderer.GetComponentInParent<PartSocket>() != null) continue;
                var copy = NewRoot(renderer.name, destination);
                copy.localPosition = source.InverseTransformPoint(renderer.transform.position);
                copy.localRotation = Quaternion.Inverse(source.rotation) * renderer.transform.rotation;
                var scale = source.lossyScale;
                var childScale = renderer.transform.lossyScale;
                copy.localScale = new Vector3(childScale.x / scale.x, childScale.y / scale.y, childScale.z / scale.z);
                copy.gameObject.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                var view = copy.gameObject.AddComponent<MeshRenderer>();
                view.sharedMaterials = renderer.sharedMaterials;
            }
        }

        static bool IsBead(Transform child, Transform part)
        {
            for (var t = child; t != null && t != part; t = t.parent)
                if (t.name == WeldBead.ContainerName) return true;
            return false;
        }

        void ShowBoarding(float elapsed)
        {
            var entry = new Vector3(0f, 0.1f, _bounds.min.z + 0.3f);
            for (var i = 0; i < _actors.Count; i++)
            {
                var progress = Mathf.Clamp01((elapsed - i * 0.45f) / 3f);
                var start = new Vector3((i - (_actors.Count - 1) * 0.5f) * 1.1f, 0f, _bounds.min.z - 4f);
                var foot = Vector3.Lerp(start, entry, progress);
                _actors[i].Root.gameObject.SetActive(progress < 1f);
                PoseActor(_actors[i], foot, Quaternion.LookRotation(entry - start), progress < 1f);
            }
            Frame(_bounds.center, Mathf.Max(_bounds.size.magnitude, 10f), new Vector3(-0.6f, 0.3f, -1f));
        }

        void ShowLaunch(float elapsed)
        {
            var t = Mathf.Clamp01(elapsed / DemoEnding.LaunchSeconds);
            _ground.gameObject.SetActive(t < 0.25f);
            _planet.gameObject.SetActive(t > 0.15f);
            _flames.gameObject.SetActive(true);
            foreach (var actor in _actors) actor.Root.gameObject.SetActive(false);
            // 원래 상대 배치를 유지한 채 전체 우주선만 위로 가속한다.
            _ship.localPosition = _launchBase + Vector3.up * (t * t * 70f);
            for (var i = 0; i < _jets.Count; i++)
                _jets[i].localScale = new Vector3(1f, 1.1f + 0.25f * Mathf.Sin(elapsed * 32f + i), 1f);
            Frame(_bounds.center + Vector3.up * (t * t * 70f),
                Mathf.Max(_bounds.size.magnitude, 9f) * (1f + 0.4f * t), new Vector3(-0.55f, 0.15f, -1f));
        }

        Vector3 _launchBase;

        void TakePhotograph()
        {
            _flames.gameObject.SetActive(false);
            _ground.gameObject.SetActive(true);
            _planet.gameObject.SetActive(false);
            // 재접속으로 발사 구간을 건너뛰었을 때도 원래 완성 자세로 복원한다.
            _ship.localPosition = _launchBase;
            var photoBounds = _bounds;
            for (var i = 0; i < _actors.Count; i++)
            {
                var foot = new Vector3((i - (_actors.Count - 1) * 0.5f) * 1.1f, 0f, _bounds.min.z - 1.5f);
                _actors[i].Root.gameObject.SetActive(true);
                PoseActor(_actors[i], foot, Quaternion.Euler(0f, 180f, 0f), false);
                photoBounds.Encapsulate(new Bounds(foot + Vector3.up * 0.7f, new Vector3(1f, 1.4f, 1f)));
            }
            Frame(photoBounds.center, Mathf.Max(photoBounds.size.magnitude, 7f), new Vector3(0f, 0.14f, -1f));
            // 실제 정지 사진: 다음 프레임의 물리·애니메이션·참가자 이탈과 무관하게 유지한다.
            var width = Mathf.Clamp(Screen.width, 640, 1920);
            var height = Mathf.Max(360, Mathf.RoundToInt(width / Mathf.Max(0.5f, _camera.aspect)));
            _photo = new RenderTexture(width, height, 24) { name = "Team Photograph" };
            _photo.Create();
            _camera.targetTexture = _photo;
            _camera.Render();
            _camera.targetTexture = null;
            // 사진과 종료 문구는 OnGUI로 표시하지만 Game View에는 활성 카메라가 필요하다.
            // 무대는 다시 그리지 않고 검은 배경만 출력해 사진을 그대로 유지한다.
            _camera.cullingMask = 0;
            _camera.backgroundColor = Color.black;
        }

        void PoseActor(Doll doll, Vector3 foot, Quaternion facing, bool walking)
        {
            doll.Teleport(StageOrigin + foot + Vector3.up * RagdollBuilder.HipsY, facing);
            if (doll.Suit == null) return;
            doll.Suit.Sync();
            doll.Suit.Locomotion?.Apply(new RabbitLocomotion.State
            {
                Speed = walking ? 2f : 0f, Grounded = true, Direction = Vector2.up
            }, Time.unscaledDeltaTime);
        }

        void Frame(Vector3 center, float diameter, Vector3 direction)
        {
            var halfFov = _camera.fieldOfView * Mathf.Deg2Rad * 0.5f;
            var distance = diameter * 0.6f / (Mathf.Sin(halfFov) * Mathf.Min(1f, _camera.aspect));
            _camera.transform.position = StageOrigin + center + direction.normalized * distance;
            _camera.transform.LookAt(StageOrigin + center);
        }

        Material GlowMaterial(Color color)
        {
            var shader = Resources.Load<Shader>("GuidanceNoFog");
            if (shader == null) return null;
            var material = new Material(shader);
            material.SetColor("_BaseColor", color);
            material.SetColor("_EmissionColor", color);
            material.SetFloat("_EmissionIntensity", 1f);
            _glowMaterials.Add(material);
            return material;
        }

        static Transform NewRoot(string name, Transform parent)
        {
            var root = new GameObject(name).transform;
            root.gameObject.layer = StageLayer;
            root.SetParent(parent, false);
            return root;
        }

        static Transform MakeShape(PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Color color)
        {
            var shape = GameObject.CreatePrimitive(type);
            shape.layer = StageLayer;
            shape.transform.SetParent(parent, false);
            shape.transform.localPosition = position;
            shape.transform.localScale = scale;
            var collider = shape.GetComponent<Collider>();
            collider.enabled = false;
            Destroy(collider);
            shape.GetComponent<Renderer>().sharedMaterial = Prim.Dough(color, 0.2f, true);
            return shape.transform;
        }

        void OnGUI()
        {
            if (_ending == null || !_ending.Active || _stage == null) return;
            GUI.depth = -900;
            var screen = new Rect(0f, 0f, Screen.width, Screen.height);
            if (_ending.Stage == DemoEndingStage.Photograph && _photo != null)
            {
                GUI.color = new Color(0.94f, 0.93f, 0.89f);
                GUI.DrawTexture(screen, Texture2D.whiteTexture);
                GUI.color = Color.white;
                GUI.DrawTexture(new Rect(24f, 24f, Screen.width - 48f, Screen.height - 65f), _photo, ScaleMode.ScaleToFit);
            }
            else if (_ending.Stage >= DemoEndingStage.EndTitle)
            {
                GUI.color = Color.black;
                GUI.DrawTexture(screen, Texture2D.whiteTexture);
                GUI.color = Color.white;
                if (_title == null)
                {
                    _font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "Noto Sans CJK KR", "Apple SD Gothic Neo", "Arial" }, 64);
                    _title = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, font = _font, fontSize = 64 };
                    _title.normal.textColor = Color.white;
                }
                GUI.Label(screen, "데모 끝", _title);
            }
        }

        void OnDestroy()
        {
            if (_stage == null) return;
            RenderSettings.fog = _savedFog;
            foreach (var camera in _previousCameras) if (camera != null) camera.enabled = true;
            foreach (var canvas in _previousCanvases) if (canvas != null) canvas.enabled = true;
            if (_photo != null) { _photo.Release(); Destroy(_photo); }
            if (_font != null) Destroy(_font);
            foreach (var entry in _previousLights) if (entry.Light != null) entry.Light.cullingMask = entry.Mask;
            foreach (var material in _glowMaterials) if (material != null) Destroy(material);
            Destroy(_stage.gameObject);
        }
    }
}
