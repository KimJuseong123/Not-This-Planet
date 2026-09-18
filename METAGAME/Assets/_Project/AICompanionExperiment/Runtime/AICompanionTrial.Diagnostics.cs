using System;
using Metagame.AICompanion;
using System.Collections.Generic;
using UnityEngine;

namespace Metagame.AIExperiment
{
    // Presentation only: never chooses a target, changes an order, or advances physics.
    public sealed partial class AICompanionTrial
    {
        const int TraceCapacity = 120;
        readonly List<DiagnosticEntry> _diagnostics = new List<DiagnosticEntry>(TraceCapacity);
        int _diagnosticRequest;
        int _activeJobRequest;
        string _diagnosticInput = "직접 테스트";
        string _timing = "STT / LLM 시간: 응답 대기";
        string _observedStatus;
        int _observedProgress = -1;
        string _observedPinName;
        float _nextDiagnosticPoll;
        GUIStyle _diagnosticLabel;
        GUIStyle _diagnosticTitle;
        Vector2 _diagnosticScroll;
        bool _scrollToLatest;

        sealed class DiagnosticEntry
        {
            public GUIContent Content;
            public bool Warning;
        }

        public int DiagnosticCount => _diagnostics.Count;
        public string DiagnosticText => string.Join("\n", _diagnostics.ConvertAll(e => e.Content.text));

        void BeginDiagnostics(string input)
        {
            ++_diagnosticRequest;
            _diagnosticInput = input;
            _timing = input == "음성" ? "STT / LLM 시간: 응답 대기" : "STT 생략 / LLM 시간: 응답 대기";
            Trace("새 요청", input + " · 화자=로컬 방장/솔로 · AI 동료 1명");
        }

        void Trace(string stage, string detail, bool warning = false, int request = -1)
        {
            if (string.IsNullOrWhiteSpace(detail)) return;
            // Keep pasted/model text bounded and literal; no GUI rich-text interpretation.
            detail = detail.Replace('\r', ' ').Replace('\n', ' ');
            if (detail.Length > 600) detail = detail.Substring(0, 600) + "…";
            int id = request < 0 ? _diagnosticRequest : request;
            string line = $"{DateTime.Now:HH:mm:ss}  #{id:000} [{stage}] {detail}";
            if (_diagnostics.Count == TraceCapacity) _diagnostics.RemoveAt(0);
            _diagnostics.Add(new DiagnosticEntry { Content = new GUIContent(line), Warning = warning });
            _scrollToLatest = true;
            Debug.Log("[AI_TRACE] " + line);
        }

        // Same entry point for HTTP responses and deterministic diagnostic regression tests.
        public bool ReceiveResponseJson(string json)
        {
            IntentResponse result = null;
            try { result = JsonUtility.FromJson<IntentResponse>(json); }
            catch (Exception) { Trace("응답 오류", "JSON 응답을 해석하지 못했어요", true); }
            if (result != null && (result.text == null || string.IsNullOrWhiteSpace(result.action)))
            {
                Trace("응답 오류", "필수 text/action 필드가 없는 서버 응답", true);
                result = null;
            }
            if (result != null)
            {
                // JsonUtility can instantiate a missing nested object with default values.
                bool hasAsr = result.asr != null && !string.IsNullOrEmpty(result.asr.asr_model);
                string stt = hasAsr ? $"STT {result.asr.asr_ms:F0}ms" : "STT 생략(텍스트/미제공)";
                string llm = string.IsNullOrEmpty(result.model) ? "LLM 시간 미제공" : $"LLM {result.llm_wall_ms:F0}ms";
                _timing = $"{stt} / {llm} / 왕복 {_elapsedMs:F0}ms";
                if (hasAsr)
                {
                    _diagnosticInput = "음성";
                    Trace("STT 정보", $"{result.asr.asr_model} · 음성 {result.asr.audio_seconds:F2}초 · 첫 적재={result.asr.asr_cold}");
                }
                else _diagnosticInput = "텍스트";
                Trace("모델 정보", $"{result.model ?? "미제공"} · {result.prompt_version ?? "버전 미제공"} · 토큰 {result.input_tokens} → {result.output_tokens}");
                Trace("처리 시간", _timing);
            }
            if (result == null)
            {
                _accepted = false; _action = "invalid"; _decision = "응답 오류";
                _receipt = "서버 응답 오류 · 기존 행동 유지";
                _message = "명령 이해 실패와 구분하여 물음표를 표시하지 않습니다";
                return TraceReceipt();
            }
            return ReceiveIntent(result.text, result.action);
        }

        bool TraceReceipt()
        {
            Trace(_accepted ? "실행 수락" : "실행 거절", _receipt + " · " + _message, !_accepted);
            if (_accepted)
            {
                _activeJobRequest = _diagnosticRequest;
                _observedProgress = -1;
                var pin = _companion != null && _companion.Motor != null ? _companion.Motor.TargetPin : null;
                if (pin != null)
                    Trace("선택한 못", $"{pin.name} · 위치 {CompanionPinTarget.Center(pin):F1} · 화자와 가까운 순 · 남은 대기 {_companion.Motor.PendingPins}개");
                var tool = _companion != null && _companion.Motor != null ? _companion.Motor.ToolJobTarget : "";
                if (!string.IsNullOrEmpty(tool)) Trace("선택한 도구", tool + " · 플레이어와 동일한 집기/사용 판정");
            }
            return _accepted;
        }

        void ObserveDiagnostics()
        {
            if (Time.unscaledTime < _nextDiagnosticPoll) return;
            _nextDiagnosticPoll = Time.unscaledTime + 0.25f;
            if (_companion == null) return;
            var status = _companion.Status;
            if (status != _observedStatus)
            {
                _observedStatus = status;
                Trace("동료 상태", status, status.Contains("실패"), _activeJobRequest);
            }
            var pin = _companion.Motor != null ? _companion.Motor.TargetPin : null;
            if (pin == null) return;
            if (_observedPinName != pin.name)
            {
                _observedPinName = pin.name;
                _observedProgress = -1;
                Trace("현재 작업 못", pin.name + " · 대기 " + _companion.Motor.PendingPins + "개", false, _activeJobRequest);
            }
            int step = Mathf.FloorToInt(pin.Progress * 4);
            if (step == _observedProgress) return;
            _observedProgress = step;
            Trace("못 진행", $"{pin.Progress:P0} · 잡힘={_companion.Rig.ClampGrabbed} · 당기는 인원={pin.Pullers}", false, _activeJobRequest);
        }

        void DrawDiagnostics()
        {
            _diagnosticLabel ??= new GUIStyle(GUI.skin.label) { fontSize = 16, wordWrap = true, richText = false };
            _diagnosticTitle ??= new GUIStyle(_diagnosticLabel) { fontSize = 19, fontStyle = FontStyle.Bold };
            var oldMatrix = GUI.matrix;
            var oldColor = GUI.color;
            var oldContent = GUI.contentColor;
            float scale = Mathf.Clamp(Mathf.Min(Screen.width / 1440f, Screen.height / 900f), 0.6f, 1.5f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            float screenWidth = Screen.width / scale;
            float screenHeight = Screen.height / scale;
            float width = Mathf.Min(740, screenWidth - 24);
            float height = Mathf.Min(_showText ? 525 : 445, screenHeight - 24);
            var area = new Rect(12, screenHeight - height - 12, width, height);
            GUI.color = new Color(0.025f, 0.04f, 0.065f, 0.94f);
            GUI.DrawTexture(area, Texture2D.whiteTexture);
            GUI.color = oldColor;
            GUI.contentColor = Color.white;
            GUILayout.BeginArea(new Rect(area.x + 12, area.y + 8, area.width - 24, area.height - 16));
            GUILayout.Label("AI 상세 로그 · " + (Authority ? "방장 / 솔로" : "관찰자"), _diagnosticTitle);
            GUILayout.Label("현재 동료: " + (_companion != null ? _companion.Status : "준비 중"), _diagnosticLabel);
            if (Authority)
            {
                GUILayout.Label("V 말하기 · F8 중단 · F7 문장 입력 · F6 연습 운석 1개", _diagnosticLabel);
                GUILayout.Label(_timing, _diagnosticLabel);
                GUI.contentColor = _accepted ? new Color(0.45f, 1f, 0.72f) : new Color(1f, 0.8f, 0.4f);
                GUILayout.Label(_busy || _recording != null ? _message : _receipt, _diagnosticLabel);
            }
            else
                GUILayout.Label("음성 원문/LLM 결과는 방장 화면에만 표시 · 여기서는 동료 상태만 관찰", _diagnosticLabel);
            GUI.contentColor = Color.white;
            if (_scrollToLatest) { _diagnosticScroll.y = float.MaxValue; _scrollToLatest = false; }
            _diagnosticScroll = GUILayout.BeginScrollView(_diagnosticScroll, GUILayout.ExpandHeight(true));
            for (int i = 0; i < _diagnostics.Count; ++i)
            {
                GUI.contentColor = _diagnostics[i].Warning ? new Color(1f, 0.72f, 0.42f) : new Color(0.79f, 0.9f, 1f);
                GUILayout.Label(_diagnostics[i].Content, _diagnosticLabel);
            }
            GUILayout.EndScrollView();
            GUI.contentColor = Color.white;
            if (_showText && Authority)
            {
                _textInput = GUILayout.TextField(_textInput, 300, GUILayout.Height(30));
                if (GUILayout.Button("이 문장으로 요청 (STT 생략)", GUILayout.Height(30))) SendText(_textInput);
            }
            GUILayout.EndArea();
            GUI.matrix = oldMatrix;
            GUI.color = oldColor;
            GUI.contentColor = oldContent;
        }
    }
}
