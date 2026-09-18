using System.Collections.Generic;
using Metagame.Gameplay.Hazards;
using Metagame.Gameplay.Parts;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Metagame.Gameplay.Cheats
{
    /// <summary>
    /// 치트 시험장(<c>Assets/Scenes/CheatArena.unity</c>)의 치트 패널.
    ///
    /// <para><b>왜 있는가.</b> 기획 수치를 바꾸고 확인할 때마다 운석이 떨어질 때까지 30초씩
    /// 기다리거나, 파츠를 찾아 고정못을 풀고 끌어와야 했다. 확인 한 번이 고치는 시간보다
    /// 길었다. 여기서는 운석을 <b>지금 조준한 곳</b>에 떨어뜨리고, 공세를 바로 시작하고,
    /// 난이도 표의 뒷 시간대로 건너뛰고, 한 키로 처음 상태로 돌아간다.</para>
    ///
    /// <para><b>게임플레이 코드를 우회하지 않는다.</b> 운석은 <see cref="MeteorSpawner.LaunchAt"/>
    /// 로 같은 스포너가 만들고, 배치는 <see cref="TestArena.Arrange"/> 가, 리스폰은
    /// <see cref="GameSession.Respawn"/> 이 한다. 치트로 본 결과가 실제 판과 다르면 치트가
    /// 아니라 시험이 틀린 것이므로, 새 동작을 만들지 않고 있는 진입만 부른다.</para>
    ///
    /// <para><b>에디터·개발 빌드 전용.</b> <c>DevManager</c> 와 같은 조건이다. 시험장 씬은
    /// 빌드 목록에 넣지 않으므로 릴리스에는 씬도 이 패널도 들어가지 않는다.</para>
    ///
    /// <para><b>키.</b> F8 패널 · F5 빠른 리셋 · F6 조준점에 운석 · F7 공세 바로 시작 ·
    /// F9 씬 다시 불러오기. F1·F2(감정·춤), F11·F12(<c>DevManager</c>)와 겹치지 않게 골랐다.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CheatArenaConsole : MonoBehaviour
    {
        public const KeyCode ToggleKey = KeyCode.F8;
        public const KeyCode QuickResetKey = KeyCode.F5;
        public const KeyCode DropMeteorKey = KeyCode.F6;
        public const KeyCode WaveKey = KeyCode.F7;

        /// <summary>
        /// 씬 다시 불러오기는 <b>Shift 를 누른 빠른 리셋</b>이다.
        ///
        /// <para>예전에는 F9 였는데 <c>DevManager</c> 가 그 키로 공세 컷씬을 연다
        /// (S15P21B202-587). 시험장에도 <c>DevManager</c> 가 있으므로 한 번 누르면 둘이
        /// 같이 돌아간다. 남은 기능 키가 없어 — F1·F2 감정·춤, F3·F4 강도, F5~F8 여기,
        /// F9·F10 컷씬, F11·F12 <c>DevManager</c> — 같은 성격의 F5 에 묶었다.
        /// 빠른 리셋과 씬 재시작은 "얼마나 되돌리느냐" 만 다르다.</para>
        /// </summary>
        public const KeyCode ReloadModifier = KeyCode.LeftShift;

        /// <summary>공세 강도 내리기·올리기. 패널을 열지 않고 뛰면서 바꾼다.</summary>
        public const KeyCode PresetDownKey = KeyCode.F3;
        public const KeyCode PresetUpKey = KeyCode.F4;

        /// <summary>
        /// 씬을 다시 불러오는 방법. 에디터 어셈블리가 채운다(<c>CheatArenaSceneReload</c>).
        ///
        /// <para>시험장 씬은 빌드 목록에 없어 <see cref="SceneManager.LoadScene(string)"/> 로는
        /// 에디터 플레이 중에도 열리지 않는다. 에디터에는 그 제한을 넘는 API 가 따로 있는데
        /// 런타임 코드가 <c>UnityEditor</c> 를 참조하면 안 되므로(작성 규칙 §1), 에디터 쪽이
        /// 여기에 함수를 꽂아 준다. 비어 있으면 빌드 목록 기준으로 시도한다.</para>
        /// </summary>
        public static System.Func<string, bool> ReloadHook;

        public bool IsOpen { get; private set; }
        public bool GodMode { get; private set; }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        const float PanelWidth = 380f;

        /// <summary>IMGUI 창 번호. 다른 창(일시 메뉴 등)과 겹치지 않을 임의의 값이다.</summary>
        const int WindowId = 0x43484154;   // 'C''H''A''T'
        const float TeleportDistance = 4.5f;
        const float AimFallbackDistance = 8f;
        const float ProbeHeight = 60f;

        static readonly float[] GravityScales = { 0.5f, 1f, 2f };
        static readonly float[] TimeScales = { 0.25f, 0.5f, 1f, 2f };
        static readonly int[] SkipMinutes = { 0, 5, 10, 15, 20 };

        /// <summary>플레이어 주변을 노리는 비율(§17.5 기본 30%).</summary>
        static readonly float[] NearPlayerWeights = { 0.30f, 0.50f, 0.70f, 0.90f };

        /// <summary>강한 충격이 되는 안쪽 비율(§20.1 경계). 기본 0.5.</summary>
        static readonly float[] HeavyFractions = { 0.25f, 0.50f, 0.75f, 1f };

        /// <summary>날아가는 속도(m/s). 기본 8.</summary>
        static readonly float[] KnockSpeeds = { 4f, 8f, 12f, 18f };

        /// <summary>
        /// 조립대에서 얼마나 떨어져 출발할지(m).
        ///
        /// 씬의 스폰 지점은 조립대에서 12m 라 "공세를 피하며 중앙까지" 를 재기에는 너무
        /// 짧다 — 걸어서 몇 초면 닿아 공세를 한 번 볼까 말까다.
        /// </summary>
        static readonly float[] StartDistances = { 20f, 40f, 80f, 120f };

        MeteorSpawner spawner;
        Vector3 baseGravity;
        bool baseGravityCaptured;
        bool savedInputSuppressed, savedCursorLocked, restorePending, keepCursorFree;
        GUIStyle titleStyle, sectionStyle, infoStyle, hintStyle, smallButton;
        Vector2 scroll;
        string lastAction = "";
        float lastActionAt = -10f;

        readonly List<SpaceshipPart> parts = new();
        readonly List<PartClamp> clampSnapshot = new();

        static GameSession Session => GameSession.Instance;
        static PlayerRig Player => Session != null ? Session.LocalPlayer : null;

        MeteorSpawner Spawner
        {
            get
            {
                if (spawner == null)
                    spawner = FindAnyObjectByType<MeteorSpawner>(FindObjectsInactive.Exclude);
                return spawner;
            }
        }

        void Start()
        {
            // SpaceBootstrap 이 Awake 에서 중력을 정한다. 그 뒤의 값이 "원래 중력" 이다.
            baseGravity = Physics.gravity;
            baseGravityCaptured = true;
        }

        void Update()
        {
            var session = Session;
            if (session == null || session.EndingActive)
            {
                if (IsOpen) Close();
                return;
            }

            if (Input.GetKeyDown(ToggleKey))
            {
                if (IsOpen) Close();
                else Open();
            }
            else if (IsOpen && Input.GetKeyDown(KeyCode.Escape))
            {
                // Esc 는 일시 메뉴도 연다. 둘이 겹쳐 뜨지 않게 이쪽은 닫되, 커서는 메뉴가
                // 정하도록 되돌리지 않는다 — 여기서 다시 잠그면 메뉴가 열린 채 커서가 사라진다.
                keepCursorFree = true;
                Close();
            }

            if (Input.GetKeyDown(QuickResetKey))
            {
                if (Input.GetKey(ReloadModifier) || Input.GetKey(KeyCode.RightShift)) ReloadScene();
                else QuickReset();
            }
            if (Input.GetKeyDown(DropMeteorKey)) DropMeteorAtAim(MeteorSize.Small);
            if (Input.GetKeyDown(WaveKey)) TriggerWave();
            if (Input.GetKeyDown(PresetDownKey)) StepPreset(-1);
            if (Input.GetKeyDown(PresetUpKey)) StepPreset(1);

            TickGodMode();
        }

        // Esc 로 닫은 프레임에 같은 입력이 게임 쪽에 다시 쓰이지 않게 늦게 복구한다(DevManager 와 같다).
        void LateUpdate()
        {
            if (restorePending) RestoreInput();
        }

        void OnDisable()
        {
            if (!IsOpen && !restorePending) return;
            IsOpen = false;
            RestoreInput();
        }

        // ── 열고 닫기 ────────────────────────────────────────────────────────

        public void Open()
        {
            var session = Session;
            if (IsOpen || session == null || restorePending) return;

            savedInputSuppressed = session.InputSuppressed;
            savedCursorLocked = session.CursorLocked;
            session.SetInputSuppressed(true);
            session.SetCursor(false);
            IsOpen = true;
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            restorePending = true;
        }

        void RestoreInput()
        {
            restorePending = false;
            var session = Session;
            if (session == null || session.EndingActive) return;
            session.SetInputSuppressed(savedInputSuppressed);
            if (!keepCursorFree) session.SetCursor(savedCursorLocked);
            keepCursorFree = false;
        }

        // ── 운석 ─────────────────────────────────────────────────────────────

        /// <summary>화면 한가운데가 가리키는 지면에 운석 하나를 떨어뜨린다.</summary>
        public void DropMeteorAtAim(MeteorSize size)
        {
            var target = Spawner;
            if (target == null) { Note("운석 스포너가 없다"); return; }
            var point = AimPoint();
            target.LaunchAt(point, size);
            Note($"{SizeName(size)} 운석 → {Format(point)}");
        }

        /// <summary>내 발밑 근처에 운석 하나를 떨어뜨린다. 피하기·방망이 시험용.</summary>
        public void DropMeteorNearPlayer(MeteorSize size)
        {
            var target = Spawner;
            var player = Player;
            if (target == null || player == null) { Note("스포너나 플레이어가 없다"); return; }

            // 바로 머리 위가 아니라 한 걸음 옆 — 예고 원이 보이고 피할 시간이 있어야 시험이 된다.
            var forward = Flat(player.FacingDirection);
            var point = GroundUnder(player.Position + forward * 2.5f);
            target.LaunchAt(point, size);
            Note($"{SizeName(size)} 운석 → 내 앞 {Format(point)}");
        }

        public void TriggerWave()
        {
            var target = Spawner;
            if (target == null) { Note("운석 스포너가 없다"); return; }
            if (target.IsPaused) target.SetPaused(false);
            target.TriggerWave();
            Note("공세를 바로 시작한다");
        }

        public void SkipToMinutes(int minutes)
        {
            var target = Spawner;
            if (target == null) return;
            if (target.ActivePreset != null) target.ClearPreset();
            target.SkipTo(minutes * 60f);

            // <b>강도는 안 바뀐다</b>(2026-09-18). 경과 시간이 공세 세기를 정하던 시절의
            // 버튼이고, 지금은 컷신 단계가 정한다(<see cref="MeteorIntensity"/>).
            // 흔적 수명처럼 시계를 쓰는 것이 남아 있어 지우지는 않았다.
            Note($"경과 {minutes}분으로 이동 — 공세 세기는 그대로다(컷신 단계가 정한다)");
        }

        /// <summary>
        /// 공세 컷신을 지난 상태를 흉내낸다. 시험장에는 <c>Stage1Sequence</c> 가 없어
        /// 비행선 트리거를 밟을 방법이 없다 — 그러면 <b>가랑비만 보고 끝난다.</b>
        /// </summary>
        public void ToggleFullBlast()
        {
            var target = Spawner;
            if (target == null) { Note("운석 스포너가 없다"); return; }
            if (target.ActivePreset != null) target.ClearPreset();

            target.SetAssault(!target.AssaultEnabled);
            var level = MeteorIntensity.For(target.AssaultEnabled);
            Note(target.AssaultEnabled
                ? $"컷신 이후 — {level.WaveIntervalSeconds:0}초마다 {level.Meteors}발 · 중형 {level.MediumChance:P0}"
                : $"컷신 이전 — {level.WaveIntervalSeconds:0}초마다 {level.Meteors}발");
        }

        /// <summary>공세 강도를 고정한다. 인원 보정은 4인으로 함께 고정된다.</summary>
        public void UsePreset(MeteorWavePreset preset)
        {
            var target = Spawner;
            if (target == null || preset == null) return;

            target.ApplyPreset(preset);

            // 반경은 스포너에서 읽는다. 프리셋의 배율에 <b>이 씬의</b> 크기별 반경을 곱한
            // 값이라야 화면에 뜨는 숫자와 실제 원이 같다 — 상수에서 만들면 씬이 기본값과
            // 다른 반경을 쓰는 순간 어긋난다.
            var small = target.ScaledImpactRadiusFor(MeteorSize.Small);
            var medium = target.ScaledImpactRadiusFor(MeteorSize.Medium);
            Note($"공세 [{preset.Name}] · {preset.WaveIntervalSeconds:0}초마다 "
                 + $"{preset.MinMeteors}~{preset.MaxMeteors}개 · 충격파 {small:0.#}/{medium:0.#}m "
                 + $"· 내 근처 {preset.NearPlayerWeight * 100f:0}%");
        }

        public void ClearPreset()
        {
            var target = Spawner;
            if (target == null) return;
            target.ClearPreset();
            Note("공세 강도 고정 해제 — 경과 시간대 표로 돌아간다");
        }

        /// <summary>강도를 한 단계 옮긴다. 고정이 없으면 제일 약한 것부터 시작한다.</summary>
        public void StepPreset(int direction)
        {
            var target = Spawner;
            if (target == null) return;

            var all = MeteorWavePreset.All;
            var index = System.Array.IndexOf(all, target.ActivePreset);
            index = index < 0 ? (direction > 0 ? 0 : all.Length - 1)
                              : Mathf.Clamp(index + direction, 0, all.Length - 1);

            UsePreset(all[index]);
        }

        // ── 플레이어 ─────────────────────────────────────────────────────────

        public void ToggleGodMode()
        {
            GodMode = !GodMode;
            Note(GodMode ? "무적 켬" : "무적 끔");
        }

        void TickGodMode()
        {
            if (!GodMode) return;
            var player = Player;
            if (player == null) return;
            if (player.Downed) player.Revive(player.MaxHealth);
            if (player.Health < player.MaxHealth) player.ResetHealth();
        }

        public void Heal()
        {
            var player = Player;
            if (player == null) return;
            if (player.Downed) player.Revive(player.MaxHealth);
            player.ResetHealth();
            Note("체력 회복");
        }

        public void KnockOut()
        {
            var player = Player;
            if (player == null) return;
            if (GodMode) { Note("무적이 켜져 있어 기절할 수 없다"); return; }
            player.GoDown("치트");
            Note("기절");
        }

        public void TeleportToSpawn()
        {
            var session = Session;
            if (session == null) return;
            TeleportTo(session.SpawnFoot, session.SpawnFoot + Vector3.right);
            Note("스폰 지점으로");
        }

        public void TeleportToAssembly()
        {
            var assembly = FindAnyObjectByType<ShipAssembly>(FindObjectsInactive.Exclude);
            if (assembly == null) { Note("조립대가 없다"); return; }
            TeleportNear(assembly.LaunchPad.position, "조립대");
        }

        /// <summary>
        /// 조립대에서 <paramref name="distance"/> 만큼 떨어진 자리에 서서 조립대를 본다.
        /// "공세를 피하며 중앙까지" 를 재는 출발선이다.
        /// </summary>
        public void TeleportToStart(float distance)
        {
            var player = Player;
            if (player == null) return;

            var center = AssemblyCenter();
            var foot = GroundUnder(center + Vector3.left * distance);
            TeleportTo(foot, center);
            Note($"출발선 {distance:0}m — 조립대까지 걸어가 본다");
        }

        Vector3 AssemblyCenter()
        {
            var assembly = FindAnyObjectByType<ShipAssembly>(FindObjectsInactive.Exclude);
            return assembly != null ? assembly.LaunchPad.position : Vector3.zero;
        }

        public void TeleportToPart(SpaceshipPart part)
        {
            if (part == null) return;
            TeleportNear(part.transform.position, part.name);
        }

        void TeleportNear(Vector3 target, string label)
        {
            var player = Player;
            if (player == null) return;

            // 대상에서 지금 내가 있는 쪽으로 한 걸음 떨어진 자리에 서서 대상을 본다.
            var away = Flat(player.Position - target);
            if (away.sqrMagnitude < 0.01f) away = Vector3.left;
            var foot = GroundUnder(target + away * TeleportDistance);
            TeleportTo(foot, target);
            Note($"{label} 앞으로");
        }

        void TeleportTo(Vector3 foot, Vector3 lookAt)
        {
            var player = Player;
            if (player == null) return;
            var dir = Flat(lookAt - foot);
            if (dir.sqrMagnitude < 0.01f) dir = Vector3.right;
            player.Teleport(foot + Vector3.up * 0.15f, Quaternion.LookRotation(dir, Vector3.up));
        }

        // ── 파츠 ─────────────────────────────────────────────────────────────

        /// <summary>파츠를 내 앞에 모으고 고정못을 푼다. 로비의 시험 진입과 같은 배치다.</summary>
        public void ArrangeParts()
        {
            var player = Player;
            if (player == null) return;
            TestArena.Arrange(player);
            Note("파츠를 내 앞에 모았다 (고정못 해제)");
        }

        public void ReleaseAllClamps()
        {
            RefreshParts();
            var released = 0;
            for (var i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                if (part == null) continue;
                clampSnapshot.Clear();
                clampSnapshot.AddRange(part.Clamps);
                var had = false;
                for (var j = 0; j < clampSnapshot.Count; j++)
                    if (clampSnapshot[j] != null && !clampSnapshot[j].IsReleased) had = true;
                if (!had) continue;
                TestArena.ReleaseClamps(part, i);
                released++;
            }
            Note($"고정못 해제 — 파츠 {released}기");
        }

        public void CompleteShip()
        {
            var ending = FindAnyObjectByType<DemoEnding>(FindObjectsInactive.Exclude);
            if (ending == null) { Note("엔딩 진행기가 없다"); return; }
            Note(ending.DebugAssembleAndComplete()
                ? "우주선을 조립하고 엔딩으로 간다"
                : "완성 불가 — 파츠 4기와 상태를 확인");
        }

        void RefreshParts()
        {
            parts.Clear();
            parts.AddRange(FindObjectsByType<SpaceshipPart>(FindObjectsInactive.Exclude));
            parts.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        }

        // ── 세계 · 리셋 ──────────────────────────────────────────────────────

        public void SetGravityScale(float scale)
        {
            if (!baseGravityCaptured) { baseGravity = Physics.gravity; baseGravityCaptured = true; }
            Physics.gravity = baseGravity * scale;
            Note($"중력 ×{scale:0.##} ({Physics.gravity.y:0.##} m/s²)");
        }

        public void SetTimeScale(float scale)
        {
            Time.timeScale = scale;
            Note($"시간 ×{scale:0.##}");
        }

        /// <summary>
        /// 플레이어·운석·세계 값만 처음으로 돌린다. 파츠는 그대로 둔다 — 파츠까지 되돌리려면
        /// <see cref="ReloadScene"/>.
        /// </summary>
        public void QuickReset()
        {
            Time.timeScale = 1f;
            if (baseGravityCaptured) Physics.gravity = baseGravity;

            var target = Spawner;
            if (target != null)
            {
                target.SetPaused(false);
                target.ResetRun();
            }

            var session = Session;
            if (session != null) session.Respawn();
            Note("빠른 리셋 — 플레이어·운석·중력·시간");
        }

        /// <summary>씬을 통째로 다시 불러온다. 파츠·고정못·용접까지 전부 처음으로.</summary>
        public void ReloadScene()
        {
            Time.timeScale = 1f;
            var scene = gameObject.scene;

            if (ReloadHook != null && ReloadHook(scene.path)) return;

            if (scene.buildIndex >= 0)
            {
                SceneManager.LoadScene(scene.buildIndex);
                return;
            }

            Note("빌드 목록에 없는 씬은 에디터에서만 다시 불러올 수 있다");
            Debug.LogWarning("[CheatArena] 씬 재로드 실패 — 빌드 목록에 없고 에디터 훅도 없다: " + scene.path);
        }

        // ── 조준 · 좌표 도우미 ───────────────────────────────────────────────

        Vector3 AimPoint()
        {
            var player = Player;
            var cam = player != null && player.Cam != null ? player.Cam.Cam : Camera.main;

            if (cam != null)
            {
                var ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
                if (UnityEngine.Physics.Raycast(ray, out var hit, 400f, GameLayers.SurfaceMask,
                                                QueryTriggerInteraction.Ignore))
                    return hit.point;
            }

            var origin = player != null ? player.Position : transform.position;
            var forward = cam != null ? Flat(cam.transform.forward) : Vector3.right;
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.right;
            return GroundUnder(origin + forward * AimFallbackDistance);
        }

        static Vector3 GroundUnder(Vector3 point)
        {
            if (UnityEngine.Physics.Raycast(point + Vector3.up * ProbeHeight, Vector3.down, out var hit,
                                            ProbeHeight * 2f, GameLayers.SurfaceMask,
                                            QueryTriggerInteraction.Ignore))
                return hit.point;
            point.y = 0f;
            return point;
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.zero;
        }

        static string Format(Vector3 p) => $"({p.x:0.#}, {p.y:0.#}, {p.z:0.#})";
        static string SizeName(MeteorSize size) => size == MeteorSize.Medium ? "중형" : "소형";

        void Note(string text)
        {
            lastAction = text;
            lastActionAt = Time.unscaledTime;
            Debug.Log("[CheatArena] " + text);
        }

        // ── 그리기 ───────────────────────────────────────────────────────────

        void OnGUI()
        {
            var session = Session;
            if (session == null || session.LocalPlayer == null || session.EndingActive) return;

            EnsureStyles();

            if (!IsOpen)
            {
                if (!session.InputSuppressed) DrawHint();
                return;
            }

            var rect = new Rect(12f, 12f, PanelWidth, Mathf.Min(Screen.height - 24f, 720f));
            GUI.Window(WindowId, rect, DrawWindow, $"치트 시험장   [{ToggleKey}] 닫기");
        }

        void DrawHint()
        {
            var text = $"[{ToggleKey}] 치트 패널   [{QuickResetKey}] 빠른 리셋   [{DropMeteorKey}] 조준점 운석   "
                       + $"[{WaveKey}] 공세 시작   [{PresetDownKey}]/[{PresetUpKey}] 공세 강도   [Shift+{QuickResetKey}] 씬 다시";

            // 달리면서 볼 상태 한 줄. 이것이 없으면 공세 강도를 바꾼 뒤 무엇을 재고 있는지
            // 확인하려고 매번 패널을 열어야 한다.
            var status = StatusLine();

            var size = hintStyle.CalcSize(new GUIContent(text));
            var statusSize = hintStyle.CalcSize(new GUIContent(status));
            var width = Mathf.Max(size.x, statusSize.x) + 16f;
            var rect = new Rect(12f, 12f, width, size.y + statusSize.y + 12f);

            var saved = GUI.color;
            GUI.color = new Color(0.04f, 0.05f, 0.1f, 0.6f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = saved;

            GUI.Label(new Rect(rect.x + 8f, rect.y + 4f, size.x, size.y), text, hintStyle);
            GUI.Label(new Rect(rect.x + 8f, rect.y + 4f + size.y, statusSize.x, statusSize.y),
                      status, infoStyle);

            if (Time.unscaledTime - lastActionAt < 2.5f && !string.IsNullOrEmpty(lastAction))
                GUI.Label(new Rect(rect.x + 8f, rect.yMax + 4f, 700f, 24f), lastAction, infoStyle);
        }

        string StatusLine()
        {
            var target = Spawner;
            if (target == null) return "운석 스포너 없음";

            var preset = target.ActivePreset;
            var name = preset != null ? preset.Name : $"시간대 {target.Elapsed / 60f:0.0}분";
            var wave = target.WaveActive
                ? $"공세 중 (남은 {target.WaveRemaining})"
                : $"다음 공세 {target.SecondsUntilWave:0.0}초";

            return $"공세 [{name}] · {wave} · 낙하 중 {target.Live.Count} · "
                   + $"충격파 소형 {target.ScaledImpactRadiusFor(MeteorSize.Small):0.#}m";
        }

        void DrawWindow(int id)
        {
            scroll = GUILayout.BeginScrollView(scroll);

            DrawPresetSection();
            DrawShockwaveSection();
            DrawMeteorSection();
            DrawPlayerSection();
            DrawPartsSection();
            DrawWorldSection();
            DrawResetSection();

            GUILayout.Space(6f);
            if (!string.IsNullOrEmpty(lastAction))
                GUILayout.Label("▸ " + lastAction, infoStyle);

            GUILayout.EndScrollView();
        }

        /// <summary>공세 강도 고르기. 이 시험장에서 제일 자주 쓰는 것이라 맨 위에 둔다.</summary>
        void DrawPresetSection()
        {
            GUILayout.Label("공세 강도", sectionStyle);
            var target = Spawner;
            if (target == null)
            {
                GUILayout.Label("이 씬에 MeteorSpawner 가 없다.", infoStyle);
                return;
            }

            var active = target.ActivePreset;
            GUILayout.Label(active != null ? $"[{active.Name}]  {active.Summary}"
                                           : "고정 없음 — 경과 시간대 표를 따른다", infoStyle);

            GUILayout.BeginHorizontal();
            foreach (var preset in MeteorWavePreset.All)
            {
                var chosen = active == preset;
                if (GUILayout.Button(chosen ? "▶ " + preset.Name : preset.Name, smallButton))
                    UsePreset(preset);
            }
            if (GUILayout.Button("해제", smallButton)) ClearPreset();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"내 근처 {target.NearPlayerWeight * 100f:0}%", infoStyle, GUILayout.Width(88f));
            for (var i = 0; i < NearPlayerWeights.Length; i++)
                if (GUILayout.Button($"{NearPlayerWeights[i] * 100f:0}%", smallButton))
                {
                    target.OverrideNearPlayerWeight(NearPlayerWeights[i]);
                    Note($"목표의 {NearPlayerWeights[i] * 100f:0}% 를 내 주변으로");
                }
            if (GUILayout.Button("기본", smallButton))
            {
                target.OverrideNearPlayerWeight(-1f);
                Note("목표 비율을 씬 기본값으로");
            }
            GUILayout.EndHorizontal();
        }

        /// <summary>충격파 세기. 반경은 강도가 정하고, 여기서는 맞았을 때의 결과를 맞춘다.</summary>
        void DrawShockwaveSection()
        {
            var target = Spawner;
            if (target == null) return;

            GUILayout.Space(6f);
            GUILayout.Label("충격파", sectionStyle);

            var small = target.ScaledImpactRadiusFor(MeteorSize.Small);
            var medium = target.ScaledImpactRadiusFor(MeteorSize.Medium);
            var tuning = target.Tuning;
            GUILayout.Label(
                $"반경 ×{target.ImpactRadiusScale:0.##} → 소형 {small:0.#}m · 중형 {medium:0.#}m"
                + $"  ·  방망이 사거리 {PhysicsScale.BatAssistRadius * target.ImpactRadiusScale:0.#}m", infoStyle);
            GUILayout.Label(
                $"안쪽 {small * tuning.HeavyImpactFraction:0.#}m 는 크게 날아가고 장비를 떨어뜨린다. "
                + "바깥은 밀리기만 한다. 운석 본체에 맞으면 기절한다.", infoStyle);

            GUILayout.BeginHorizontal();
            GUILayout.Label($"강함 경계 {tuning.HeavyImpactFraction:0.##}", infoStyle, GUILayout.Width(88f));
            for (var i = 0; i < HeavyFractions.Length; i++)
                if (GUILayout.Button($"{HeavyFractions[i]:0.##}", smallButton))
                {
                    tuning.SetHeavyImpactFraction(HeavyFractions[i]);
                    Note($"강한 충격 경계 {HeavyFractions[i]:0.##} (반경의 안쪽 비율)");
                }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"날아가는 힘 {tuning.HeavyKnockSpeed:0.#}", infoStyle, GUILayout.Width(88f));
            for (var i = 0; i < KnockSpeeds.Length; i++)
                if (GUILayout.Button($"{KnockSpeeds[i]:0}", smallButton))
                {
                    tuning.SetHeavyKnockSpeed(KnockSpeeds[i]);
                    Note($"날아가는 속도 {KnockSpeeds[i]:0} m/s");
                }
            GUILayout.EndHorizontal();
        }

        void DrawMeteorSection()
        {
            GUILayout.Space(6f);
            GUILayout.Label("운석", sectionStyle);
            var target = Spawner;
            if (target == null) return;

            GUILayout.Label(
                $"경과 {target.Elapsed / 60f:0.0}분 · 간격 {target.CurrentInterval:0}초 · "
                + $"다음 공세 {target.SecondsUntilWave:0}초 후"
                + (target.WaveActive ? $" · 공세 중 (남은 {target.WaveRemaining})" : "")
                + (target.IsPaused ? " · 일시정지" : ""), infoStyle);
            GUILayout.Label(
                $"예고 {target.Pending.Count} · 낙하 중 {target.Live.Count} · 흔적 {target.Scars.Count} · "
                + $"중형 확률 {target.MediumChanceAt(target.Elapsed) * 100f:0}% · 인원 {target.Crew}"
                + (target.CrewOverride > 0 ? " (강제)" : ""), infoStyle);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button($"조준점에 소형 [{DropMeteorKey}]")) DropMeteorAtAim(MeteorSize.Small);
            if (GUILayout.Button("조준점에 중형")) DropMeteorAtAim(MeteorSize.Medium);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("내 앞에 소형")) DropMeteorNearPlayer(MeteorSize.Small);
            if (GUILayout.Button("내 앞에 중형")) DropMeteorNearPlayer(MeteorSize.Medium);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(target.AssaultEnabled ? "컷신 이후 ▣ → 이전으로" : "컷신 이전 □ → 이후로"))
                ToggleFullBlast();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button($"공세 바로 시작 [{WaveKey}]")) TriggerWave();
            if (GUILayout.Button(target.IsPaused ? "공세 재개" : "공세 일시정지"))
            {
                target.SetPaused(!target.IsPaused);
                Note(target.IsPaused ? "공세 일시정지 (떨어지는 것은 그대로)" : "공세 재개");
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("경과 시간", infoStyle, GUILayout.Width(64f));
            for (var i = 0; i < SkipMinutes.Length; i++)
                if (GUILayout.Button($"{SkipMinutes[i]}분", smallButton)) SkipToMinutes(SkipMinutes[i]);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("인원 보정", infoStyle, GUILayout.Width(64f));
            if (GUILayout.Button("자동", smallButton)) { target.OverrideCrew(0); Note("인원 보정 자동"); }
            for (var crew = 1; crew <= 4; crew++)
                if (GUILayout.Button($"{crew}인", smallButton)) { target.OverrideCrew(crew); Note($"인원 {crew}명으로 강제"); }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("흔적만 지우기")) { target.ClearScars(); Note("흔적을 지웠다"); }
            if (GUILayout.Button("운석·스케줄 초기화")) { target.ResetRun(); Note("운석 스포너 초기화 (경과 0분)"); }
            GUILayout.EndHorizontal();
        }

        void DrawPlayerSection()
        {
            GUILayout.Space(6f);
            GUILayout.Label("플레이어", sectionStyle);
            var player = Player;
            if (player == null) return;

            GUILayout.Label(
                $"체력 {player.Health:0}/{player.MaxHealth:0}"
                + (player.Downed ? " · 기절" : "") + (GodMode ? " · 무적" : "")
                + $" · 위치 {Format(player.Position)}", infoStyle);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(GodMode ? "무적 끄기" : "무적 켜기")) ToggleGodMode();
            if (GUILayout.Button("체력 회복")) Heal();
            if (GUILayout.Button("기절시키기")) KnockOut();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("스폰으로")) TeleportToSpawn();
            if (GUILayout.Button("조립대로")) TeleportToAssembly();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("출발선", infoStyle, GUILayout.Width(52f));
            for (var i = 0; i < StartDistances.Length; i++)
                if (GUILayout.Button($"{StartDistances[i]:0}m", smallButton))
                    TeleportToStart(StartDistances[i]);
            GUILayout.EndHorizontal();
            GUILayout.Label("조립대에서 그만큼 떨어진 자리에 서서 조립대를 봅니다. "
                            + "걸어가며 공세를 피할 수 있는지 재는 출발선입니다.", infoStyle);
        }

        void DrawPartsSection()
        {
            GUILayout.Space(6f);
            GUILayout.Label("파츠", sectionStyle);
            RefreshParts();

            for (var i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                if (part == null) continue;
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{part.name}  {part.State}  고정못 {part.RemainingRequiredClamps}",
                                infoStyle);
                if (GUILayout.Button("앞으로", smallButton, GUILayout.Width(64f))) TeleportToPart(part);
                GUILayout.EndHorizontal();
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("파츠 내 앞에 모으기")) ArrangeParts();
            if (GUILayout.Button("고정못 전부 풀기")) ReleaseAllClamps();
            GUILayout.EndHorizontal();
            if (GUILayout.Button("우주선 즉시 완성 → 엔딩")) CompleteShip();
        }

        void DrawWorldSection()
        {
            GUILayout.Space(6f);
            GUILayout.Label("세계", sectionStyle);

            GUILayout.BeginHorizontal();
            GUILayout.Label($"중력 {Physics.gravity.y:0.##}", infoStyle, GUILayout.Width(80f));
            for (var i = 0; i < GravityScales.Length; i++)
                if (GUILayout.Button($"×{GravityScales[i]:0.##}", smallButton)) SetGravityScale(GravityScales[i]);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"시간 ×{Time.timeScale:0.##}", infoStyle, GUILayout.Width(80f));
            for (var i = 0; i < TimeScales.Length; i++)
                if (GUILayout.Button($"×{TimeScales[i]:0.##}", smallButton)) SetTimeScale(TimeScales[i]);
            GUILayout.EndHorizontal();
        }

        void DrawResetSection()
        {
            GUILayout.Space(6f);
            GUILayout.Label("리셋", sectionStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button($"빠른 리셋 [{QuickResetKey}]")) QuickReset();
            if (GUILayout.Button($"씬 다시 불러오기 [Shift+{QuickResetKey}]")) ReloadScene();
            GUILayout.EndHorizontal();
            GUILayout.Label("빠른 리셋: 플레이어·운석·중력·시간.  씬 다시: 파츠·고정못·용접까지 전부.", infoStyle);
        }

        void EnsureStyles()
        {
            if (titleStyle != null) return;
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold };
            sectionStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14, fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.82f, 1f, 0.94f) },
            };
            infoStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
            hintStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12, normal = { textColor = new Color(0.82f, 1f, 0.94f) },
            };
            smallButton = new GUIStyle(GUI.skin.button) { fontSize = 12, padding = new RectOffset(6, 6, 3, 3) };
        }
#else
        void Awake() => enabled = false;
#endif
    }
}
