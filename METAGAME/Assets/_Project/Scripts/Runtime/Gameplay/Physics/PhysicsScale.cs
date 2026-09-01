using Metagame.Core;

namespace Metagame.Gameplay
{
    /// <summary>
    /// 퍼즐·상호작용에 쓰는 물리 기준값. 1 unit = 1 meter.
    ///
    /// 세계와 캐릭터 값은 여기서 정의하지 않고 <see cref="Tuning"/> 을 그대로 쓴다.
    /// 중력·틱·몸무게가 두 곳에 따로 있으면 서로 어긋나기 때문이다.
    /// 이 클래스가 정의하는 것은 파츠와 견인 도구처럼 퍼즐 전용인 값뿐이다.
    ///
    /// 치수와 질량은 캐릭터를 기준자로 삼아 비율로 정했다.
    /// 캐릭터가 1.33m / 35kg 이므로 파츠는 그 2.8배 높이에 약 14배 질량이다.
    /// </summary>
    public static class PhysicsScale
    {
        // --- 단위 ---
        public const float MetersPerUnit = 1f;

        // --- 우주선 파츠 ---

        /// <summary>
        /// 형태를 따로 정하지 않은 파츠의 기본 크기. <b>실제 스테이지1 파츠는 이 값을 쓰지 않는다</b> —
        /// 네 파츠는 각자의 실측 형태를 <see cref="Parts.PartSpec"/> 에 갖고 있다.
        /// 이 값은 테스트가 만드는 일반 파츠와, 명세가 없는 파츠의 폴백으로만 남는다.
        /// </summary>
        public const float PartWidth = 3f;
        public const float PartHeight = 3.7f;

        /// <summary>
        /// 형태를 따로 정하지 않은 파츠의 질량. 위와 같은 이유로 폴백이다.
        /// 실제 파츠 질량은 <see cref="Parts.PartSpec.Mass"/> 가 부피에서 계산한다.
        /// </summary>
        public const float PartMass = 500f;

        /// <summary>
        /// 파츠 밀도(kg/m³). 파츠 질량을 결정하는 <b>유일한 손잡이</b>다.
        ///
        /// 질량을 파츠마다 손으로 적지 않는 이유는 두 가지다. 모델이 바뀌면 네 값을 따로
        /// 고쳐야 하고, 고치는 사이 서로의 비율이 슬그머니 어긋난다. 부피에 밀도를 곱하면
        /// 형태가 바뀔 때 질량이 저절로 따라온다.
        ///
        /// 70 은 네 파츠의 평균 질량이 예전 고정값 <see cref="PartMass"/>(500kg) 근처에
        /// 오도록 잡은 값이다. 기획서 용어로 <b>초기 테스트값</b>이며 플레이 테스트로 조정한다.
        /// </summary>
        public const float PartDensity = 70f;

        /// <summary>
        /// 견인 중인 파츠에 적용하는 취급 질량.
        ///
        /// 파츠는 견인 중에도 플레이어와 충돌한다. 동료를 툭 치며 방해하는 것이
        /// 협동 게임의 재미이기 때문이다. 다만 놓인 상태의 질량 그대로면 플레이어를
        /// 짓눌러 버리므로 견인 중에만 이 값으로 낮춘다. 내려놓는 순간 <see cref="PartMass"/> 로 돌린다.
        ///
        /// 사람 한 명 무게로 잡았다. 밀칠 수는 있지만 깔아뭉갤 수는 없는 값이다.
        /// 이보다 크게 낮추면 운반 속도로 부딪혀도 플레이어가 몇 cm 밀릴 뿐이라
        /// 방해가 성립하지 않는다. 충돌 위력이 질량에 비례하기 때문이다.
        ///
        /// "무거운 것을 빔으로 들면 가벼운 느낌"은 이 질량이 아니라 견인 빔의 제어 게인으로
        /// 만든다. 캐릭터의 손 잡기가 이미 같은 방식을 쓴다
        /// (<see cref="Tuning.CarryMassScale"/>, <see cref="Tuning.CarrySupport"/>).
        /// </summary>
        public const float CarriedPartMass = Tuning.BodyMass;

        /// <summary>
        /// 견인 중 대형 파츠의 회전 폭주를 막기 위한 per-Rigidbody 제한값.
        /// 전역 최대 각속도는 소품·운석까지 함께 묶이므로 건드리지 않고
        /// Part 프리팹의 Rigidbody.maxAngularVelocity 에만 적용한다.
        /// 견인 구현 티켓에서 실측 후 조정한다.
        /// </summary>
        public const float PartMaxAngularSpeed = 5f;

        // --- 견인 도구 ---
        /// <summary>비행접시형 운반 도구. 파츠에 끌려다니지 않을 정도의 무게.</summary>
        public const float CarrierMass = 25f;

        // --- 접합 허용 오차 ---
        //
        // 상세 기획 S15P21B202-107 §15.2 가 정한 세 값이다. 예전에는 소켓 사이의 3차원 거리
        // 하나(1.5m)와 각도 하나(30도)로 판정했는데, 거리 하나로는 "옆으로 어긋난 것" 과
        // "위아래로 뜬 것" 을 구분하지 못한다. 접합선은 옆으로 조금 어긋나도 이어지지만
        // 위아래로 벌어지면 이어지지 않으므로, 기획은 둘을 따로 본다.
        //
        // 기획서 용어로 <b>초기 테스트값</b>이며 플레이 테스트로 조정한다.

        /// <summary>
        /// 두 접합면 중심이 <b>옆으로</b> 어긋나도 되는 거리. 접합면을 따라 잰다.
        /// </summary>
        public const float WeldCenterTolerance = 0.8f;

        /// <summary>
        /// 두 접합면이 <b>위아래로</b> 벌어져도 되는 거리. 접합면 법선 방향으로 잰다.
        /// 이만큼 넘게 뜨면 원형 접합선이 이어지지 않는다.
        /// </summary>
        public const float WeldGapTolerance = 0.2f;

        /// <summary>
        /// 두 접합면이 마주보는 각도의 오차 한계.
        ///
        /// 기획은 "파츠 기울기 최대 약 25도" 라고 적었는데, 무엇을 기준으로 한 기울기인지는
        /// 적혀 있지 않다. 여기서는 <b>두 접합면 사이의 상대 각도</b>로 읽었다. 탑 전체가
        /// 같은 방향으로 기울어 있어도 두 면이 나란하면 접합선은 이어지기 때문이다.
        /// 탑이 통째로 기우는 것은 접합 판정이 아니라 물리가 다룰 문제다(넘어진다).
        /// </summary>
        public const float WeldTiltToleranceDegrees = 25f;

        /// <summary>
        /// 용접 전 파츠를 잡아 두는 조인트가 끊어지는 힘.
        ///
        /// 운석 충격에는 끊어지고 일반 조작에는 끊어지지 않아야 하는 값이다.
        /// S15P21B202-70 에서 운석을 실제로 떨어뜨려 확정했다.
        /// 60kg 운석이 30m 에서 떨어질 때 이 값은 끊어지고 WeldedBreakForce 는 버틴다.
        ///
        /// <b>"탑은 스스로 무너지지 않는다" 는 더 이상 사실이 아니다.</b> S15P21B202-70 은
        /// 파츠가 모두 500kg 이고 조립이 완벽히 정렬되던 때의 값이다. S15P21B202-166 이
        /// 질량을 406~745kg 으로 나누고 S15P21B202-172 가 정렬을 없앤 뒤로는 비뚤게 쌓은
        /// 탑이 스스로 무너진다 — 기획 §15.1 이 요구하는 동작이다.
        ///
        /// S15P21B202-194 에서 실측한 경계(층마다 옆으로 미는 거리):
        ///   0.30m 까지  선다
        ///   0.35m 부터  무너진다
        ///
        /// 붕괴는 이 강도가 아니라 <b>기하</b>가 결정한다. 토크 상한을 5000 에서 500 까지
        /// 낮춰도 경계가 움직이지 않았다 — 무게중심이 밑면을 벗어나면 넘어지고, 조인트는
        /// 그 결과로 끊어진다. 그래서 이 값들은 조정하지 않았다.
        /// 실측 근거는 docs/records/S15P21B202-194-tower-collapse.md 에 있다.
        /// </summary>
        public const float UnweldedBreakForce = 8000f;

        /// <summary>용접 전 조인트가 끊어지는 토크. 위 항목과 함께 S15P21B202-70 에서 확정했다.</summary>
        public const float UnweldedBreakTorque = 5000f;

        // --- 용접 ---
        //
        // S15P21B202-230 에서 원형 접합선 관련 값을 모두 걷어냈다. 접합선이 하던 일을
        // 비드가 하므로(S15P21B202-226 · 228 · 229) 시간·조각 수는 뜻을 잃었다.
        // <b>사거리만 남는다</b> — 그것은 접합선의 값이 아니라 용접기의 값이었다.

        /// <summary>
        /// 용접기가 몸체에 닿는다고 보는 거리(m).
        ///
        /// 기획 §16.1 "용접기 최대 사거리는 약 0.8m". <see cref="Metagame.Core.Tuning"/>
        /// 의 <c>ToolBeamRange</c> 가 이미 0.80m 이며 같은 값이어야 한다 — 빔이 닿아
        /// 보이는데 용접이 안 되거나 그 반대면 플레이어가 이유를 알 수 없다.
        ///
        /// 예전 이름은 <c>WeldSeamRange</c> 였다. 접합선이 사라져도 이 값은 남으므로
        /// 이름에서 접합선을 뗐다 — 처음부터 접합선의 값이 아니라 <b>도구</b>의 값이었다.
        /// </summary>
        public const float WeldRange = 0.80f;

        // --- 용접 비드 (기획 §16.2 의 "실제 금속 비드") ---
        /// <summary>
        /// 비드 덩어리 하나의 최대 반지름(m). 그림의 "상한선" 이다.
        ///
        /// 이 값에 이르면 그 덩어리는 더 자라지 않고 <b>다음 덩어리가 시작된다.</b>
        /// 상한이 없으면 콜라이더 하나가 파츠보다 커진다.
        ///
        /// 0.10m 은 파츠 폭(<see cref="PartWidth"/>) 의 1/20 쯤이라 눈에는 확실히 보이고
        /// 파츠 형태를 가리지는 않는 크기다.
        /// </summary>
        public const float WeldBeadMaxRadius = 0.10f;

        /// <summary>
        /// 갓 생긴 비드 덩어리의 반지름(m).
        ///
        /// 0 에서 시작하면 좌클릭한 순간 아무것도 보이지 않아 "되고 있는지" 를 알 수 없다.
        /// 기획 §16.2 가 진행 바를 금지하므로 <b>보이는 것 자체가 피드백이다</b> —
        /// 첫 프레임부터 작게라도 보여야 한다.
        /// </summary>
        public const float WeldBeadMinRadius = 0.03f;

        /// <summary>
        /// 덩어리 하나가 최소에서 최대까지 자라는 시간(초).
        ///
        /// 기획 §16.1 이 접합부 하나의 최단 용접 시간을 약 10초로 잡는다. 덩어리 하나에
        /// 1.6초면 사슬 여섯 칸을 채우는 데 약 10초라, 그 총량이 그대로 유지된다.
        ///
        /// 이보다 빠르면 <b>쌓는</b> 느낌이 사라진다 — 비드는 훑는 작업이 아니라 한 자리에
        /// 대고 있는 작업이므로, 덩어리가 커지는 것이 눈에 보일 만큼은 느려야 한다.
        /// </summary>
        public const float WeldBeadBlobSeconds = 1.6f;

        /// <summary>
        /// 한 사슬에 이어 붙는 덩어리 수 = <b>최대 다리 길이</b>.
        ///
        /// 6개 × <see cref="WeldBeadStep"/> 0.13m + 반지름 0.10m ≈ 0.75m 를 뻗는다.
        /// 용접기 사거리(<see cref="WeldRange"/> 0.80m)보다 짧게 둔 것이 의도다 —
        /// 길이 상한이 사거리보다 길면 플레이어가 자기 비드를 따라가지 못해 끝을
        /// 놓치고, 왜 멈췄는지 알 수 없다.
        /// </summary>
        public const int WeldBeadBlobsPerChain = 6;

        /// <summary>덩어리 중심 사이 간격(m). 지름(0.20m)보다 좁아 서로 겹쳐 기둥으로 보인다.</summary>
        public const float WeldBeadStep = 0.13f;

        /// <summary>
        /// 사슬을 이어 붙일지 새로 시작할지 가르는 거리(m).
        ///
        /// 노즐이 기존 사슬의 끝에서 이 거리 안에 있으면 <b>그 사슬을 잇는다.</b>
        /// 밖이면 새 사슬을 시작한다. 이 값이 없으면 좌클릭할 때마다 새 사슬이 생겨
        /// 기둥이 자라지 않는다.
        /// </summary>
        public const float WeldBeadChainReuseRadius = 0.35f;

        /// <summary>
        /// 파츠 하나가 가질 수 있는 총 덩어리 수.
        ///
        /// 성능 상한이다. 콜라이더가 무한히 늘면 파츠의 컴파운드 콜라이더가 그만큼
        /// 무거워진다. 48개 = 사슬 8개 × 6개.
        /// </summary>
        public const int WeldBeadsPerPart = 48;

        /// <summary>
        /// 최대 크기 덩어리 하나의 질량(kg). 작은 덩어리는 부피비(반지름의 세제곱)로 준다.
        ///
        /// 강철 밀도(7,800kg/m³)를 그대로 쓰면 덩어리 하나가 33kg 이라 48개가 1.6톤 —
        /// 파츠(406~745kg)보다 무거워져 탑이 비드 무게로 무너진다. 비드는 속이 찬
        /// 강철 덩어리가 아니라 <b>덧붙인 살</b> 이므로 훨씬 가볍게 둔다.
        ///
        /// 4kg × 48 = 192kg 이면 가장 가벼운 파츠의 절반이 안 되고, 그러면서도
        /// 한쪽에만 잔뜩 쌓으면 무게중심이 눈에 보이게 치우친다.
        /// </summary>
        public const float WeldBeadFullMass = 4f;

        /// <summary>
        /// 비드가 다른 몸체에 <b>닿았다</b>고 보는 여유(m).
        ///
        /// 0 이면 콜라이더가 정확히 맞닿아야 하는데, 조인트가 걸리기 전의 두 파츠는
        /// 언제나 조금씩 떨리므로 닿았다 말았다 한다. 그러면 접합이 붙었다 풀렸다 한다.
        /// </summary>
        public const float WeldBeadContactSkin = 0.02f;

        /// <summary>
        /// 접합이 <b>완전 용접 강도</b>에 이르는 데 필요한, 상대 몸체에 닿아 있는
        /// 최대 크기 덩어리의 수.
        ///
        /// <b>기둥 길이가 아니라 닿아 있는 부피로 센다.</b> 6칸 기둥 하나가 2칸 기둥
        /// 하나보다 튼튼하면 물리적으로 거꾸로다 — 얇고 긴 다리는 약해야 한다. 긴 기둥은
        /// 끝만 닿으므로 저절로 한 덩어리 몫이 된다.
        ///
        /// 사슬이 여럿이면 <b>더해진다.</b> 그래서 한 자리에 몰아 쏘는 것으로는 3 을
        /// 채울 수 없고 여러 군데를 이어야 한다 — 기획 §16.1 의 "원형 접합부 전체를
        /// 따라간다" 를 비드 방식으로 되살린 것이 이 값이다.
        ///
        /// 3 은 기획서 용어로 <b>초기 테스트값</b>이며 플레이 테스트로 조정한다.
        /// </summary>
        public const int WeldBeadFullStrengthBlobs = 3;

        // --- 바닥 고정못 (기획 §12.1 · §12.2) ---
        /// <summary>
        /// 한 명이 고정못 하나를 뽑는 데 걸리는 시간(초).
        ///
        /// 기획 §12.2 의 "일반 1명 약 8초" 다. 예전에는 고정못이 홀드 1.5초로 풀렸고
        /// §12.2 의 어느 값도 아니었다 — 5배 넘게 짧아서 §12.2 의 "인원이 모이면
        /// 빨라진다" 라는 협동 설계가 아예 드러나지 않았다.
        /// </summary>
        public const float ClampPullSeconds = 2f;

        /// <summary>
        /// 두 번째 사람이 붙었을 때 늘어나는 속도 배수.
        ///
        /// 기획 §12.2 는 1명 8초 · 2명 약 4.5초다. 8 / 4.5 = 1.78 이므로 사람이 하나
        /// 늘 때마다 0.78 배씩 더해진다 — 8 / 1.78 = 4.49초.
        ///
        /// <b>1/n 이 아니다.</b> 그러면 2명이 4.0초가 되어 기획표와 어긋난다. 둘이
        /// 붙으면 서로 방해도 되기 때문에 정확히 절반이 되지 않는 것이 의도로 보인다.
        /// </summary>
        public const float ClampPullAssist = 0.78f;

        /// <summary>
        /// 한 고정못에 동시에 붙을 수 있는 인원. 기획 §12.2 "같은 고정못은 최대 2명".
        ///
        /// 3~4명은 한 곳에 몰리는 대신 다른 방향 고정못을 병렬로 뽑는 것이 §12.2 의
        /// 의도다. 멀티 동기화는 [Network] 몫이라 지금 실제로 도는 것은 1명 경로다.
        /// </summary>
        public const int ClampMaxPullers = 2;

        /// <summary>
        /// 뒤로 당기는 것으로 인정하는 최소 방향 일치도.
        ///
        /// 고정못에서 플레이어를 향하는 수평 방향과 이동 입력의 내적이다. 0.5 는 약
        /// 60도까지 허용한다 — 기획 §12.1 "고정못 반대 방향으로 뒤로 이동해 뽑는다" 를
        /// 정확히 180도로만 받으면 조작이 지나치게 까다롭다.
        ///
        /// <b>제자리에서 버튼만 누르면 진행되지 않는다.</b> 예전 방식(마우스를 위로 16도)
        /// 은 한 번 들어올린 뒤에는 가만히 있어도 시간이 흘렀다.
        /// </summary>
        public const float ClampPullAlignment = 0.5f;

        /// <summary>
        /// 마지막 고정못이 빠질 때 플레이어가 뒤로 넘어지는 속도(m/s).
        ///
        /// 기획 §12.2 "마지막 고정못이 빠지면 플레이어가 뒤로 넘어질 수 있다".
        /// 뒤로 당기던 힘이 갑자기 풀리는 것이므로 뒤쪽으로만 준다.
        /// 넘어짐 자체는 <c>PlayerRig.Balance</c> 가 이미 스스로 세운다.
        /// </summary>
        public const float ClampSnapBackSpeed = 3.5f;

        /// <summary>
        /// 고정못을 당기는 동안 허용하는 뒤쪽 이동 속도(m/s).
        ///
        /// 기획 §12.1 은 "몸을 뒤로 젖히고 발을 구르며 낑낑대는" 그림이다 — 뒤로 밀지만
        /// 거의 제자리다. 고정못이 몸을 붙들고 있기 때문이다.
        ///
        /// 이것이 없으면 뒤로 이동 입력이 그대로 보행이 되어 <b>고정못을 잡은 채 걸어서
        /// 멀어지면서 계속 뽑힌다.</b> 8초면 20m 넘게 가고, 놓은 뒤에는 다시 잡을 수도
        /// 없다. 0 이 아니라 조금 남기는 것은 완전히 붙들면 조작이 얼어붙은 느낌이
        /// 나기 때문이다.
        /// </summary>
        public const float ClampPullTetherSpeed = 0.3f;

        // --- 견인 조종 모듈 (기획 §14.1 · §14.2) ---
        /// <summary>
        /// 가속 이동(Shift) 최고 수평 속도(m/s). 기획 §14.1 "가속 12m/s".
        /// 일반은 8m/s 이며 <see cref="TransportVehicle"/> 이 갖고 있다.
        /// </summary>
        public const float CraftBoostSpeed = 12f;

        /// <summary>
        /// 파츠를 견인하는 동안 최고 속도에 곱하는 값. 기획 §14.1 "약 20% 감소".
        /// </summary>
        public const float CraftTowSpeedFactor = 0.8f;

        /// <summary>
        /// 무인 호버에서 고도를 다시 잡는 데 허용하는 어긋남(m).
        ///
        /// 기획 §14.2 "수평은 복구하지만 호버 위치를 자동으로 되찾지는 않는다".
        /// 밀려난 만큼은 그대로 두고 <b>그 자리에서</b> 다시 뜬다 — 그래서 밀린 높이가
        /// 이 값을 넘으면 목표 고도를 지금 높이로 옮긴다. 되돌리면 기획과 어긋난다.
        /// </summary>
        public const float CraftHoverDriftBand = 0.5f;

        // --- 기절과 동료 구조 (기획 §26-13) ---
        /// <summary>
        /// 다운한 동료를 일으키는 데 걸리는 시간(초).
        ///
        /// 고정못(8초)보다 짧다. 운석이 계속 떨어지는 중에 구조자가 8초를 서 있으면
        /// 둘 다 쓰러진다 — 구조가 성립하지 않는다. 용접 한 조각(0.83초)보다는 길어야
        /// 위험을 감수하는 판단이 생긴다.
        /// </summary>
        public const float RescueSeconds = 4f;

        /// <summary>
        /// 구조가 끝났을 때 돌려주는 체력 비율.
        ///
        /// 가득 채우면 다운이 벌이 되지 않고, 너무 적으면 일으켜 놓자마자 다시
        /// 쓰러진다. 절반이면 다음 운석 두 발까지는 버틴다(운석 직격 25).
        /// </summary>
        public const float RescueHealthRatio = 0.5f;

        /// <summary>
        /// 전원이 다운했을 때 스폰 지점에서 되살아나기까지의 시간(초).
        ///
        /// 기획 §26-13 "4인에서 전원 기절 실패가 긴장감을 주되 과도한 반복을 만들지
        /// 않는가". 아무도 일으킬 수 없으므로 출구가 필요하다 — 없으면 게임이 멈춘다.
        /// 예전에는 체력 0에서 즉시 부활하는 것이 우연히 그 역할을 했다.
        ///
        /// 파츠·용접·운석 흔적은 그대로 둔다. <b>스테이지 초기화가 아니다</b>
        /// (§22.4 의 발사 실패와 같은 원칙).
        /// </summary>
        public const float AllDownedRespawnSeconds = 8f;


        /// <summary>
        /// 용접이 끝난 접합이 버티는 힘. 미접합의 25배다.
        ///
        /// 무한으로 두지 않았다. 기획서가 "운석 충격에도 쉽게 분리되지 않는다" 라고
        /// 표현하고 있어, 극단적인 충격에는 떨어질 여지를 남기는 편이 의도에 맞다.
        /// </summary>
        public const float WeldedBreakForce = 200000f;

        /// <summary>용접이 끝난 접합이 버티는 토크.</summary>
        public const float WeldedBreakTorque = 150000f;

        // --- 발사 하중 시험 ---
        //
        // 기획 S15P21B202-107 §22.3 이다. 하중을 계산해 접합부에 나눠 주지 않고, 최하단에
        // 추력을 주어 위 접합부가 물리로 하중을 받게 한다. 비뚤게 쌓였으면 무게중심이
        // 축에서 벗어나 토크까지 걸린다 — 기획이 요구하는 "비뚤어진 배치에서 생기는 하중" 이
        // 계산식 없이 나온다. 전부 기획서 용어로 초기 테스트값이다.

        /// <summary>엔진 출력이 0에서 100%까지 오르는 시간(s). 기획의 "5초 동안".</summary>
        public const float LaunchRampSeconds = 5f;

        /// <summary>전원이 탑승한 뒤 발사까지의 카운트다운(s). 기획 §22.3 의 "3초".</summary>
        public const float LaunchCountdownSeconds = 3f;

        /// <summary>
        /// 발사 구역 반경(m).
        ///
        /// 기획 §8.1 은 "엔진 모듈 전체가 원형 구역 안에 들어오면 그 위에 조립할 수 있다" 고
        /// 한다. Stage1SkinBuilder 가 조립 위치 주변 키아웃 반경으로 9m 를 쓰고 있어 그에
        /// 맞췄다 — 두 값이 다르면 "자연물이 없는데 조립이 안 되는" 자리가 생긴다.
        /// </summary>
        public const float LaunchPadRadius = 9f;

        /// <summary>
        /// 탑승을 허용하는 최대 기울기(도). 기획 §22.1 "우주선이 탑승 가능한 정도로 서 있다".
        ///
        /// 발사 시험의 <see cref="LaunchMaxStandingTilt"/>(12도)보다 너그럽게 둔다. 탑승은
        /// 되지만 하중 시험에서 떨어지는 구간이 있어야 §16.2 의 "발사 하중으로 품질을
        /// 검증한다" 가 성립한다. 탑승 조건이 더 엄하면 시험이 판정할 것이 없다.
        /// </summary>
        public const float BoardingMaxTilt = 20f;

        /// <summary>
        /// 100% 출력에서의 추력을 탑 전체 무게의 몇 배로 줄지.
        ///
        /// 절대값으로 두지 않는 이유는 파츠 질량이 바뀌면 이륙 여부가 뒤집히기 때문이다.
        /// 무게에 비례시키면 <see cref="PartDensity"/> 를 만져도 시험의 성격이 유지된다.
        /// </summary>
        public const float LaunchThrustRatio = 3.2f;

        /// <summary>
        /// 추력을 주는 지점을 최하단 무게중심보다 이만큼 아래로 내린다.
        /// 무게중심에 주면 토크가 전혀 생기지 않아 비뚤어진 배치가 하중에 드러나지 않는다.
        /// </summary>
        public const float LaunchNozzleDrop = 1.0f;

        /// <summary>이 높이만큼 올라오면 "지면에서 완전히 떠올랐다" 로 본다(m).</summary>
        public const float LaunchLiftOffHeight = 1.5f;

        /// <summary>
        /// 시험을 시작할 때 허용하는 기울기(도).
        /// 이보다 기울어 있으면 추력을 주기 전에 실패다 — 기획 §22.3 "우주선이 자기 무게로
        /// 서 있는지 먼저 확인한다". 추력이 넘어짐을 가려 버리면 실패 이유를 알 수 없다.
        /// </summary>
        public const float LaunchMaxStandingTilt = 12f;

        /// <summary>
        /// 하중 시험을 통과한 뒤 상승하는 시간(초). 기획 §22.5 "약 8~10초" 의 가운데다.
        ///
        /// 이 시간이 끝나면 1장 완료다. 컷신과 2장 로딩이 붙기 전에는 임시 완료 처리로
        /// 대체한다(기획 §22.5).
        /// </summary>
        public const float AscentSeconds = 9f;

        /// <summary>
        /// 상승 속도의 상한(m/s).
        ///
        /// 추력이 무게의 <see cref="LaunchThrustRatio"/>(3.2배)이므로 그냥 두면 순가속이
        /// 중력의 2.2배로 계속 붙는다. 9초면 60m/s 를 넘고 500m 이상 올라간다 — 카메라가
        /// 따라갈 수 없고 지형이 순식간에 사라진다. 기획 §22.5 는 상승 중에 카메라를
        /// 돌려 볼 수 있어야 한다고 하므로 볼 수 있는 속도로 묶는다.
        ///
        /// 9초 × 12m/s ≈ 108m — 절벽(32기)과 배경 행성이 보이는 높이다.
        /// </summary>
        public const float AscentMaxSpeed = 12f;

        /// <summary>
        /// 상한에 이른 뒤 추력을 줄이는 세기.
        ///
        /// 추력을 딱 끊으면 중력이 즉시 이겨 상승이 멈칫한다. 상한 근처에서 부드럽게
        /// 줄여 무게를 상쇄하는 만큼만 남긴다.
        /// </summary>
        public const float AscentSpeedDamp = 2f;

        /// <summary>
        /// 발사 실패로 튀어나올 때의 수평 속도(m/s).
        ///
        /// 기획 §22.4 는 "사방으로 튀어나온다 … 잠시 구르지만 기절하지 않고 자동으로
        /// 일어난다" 다. 무너진 파츠 더미 밖으로 나갈 만큼은 되어야 하고, 지형 밖으로
        /// 날려 보낼 만큼이면 안 된다. 파츠 최대 발자국이 약 2.5m 이므로 그 두 배쯤
        /// 나가는 값으로 잡았다.
        /// </summary>
        public const float EjectHorizontalSpeed = 6f;

        /// <summary>튀어나올 때의 상승 속도(m/s). 잔해를 넘어가되 높이 뜨지는 않는다.</summary>
        public const float EjectVerticalSpeed = 3f;

        /// <summary>
        /// 실패 판정에서 최상단 모듈이 "지면에 닿았다" 고 볼 높이(m).
        ///
        /// 접촉 콜백을 쓰지 않는 이유는 무너지는 중에 파츠끼리도 닿기 때문이다.
        /// 지면 높이로 보면 무엇에 닿았는지 가릴 필요가 없다.
        ///
        /// <b>이것만으로는 부족하다.</b> 최상단 모듈이 잔해 위에 얹혀 멈추면 지면 높이에
        /// 영원히 닿지 않는다 — 견인 조종 모듈 반높이 1.22m 위에 엔진 모듈 2.16m 가
        /// 깔리면 밑면이 2.16m 에서 멈춘다. 그래서 <see cref="EjectRestSpeed"/> 로
        /// "낙하가 끝났다" 도 함께 본다.
        /// </summary>
        public const float EjectGroundClearance = 1.2f;

        /// <summary>
        /// 낙하가 끝났다고 볼 속도(m/s). 어디에 얹혀 멈췄든 여기에 걸린다.
        ///
        /// 기획 §22.4 의 "해당 모듈이 지면에 닿는 순간" 이 뜻하는 것은 낙하가 끝나는
        /// 순간이다. 잔해 위에 멈춘 것도 그 순간이다.
        /// </summary>
        public const float EjectRestSpeed = 0.6f;

        /// <summary>
        /// 그 속도가 이만큼 이어져야 멈춘 것으로 본다(초).
        ///
        /// 한 순간만 보면 튀어오른 정점에서 걸린다 — 거기서는 속도가 0 을 지난다.
        /// </summary>
        public const float EjectRestSeconds = 0.25f;

        // --- 견인 광선 ---
        /// <summary>빔이 파츠를 붙잡을 수 있는 최대 거리.</summary>
        public const float BeamRange = 12f;

        /// <summary>붙잡은 파츠를 빔 기준점 앞 몇 미터에 두는지.</summary>
        public const float BeamHoldDistance = 6f;

        /// <summary>
        /// 목표점까지의 거리에 곱하는 가속 계수(1/s²). 스프링의 k 에 해당한다.
        /// </summary>
        public const float BeamAccel = 25f;

        /// <summary>
        /// 속도에 곱하는 감쇠 계수(1/s). 스프링-댐퍼가 임계 감쇠가 되는 조건은 d = 2*sqrt(k) 이고
        /// sqrt(25) * 2 = 10 이다. 이 값을 낮추면 파츠가 목표점을 지나쳐 출렁인다.
        /// </summary>
        public const float BeamDamp = 10f;

        /// <summary>
        /// 가속 상한(m/s²). 중력 보상분(2.8)보다 충분히 커야 파츠가 처지지 않는다.
        /// </summary>
        public const float BeamAccelMax = 30f;

        /// <summary>
        /// 중력을 얼마나 대신 들어주는지. 1 이면 완전 무중력처럼 뜬다.
        /// 빔을 놓으면 보상이 사라져 그대로 떨어진다.
        /// </summary>
        public const float BeamGravitySupport = 1f;

        /// <summary>
        /// 목표 회전으로 돌리는 각속도 계수(1/s). 결과는 PartMaxAngularSpeed 로 잘린다.
        /// </summary>
        public const float BeamAngularGain = 6f;

        // --- 광역 저중력장 ---
        //
        // 상세 기획 S15P21B202-107 §14.3 이 정한 원뿔이다. 기체 아래 깊이 10m, 지면 반경 4m,
        // 기체 쪽이 좁고 지면 쪽이 넓다. 전부 기획서 용어로 초기 테스트값이다.

        /// <summary>원뿔 깊이(m). 이보다 깊으면 영역 밖이다.</summary>
        public const float FieldDepth = 10f;

        /// <summary>가장 깊은 곳에서의 반경(m).</summary>
        public const float FieldGroundRadius = 4f;

        /// <summary>
        /// 꼭짓점(기체 바로 아래)에서의 반경(m). 0 으로 두면 기체에 붙은 파츠가 영역에서
        /// 빠져 덜컥거리므로 약간 벌려 둔다.
        /// </summary>
        public const float FieldCraftRadius = 0.8f;

        /// <summary>파츠가 유지하는 깊이(m). 기획의 "기체 아래 약 5~6m".</summary>
        public const float FieldHoldDepth = 5.5f;

        /// <summary>
        /// 중력을 얼마나 대신 들어주는지. 1 이면 완전 무중력이다.
        /// 빔의 <see cref="BeamGravitySupport"/> 와 같은 뜻이며 값도 맞춰 둔다.
        /// </summary>
        public const float FieldGravitySupport = 1f;

        /// <summary>
        /// 원뿔 축으로 모으는 가속(m/s²). 기획은 "영역 중심으로 <b>천천히</b> 끌린다" 고 하므로
        /// 목표 깊이로 당기는 힘보다 훨씬 약하다. 세게 주면 자석처럼 달라붙어
        /// 정밀 조립이 불가능해진다.
        /// </summary>
        public const float FieldPullAccel = 1.6f;

        /// <summary>축으로 모으는 힘의 감쇠 계수(1/s). 없으면 축을 지나쳐 좌우로 출렁인다.</summary>
        public const float FieldPullDamp = 1.2f;

        // 부유하는 플레이어의 조작값(기획 §14.3). 파츠와 달리 힘으로 끌지 않고 속도를 직접
        // 준다 — 기획이 "강제 견인되지 않고 직접 움직인다" 고 정했기 때문이다.

        /// <summary>부유 중 수평 최고 속도(m/s).</summary>
        public const float FieldFloatSpeed = 3f;

        /// <summary>부유 중 상승·하강 속도(m/s).</summary>
        public const float FieldFloatVerticalSpeed = 2f;

        /// <summary>입력을 놓고 멈추기까지의 시간(s). 기획의 "약 0.5초 관성 후 정지".</summary>
        public const float FieldFloatStopSeconds = 0.5f;

        /// <summary>
        /// 저중력장이 자유 파츠의 바닥을 아래로 돌려 세우는 데 걸리는 시간(s).
        /// 기획 §14.4 의 "약 2초에 걸쳐 바닥이 아래를 향하도록 자세를 보정한다".
        ///
        /// S15P21B202-172 가 없앤 자동 정렬과 다르다. 172 가 금지한 것은 접합할 때 소켓에
        /// 맞춰 자세를 교정하는 것이고, 이것은 자유 파츠를 세우는 것이다. 기획이 §15.1 에서
        /// 앞을 금지하고 §14.4 에서 뒤를 요구한다. 요(y축 회전)는 건드리지 않는다 —
        /// 어느 방향을 보고 놓을지는 플레이어가 정한다.
        /// </summary>
        public const float FieldUprightSeconds = 2f;

        /// <summary>이 각도 안이면 이미 선 것으로 본다.</summary>
        public const float FieldUprightToleranceDegrees = 0.5f;

        /// <summary>
        /// 세우는 각속도(도/초). 90도를 <see cref="FieldUprightSeconds"/> 에 도는 속도다.
        ///
        /// 남은 각도에 비례하는 속도를 주면 지수적으로 접근해 목표에 영영 닿지 않는다
        /// (계측: 75도에서 3초 뒤에도 27.7도 남음). 일정한 속도로 돌리고 마지막에만
        /// 남은 만큼 줄인다.
        /// </summary>
        public const float FieldUprightDegreesPerSecond = 90f / FieldUprightSeconds;

        /// <summary>목표 높이까지의 오차에 곱하는 가속 계수(1/s²). 스프링의 k 다.</summary>
        public const float FieldHoldAccel = 6f;

        /// <summary>
        /// 세로 속도에 곱하는 감쇠 계수(1/s). 임계 감쇠 조건은 2*sqrt(k) = 2*sqrt(6) ≈ 4.9 인데,
        /// 그보다 조금 낮춰 급가속 때 파츠가 뒤처졌다 따라붙는 여운을 남긴다(기획 §14.3).
        /// </summary>
        public const float FieldHoldDamp = 4f;

        /// <summary>가속 상한(m/s²). 중력 보상분(2.8)보다 충분히 커야 파츠가 처지지 않는다.</summary>
        public const float FieldAccelMax = 18f;

        /// <summary>
        /// 저중력장이 작용하는 레이어.
        ///
        /// 파츠와 소품(뽑힌 고정못·장비)에만 준다. 운석은 기획이 명시적으로 제외한다 —
        /// "낙하 중·반사된 운석과 박힌 운석은 저중력장 영향을 받지 않는다"(§14.3).
        /// 플레이어도 지금은 빼 둔다. 부유는 강제 견인이 아니라 직접 조작이라
        /// 별도 이슈에서 다룬다.
        /// </summary>
        public const int FieldMask = GameLayers.PartMask | GameLayers.PropMask;

        // --- 운석 ---
        /// <summary>
        /// 운석 질량. 이 값이 이 시스템의 조정 대상이다.
        ///
        /// 착탄 충격이 미접합 조인트(UnweldedBreakForce)는 끊고 용접된 조인트
        /// (WeldedBreakForce)는 끊지 못하는 구간에 들어와야 한다.
        /// 계산으로는 정할 수 없다. 접촉 지속 시간에 따라 실제로 걸리는 힘이 달라지므로
        /// 미접합 탑과 용접 탑에 실제로 떨어뜨려 스윕으로 잡았다.
        ///
        /// 낙하 고도 30m, 저중력 -2.8 기준 실측 결과:
        ///   20 ~ 140kg  미접합만 끊어진다 (의도한 구간)
        ///   150kg 이상  용접한 접합까지 끊어진다
        /// 안전 구간의 중앙 부근인 60kg 으로 정했다. 위아래로 여유가 있다.
        ///
        /// 실측 방법은 S15P21B202-70 기록 문서에 있다.
        /// </summary>
        public const float MeteorMass = 60f;

        /// <summary>작은 운석의 반지름. 파츠(3m 폭)보다 확실히 작아야 "작은 운석" 으로 읽힌다.</summary>
        public const float MeteorRadius = 0.45f;

        /// <summary>스폰 고도. 저중력에서 이 높이를 떨어지면 착탄 속도가 정해진다.</summary>
        public const float MeteorDropHeight = 30f;

        /// <summary>
        /// 낙하 예고가 켜져 있는 시간(초). 기획 §17.2 "착탄 3초 전".
        ///
        /// <b>이 시간 동안 운석이 실제로 떨어지고 있어야 한다.</b> 예전에는 1.8초였고,
        /// 예고가 <b>끝난 뒤</b>에 운석을 30m 위에 만들었다. 중력 −2.8m/s² 에서 30m
        /// 자유낙하는 4.63초이므로 표시가 착탄 4.63초 <b>전에 사라졌다</b> — 기획과
        /// 정반대였고, S15P21B202-233 이 방망이를 만들며 드러났다(§18.1 이 "타격
        /// 가능한 순간에는 낙하 표시가 강하게 점멸한다" 고 정하는데 그 순간에는 표시가
        /// 없었다).
        ///
        /// 지금은 예고와 함께 운석을 만들고 <see cref="MeteorDropSpeed"/> 로 아래로
        /// 밀어, 예고가 끝나는 순간 착탄하게 맞춘다.
        /// </summary>
        public const float MeteorWarningSeconds = 3f;

        /// <summary>
        /// 운석을 만들 때 주는 초기 하강 속도(m/s).
        ///
        /// <see cref="MeteorDropHeight"/>(30m)에서 <see cref="MeteorWarningSeconds"/>(3초)에
        /// 닿게 하는 값이다. h = v₀·t + ½·|g|·t² 를 v₀ 로 풀면
        ///
        ///   v₀ = (30 − ½ × 2.8 × 3²) / 3 = (30 − 12.6) / 3 = 5.8
        ///
        /// <b>높이를 낮추는 쪽을 고르지 않았다.</b> 3초에 자유낙하로 닿는 높이는 12.6m 인데,
        /// 기획 §17.1 "하늘에서 낙하한다" 와 §17.2 "화면 밖 운석은 방향성 낙하음으로
        /// 인지한다" 가 높이를 요구한다. 속도를 주는 편이 그 둘을 지킨다.
        /// </summary>
        public const float MeteorDropSpeed = 5.8f;

        /// <summary>
        /// 소형 운석의 넉백 반경(m). 기획 §17.7 "소형 · 충격 반경 약 2m".
        ///
        /// <b>예전에는 크기 구분 없이 4m 하나였다</b> — §17.7 의 소형(2m)보다 두 배 넓고
        /// 중형(3.5m)보다도 넓어, 기획 어느 값도 아니었다. S15P21B202-234 에서 나눴다.
        /// </summary>
        public const float MeteorSmallImpactRadius = 2f;

        /// <summary>
        /// 중형 운석의 넉백 반경(m). 기획 §17.7 "중형 · 충격 반경 약 3.5m".
        /// 10분 이후에만 나온다.
        /// </summary>
        public const float MeteorMediumImpactRadius = 3.5f;

        /// <summary>착탄 시 주변에 주는 폭발력.</summary>
        public const float MeteorImpactForce = 900f;

        /// <summary>착탄 후 사라지기까지의 시간.</summary>
        public const float MeteorDespawnSeconds = 6f;

        // --- 운석 방망이 (기획 §18) ---
        /// <summary>
        /// 착탄 몇 초 전부터 쳐낼 수 있는지.
        ///
        /// 기획 §18.1 "운석 착탄 약 1초 전부터 타격 가능하다".
        ///
        /// <b>이 값을 쓰려면 착탄 시각을 알아야 한다.</b> 낙하 예고
        /// (<see cref="Hazards.PendingMeteorDrop"/>)는 운석이 <b>생기기 전</b>의 시간이고,
        /// 운석은 예고가 끝난 뒤 30m 위에서 생겨 4.63초를 더 떨어진다. 그래서
        /// <see cref="Hazards.Meteor.TryPredictImpact"/> 가 위치·속도·중력으로 직접 푼다.
        /// </summary>
        public const float BatSwingWindowSeconds = 1f;

        /// <summary>
        /// 낙하 지점에서 이만큼 안에 서 있어야 쳐낼 수 있다(m).
        ///
        /// 기획 §18.1 "플레이어가 낙하 지점 반경 약 2.5m 안에 있어야 한다".
        /// 소형 운석의 충격 반경(<see cref="MeteorSmallImpactRadius"/> 2m)보다 조금 넓고
        /// 중형(<see cref="MeteorMediumImpactRadius"/> 3.5m)보다는 좁다 — 맞을 자리에
        /// 서야 쳐낼 수 있고, 그것이 §18 이 만드는 긴장이다. 중형은 쳐내지 못한 채
        /// 맞을 수도 있는 자리가 생긴다.
        ///
        /// <b>S15P21B202-233 의 이 주석은 틀려 있었다.</b> "충격 반경 2m" 라고 적었지만
        /// 당시 상수는 크기 구분 없이 4m 하나여서, 실제로는 이 값(2.5m)이 충격 반경보다
        /// <b>좁았다</b>. 기획 §17.7 을 보고 코드 값을 확인하지 않은 것이다.
        /// S15P21B202-234 에서 상수를 §17.7 의 두 값으로 나누며 함께 고쳤다.
        ///
        /// <b>방망이 메시가 운석에 닿는지는 보지 않는다.</b> 기획이 "실제 방망이 메시가
        /// 운석과 정확히 닿지 않아도 넓은 에너지 궤적으로 판정을 납득시킨다" 고 정한다.
        /// </summary>
        public const float BatAssistRadius = 2.5f;

        /// <summary>
        /// 헛스윙 뒤 다시 휘두를 수 없는 시간(초). 기획 §18.1 "약 1초".
        ///
        /// <b>맞혔을 때는 쿨다운이 없다.</b> 기획이 헛스윙에만 대기를 두었고, 맞히면
        /// 목표가 사라지므로 연타할 것이 남지 않는다.
        /// </summary>
        public const float BatMissCooldownSeconds = 1f;

        /// <summary>
        /// 쳐낸 운석의 속도(m/s).
        ///
        /// 30m 에서 떨어진 운석의 착탄 속도가 sqrt(2 x 2.8 x 30) = 12.96m/s 다. 그보다
        /// 조금 빠르게 잡아, 쳐낸 운석이 되돌아오지 않고 멀리 날아가게 했다.
        /// </summary>
        public const float BatDeflectSpeed = 14f;

        /// <summary>
        /// 쳐낸 운석이 수평에서 들리는 각도(도).
        ///
        /// 기획 §18.2 "카메라의 수평 방향에 약간의 위쪽 각도를 더해 결정한다",
        /// "카메라가 지나치게 위나 아래를 보더라도 수직 각도는 자동 보정한다".
        /// <b>보정을 따로 만들지 않았다</b> — 조준의 수평 성분만 쓰고 이 각도를 더하면
        /// 위를 보든 아래를 보든 결과가 같다. 규칙을 두 번 쓰지 않는 편이 어긋나지 않는다.
        /// </summary>
        public const float BatLaunchDegrees = 30f;

        /// <summary>
        /// 한 번 휘두르는 데 걸리는 시간(초).
        ///
        /// <b>짧아야 한다.</b> 기획 §18.1 이 "완벽한 프레임, 등급과 치명타 판정은 없다"
        /// 고 정하므로 동작이 판정을 좌우하면 안 된다. 타격 창(<see cref="BatSwingWindowSeconds"/>
        /// 1초)의 3분의 1쯤이면, 창 안에서 클릭한 스윙은 창이 닫히기 전에 끝난다.
        /// </summary>
        public const float BatSwingSeconds = 0.28f;

        /// <summary>스윙이 시작될 때 도구가 조준선보다 위로 들리는 각도(도).</summary>
        public const float BatSwingUpDegrees = 55f;

        /// <summary>
        /// 스윙이 끝날 때의 각도(도). 음수면 조준선보다 아래다.
        ///
        /// 위에서 앞아래로 내려찍는 궤적이다. 야구 스윙처럼 수평으로 돌리지 않는 이유는
        /// 목표가 <b>떨어지는 운석</b>이라서다 — 위에서 내려오는 것을 아래에서 위로 걷어
        /// 올리는 것이 §18.2 의 "약간의 위쪽 각도를 더해" 와도 맞는다.
        /// </summary>
        public const float BatSwingDownDegrees = -35f;

        /// <summary>
        /// 방망이가 닿는 거리(m). 동료(기획 §18.3)와 소품에 같이 쓴다.
        ///
        /// 예전 이름은 <c>BatPlayerReach</c> 였다. 사거리는 <b>도구의 성질</b>이지 맞는
        /// 쪽의 성질이 아니다 — 소품이 대상에 들어오면서(S15P21B202-245) 이름이 사실과
        /// 어긋났다. 동료와 소품이 다른 거리에서 맞으면 플레이어가 두 값을 배워야 한다.
        ///
        /// 운석은 이 값을 쓰지 않는다. 운석은 <see cref="BatAssistRadius"/>(2.5m)로
        /// <b>낙하 지점</b>까지의 거리를 재기 때문이다 — 공중에 있는 것을 팔 길이로
        /// 재면 맞을 수 없다.
        /// </summary>
        public const float BatStrikeReach = 2f;

        /// <summary>
        /// 소품을 밀치는 충격량(N·s). 기획에 없는 값이다(S15P21B202-245).
        ///
        /// <b>속도가 아니라 충격량이다.</b> 소품은 질량이 제각각인데(<c>LevelBuilder</c> 가
        /// 개별로 받는다) 속도를 대입하면 무거운 상자와 가벼운 조각이 똑같이 날아간다.
        /// 충격량은 질량이 나누므로 무거운 것이 덜 움직인다 — 방망이로 <b>밀친다</b> 는
        /// 감각이 그것이다.
        ///
        /// 60kg 운석이 <see cref="BatDeflectSpeed"/>(14m/s)로 날아가는 것과 비슷한 크기로
        /// 잡았다. 20kg 짜리 상자면 초속 4m 쯤이다.
        /// </summary>
        public const float BatPropImpulse = 80f;

        /// <summary>
        /// 직접 맞은 동료가 밀려나는 속도(m/s).
        ///
        /// 마지막 고정못이 빠질 때(<see cref="ClampSnapBackSpeed"/> 3.5m/s)와 비슷하게
        /// 잡았다. 기획 §18.3 이 "넘어지지만 기절하지는 않는다" 고 정하므로 그 정도면 된다.
        /// </summary>
        public const float BatPlayerKnockSpeed = 4f;

        /// <summary>
        /// 착탄 여부와 무관한 수명 상한.
        ///
        /// 착탄 처리는 물리 충돌 콜백에 달려 있는데, 운석이 플레이 공간 밖으로 떨어지거나
        /// 콜백을 놓치면 영구히 남는다. 상한을 두어 어떤 경우에도 사라지게 한다.
        /// 낙하 시간(30m, 저중력에서 약 4.6초)보다 충분히 길어야 한다.
        /// </summary>
        public const float MeteorMaxLifeSeconds = 20f;

        // --- 운석 난이도 곡선 ---
        /// <summary>초반 낙하 간격. 조작과 핵심 루프를 익힐 여유를 준다.</summary>
        public const float MeteorIntervalStart = 8f;

        /// <summary>후반 낙하 간격.</summary>
        public const float MeteorIntervalMin = 2f;

        /// <summary>
        /// 간격이 최소까지 줄어드는 데 걸리는 시간.
        /// 1장의 목표 플레이 시간이 미정이라 임시값이다. 기획 확정 후 맞춘다.
        /// </summary>
        public const float MeteorRampSeconds = 180f;

        // --- 운석 공세 (기획 §17.3~17.6) ---
        /// <summary>
        /// 공세가 시작되기 몇 초 전에 신호를 보내는지. 기획 §17.3 "공세 약 5초 전".
        ///
        /// 신호만 내보낸다 — 유성우가 많아지고 진동음이 울리고 조명이 깜빡이는 것은
        /// [Art]·[Sound] 가 만든다. <b>문구나 카운트다운 숫자는 표시하지 않는다</b>(§17.3).
        /// </summary>
        public const float MeteorWaveLeadSeconds = 5f;

        /// <summary>공세 안에서 운석 사이의 최소 간격(초). 기획 §17.4 "1~2초 간격".</summary>
        public const float MeteorWithinWaveMinSeconds = 1f;

        /// <summary>공세 안에서 운석 사이의 최대 간격(초).</summary>
        public const float MeteorWithinWaveMaxSeconds = 2f;

        /// <summary>
        /// 플레이어를 노릴 때 그 사람에게서 떨어뜨리는 최소 거리(m).
        ///
        /// 기획 §17.5 "무작위 플레이어 주변 1~3m", "플레이어의 정확한 위치에 강제로
        /// 명중시키지 않는다". 0 으로 두면 피할 수 없는 공격이 되어, §3.3 의 "실패는
        /// 대부분 복구 가능하게 만든다" 와 어긋난다.
        /// </summary>
        public const float MeteorPlayerTargetMinRadius = 1f;

        /// <summary>플레이어를 노릴 때의 최대 거리(m).</summary>
        public const float MeteorPlayerTargetMaxRadius = 3f;

        /// <summary>
        /// 같은 자리를 연속으로 고르지 않기 위한 최소 이동 거리(m).
        ///
        /// 기획 §17.5 "동일한 플레이어나 위치를 연속 선택하지 않는다". 거리로 재는 이유는
        /// 좌표가 실수여서 "같은 위치" 를 등호로 판정할 수 없기 때문이다.
        /// </summary>
        public const float MeteorTargetRepeatDistance = 4f;

        // --- 전역 물리 설정 기록값 (ProjectSettings/DynamicsManager.asset) ---
        // Unity 6 기본값이 이미 목표치 이상이라 변경하지 않았다. 회귀 감시용으로만 기록한다.
        public const int SolverIterations = 14;
        public const int SolverVelocityIterations = 4;
        public const float MaxAngularSpeed = 28f;
    }
}
