using System;
using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Metagame.Gameplay;
using Mirror;
using UnityEngine;
using UnityEngine.Networking;

namespace Metagame.Network
{
    /// <summary>현재 PC가 소유한 추가팩만 실행한다. 모델 다운로드, 공개 서버 접속, 임의 PID 종료는 없다.</summary>
    public sealed class AiLocalRuntime : MonoBehaviour, IAiRuntimeStatus
    {
        [Serializable] public sealed class Health { public string service; public string protocol; public string state; public string command_schema; public bool voice_plan; }
        [Serializable] public sealed class Decision { public string action; public string recipients; public bool approach; public bool safety_override; public string text; public float elapsed_ms; }
        // `text` 는 워커가 이미 보내고 있었다 — 여기서 선언하지 않아 JsonUtility 가 버렸다
        // (S15P21B202-708). 선언만 더하면 워커도 추가팩도 건드리지 않고 전사를 얻는다.
        // `asr_ms`·`llm_ms` 는 **워커가 이미 나눠 재던 값**이다 (S15P21B202-734). 여기서
        // 선언하지 않으면 JsonUtility 가 버리므로, 19.7초가 받아쓰기 탓인지 판단 탓인지
        // 가릴 수 없다. `unsaid_social` 은 막은 감정 표현 이름이다(-736).
        [Serializable] public sealed class PlanDecision { public string status; public AiCommandAssignment[] orders; public bool safety_override; public float elapsed_ms; public float asr_ms; public float llm_ms; public float llm_load_ms; public int llm_tokens; public string unsaid_social; public string text; }
        [Serializable] sealed class TextRequest { public string text; }
        public static AiLocalRuntime Current { get; private set; }
        public AiRuntimeState State { get; private set; } = AiRuntimeState.Missing;
        readonly AiStatusLog statusLog = new AiStatusLog();
        string message = "추가팩 대기";

        /// <summary>
        /// 지금 무슨 일인지. <b>화면과 로그가 같은 값을 본다</b> (S15P21B202-708).
        ///
        /// <para>판단 지점마다 이미 여기에 적고 있었는데 화면에서만 쓰여서, 다음 문구로 덮이면
        /// 사라졌다 — QA 뒤 <c>Player.log</c> 에 <b>AI 줄이 하나도 없었다.</b> 기록할 자리를
        /// 따로 만들지 않는다: 만들면 다음에 생기는 판단 지점이 조용히 빠진다.</para>
        /// </summary>
        public string Message
        {
            get => message;
            private set
            {
                if (message == value) return;
                message = value;
                statusLog.Write("[AI]", value);
            }
        }
        public bool Busy { get; private set; }
        public string PackRoot { get; private set; }
        public AiPackInstaller Installer { get; private set; }
        public const string Address = "http://127.0.0.1:18765";
        Process worker;
        string token;
        int generation;
        bool attempted;
        UnityWebRequest pendingHealth, pendingText;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            if (Application.isBatchMode || Current != null) return;
            new GameObject("Local AI runtime").AddComponent<AiLocalRuntime>();
#endif
        }

        void Awake()
        {
            if (Current != null && Current != this) { Destroy(this); return; }
            Current = this;
            DontDestroyOnLoad(gameObject);
            PackRoot = DefaultPackRoot();
            Installer = gameObject.AddComponent<AiPackInstaller>();
            AiHostSettings.BindRuntime(this);
        }

        public static string DefaultPackRoot()
        {
#if UNITY_EDITOR
            string localPack = Path.GetFullPath(Path.Combine(Application.dataPath, "../../artifacts/ai-pack/p3"));
            if (HasPack(localPack)) return localPack;
#endif
            // Fresh clones can reuse the installed optional pack; models are not Git assets.
            return Path.Combine(Application.persistentDataPath, "AI/p3");
        }

        /// <summary>
        /// 자식 프로세스에게 홈을 알리는 환경 변수 이름. <b>상수로 두는 이유가 있다</b> —
        /// 문자열로 두면 배선을 지키는 검사가 <c>TestSource.Blank</c> 에 가려 <b>헛돈다.</b>
        /// 이름은 글자로 고정하고, 값은 값으로 확인한다.
        /// </summary>
        public const string HomeVariableWindows = "USERPROFILE", HomeVariableUnix = "HOME";

        /// <summary>추가팩 안의 <b>쓰기 가능한 홈</b>. ollama 의 키 파일이 여기 들어간다.</summary>
        public static string PackHome(string root)
            => string.IsNullOrEmpty(root) ? null : Path.Combine(root, "home");

        /// <summary>
        /// 워커(와 그 자식 ollama)의 <b>홈을 추가팩 안으로</b> 돌린다 (S15P21B202-707).
        ///
        /// <para><b>무엇이 실패했나.</b> <c>ollama serve</c> 는 처음 뜰 때 홈에 키 파일을
        /// 만든다 — <c>%USERPROFILE%\.ollama\id_ed25519</c>. 그 경로가 쓰기 불가하면(정책으로
        /// 잠긴 프로파일·로밍 프로파일) ollama 가 뜨지 못하고, 워커는 <c>ready</c> 로 올라오지
        /// 못한 채 계속 데우는 상태로 남는다. 2026-09-22 에 실제로 났다.</para>
        ///
        /// <para><b>왜 여기서 고치나.</b> 워커는 <c>os.environ.copy()</c> 로 환경을 물려받아
        /// ollama 에 넘긴다. 그래서 <b>띄우는 쪽이 정해 주면 그대로 내려간다</b> — 워커를 고쳐
        /// 2.34GB 추가팩을 다시 구워 올리지 않아도 된다.</para>
        ///
        /// <para><b>왜 추가팩 안인가.</b> 추가팩은 자기 폴더 안에서 완결되어야 한다. 이 한
        /// 가지만 밖(사용자 프로파일)을 쓰고 있었다 — 그래서 <b>추가팩을 지워도 흔적이
        /// 남았고</b>, 홈이 잠긴 PC 에서는 아예 못 떴다.</para>
        ///
        /// <para>홈을 못 만들면 <b>덮어쓰지 않고 그대로 띄운다.</b> 예전처럼 동작할 뿐이고,
        /// 여기서 멈추면 홈이 멀쩡한 PC 까지 AI 를 못 쓰게 된다.</para>
        /// </summary>
        void PointHomeIntoThePack(ProcessStartInfo start)
        {
            string home = PackHome(PackRoot);
            if (string.IsNullOrEmpty(home)) return;
            try { Directory.CreateDirectory(home); }
            catch (Exception) { return; }
            start.Environment[HomeVariableWindows] = home;
            start.Environment[HomeVariableUnix] = home;
        }

        public static bool HasPack(string root) => !string.IsNullOrEmpty(root)
            && File.Exists(Path.Combine(root, "runtime/ntp-ai-worker/ntp-ai-worker.exe"))
            && HasAsrModel(Path.Combine(root, "models"))
            && File.Exists(Path.Combine(root, "models/ollama/manifests/registry.ollama.ai/library/qwen3/1.7b"));

        /// <summary>
        /// 받아쓰기 모델이 하나라도 있는가 — <b>어느 모델인지는 묻지 않는다</b> (S15P21B202-726).
        ///
        /// <para>전에는 <c>models/whisper-small-ko-zeroth/model.bin</c> 을 글자 그대로 찾았다.
        /// 그래서 모델을 바꾸자 <b>설치를 마친 팩이 "없는 팩" 이 됐다</b> — 방장은 1.9GB 를
        /// 받고 "설치 파일 누락" 을 보고, 버튼은 또 받으라고 한다.</para>
        ///
        /// <para>게임이 알아야 하는 것은 <b>"팩이 깔렸는가"</b> 뿐이다. 어느 모델을 쓰는지는
        /// 워커가 정하고, 제 모델을 못 찾으면 워커가 <c>failed</c> 로 사유를 말한다. 그것을
        /// 여기서 한 번 더, 다른 글자로 적으면 <b>한 문제를 두 곳이 다르게 푸는 것</b>이다.</para>
        ///
        /// <para><c>models/</c> 바로 아래만 본다. <c>ollama</c> 폴더의 blob 까지 뒤지지 않으므로
        /// 이 검사는 프레임마다 돌아도 예전과 같은 값이다 (<c>AiRoomControls.NeedsPreparing</c>).</para>
        /// </summary>
        static bool HasAsrModel(string models)
        {
            try
            {
                foreach (string folder in Directory.EnumerateDirectories(models))
                    if (File.Exists(Path.Combine(folder, "model.bin"))) return true;
            }
            catch (Exception) { }
            return false;
        }

        public static bool ValidHealth(string json)
        {
            try
            {
                var h = JsonUtility.FromJson<Health>(json);
                return h != null && h.service == "ntp-ai-worker" && h.protocol == "companion-v4" && h.state == "ready" && h.command_schema == "work-v1" && h.voice_plan;
            }
            catch (ArgumentException) { return false; }
        }

        static bool HostWantsRuntime => AiHostSettings.Requested && !AiHostSettings.IdleTest
            && (NetworkServer.active || AiHostSettings.CanConfigure);

        void Update()
        {
            if (Installer != null && Installer.Busy) return;
            if (!HostWantsRuntime)
            {
                if (attempted || worker != null) StopRuntime();
                return;
            }
            if (!attempted) StartRuntime();
        }

        public void Retry()
        {
            if (!AiHostSettings.CanConfigure) return;
            StopRuntime();
        }
        public void PreparePackInstall() { StopRuntime(); }

        void StartRuntime()
        {
            attempted = true;
            if (!HasPack(PackRoot)) { State = AiRuntimeState.Missing; Message = "추가팩 없음: " + PackRoot; return; }
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            try
            {
                // 점유 포트는 건드리지 않고 실패한다. 토큰은 명령행·파일·로그에 남기지 않는다.
                using (var port = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork,
                    System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp))
                    port.Bind(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 18765));
                byte[] bytes = new byte[32];
                using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
                token = BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
                using (var parent = Process.GetCurrentProcess())
                {
                    double birth = (parent.StartTime.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
                    var start = new ProcessStartInfo
                    {
                        FileName = Path.Combine(PackRoot, "runtime/ntp-ai-worker/ntp-ai-worker.exe"),
                        Arguments = "--model-root \"" + Path.Combine(PackRoot, "models") + "\" --parent-pid " + parent.Id
                            + " --parent-started " + birth.ToString("R", CultureInfo.InvariantCulture),
                        UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true
                    };
                    PointHomeIntoThePack(start);
                    worker = new Process { StartInfo = start };
                    worker.Start();
                    worker.StandardInput.WriteLine(token);
                    worker.StandardInput.Close();
                }
                State = AiRuntimeState.Loading;
                Message = "추가팩 로딩 중";
                StartCoroutine(Watch(++generation));
            }
            catch (Exception e) { Fail("추가팩 실행 실패 (" + e.GetType().Name + ")"); }
#else
            State = AiRuntimeState.Failed; Message = "Windows 추가팩만 지원합니다";
#endif
        }

        /// <summary>
        /// <b>대답이 없을 때</b>까지만 기다리는 시간 (S15P21B202-706).
        ///
        /// <para>워커 프로세스는 떠 있는데 HTTP 로 아무 대답도 없는 상태다 — 포트를 못 열었거나
        /// 시작하다 멈춘 것이다. 이 경우는 기다려도 나아지지 않는다.</para>
        /// </summary>
        public const float SilenceLimit = 120f;

        /// <summary>
        /// <b>데우는 중이어도</b> 여기까지 (S15P21B202-706).
        ///
        /// <para><b>2026-09-22 실측 3분 47초(227초).</b> 예전 마감은 120초 하나뿐이어서, 워커가
        /// 살아서 대답하며 모델을 데우고 있는데도 <b>절반쯤에서 잘렸다.</b> QA 에서 "추가팩 설치
        /// 완료인데 AI 가 준비되지 않는다" 로 보인 것이 이것이다.</para>
        ///
        /// <para>그렇다고 무한정 기다리면 <b>영영 안 되는 것과 오래 걸리는 것이 같아진다.</b>
        /// 실측의 네 배쯤에서 끊는다.</para>
        /// </summary>
        public const float WarmupLimit = 900f;

        /// <summary>사람이 읽는 경과 시간. <c>"1분 12초"</c> · <c>"42초"</c>.</summary>
        public static string Clock(float seconds)
        {
            int total = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return total < 60 ? total + "초" : total / 60 + "분 " + total % 60 + "초";
        }

        /// <summary>
        /// 준비 중 <b>로그</b>에 적을 말 (S15P21B202-706).
        ///
        /// <para><b>멈춘 것과 느린 것은 다르다.</b> 예전에는 <c>"추가팩 로딩 중"</c> 이 최대 2분
        /// 동안 한 글자도 바뀌지 않아 둘을 구분할 수 없었다. <b>글자가 바뀌면 살아 있는 것</b>이고,
        /// 대답이 오는지 아닌지가 그다음으로 알아야 할 것이다.</para>
        ///
        /// <para><b>화면 글자가 아니다</b> (S15P21B202-713). 화면은 경과가 아니라 <b>남은 시간</b>을
        /// 말해야 하고 "모델 데우는 중" · "첫 실행은 몇 분" 같은 속사정을 말하지 않는다 —
        /// <see cref="AiPrepProgress.Display"/> 가 맡는다. 여기는 경과와 이 PC 의 예상을 그대로 적는다.</para>
        ///
        /// <para>값으로 검증할 수 있게 떼어 둔다 — 실제 워커 없이 확인할 수 있어야 한다.</para>
        /// </summary>
        public static string WarmupMessage(bool answered, float waited, float expected)
            => answered
                ? "AI 준비 중 · 모델 데우는 중 " + Clock(waited) + " · 이 PC 예상 " + Clock(expected)
                : "AI 준비 중 · 추가팩 응답 대기 " + Clock(waited);

        /// <summary>
        /// 이 워커가 데우기를 시작한 시각(<c>realtimeSinceStartup</c>). <see cref="State"/> 가
        /// <see cref="AiRuntimeState.Loading"/> 일 때만 뜻이 있고, Ready 에 닿으면 음수로 돌아간다.
        /// </summary>
        public float WarmupBegan { get; private set; } = -1f;

        /// <summary>이번 데우기에 걸릴 것으로 보는 초 — 이 PC 의 기록, 없으면 <see cref="AiPrepProgress.FallbackWarmupSeconds"/>.</summary>
        public float WarmupExpected { get; private set; } = AiPrepProgress.FallbackWarmupSeconds;

        Process warmupWorker;
        string warmupPack;
        bool warmupFirst;

        /// <summary>
        /// 이 PC 에서 이 팩이 데워지는 데 걸릴 것으로 보는 초 (S15P21B202-713).
        ///
        /// <para>워커는 데우는 동안 단계를 알려 주지 않는다 — <c>loading|ready|failed</c> 뿐이다.
        /// 그래서 남은 시간은 <b>이 PC 가 지난번에 걸린 시간</b>으로만 어림할 수 있다. 측정 둘이
        /// 열 배 차이(20초 대 227초)라 고정값 하나로는 몇 분씩 틀린다.</para>
        /// </summary>
        public static float ExpectedWarmup(string packId, bool firstRun)
            => AiPrepProgress.ExpectedWarmup(firstRun,
                PlayerPrefs.GetFloat(AiPrepProgress.WarmupKey(packId, true), 0f),
                PlayerPrefs.GetFloat(AiPrepProgress.WarmupKey(packId, false), 0f));

        /// <summary>
        /// 설치가 막 끝났다 — 이 팩의 다음 데우기는 <b>설치 뒤 첫 실행</b>이다 (S15P21B202-713).
        /// 첫 실행이 Ready 에 닿으면 지운다.
        /// </summary>
        public static void MarkInstalled(string packId)
        {
            PlayerPrefs.SetInt(AiPrepProgress.InstalledKey(packId), 1);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// 데우기 시계를 켠다. <b>같은 워커면 다시 켜지 않는다</b> — 명령을 끊느라
        /// <c>Watch</c> 가 다시 돌아도 데우기는 처음부터가 아니다. 이미 Ready 면 잴 것이 없다.
        /// </summary>
        void StartWarmupClock(float began)
        {
            if (State == AiRuntimeState.Ready || (warmupWorker == worker && WarmupBegan >= 0f)) return;
            warmupWorker = worker;
            WarmupBegan = began;
            warmupPack = AiPackInstaller.PackId(PackRoot);
            warmupFirst = PlayerPrefs.HasKey(AiPrepProgress.InstalledKey(warmupPack));
            WarmupExpected = ExpectedWarmup(warmupPack, warmupFirst);
        }

        /// <summary>
        /// Ready 에 닿았다 — 걸린 시간을 이 PC 의 기록으로 남기고, 로그에 붙일 한 조각을 돌려준다
        /// (S15P21B202-713). 다음 준비의 남은 시간이 이 값에서 나온다.
        /// </summary>
        string RecordWarmup(float now)
        {
            if (WarmupBegan < 0f) return "";
            float took = now - WarmupBegan;
            WarmupBegan = -1f;
            PlayerPrefs.SetFloat(AiPrepProgress.WarmupKey(warmupPack, warmupFirst), took);
            if (warmupFirst) PlayerPrefs.DeleteKey(AiPrepProgress.InstalledKey(warmupPack));
            PlayerPrefs.Save();
            return " · 데우기 " + Clock(took) + (warmupFirst ? " (설치 뒤 첫 실행)" : "") + " · 예상 " + Clock(WarmupExpected);
        }

        IEnumerator Watch(int epoch)
        {
            float began = Time.realtimeSinceStartup;
            float silentUntil = began + SilenceLimit;
            int failures = 0;
            StartWarmupClock(began);
            while (epoch == generation)
            {
                if (worker == null || worker.HasExited) { Fail("추가팩이 종료됐습니다"); yield break; }
                using (var request = UnityWebRequest.Get(Address + "/api/health"))
                {
                    request.SetRequestHeader("Authorization", "Bearer " + token);
                    request.timeout = 3;
                    pendingHealth = request;
                    yield return request.SendWebRequest();
                    pendingHealth = null;
                    if (epoch != generation) yield break;
                    bool answered = request.result == UnityWebRequest.Result.Success;
                    // 아직 ready 가 아니어도 **대답이 왔다면 살아서 데우고 있다.** 그 사실만으로
                    // 침묵 마감을 다시 민다 — 예전에는 이 구분이 없어 데우는 중에 잘렸다.
                    if (answered) silentUntil = Time.realtimeSinceStartup + SilenceLimit;
                    if (answered && ValidHealth(request.downloadHandler.text))
                    {
                        if (State != AiRuntimeState.Ready)
                            Message = "준비 완료 · 행동 / 대상 / 접근 여부 LLM 판단" + RecordWarmup(Time.realtimeSinceStartup);
                        State = AiRuntimeState.Ready; failures = 0;
                    }
                    else if (State == AiRuntimeState.Ready && ++failures >= 3)
                    { Fail("추가팩 연결을 잃었습니다"); yield break; }
                    else if (State != AiRuntimeState.Ready)
                    {
                        float waited = Time.realtimeSinceStartup - began;
                        if (waited > WarmupLimit)
                        { Fail("추가팩 준비 시간 초과 · 로비에서 다시 준비해 주세요"); yield break; }
                        if (Time.realtimeSinceStartup > silentUntil)
                        { Fail("추가팩이 응답하지 않습니다 · 로비에서 다시 준비해 주세요"); yield break; }
                        Message = WarmupMessage(answered, waited, WarmupExpected);
                    }
                }
                yield return new WaitForSecondsRealtime(.5f);
            }
        }

        public bool SendText(string text, NetPlayer selected)
            => SendText(text, AiRecipients.Selected, selected);

        public bool SendText(string text, AiRecipients recipients, NetPlayer selected)
        {
            var session = GameSession.Instance;
            if (Busy || State != AiRuntimeState.Ready || !NetworkServer.active || session == null
                || session.LocalPlayer == null || session.CinematicInputBlocked || session.EndingActive
                || AiHostSettings.ServerState != AiRoomState.Active || string.IsNullOrWhiteSpace(text) || text.Length > 300) return false;
            var group = AiGroupOrders.Capture(AiRecipients.All, null, session.LocalPlayer);
            if (group.Count == 0) { Message = "배정 가능한 AI 없음"; return false; }
            Busy = true;
            Message = "판단 대상: " + group.Names;
            StartCoroutine(Classify(text, group, recipients, selected, session, generation));
            return true;
        }

        IEnumerator Classify(string text, AiGroupOrders group, AiRecipients fallback, NetPlayer selected, GameSession session, int epoch)
        {
            using (var request = new UnityWebRequest(Address + "/api/companion-plan/text", "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(new TextRequest { text = text })));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", "Bearer " + token);
                request.timeout = 90;
                pendingText = request;
                yield return request.SendWebRequest();
                pendingText = null;
                if (epoch != generation) yield break;
                Busy = false;
                if (request.result != UnityWebRequest.Result.Success) { Message = "명령 통신 실패 · 기존 행동 유지"; yield break; }
                if (!NetworkServer.active || session == null || session != GameSession.Instance || session.EndingActive
                    || session.CinematicInputBlocked
                    || AiHostSettings.ServerState != AiRoomState.Active) { Message = "대상·게임 상태가 바뀌어 명령 취소"; yield break; }
                PlanDecision result = null;
                try { result = JsonUtility.FromJson<PlanDecision>(request.downloadHandler.text); }
                catch (ArgumentException) { }
                ReportPlan(result);
                Message = result == null ? "잘못된 응답 · 기존 행동 유지"
                    : (result.status == "none" || result.status == "clarify") && result.orders != null && result.orders.Length == 0
                    ? "실행할 작업 불명확/미지원 · 기존 행동 유지"
                    : result.status != "ok" || !AiCommandPlan.Valid(result.orders) ? "잘못된 계획 · 기존 행동 유지"
                    : group.ExecutePlan(result.orders, fallback, selected, session.LocalPlayer)
                    + (result.safety_override ? " · 금지 표현 안전 중단" : "")
                    + " (" + result.elapsed_ms.ToString("0") + "ms)";
            }
        }

        public bool SendVoice(byte[] wav, NetPlayer requester, AiGroupOrders group)
        {
            var session = GameSession.Instance;
            if (Busy || State != AiRuntimeState.Ready || !NetworkServer.active || session == null
                || requester == null || requester.IsAi || requester.VisualRig == null
                || session.CinematicInputBlocked || session.EndingActive || AiHostSettings.ServerState != AiRoomState.Active
                || !AiVoicePacket.Valid(wav)) return false;
            var speaker = requester.VisualRig;
            if (group == null || group.Count == 0) return false;
            NetPlayer nearest = group.Nearest;
            Busy = true;
            StartCoroutine(ClassifyVoice(wav, requester, speaker, group, nearest, session, generation));
            return true;
        }

        IEnumerator ClassifyVoice(byte[] wav, NetPlayer requester, PlayerRig speaker, AiGroupOrders group,
            NetPlayer selected, GameSession session, int epoch)
        {
            var form = new WWWForm();
            form.AddBinaryData("audio", wav, "command.wav", "audio/wav");
            using (var request = UnityWebRequest.Post(Address + "/api/companion-plan/voice", form))
            {
                request.SetRequestHeader("Authorization", "Bearer " + token); request.timeout = 90;
                pendingText = request;
                yield return request.SendWebRequest();
                pendingText = null;
                if (epoch != generation) yield break;
                Busy = false;
                if (requester == null || requester.VisualRig != speaker || speaker == null) yield break;
                if (!NetworkServer.active || session == null || session != GameSession.Instance || session.EndingActive
                    || session.CinematicInputBlocked || AiHostSettings.ServerState != AiRoomState.Active)
                { requester.ReportAiVoice("상태가 바뀌어 명령을 취소했어요"); yield break; }
                if (request.result != UnityWebRequest.Result.Success)
                { requester.ReportAiVoice("음성 처리에 실패했어요. 다시 말해 주세요"); yield break; }
                PlanDecision result = null;
                try { result = JsonUtility.FromJson<PlanDecision>(request.downloadHandler.text); }
                catch (ArgumentException) { }
                ReportPlan(result);
                // **못 알아들었으면 들은 말을 보여 준다** (S15P21B202-736). 그래야 "내가
                // 뭐라고 들렸는지" 를 알고 다시 말할 수 있다 — 지금은 같은 말을 같은
                // 목소리로 되풀이하는 것 말고 할 수 있는 것이 없다.
                if (result == null || result.status != "ok" || !AiCommandPlan.Valid(result.orders))
                { requester.ReportAiVoice(Heard(result) + "명령으로 알아듣지 못했어요"); yield break; }
                // Snapshot membership/revisions prevents delayed replies from replacing newer orders.
                Message = group.ExecutePlan(result.orders, AiRecipients.Selected, selected, speaker);
                // **무엇을 시켰는지 보여 준다** (S15P21B202-736). 전에는 개수뿐이라, 헛말이
                // 춤으로 실행돼도 플레이어는 동료가 왜 춤추는지 알 수 없었다.
                //
                // 아무도 안 받았으면 **왜인지**를 말한다 (S15P21B202-730). 전에는 언제나
                // "지금 수행할 수 있는 AI 동료가 없어요" 라서, 쓰러진 채 말한 사람은
                // 자기가 기절해서인지 동료가 바빠서인지 알 수 없었다.
                requester.ReportAiVoice(group.LastAccepted > 0
                    ? Heard(result) + (AiCommandPlan.Spoken(result.orders) ?? "명령") + " 시켰어요"
                    : Heard(result) + (group.LastRefusal ?? "지금 수행할 수 있는 AI 동료가 없어요"));
            }
        }

        /// <summary>
        /// <b>무엇을 듣고 무엇을 하기로 했는지</b> 한 줄씩 (S15P21B202-708).
        ///
        /// <para>실행 요약(<c>ExecutePlan</c> 의 반환)은 <b>결과</b>만 말한다. 명령이 엉뚱하게
        /// 수행됐을 때, 잘못 들은 것인지 · 잘못 계획한 것인지 · 계획대로 됐는데 결과가 다른
        /// 것인지를 가르려면 <b>앞의 둘</b>이 있어야 한다.</para>
        ///
        /// <para><b>전사는 사람이 말한 내용이다.</b> 참가자가 말한 것도 호스트의
        /// <c>Player.log</c> 에 글자로 남는다 — 음성은 어차피 판단을 위해 호스트로 가지만,
        /// 글자로 파일에 남는 것은 다른 일이라 QA 를 위한 결정으로 적어 둔다.</para>
        ///
        /// <para>상태 문구와 달리 <b>매번 적는다.</b> 명령은 사람이 V 를 누를 때만 나므로
        /// 로그를 덮지 않는다.</para>
        /// </summary>
        /// <summary>
        /// <b>무엇으로 들었는지</b>를 앞에 붙인다 (S15P21B202-736). 못 들었으면 빈 문자열.
        ///
        /// <para>받아쓴 말이 화면에 없으면 플레이어가 할 수 있는 것은 <b>같은 말을 같은
        /// 목소리로 되풀이하는 것</b>뿐이다. 무엇으로 들렸는지 보이면 다르게 말할 수 있다.</para>
        ///
        /// <para>길면 자른다 — 화면 아래 두 줄짜리 자리다.</para>
        /// </summary>
        public const int HeardLimit = 24;

        static string Heard(PlanDecision result)
        {
            string said = result != null ? result.text : null;
            if (string.IsNullOrWhiteSpace(said)) return "";
            said = said.Trim();
            if (said.Length > HeardLimit) said = said.Substring(0, HeardLimit) + "…";
            return "\"" + said + "\" 로 듣고 ";
        }

        /// <summary>
        /// 시간을 <b>나눠</b> 적는다 (S15P21B202-734).
        ///
        /// <para>2026-09-23 QA 에 <c>19731ms</c> 가 찍혔는데 <b>받아쓰기 탓인지 판단
        /// 탓인지 가릴 수 없었다.</b> 워커는 둘을 따로 재고 있었고, 합친 값 하나만
        /// 넘어왔다.</para>
        ///
        /// <para><c>load</c> 는 모델이 내려갔다 다시 올라온 시간이다 — 크면 고칠 곳이
        /// <c>keep_alive</c> 이고, <c>eval</c>·토큰 수가 크면 <c>num_predict</c> 다.
        /// <b>합계만 보면 둘이 구별되지 않는다.</b></para>
        ///
        /// <para>값이 없는 옛 워커에서는 합계만 적는다 — 새 필드는 0 으로 온다.</para>
        /// </summary>
        static string Timing(PlanDecision result)
        {
            string total = result.elapsed_ms.ToString("0") + "ms";
            if (result.asr_ms <= 0 && result.llm_ms <= 0) return total;
            string split = " (받아쓰기 " + (result.asr_ms / 1000f).ToString("0.0")
                         + "초 · 판단 " + (result.llm_ms / 1000f).ToString("0.0") + "초";
            if (result.llm_load_ms > 0)
                split += " · 모델 올리기 " + (result.llm_load_ms / 1000f).ToString("0.0") + "초";
            if (result.llm_tokens > 0) split += " · " + result.llm_tokens + "토큰";
            return total + split + ")";
        }

        void ReportPlan(PlanDecision result)
        {
            if (result == null) { UnityEngine.Debug.Log("[AI] 응답을 읽지 못했다"); return; }
            if (!string.IsNullOrWhiteSpace(result.text)) UnityEngine.Debug.Log("[AI] 들음: " + result.text);
            UnityEngine.Debug.Log("[AI] 계획 " + result.status + " " + AiCommandPlan.Describe(result.orders)
                      + (result.safety_override ? " · 안전 중단" : "")
                      + (string.IsNullOrEmpty(result.unsaid_social) ? "" : " · 말에 없던 " + result.unsaid_social + " 막음")
                      + " · " + Timing(result));
        }

        public static bool ValidDecision(Decision result)
        {
            if(result==null) return false;
            bool action = AiCommandPlan.Action(result.action) || result.action=="none" || result.action=="clarify";
            bool target = result.recipients=="default" || result.recipients=="all" || result.recipients=="one" || result.recipients=="two"
                || result.recipients=="red" || result.recipients=="blue" || result.recipients=="yellow" || result.recipients=="green";
            return action && target && (!result.approach || AiCompanionController.IsSocial(result.action));
        }

        public bool TestPullPin(NetPlayer selected)
            => SendDirect("pull_pin", AiRecipients.Selected, selected);

        public bool SendDirect(string action, AiRecipients recipients, NetPlayer selected)
        {
            var session = GameSession.Instance;
            if (!NetworkServer.active || session == null || session.LocalPlayer == null
                || session.CinematicInputBlocked || session.EndingActive || AiHostSettings.ServerState != AiRoomState.Active) return false;
            CancelPendingText();
            var group = AiGroupOrders.Capture(recipients, selected, session.LocalPlayer);
            Message = group.Execute(action, session.LocalPlayer) + " · 직접 실행(LLM 미사용)";
            return group.Count > 0;
        }

        public void StopOrders()
        {
            if (!NetworkServer.active) return;
            // 늦게 도착한 follow 응답이 중단을 뒤집지 못한다.
            CancelPendingText();
            foreach (var player in NetPlayer.AllServerPlayers)
                if (player != null && player.IsAi) player.TryApplyAiOrder("cancel", null);
            Message = "모든 AI 중단";
        }

        void CancelPendingText()
        {
            ++generation;
            pendingText?.Abort(); pendingText = null; Busy = false;
            pendingHealth?.Abort(); pendingHealth = null;
            StopAllCoroutines();
            if (worker != null && !worker.HasExited) StartCoroutine(Watch(generation));
        }

        void Fail(string reason)
        {
            StopRuntime(); attempted = true; State = AiRuntimeState.Failed; Message = reason;
        }

        void StopRuntime()
        {
            ++generation;
            pendingHealth?.Abort(); pendingText?.Abort();
            pendingHealth = pendingText = null;
            StopAllCoroutines(); Busy = false;
            if (worker != null)
            {
                try { if (!worker.HasExited) worker.Kill(); }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
                worker.Dispose(); worker = null;
            }
            // worker의 Windows Job Object가 자신이 만든 Ollama 자식만 정리한다.
            token = null; attempted = false; State = AiRuntimeState.Missing;
            Message = "추가팩 대기";
        }

        void OnDestroy()
        {
            if (Current != this) return;
            StopRuntime(); AiHostSettings.UnbindRuntime(this); Current = null;
        }
        void OnDisable() { if (Current == this) StopRuntime(); }
        void OnApplicationQuit() { if (Current == this) StopRuntime(); }
    }
}
