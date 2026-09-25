"""낱말 규칙으로 먼저 정하는 명령 (S15P21B202-741). **표준 라이브러리만** 쓴다 — CI 가 그대로 시험한다.

**왜 규칙이 먼저인가.** 목표가 정확도가 아니라 **시간**이다(2026-09-25). 잘못 알아들으면 다시
말하면 되지만, 말을 끝내고 몇 초씩 기다리면 몰입이 깨진다. 같은 녹음 74개로 쟀을 때:

    판단기만           말이 끝난 뒤 6.4초 · 맞음 43/74
    규칙 먼저 → 판단기  2.0초          · 맞음 50/74   (규칙이 51개를 판단기 없이 정했다)

규칙이 정하면 판단기(qwen3, CPU 로 2~5초 · 게임 부하에서 더)를 부르지 않는다. 못 정하면 지금처럼
판단기다. 정확도 숫자는 대본을 아는 사람이 규칙을 써서 부풀었을 수 있다 — 시간 숫자는 그렇지 않다.

**낱말은 판단 프롬프트(`companion_plan.SYSTEM`)와 받아쓰기 힌트(`engine.ASR_HOTWORDS`)에서만
가져온다.** 판단기가 이미 배운 뜻을 그대로 옮긴 것이다. 새 행동을 규칙에만 넣으면 판단기와
뜻이 갈린다.

**특정한 것을 먼저 둔다.** 위에서부터 처음 맞는 규칙 하나로 정한다. `방망이 가져와` 는 가져오기가
줍기보다, `견인 장치에서 내려` 는 하차가 도구 내려놓기보다 먼저다.

**멈춤과 부정문이 맨 앞이다.** `춤추지 마` 에 춤추면 안 된다 — 되돌려야 하는 행동은 못 알아듣는
것보다 나쁘다(S15P21B202-736). 판단기 쪽의 "멈춤 말은 cancel" 뒤처리와 같은 뜻이다.

**못 정하는 것은 넘긴다.** 두 동료를 나눠 부르는 말(`빨강이랑 파랑이`)과 `그다음·말고` 가 든
말은 규칙이 나눌 수 없으므로 판단기로 보낸다.
"""
import re

#: (정규식, 행동[:목적지]). 위에서부터 처음 맞는 것 하나.
RULES = [
    (r"그만|멈춰|중단|취소|지\s?마", "cancel"),
    (r"살려|일으켜|구해", "revive"),
    (r"춤", "dance"),
    (r"인사|안녕", "greet"),
    (r"하트", "heart"),
    (r"따봉|응원|화이팅|파이팅", "cheer"),
    # `다들 못 뽑아` 의 "다들" 은 대상이다 — "다" 는 못 **뒤에** 떨어져 있을 때만 "전부" 다.
    (r"못.*(다\s|다$|전부|모두)|(전부|모두).*못", "pull_all_pins"),
    (r"못.*(뽑|빼)", "pull_pin"),
    (r"용접건.*(가져|갖다|갖고|줘)", "bring_welder"),
    (r"용접건.*(집|주워|들)", "pickup_welder"),
    (r"(방망이|몽둥이).*(가져|갖다|갖고|줘)", "bring_bat"),
    (r"운석.*(막|쳐|방어)|(방망이|몽둥이).*운석", "defend_meteors"),
    (r"(방망이|몽둥이).*(집|주워|들)", "pickup_bat"),
    (r"연습공|공.*(쳐|때려)", "hit_target"),
    (r"견인.*(내려|하차)", "leave_tractor"),
    (r"견인.*(움직|이동|앞으로|뒤로|옮)", "move_tractor"),
    (r"견인.*(타|올라|탑승)", "board_tractor"),
    (r"(높이|고도).*(유지|그대로)", "hold_tractor"),
    (r"도구.*(가져|갖다|갖고|줘)", "bring_tool"),
    (r"도구.*(집|주워)", "pickup_tool"),
    (r"내려\s?놔|내려놓|바닥에", "drop_tool"),
    (r"(나|날|저).*(들어\s?올려|올려\s?줘)", "lift_player"),
    (r"쌓", "stack_part"),
    (r"조립대로.*(옮|운반)|운반", "transport_part"),
    (r"떼|제거", "cut_weld"),
    (r"용접", "weld"),
    (r"들어서?\s?(들고|있)|들고\s?있", "lift_part"),
    (r"(파츠|이거|그거).*(놔|놓)|놔|놓아", "release_part"),
    (r"잡", "grab_part"),
    (r"밀|끌", "carry_part"),
    (r"따라", "follow"),
    (r"모여", "gather"),
    (r"기다려|가만히|대기", "wait"),
    (r"조립대", "goto:assembly"),
    (r"언덕|위로\s?올라", "goto:high"),
    (r"저기|저쪽|저곳", "goto:aim"),
    (r"여기|이리|이쪽|내\s?(옆|쪽)", "goto:speaker"),
]

COLORS = {"빨강": "red", "파랑": "blue", "노랑": "yellow", "초록": "green"}

#: 규칙이 나눌 수 없는 말. 판단기로 보낸다.
HAND_OVER = re.compile(r"이랑|하고\s|그다음|다음에|그런\s?뒤|말고")

_COMPILED = [(re.compile(p), a) for p, a in RULES]


def recipient(text):
    """말에 든 대상. 1~4 로 고른 명령은 게임이 이 값을 덮는다(AiCommandPlan.ForTarget)."""
    if re.search(r"다들|모두|전원", text):
        return "all"
    if "나머지" in text:
        return "remaining"
    found = [color for word, color in COLORS.items() if word in text]
    return found[0] if len(found) == 1 else "default"


# ── 라이브 시연 (S15P21B202-746) ──────────────────────────────────────────────
#
# 명령은 넷뿐이다 — 방망이로 운석 쳐내기 · 기절한 사람 살리기 · 고정못 뽑기 · 이쪽으로 오기.
# 2026-09-25 QA: 조용한 곳에서는 받아쓰기가 어느 정도 되는데 그 뒤의 행동이 무너졌다. 넷을 제대로
# 하는 것이 서른 개를 조금씩 하는 것보다 시연에 맞다.
#
# **판단기(qwen3)를 부르지 않는다.** 넷 중 하나를 고르는 데 2~5초를 쓸 이유가 없다. 낱말 규칙이
# 먼저 보고, 못 정하면 받아쓰기가 틀어진 말(`반망이 집어` · `고정못 때`)을 자모 단위로 넷의 본보기와
# 견줘 가장 가까운 것을 고른다. 그래도 멀면 아무것도 하지 않는다 — 엉뚱한 행동은 못 알아듣는 것보다 나쁘다.
#
# 게임도 같은 넷으로 좁힌다(`AiDemoCommands`). 이름은 그쪽과 같아야 한다 — 검사가 본다.

#: 켜 두면 워커가 `demo_decide` 만 쓴다. 끄면 예전처럼 규칙 → 판단기.
DEMO = True

DEMO_ACTIONS = ("defend_meteors", "revive", "pull_pin", "come")

#: 멈추라는 말에는 아무것도 하지 않는다 — 넷 중 어느 것도 멈춤이 아니다.
DEMO_STOP = re.compile(r"그만|멈춰|중단|취소|지\s?마")

#: (정규식, 행동). 위에서부터 처음 맞는 것 하나. **특정한 것이 먼저다** — `방망이 가져와` 의 "와" 는 오기가 아니다.
DEMO_RULES = [
    (r"운석|방망이|몽둥이|쳐\s?내|막아|방어", "defend_meteors"),
    (r"살려|일으켜|구해|기절|쓰러", "revive"),
    (r"못|뽑|빼", "pull_pin"),
    (r"이쪽|이리|여기|따라|모여|와", "come"),
]

#: 받아쓰기가 틀어졌을 때 견줄 본보기. 화면·안내에서 알려 주는 말과 같게 둔다.
DEMO_EXAMPLES = {
    "defend_meteors": ("방망이 주워서 운석 쳐내", "방망이 집어", "운석 막아", "운석 쳐내"),
    "revive": ("기절한 사람 살려", "쓰러진 사람 일으켜", "일으켜", "살려 줘"),
    "pull_pin": ("고정못 뽑아", "고정못 빼", "못 뽑아"),
    "come": ("이쪽으로 와", "이리 와", "여기로 와"),
}

#: 본보기와 이만큼 닮아야 고른다(자모 두 글자 묶음의 다이스 계수). 낮추면 잡담에도 움직인다.
DEMO_SIMILAR = .4

_DEMO_COMPILED = [(re.compile(p), a) for p, a in DEMO_RULES]
_LEADS = "ㄱㄲㄴㄷㄸㄹㅁㅂㅃㅅㅆㅇㅈㅉㅊㅋㅌㅍㅎ"
_VOWELS = "ㅏㅐㅑㅒㅓㅔㅕㅖㅗㅘㅙㅚㅛㅜㅝㅞㅟㅠㅡㅢㅣ"
_TAILS = " ㄱㄲㄳㄴㄵㄶㄷㄹㄺㄻㄼㄽㄾㄿㅀㅁㅂㅄㅅㅆㅇㅈㅊㅋㅌㅍㅎ"


def jamo(text):
    """한글 음절을 자모로 푼다. 공백·문장부호는 뺀다. `반망이` 와 `방망이` 가 한 글자만 다르게 된다."""
    out = []
    for c in text or "":
        code = ord(c) - 0xAC00
        if 0 <= code < 11172:
            out.append(_LEADS[code // 588]); out.append(_VOWELS[(code % 588) // 28])
            if code % 28: out.append(_TAILS[code % 28])
        elif c.isalnum():
            out.append(c)
    return "".join(out)


def similarity(a, b):
    """자모 두 글자 묶음의 다이스 계수. 0~1. **순수 함수**."""
    x, y = jamo(a), jamo(b)
    if len(x) < 2 or len(y) < 2:
        return 0.0
    grams = {}
    for i in range(len(x) - 1):
        grams[x[i:i + 2]] = grams.get(x[i:i + 2], 0) + 1
    common = 0
    for i in range(len(y) - 1):
        g = y[i:i + 2]
        if grams.get(g, 0) > 0:
            grams[g] -= 1; common += 1
    return 2.0 * common / ((len(x) - 1) + (len(y) - 1))


def demo_decide(text):
    """시연 명령의 계획. 넷 중 하나이거나 status=none. **판단기로 넘기지 않는다**. **순수 함수**."""
    said = (text or "").strip()
    none = {"status": "none", "orders": []}
    if not said or DEMO_STOP.search(said):
        return none
    action = next((a for p, a in _DEMO_COMPILED if p.search(said)), None)
    if action is None:
        best, score = None, 0.0
        for act, examples in DEMO_EXAMPLES.items():
            for example in examples:
                s = similarity(said, example)
                if s > score:
                    best, score = act, s
        if score >= DEMO_SIMILAR:
            action = best
    if action is None:
        return none
    return {"status": "ok", "orders": [{"recipients": recipient(said), "steps": [
        {"action": action, "approach": False, "destination": "default"}]}]}


def decide(text):
    """규칙으로 정한 계획, 또는 None(판단기로 넘긴다). **순수 함수**.

    돌려주는 꼴은 `companion_plan.validate` 가 받는 것과 같다 — 게임이 차이를 모른다.
    """
    said = (text or "").strip()
    if not said or HAND_OVER.search(said):
        return None
    if sum(word in said for word in COLORS) > 1:
        return None
    for pattern, act in _COMPILED:
        if pattern.search(said):
            action, _, destination = act.partition(":")
            return {"status": "ok", "orders": [{"recipients": recipient(said), "steps": [
                {"action": action, "approach": False, "destination": destination or "default"}]}]}
    return None
