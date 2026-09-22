using System;
using System.Collections.Generic;
using Metagame.Core;
using Metagame.Gameplay;
using Metagame.Gameplay.Stage2;
using UnityEngine;

namespace Metagame.UI
{
    public sealed class Stage2Hud : MonoBehaviour
    {
        Stage2Yard _yard;
        GameSession _session;
        Func<uint> _localId;
        GUIStyle _title, _text, _small;
        readonly Dictionary<uint, LineRenderer> _sprays = new();
        Material _sprayMaterial;
        Transform _toolView, _wrenchView, _sprayerView;
        public float WrenchAngle { get; set; }
        public Stage2Target Focus { get; set; }
        public bool GuideOpen { get; set; }
        public void Configure(Stage2Yard yard, GameSession session, Func<uint> localId)
        { _yard = yard; _session = session; _localId = localId; }

        void LateUpdate()
        {
            if (_yard == null) return;
            UpdateToolView();
            foreach (var line in _sprays.Values) line.enabled = false;
            foreach (var player in _yard.State.Players)
            {
                if (!player.Spraying) continue;
                if (!_sprays.TryGetValue(player.Id, out var line))
                {
                    var root = new GameObject("Decontamination spray " + player.Id);
                    root.transform.SetParent(transform);
                    line = root.AddComponent<LineRenderer>();
                    // Reuse a material authored into this scene, so builds retain the shader.
                    _sprayMaterial ??= _yard.GetComponentInChildren<Renderer>().sharedMaterial;
                    line.sharedMaterial = _sprayMaterial;
                    line.positionCount = 2; line.startWidth = .08f; line.endWidth = .65f;
                    line.startColor = line.endColor = new Color(.25f, .85f, 1);
                    _sprays.Add(player.Id, line);
                }
                line.enabled = true; line.SetPosition(0, player.SprayStart); line.SetPosition(1, player.SprayEnd);
            }
        }

        void UpdateToolView()
        {
            if (_session?.LocalPlayer?.Cam == null) return;
            if (_toolView == null)
            {
                _toolView = new GameObject("Stage2 equipped hand tool").transform;
                _toolView.SetParent(_session.LocalPlayer.Cam.transform,false);
                _toolView.localPosition = new Vector3(.32f,-.28f,.62f);
                var material = _yard.GetComponentInChildren<Renderer>().sharedMaterial;
                _wrenchView = new GameObject("Manual wrench").transform; _wrenchView.SetParent(_toolView,false);
                Stage2Yard.BuildWrenchVisual(_wrenchView, material);
                _sprayerView = new GameObject("Decontamination nozzle").transform; _sprayerView.SetParent(_toolView,false);
                ToolPiece(_sprayerView,Vector3.zero,new Vector3(.12f,.12f,.34f),material);
                ToolPiece(_sprayerView,new Vector3(0,-.12f,-.07f),new Vector3(.06f,.2f,.06f),material);
                ToolPiece(_sprayerView,new Vector3(0,0,.22f),new Vector3(.2f,.2f,.12f),material);
            }
            var tool = _yard.PlayerState(_localId()).Tool;
            bool showTool = !GuideOpen && _yard.State.Pilot != _localId() && !_session.LocalPlayer.Downed;
            _wrenchView.gameObject.SetActive(tool == YardTool.Wrench && showTool);
            _sprayerView.gameObject.SetActive(tool == YardTool.Sprayer && showTool);
            _wrenchView.localRotation = Quaternion.Euler(15,0,-WrenchAngle);
        }

        static void ToolPiece(Transform parent,Vector3 position,Vector3 scale,Material material)
        {
            var piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
            piece.transform.SetParent(parent,false); piece.transform.localPosition=position; piece.transform.localScale=scale;
            var collider=piece.GetComponent<Collider>(); collider.enabled=false; Destroy(collider);
            piece.GetComponent<Renderer>().sharedMaterial=material;
        }

        void OnGUI()
        {
            if (_yard == null || _session == null || _session.LocalPlayer == null) return;
            _title ??= new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            _text ??= new GUIStyle(GUI.skin.label) { fontSize = 18, wordWrap = true, normal = { textColor = Color.white } };
            _small ??= new GUIStyle(_text) { fontSize = 15 };
            float scale = Mathf.Min(Screen.width / 1600f, Screen.height / 900f);
            var previous = GUI.matrix; GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
            // 공통 팀 상태 HUD의 실제 픽셀 높이를 이 패널의 좌표계로 변환한다.
            float teamOffset = GameHud.TeamStatusHeight * UiScale.Value / Mathf.Max(.01f, scale);
            GUI.BeginGroup(new Rect(0, teamOffset, 1600, 550));
            GUI.Box(new Rect(24, 24, 530, 200), "");
            GUI.Label(new Rect(42, 34, 500, 38), "CHAPTER 02  /  폐허가 된 지구의 발사장", _title);
            int complete = 0; foreach (var part in _yard.State.Parts) if (part.Secured) complete++;
            GUI.Label(new Rect(42, 78, 480, 60), $"로켓 조립 {complete}/3  ·  훅 {(_yard.State.Attached < 0 ? "분리" : "연결됨")}\n컨테이너 → 인양 → 사다리 → 볼트 3곳 → 하중 해제", _text);
            var me = _yard.PlayerState(_localId());
            GUI.Label(new Rect(42, 142, 480, 65), $"장비: {me.Tool}   오염: {me.Contamination:P0}\n{TabletHelpContent.Name(Controls.TransportAction)} 상호작용 · {TabletHelpContent.Name(Controls.Tablet)} 작업 태블릿 · {TabletHelpContent.Name(Controls.Restart)} 개인 복구", _small);
            int row=0;
            foreach(var member in _yard.State.Players)
            {
                GUI.Label(new Rect(24,245+row++*30,510,30),$"동료 {member.Id}  오염 {member.Contamination:P0}  {(member.Downed ? $"기절 — 제독 후 {TabletHelpContent.Name(Controls.RescueTeammate)} 구조" : "작업 가능")}",_small);
            }
            string warning = _yard.GasActive ? "유독 가스 누출 중 — 초록 구역 이탈 / 동료에게 제독 요청"
                : _yard.GasWarning ? "6초 예고 — 배관 경고 구역에서 누출 예정" : _yard.State.GasEnabled ? "누출 휴지기 — 남은 오염은 동료에게 제독 요청" : "첫 조립 학습 구간 / 준비 구역은 안전";
            GUI.Label(new Rect(580, 28, 980, 55), warning, _text);
            int pressure=_yard.PressurePhase(_yard.ActivePressurePump);
            if(pressure>0)GUI.Label(new Rect(580,145,980,55),pressure==1
                ? $"배수 압력 상승 — 주황 분출선에서 대피 / 펌프 손잡이 {TabletHelpContent.Name(Controls.TransportAction)}로 감압"
                : "고압 역류 분출 — 물줄기를 피하거나 감압 밸브를 여세요",_text);
            var swimmer = _session.LocalPlayer;
            if (swimmer != null && swimmer.InWater)
                GUI.Label(new Rect(580, 85, 980, 55), swimmer.Swimming
                    ? "깊은 배수로 — WASD 이동 · Space 상승 · Ctrl 하강 · 완만한 둑으로 나가기"
                    : "얕은 배수로 — 물의 저항으로 이동이 느려집니다", _text);
            GUI.EndGroup();
            if (Focus != null)
            {
                string hint = FocusHintFor(Focus.Kind, Focus.Label,
                    Focus.Kind == YardTargetKind.Bolt ? _yard.BoltCondition(Focus.Index / 3) : null);
                GUI.Box(new Rect(490, 718, 650, 75), "");
                GUI.Label(new Rect(510, 730, 610, 60), hint, _text);
            }
            GUI.Label(new Rect(796, 447, 20, 20), "+", _text);
            if (_yard.State.Pilot == _localId())
            {
                GUI.Box(new Rect(420, 782, 1160, 100), "");
                GUI.Label(new Rect(440, 793, 1120, 85),
                    $"크레인 조종 · 3인칭   |   {Controls.MoveLeft}/{Controls.MoveRight} 회전 · {Controls.MoveForward}/{Controls.MoveBack} 반경\n" +
                    $"{Controls.Jump} 상승 · {Controls.ArmsUp} 하강 · {Controls.Run} 정밀 · 마우스 시점\n" +
                    $"{Controls.TransportAction} 조종 종료 · {Controls.Tablet} 작업 안내 — 내려도 하중 유지", _text);
            }
            else if (me.Tool == YardTool.Wrench && !GuideOpen)
                GUI.Label(new Rect(470, 815, 1050, 50), $"렌치: 좌클릭 + 마우스 원형 회전으로 체결 · {Controls.ToolAction} 던지기 · 떨어진 렌치 {Controls.TransportAction} 줍기", _text);
            if (_yard.State.Launched)
            {
                GUI.Box(new Rect(370, 300, 860, 230), "");
                GUI.Label(new Rect(410, 345, 800, 140), "로켓 조립 및 발사 준비 완료\n탐사대가 지구를 탈출했습니다.\n초안 완료 — Esc 메뉴에서 로비로 돌아갈 수 있습니다.", _title);
            }
            if (GuideOpen) DrawGuide();
            GUI.matrix = previous;
        }

        /// <summary>2장 장비와 시설은 E 상호작용, 볼트 체결은 마우스 입력을 유지한다.</summary>
        public static string FocusHintFor(YardTargetKind kind, string label, string boltCondition) =>
            kind switch
            {
                YardTargetKind.Bolt => boltCondition,
                // 샤워는 버튼이 아니라 범위 안에 서 있으면 제독한다.
                YardTargetKind.Shower => "고정 샤워 가까이에서 제독",
                _ => TabletHelpContent.Cap(Controls.TransportAction) + "  " + label,
            };

        void DrawGuide()
        {
            GUI.Box(new Rect(270, 110, 1060, 630), "");
            GUI.Label(new Rect(310, 135, 980, 45), "작업 태블릿  /  지구 탈출 계획", _title);
            GUI.Label(new Rect(310, 200, 620, 510),
                "01  상부 뚜껑을 열고 큰 인양 고리에 훅을 연결합니다.\n\n" +
                "02  승강기로 조종석에 올라 회전·반경·높이를 조작합니다.\n     접합면은 동료가 가까이서 확인합니다.\n\n" +
                "03  사다리를 들고 평평한 바닥에 설치합니다.\n     설치 시 다음 접합 높이에 맞춰 펼쳐집니다. 점유 중 이동 금지.\n\n" +
                $"04  렌치 장착 → 볼트 조준 → 좌클릭 유지 + 마우스 원형 이동.\n     3곳 체결 → 와이어 하강 → 큰 고리 {TabletHelpContent.Name(Controls.TransportAction)}로 연결 해제.\n     수정: 볼트 좌+우클릭 / {Controls.ToolAction} 던지기 · {Controls.TransportAction} 줍기.\n\n" +
                $"05  첫 체결 뒤 오염 시작. 분사기를 들고 좌클릭으로 동료 제독.\n     기절한 동료는 제독 후 {TabletHelpContent.Name(Controls.RescueTeammate)} 구조. 솔로는 파란 고정 샤워 이용.\n\n" +
                "06  모두 체결·훅 분리·크레인 후퇴 후 발사대에 모여 출발.\n     유실 파츠는 준비 구역의 복구 패널에서 회수합니다.", _small);
            var rect = new Rect(1080, 245, 70, 275);
            var old = GUI.color;
            for (int i = 0; i < 3; i++)
            {
                var p = _yard.State.Parts[i];
                GUI.color = p.Secured ? new Color(.35f, .9f, .6f) : new Color(.85f, .7f, .35f);
                GUI.DrawTexture(new Rect(rect.x, rect.y + (2-i)*90, 70, 80), Texture2D.whiteTexture);
                GUI.color = Color.white;
                GUI.Label(new Rect(990, rect.y + (2-i)*90, 90, 80), $"{i+1}단\n{p.A:P0}\n{p.B:P0} / {p.C:P0}", _small);
            }
            GUI.color = old;
            GUI.Label(new Rect(965, 550, 325, 170), $"고정 하단 → 연료부 → 거주부 → 노즈\n황색: 작업 / 청색: 지원 / 녹색: 위험\n\n배수 압력 경고 시 주황 분출선 대피\n펌프 손잡이 {TabletHelpContent.Name(Controls.TransportAction)}: 45초 감압\n{TabletHelpContent.Name(Controls.Tablet)} 닫기", _small);
        }
    }
}
