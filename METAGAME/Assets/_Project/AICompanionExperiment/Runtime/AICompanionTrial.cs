using System;
using System.Collections;
using Metagame.Gameplay;
using Metagame.UI;
using Mirror;
using UnityEngine;
using UnityEngine.Networking;

namespace Metagame.AIExperiment
{
    public sealed partial class AICompanionTrial : MonoBehaviour
    {
        [SerializeField] NetworkCompanion _companionPrefab;
        [SerializeField] string _model = "qwen3:1.7b";
        [SerializeField] Metagame.Gameplay.Hazards.Meteor _practiceMeteorPrefab;
        Metagame.Gameplay.Hazards.Meteor _practiceMeteor;
        NetworkCompanion _companion;
        GameSession _session;
        EmoteHud _emoteHud;
        AudioClip _recording;
        string _device;
        string _recognized = "아직 입력 없음";
        string _decision = "대기";
        string _action = "none";
        string _receipt = "아직 요청 없음";
        bool _accepted;
        string _message = "V를 누르고 또렷하게 말한 뒤 놓으세요";
        string _textInput = "나 좀 따라와";
        UnityWebRequest _request;
        int _generation;
        float _recordStart;
        float _elapsedMs;
        bool _busy;
        bool _showText;
        bool _previousScriptedInput;
        // 연속으로 알아듣지 못하면 발화 안내를 띄운다. 측정에서 또박또박 발화가 CER을 0.60 -> 0.29로 낮췄다.
        int _unclearStreak;
        const int UnclearHintAfter = 2;
        const string SpeechHint = "마이크에 가까이, 조금 더 또렷하고 천천히 말해보세요";
        // 단어 오디션 실측: '그만해'는 생존 1.00, '멈춰'는 0.60. 안내에는 잘 인식되는 쪽을 쓴다.
        const string StopHint = "멈추려면 ‘그만해’라고 말하거나 F8을 누르세요";
        public NetworkCompanion Companion => _companion;
        public bool Busy => _busy;
        public string Decision => _action;
        public string Receipt => _receipt;
        public void Configure(NetworkCompanion prefab) => _companionPrefab = prefab;
        bool Authority => !NetworkClient.active || NetworkServer.active;

        IEnumerator Start()
        {
            Application.runInBackground = true;
            yield return null;
            _session = FindAnyObjectByType<GameSession>();
            _emoteHud = FindAnyObjectByType<EmoteHud>();
            if (_session == null) { _message = "GameSession이 없어 실험을 시작할 수 없습니다."; yield break; }
            if (Authority)
            {
                _companion = Instantiate(_companionPrefab);
                if (NetworkServer.active) NetworkServer.Spawn(_companion.gameObject);
                else _companion.InitializeSolo();
            }
        }

        void Update()
        {
            if (_companion == null) _companion = FindAnyObjectByType<NetworkCompanion>();
            ObserveDiagnostics();
            if (!Authority || _session == null) return;
            if (Input.GetKeyDown(KeyCode.F8)) StopImmediately();
            if (Input.GetKeyDown(KeyCode.F6) && !_showText) SpawnPracticeMeteor();
            if (Input.GetKeyDown(KeyCode.F7))
            {
                CancelPending();
                _showText = !_showText;
                if (_showText)
                {
                    _previousScriptedInput = _session.ScriptedInput;
                    _session.ScriptedInput = true;
                    _session.LocalPlayer?.SetInput(default);
                }
                else _session.ScriptedInput = _previousScriptedInput;
                Cursor.lockState = _showText ? CursorLockMode.None : CursorLockMode.Locked;
                Cursor.visible = _showText;
            }
            if (_showText || Time.timeScale == 0 || _session.EmoteWheelOpen) return;
            if (Input.GetKeyDown(KeyCode.V) && !_busy) StartRecording();
            if (_recording != null && (Input.GetKeyUp(KeyCode.V) || Time.realtimeSinceStartup - _recordStart >= 7.5f))
                FinishRecording();
        }

        void StartRecording()
        {
            BeginDiagnostics("음성");
            if (Microphone.devices.Length == 0) { _message = "마이크 없음 · F7로 문장 테스트 가능"; Trace("녹음 실패", _message, true); return; }
            _device = Microphone.devices[0];
            try
            {
                _recording = Microphone.Start(_device, false, 8, 16000);
                _recordStart = Time.realtimeSinceStartup;
                _message = "● 녹음 중 · V를 놓으면 전송 (최대 7.5초)";
                Trace("녹음 시작", "마이크: " + _device + " · V를 놓으면 전송");
            }
            catch (Exception e) { _message = "마이크 시작 실패: " + e.Message; Trace("녹음 실패", _message, true); }
        }

        void FinishRecording()
        {
            int length = Microphone.GetPosition(_device);
            Microphone.End(_device);
            var clip = _recording; _recording = null;
            if (length < clip.frequency / 5) { Destroy(clip); _message = "너무 짧아요. V를 누른 채 말해주세요."; Trace("녹음 제외", _message, true); return; }
            Trace("녹음 완료", $"{(float)length / clip.frequency:F2}초 · {clip.frequency}Hz · {clip.channels}채널");
            var samples = new float[length * clip.channels];
            clip.GetData(samples, 0);
            byte[] wave = CompanionPolicy.EncodeWave(samples, clip.channels, clip.frequency);
            Destroy(clip);
            var form = new WWWForm();
            form.AddBinaryData("audio", wave, "command.wav", "audio/wav");
            form.AddField("model", _model);
            StartCoroutine(Send(UnityWebRequest.Post("http://127.0.0.1:8765/api/companion-v4/voice", form)));
        }

        public void SendText(string text)
        {
            if (_busy || !Authority || string.IsNullOrWhiteSpace(text)) return;
            BeginDiagnostics("텍스트");
            Trace("직접 입력", text + " · STT 생략");
            var payload = JsonUtility.ToJson(new TextRequest { text = text, model = _model });
            var request = new UnityWebRequest("http://127.0.0.1:8765/api/companion-v4/text", "POST");
            request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(payload));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            StartCoroutine(Send(request));
        }

        IEnumerator Send(UnityWebRequest request)
        {
            _busy = true; _request = request;
            int generation = ++_generation;
            float start = Time.realtimeSinceStartup;
            _message = "로컬 음성/행동 모델 처리 중…";
            Trace("전송", request.url + " · 모델 " + _model);
            request.timeout = 30;
            yield return request.SendWebRequest();
            if (generation == _generation)
            {
                _elapsedMs = (Time.realtimeSinceStartup - start) * 1000;
                if (request.result != UnityWebRequest.Result.Success)
                {
                    _accepted = false; _receipt = CompanionPolicy.ServiceError(request.responseCode) + " · 기존 행동 유지";
                    _decision = "판단 실패";
                    _action = "invalid";
                    _message = CompanionPolicy.ServiceError(request.responseCode);
                    Trace("연결 안내", _message, true);
                    Trace("통신 실패", $"HTTP {request.responseCode} · {request.error} · {_elapsedMs:F0}ms · 기존 행동 유지", true);
                    Trace("서버 응답", request.downloadHandler?.text, true);
                }
                else
                {
                    ReceiveResponseJson(request.downloadHandler.text);
                }
                _busy = false; _request = null;
            }
            request.Dispose();
        }

        public bool ReceiveIntent(string text, string action)
        {
            Trace(_diagnosticInput == "음성" ? "STT 인식" : "입력 문장", string.IsNullOrWhiteSpace(text) ? "(비어 있음)" : text);
            Trace("LLM 선택", (action ?? "(없음)") + " → " + CompanionPolicy.Label(action));
            _action = action ?? "invalid";
            _recognized = string.IsNullOrWhiteSpace(text) ? "인식하지 못했어요" : text;
            _decision = CompanionPolicy.Label(action) + " (" + (action ?? "invalid") + ")";
            _accepted = false;
            if (!Authority) { _receipt = "방장만 요청할 수 있어요"; return TraceReceipt(); }
            if (string.IsNullOrWhiteSpace(text))
            {
                ShowQuestion();
                _receipt = "음성 인식 실패 · 기존 행동 유지";
                _message = NoteUnclear("명령을 받아 멈춘 것이 아니에요");
                return TraceReceipt();
            }
            if (!CompanionPolicy.TryParse(action, out _))
            {
                ShowQuestion();
                _receipt = action == "clarify" ? "확인 필요 · 기존 행동 유지" : "실행할 요청 없음 · 기존 행동 유지";
                // 미지원 요청(none)은 또렷하게 말해도 달라지지 않으므로 안내 대상에서 뺀다.
                _message = action == "clarify"
                    ? NoteUnclear(StopHint)
                    : StopHint;
                return TraceReceipt();
            }
            _unclearStreak = 0;
            string feedback = "동료가 준비되지 않았어요";
            _accepted = _companion != null && _companion.TryApply(action, _session?.LocalPlayer?.Hips?.transform, out feedback);
            _receipt = _accepted ? "명령 수락 · " + CompanionPolicy.Label(action) : "실행 불가 · 기존 행동 유지";
            _message = feedback;
            return TraceReceipt();
        }

        void CancelPending()
        {
            if (_busy || _recording != null) Trace("요청 취소", "처리 중 요청/녹음 취소 · 늦게 온 응답은 적용하지 않음", true);
            ++_generation; // A delayed follow response cannot revive a cancelled command.
            _request?.Abort(); _request = null; _busy = false;
            if (_recording != null)
            {
                Microphone.End(_device); Destroy(_recording); _recording = null;
            }
        }

        public void StopImmediately()
        {
            CancelPending();
            _companion?.Apply("cancel", null);
            _activeJobRequest = _diagnosticRequest;
            _accepted = true; _receipt = "F8 수동 중단"; _decision = "중단 (cancel)"; _action = "cancel";
            _message = "즉시 중단 · 이전 응답은 무시합니다";
            Trace("수동 중단", _message);
        }

        void OnApplicationFocus(bool focused) { if (!focused && _recording != null) CancelPending(); }
        void OnDisable()
        {
            if (_showText && _session != null) _session.ScriptedInput = _previousScriptedInput;
            var request = _request;
            StopImmediately(); StopAllCoroutines(); request?.Dispose();
        }

        void OnGUI()
        {
            DrawDiagnostics();
            if (_emoteHud != null && _companion != null && _session != null && !_session.EndingActive)
                _emoteHud.DrawWorldEmote(_session.LocalPlayer?.Cam?.Cam, _companion.EmoteAnchor,
                    _companion.EmoteId, _companion.EmoteExpiresAt, _companion.EmoteNow);
        }

        /// <summary>알아듣지 못한 횟수를 세고, 반복되면 발화 안내를 덧붙인다.</summary>
        string NoteUnclear(string message)
        {
            if (++_unclearStreak < UnclearHintAfter) return message;
            // TraceReceipt records the complete message once, including this hint.
            return message + " · " + SpeechHint;
        }

        void ShowQuestion()
        {
            if (_companion != null && _companion.ShowQuestionEmote())
                Trace("이모티콘", "? · 이해하지 못함/미지원 요청 · 기존 행동 유지");
        }

        public void SpawnPracticeMeteor()
        {
            if (!Authority || _companion == null || _companion.Rig == null || _practiceMeteor != null) return;
            // Trial samples are not registered in the production MeteorSpawner's network list.
            if (NetworkServer.active || NetworkClient.active)
            { Trace("운석 연습", "F6 샘플은 ‘혼자 테스트’에서 사용하세요 · 멀티에서 보이지 않는 운석은 만들지 않습니다", true); return; }
            if (_practiceMeteorPrefab == null) { Trace("운석 연습", "연습 운석 프리팹 미설정", true); return; }
            var rig = _companion.Rig;
            var point = rig.Position + Vector3.right * 1.2f + Vector3.up * 3f;
            _practiceMeteor = Instantiate(_practiceMeteorPrefab, point, Quaternion.identity);
            _practiceMeteor.SetGroundHeight(rig.FootPosition.y);
            _practiceMeteor.Body.isKinematic = false;
            _practiceMeteor.Body.useGravity = true;
            _practiceMeteor.Body.linearVelocity = Vector3.down * 1.5f;
            // One manual sample in this isolated trial, never an automatic Stage1 wave.
            Destroy(_practiceMeteor.gameObject, 6f);
            Trace("운석 연습", "F6 · AI 옆에 운석 1개 · 기존 충돌/타격 규칙 · 6초 후 정리");
        }

        [Serializable] sealed class TextRequest { public string text; public string model; }
        [Serializable] sealed class IntentResponse
        {
            public string text;
            public string action;
            public string model;
            public string prompt_version;
            public float llm_wall_ms;
            public int input_tokens;
            public int output_tokens;
            public AsrTiming asr;
        }
        [Serializable] sealed class AsrTiming
        {
            public float asr_ms;
            public float audio_seconds;
            public string asr_model;
            public bool asr_cold;
        }
    }
}
