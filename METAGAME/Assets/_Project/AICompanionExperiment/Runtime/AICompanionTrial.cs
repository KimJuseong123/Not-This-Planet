using System;
using System.Collections;
using Metagame.Gameplay;
using Mirror;
using UnityEngine;
using UnityEngine.Networking;

namespace Metagame.AIExperiment
{
    public sealed class AICompanionTrial : MonoBehaviour
    {
        [SerializeField] NetworkCompanion _companionPrefab;
        [SerializeField] string _model = "qwen3:1.7b";
        NetworkCompanion _companion;
        GameSession _session;
        AudioClip _recording;
        string _device;
        string _recognized = "아직 입력 없음";
        string _decision = "대기";
        string _action = "none";
        string _receipt = "아직 요청 없음";
        bool _accepted;
        string _message = "V를 누르고 말한 뒤 놓으세요";
        string _textInput = "나 좀 따라와";
        UnityWebRequest _request;
        int _generation;
        float _recordStart;
        float _elapsedMs;
        bool _busy;
        bool _showText;
        bool _previousScriptedInput;
        GUIStyle _label;
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
            if (!Authority || _session == null) return;
            if (Input.GetKeyDown(KeyCode.F8)) StopImmediately();
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
            if (_showText || Time.timeScale == 0) return;
            if (Input.GetKeyDown(KeyCode.V) && !_busy) StartRecording();
            if (_recording != null && (Input.GetKeyUp(KeyCode.V) || Time.realtimeSinceStartup - _recordStart >= 7.5f))
                FinishRecording();
        }

        void StartRecording()
        {
            if (Microphone.devices.Length == 0) { _message = "마이크 없음 · F7로 문장 테스트 가능"; return; }
            _device = Microphone.devices[0];
            try
            {
                _recording = Microphone.Start(_device, false, 8, 16000);
                _recordStart = Time.realtimeSinceStartup;
                _message = "● 녹음 중 · V를 놓으면 전송 (최대 7.5초)";
            }
            catch (Exception e) { _message = "마이크 시작 실패: " + e.Message; }
        }

        void FinishRecording()
        {
            int length = Microphone.GetPosition(_device);
            Microphone.End(_device);
            var clip = _recording; _recording = null;
            if (length < clip.frequency / 5) { Destroy(clip); _message = "너무 짧아요. V를 누른 채 말해주세요."; return; }
            var samples = new float[length * clip.channels];
            clip.GetData(samples, 0);
            byte[] wave = CompanionPolicy.EncodeWave(samples, clip.channels, clip.frequency);
            Destroy(clip);
            var form = new WWWForm();
            form.AddBinaryData("audio", wave, "command.wav", "audio/wav");
            form.AddField("model", _model);
            StartCoroutine(Send(UnityWebRequest.Post("http://127.0.0.1:8765/api/companion-v2/voice", form)));
        }

        public void SendText(string text)
        {
            if (_busy || !Authority || string.IsNullOrWhiteSpace(text)) return;
            var payload = JsonUtility.ToJson(new TextRequest { text = text, model = _model });
            var request = new UnityWebRequest("http://127.0.0.1:8765/api/companion-v2/text", "POST");
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
            request.timeout = 30;
            yield return request.SendWebRequest();
            if (generation == _generation)
            {
                _elapsedMs = (Time.realtimeSinceStartup - start) * 1000;
                if (request.result != UnityWebRequest.Result.Success)
                {
                    _accepted = false; _receipt = "통신 실패 · 기존 행동 유지";
                    _decision = "판단 실패";
                    _action = "invalid";
                    _message = "음성 서버(8765) 실행/처리 중 여부를 확인하세요. " + request.responseCode;
                }
                else
                {
                    IntentResponse result = null;
                    try { result = JsonUtility.FromJson<IntentResponse>(request.downloadHandler.text); }
                    catch (Exception) { /* Invalid responses are rejected, not interpreted as a stop. */ }
                    ReceiveIntent(result?.text, result?.action);
                }
                _busy = false; _request = null;
            }
            request.Dispose();
        }

        public bool ReceiveIntent(string text, string action)
        {
            _action = action ?? "invalid";
            _recognized = string.IsNullOrWhiteSpace(text) ? "인식하지 못했어요" : text;
            _decision = CompanionPolicy.Label(action) + " (" + (action ?? "invalid") + ")";
            _accepted = false;
            if (!Authority) { _receipt = "방장만 요청할 수 있어요"; return false; }
            if (string.IsNullOrWhiteSpace(text))
            { _receipt = "음성 인식 실패 · 기존 행동 유지"; _message = "명령을 받아 멈춘 것이 아니에요"; return false; }
            if (!CompanionPolicy.TryParse(action, out _))
            {
                _receipt = action == "clarify" ? "확인 필요 · 기존 행동 유지" : "실행할 요청 없음 · 기존 행동 유지";
                _message = "멈추려면 ‘멈춰’라고 말하거나 F8을 누르세요";
                return false;
            }
            string feedback = "동료가 준비되지 않았어요";
            _accepted = _companion != null && _companion.TryApply(action, _session?.LocalPlayer?.Hips?.transform, out feedback);
            _receipt = _accepted ? "명령 수락 · " + CompanionPolicy.Label(action) : "실행 불가 · 기존 행동 유지";
            _message = feedback;
            return _accepted;
        }

        void CancelPending()
        {
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
            _accepted = true; _receipt = "F8 수동 중단"; _decision = "중단 (cancel)"; _action = "cancel";
            _message = "즉시 중단 · 이전 응답은 무시합니다";
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
            _label ??= new GUIStyle(GUI.skin.label) { fontSize = 17, wordWrap = true };
            float width = Mathf.Min(700, Screen.width - 30);
            GUILayout.BeginArea(new Rect(15, Screen.height - (_showText ? 330 : 250), width, _showText ? 315 : 235), GUI.skin.box);
            GUILayout.Label("AI 동료 실험 · " + (_companion != null ? _companion.Status : "동료 준비 중"), _label);
            if (Authority)
            {
                GUILayout.Label("V 누른 채 말하기 / F8 중단 / F7 문장 입력", _label);
                GUILayout.Label("들린 말: " + _recognized + "\n판단: " + _decision + " · 응답 " + _elapsedMs.ToString("0") + "ms", _label);
                var oldColor = GUI.contentColor;
                GUI.contentColor = _accepted ? new Color(0.45f, 1f, 0.72f) : new Color(1f, 0.78f, 0.35f);
                GUILayout.Label(_receipt, _label);
                GUI.contentColor = oldColor;
                GUILayout.Label(_message, _label);
                if (_showText)
                {
                    _textInput = GUILayout.TextField(_textInput, 300, GUILayout.Height(30));
                    if (GUILayout.Button("이 문장으로 요청", GUILayout.Height(30))) SendText(_textInput);
                }
            }
            else GUILayout.Label("관찰자 · 방장의 음성으로 움직이는 동료를 확인하세요", _label);
            GUILayout.EndArea();
        }

        [Serializable] sealed class TextRequest { public string text; public string model; }
        [Serializable] sealed class IntentResponse { public string text; public string action; }
    }
}
