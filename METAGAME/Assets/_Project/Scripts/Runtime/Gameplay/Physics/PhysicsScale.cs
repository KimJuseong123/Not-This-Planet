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
        /// <summary>용접점 하나를 채우는 데 걸리는 시간.</summary>
        /// <summary>
        /// 예전 용접점 하나를 채우는 시간. 원형 접합선으로 바뀐 뒤에는 쓰지 않는다.
        /// </summary>
        [System.Obsolete("원형 접합선으로 전환했다. WeldSeamSeconds 와 WeldSeamSegments 를 쓴다.")]
        public const float WeldSecondsPerPoint = 3f;

        /// <summary>
        /// 원형 접합선 하나를 <b>빠짐없이 훑었을 때</b>의 용접 시간(초).
        ///
        /// 기획 §16.1 "원형 접합부 하나의 최단 용접 시간은 약 10초, 일반적인 완료 시간은
        /// 12~15초를 목표로 한다". 여기 적는 것이 <b>최단</b> 시간이다 — 12~15초는
        /// 겹쳐 대거나 빠뜨렸다가 되돌아오는 실제 플레이에서 저절로 나온다.
        ///
        /// 예전 방식(용접점 4개 × 3초 = 12초)과 총량이 비슷하다. 바뀌는 것은 총량이
        /// 아니라 <b>기제</b> 다 — 한 점에 대고 있으면 되던 것이 이제는 돌아야 한다.
        /// </summary>
        public const float WeldSeamSeconds = 10f;

        /// <summary>
        /// 원형 접합선을 몇 조각으로 나눌지.
        ///
        /// 조각 하나가 30도다. 이 값이 §16.1 의 "같은 위치에 계속 대고 있어도 추가
        /// 진행되지 않는다" 를 만든다 — 조각이 차면 그 자리에서는 더 오르지 않는다.
        ///
        /// 너무 적으면(4개) 예전 점 용접과 다를 바 없고, 너무 많으면(72개) 한 조각이
        /// 0.14초라 훑는 손맛이 사라진다.
        /// </summary>
        public const int WeldSeamSegments = 12;

        /// <summary>
        /// 조각 하나를 채우는 시간(초). 12조각 × 0.833초 ≈ 10초.
        /// </summary>
        public static float WeldSeamSecondsPerSegment => WeldSeamSeconds / WeldSeamSegments;

        /// <summary>
        /// 용접기가 접합선에 닿는다고 보는 거리(m).
        ///
        /// 기획 §16.1 "용접기 최대 사거리는 약 0.8m". <see cref="Metagame.Core.Tuning"/>
        /// 의 <c>ToolBeamRange</c> 가 이미 0.80m 이며 같은 값이어야 한다 — 빔이 닿아
        /// 보이는데 용접이 안 되거나 그 반대면 플레이어가 이유를 알 수 없다.
        /// </summary>
        public const float WeldSeamRange = 0.80f;

        /// <summary>
        /// 원형 접합선 하나를 <b>빠짐없이 훑었을 때</b>의 완전 절단 시간(초).
        ///
        /// 기획 §16.3 "완전 절단의 최단 시간은 약 6초, 일반적인 시간은 8~10초이다".
        /// 여기 적는 것이 <b>최단</b> 시간이다 — 8~10초는 겹쳐 대거나 빠뜨렸다가
        /// 되돌아오는 실제 플레이에서 나온다(용접과 같은 이유로 상수로 넣지 않는다).
        ///
        /// 용접(10초)보다 빠른 것이 기획 의도다. 부수는 것이 만드는 것보다 쉬워야
        /// 실수를 되돌리는 부담이 작다.
        /// </summary>
        public const float CutSeamSeconds = 6f;

        /// <summary>조각 하나를 자르는 시간(초). 12조각 × 0.5초 = 6초.</summary>
        public static float CutSeamSecondsPerSegment => CutSeamSeconds / WeldSeamSegments;


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
        /// 예고부터 낙하까지의 시간. 플레이어가 한 걸음 비킬 수 있어야 한다.
        /// 기획의 미정 사항이던 "피할 수 있는 반응 시간" 을 이 값으로 정했다.
        /// </summary>
        public const float MeteorWarningSeconds = 1.8f;

        /// <summary>착탄 시 넉백이 미치는 반경.</summary>
        public const float MeteorImpactRadius = 4f;

        /// <summary>착탄 시 주변에 주는 폭발력.</summary>
        public const float MeteorImpactForce = 900f;

        /// <summary>착탄 후 사라지기까지의 시간.</summary>
        public const float MeteorDespawnSeconds = 6f;

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

        // --- 전역 물리 설정 기록값 (ProjectSettings/DynamicsManager.asset) ---
        // Unity 6 기본값이 이미 목표치 이상이라 변경하지 않았다. 회귀 감시용으로만 기록한다.
        public const int SolverIterations = 14;
        public const int SolverVelocityIterations = 4;
        public const float MaxAngularSpeed = 28f;
    }
}
