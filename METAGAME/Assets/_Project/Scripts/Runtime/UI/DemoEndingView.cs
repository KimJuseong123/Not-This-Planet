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
        GameObject[] _partVisuals;
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
        GUIStyle _title, _timeLabel;
        Font _font;
        bool _returning, _savedFog;

        public void Configure(DemoEnding ending, Func<bool> returnToLobby, GameObject[] partVisuals = null)
        {
            _ending = ending;
            _returnToLobby = returnToLobby;
            _partVisuals = partVisuals;
        }

        public void Prepare()
        {
            if (_stage != null) return;
            BuildStage();
            ShowBoarding(0f);
        }

        public void Tick() => LateUpdate();

        void LateUpdate()
        {
            if (_ending == null || !_ending.Active) return;
            if (_stage == null) Prepare();
            if (!_ending.PlaybackStarted) return;
            var stage = _ending.Stage;
            if (stage == DemoEndingStage.Lobby)
            {
                if (!_returning) _returning = _returnToLobby();
                return;
            }
            if (stage == DemoEndingStage.Boarding) ShowBoarding(_ending.Elapsed);
            else if (stage == DemoEndingStage.Launching)
                ShowLaunch(_ending.Elapsed - _ending.BoardingEndsAt);
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
            _camera.farClipPlane = 2000f;
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
                Transform source = null;
                foreach (var part in _ending.Parts)
                    if (part != null && part.name == pose.Name) { source = part.transform; break; }
                if (source == null && _partVisuals != null)
                    for (var p = 0; p < PartSpec.All.Length && p < _partVisuals.Length; p++)
                        if (pose.Name == PartSpec.All[p].Name && _partVisuals[p] != null)
                            source = _partVisuals[p].transform;
                if (source == null) continue;
                var root = NewRoot(pose.Name, _ship);
                root.localPosition = pose.Position - origin;
                root.localRotation = pose.Rotation;
                root.localScale = pose.Scale;
                CopyMeshes(source, root);
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
            _planet.gameObject.SetActive(true);
            var random = new System.Random(117);
            for (var i = 0; i < 110; i++)
                MakeShape(PrimitiveType.Sphere, _stage,
                    new Vector3((float)random.NextDouble() * 240f - 120f,
                        (float)random.NextDouble() * 160f - 25f, 90f + (float)random.NextDouble() * 40f),
                    Vector3.one * (0.09f + (float)random.NextDouble() * 0.18f), Color.white);

            var fireMaterial = GlowMaterial(new Color(1f, 0.28f, 0.025f));
            var coreMaterial = GlowMaterial(new Color(1f, 0.93f, 0.55f));
            // 추진 엔진에만 노즐을 붙인다. 조립 순서와 루트 배열 순서는 다를 수 있다.
            foreach (var pose in snapshot.Parts)
            {
                if (pose.Name != PartSpec.All[0].Name) continue;
                var spec = PartSpec.Find(pose.Name);
                var nozzle = pose.Position - origin + pose.Rotation * Vector3.down * (spec.Height * pose.Scale.y * 0.5f);
                var jet = NewRoot("Thruster", _flames);
                jet.localPosition = nozzle;
                jet.localRotation = pose.Rotation;
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
                PlayerAppearance.Apply(doll, player.Appearance);
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
                if (filter == null || filter.sharedMesh == null
                    || (source.gameObject.scene.IsValid() && !renderer.gameObject.activeInHierarchy)) continue;
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
            int greeting = DemoEnding.GreetingActorAt(elapsed, _actors.Count);
            float progress = DemoEnding.BoardingProgressAt(elapsed, _actors.Count);
            for (var i = 0; i < _actors.Count; i++)
            {
                var start = GreetingFoot(i);
                var foot = Vector3.Lerp(start, entry, progress);
                _actors[i].Root.gameObject.SetActive(progress < 1f);
                var walking = progress > 0f && progress < 1f;
                PoseActor(_actors[i], foot, walking ? Quaternion.LookRotation(entry - start)
                    : Quaternion.Euler(0f, 180f, 0f), walking,
                    greeting == i ? i : -1, elapsed - i * DemoEnding.GreetingSeconds);
            }
            if (greeting >= 0)
            {
                float localTime = elapsed - greeting * DemoEnding.GreetingSeconds;
                GreetingCamera(greeting, localTime, out var position, out var focus);
                if (greeting > 0 && localTime < 0.3f)
                {
                    GreetingCamera(greeting - 1, DemoEnding.GreetingSeconds, out var previousPosition, out var previousFocus);
                    float blend = Mathf.SmoothStep(0f, 1f, localTime / 0.3f);
                    position = Vector3.Lerp(previousPosition, position, blend);
                    focus = Vector3.Lerp(previousFocus, focus, blend);
                }
                _camera.transform.position = position;
                _camera.transform.LookAt(focus);
            }
            else
            {
                FrameDeparture();
                // 마지막 얼굴에서 전원 탑승을 볼 수 있는 와이드 화면으로 복귀한다.
                if (_actors.Count > 0)
                {
                    GreetingCamera(_actors.Count - 1, DemoEnding.GreetingSeconds, out var position, out var focus);
                    float blend = Mathf.SmoothStep(0f, 1f,
                        (elapsed - _actors.Count * DemoEnding.GreetingSeconds) / 0.75f);
                    _camera.transform.position = Vector3.Lerp(position, _camera.transform.position, blend);
                    _camera.transform.LookAt(Vector3.Lerp(focus, StageOrigin + _bounds.center, blend));
                }
            }
        }

        Vector3 GreetingFoot(int index) => new Vector3(
            (index - (_actors.Count - 1) * 0.5f) * 1.1f, 0f, _bounds.min.z - 4f);

        void GreetingCamera(int index, float localTime, out Vector3 position, out Vector3 focus)
        {
            // 같은 서버 시각을 샘플링하면 프레임 속도·늦은 씬 진입과 무관하게 같은 컷이다.
            float closeUp = Mathf.SmoothStep(0f, 1f, (localTime - 0.6f) / 0.65f);
            var center = GreetingFoot(index) + Vector3.up * Mathf.Lerp(0.72f, RagdollBuilder.HeadY + 0.08f, closeUp);
            float diameter = Mathf.Lerp(1.9f, 0.92f, closeUp);
            float halfFov = _camera.fieldOfView * Mathf.Deg2Rad * 0.5f;
            float distance = diameter * 0.6f / (Mathf.Sin(halfFov) * Mathf.Min(1f, _camera.aspect));
            focus = StageOrigin + center;
            position = focus + new Vector3(0f, 0.04f, -1f).normalized * distance;
        }

        void ShowLaunch(float elapsed)
        {
            var t = Mathf.Clamp01(elapsed / DemoEnding.LaunchSeconds);
            _ground.gameObject.SetActive(true);
            _planet.gameObject.SetActive(true);
            _flames.gameObject.SetActive(true);
            foreach (var actor in _actors) actor.Root.gameObject.SetActive(false);
            // 원래 상대 배치를 유지한 채 전체 우주선만 위로 가속한다.
            var rise = Vector3.up * (4f * t + 320f * t * t * t);
            _ship.localPosition = _launchBase + rise;
            for (var i = 0; i < _jets.Count; i++)
                _jets[i].localScale = new Vector3(1f, 1.1f + 0.25f * Mathf.Sin(elapsed * 32f + i), 1f);
            // 출발 지점에 카메라를 고정한다. 회전만 추적하므로 멀어질수록 기체가 작아진다.
            FrameDeparture();
            _camera.transform.LookAt(StageOrigin + _bounds.center + rise);
        }

        void FrameDeparture() => Frame(_bounds.center, Mathf.Max(_bounds.size.magnitude, 10f),
            new Vector3(-0.6f, 0.3f, -1f));

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
                PoseActor(_actors[i], foot, Quaternion.Euler(0f, 180f + (i % 3 - 1) * 12f, 0f), false, i, 0f);
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

        void PoseActor(Doll doll, Vector3 foot, Quaternion facing, bool walking, int gesture = -1, float elapsed = 0f)
        {
            doll.Teleport(StageOrigin + foot + Vector3.up * RagdollBuilder.HipsY, facing);
            if (gesture >= 0)
            {
                // 키네마틱 표시 인형의 팔을 어깨 중심으로 돌린 후 스킨에 동기화한다.
                var wave = Mathf.Sin(elapsed * 9f + gesture * 1.3f) * 15f;
                if (gesture % 3 != 1) RaiseArm(doll.Right, facing, 135f + wave);
                if (gesture % 3 != 0) RaiseArm(doll.Left, facing, -125f - wave);
                doll.Head.transform.rotation *= Quaternion.Euler(0f, 0f, (gesture % 3 - 1) * 9f);
            }
            if (doll.Suit == null) return;
            doll.Suit.Sync();
            if (gesture < 0) doll.Suit.Locomotion?.Apply(new RabbitLocomotion.State
            {
                Speed = walking ? 2f : 0f, Grounded = true, Direction = Vector2.up
            }, Time.unscaledDeltaTime);
        }

        static void RaiseArm(Arm arm, Quaternion facing, float angle)
        {
            var shoulder = arm.Upper.transform.position + facing * Vector3.up
                * ((RagdollBuilder.UpperArmTop - RagdollBuilder.UpperArmBot) * 0.5f);
            var rotation = Quaternion.AngleAxis(angle, facing * Vector3.forward);
            RotateLimb(arm.Upper.transform, shoulder, rotation);
            RotateLimb(arm.Lower.transform, shoulder, rotation);
            RotateLimb(arm.Hand.Body.transform, shoulder, rotation);
        }

        static void RotateLimb(Transform limb, Vector3 pivot, Quaternion rotation)
        {
            limb.SetPositionAndRotation(pivot + rotation * (limb.position - pivot), rotation * limb.rotation);
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
            if (_ending == null || !_ending.Active || !_ending.PlaybackStarted || _stage == null) return;
            var savedColor = GUI.color;
            var savedMatrix = GUI.matrix;
            var savedDepth = GUI.depth;
            GUI.depth = -900;
            var screen = new Rect(0f, 0f, Screen.width, Screen.height);
            if (_ending.Stage == DemoEndingStage.Photograph && _photo != null)
            {
                var elapsed = _ending.Elapsed - _ending.PhotographStartsAt;
                var reveal = Mathf.SmoothStep(0f, 1f, elapsed / 0.65f);
                var fade = Mathf.SmoothStep(0f, 1f,
                    (elapsed - (DemoEnding.PhotographSeconds - DemoEnding.FadeSeconds)) / DemoEnding.FadeSeconds);
                GUI.color = new Color(0.025f, 0.035f, 0.055f);
                GUI.DrawTexture(screen, Texture2D.whiteTexture);
                var height = Screen.height * 0.78f * Mathf.Lerp(0.88f, 1f, reveal);
                var width = Mathf.Min(Screen.width * 0.82f, height * _photo.width / _photo.height);
                height = width * _photo.height / _photo.width;
                var border = Mathf.Max(8f, Screen.height * 0.018f);
                var card = new Rect((Screen.width - width) * 0.5f - border,
                    (Screen.height - height) * 0.5f - border + (1f - reveal) * Screen.height * 0.12f,
                    width + border * 2f, height + border * 3.5f);
                GUIUtility.RotateAroundPivot(Mathf.Lerp(-6f, -2f, reveal), card.center);
                GUI.color = new Color(0f, 0f, 0f, 0.5f);
                GUI.DrawTexture(new Rect(card.x + border * 0.6f, card.y + border * 0.8f, card.width, card.height), Texture2D.whiteTexture);
                GUI.color = new Color(0.96f, 0.95f, 0.91f);
                GUI.DrawTexture(card, Texture2D.whiteTexture);
                GUI.color = Color.white;
                GUI.DrawTexture(new Rect(card.x + border, card.y + border, width, height), _photo, ScaleMode.ScaleToFit);
                GUI.matrix = savedMatrix;
                GUI.color = new Color(0f, 0f, 0f, fade);
                GUI.DrawTexture(screen, Texture2D.whiteTexture);
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
                    _timeLabel = new GUIStyle(_title);
                }
                _title.fontSize = Mathf.Clamp(Mathf.RoundToInt(Screen.height * 0.085f), 24, 80);
                _timeLabel.fontSize = Mathf.Max(16, _title.fontSize / 3);
                GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01((_ending.Elapsed - _ending.EndTitleStartsAt) / 0.6f));
                GUI.Label(new Rect(0f, Screen.height * 0.36f, Screen.width, Screen.height * 0.2f), "데모 끝", _title);
                var duration = TimeSpan.FromSeconds(Math.Floor(_ending.Snapshot.PlayTimeSeconds));
                GUI.Label(new Rect(0f, Screen.height * 0.57f, Screen.width, Screen.height * 0.08f),
                    $"플레이 타임  {(long)duration.TotalHours:00}시간 {duration.Minutes:00}분 {duration.Seconds:00}초", _timeLabel);
            }
            GUI.color = savedColor;
            GUI.matrix = savedMatrix;
            GUI.depth = savedDepth;
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
