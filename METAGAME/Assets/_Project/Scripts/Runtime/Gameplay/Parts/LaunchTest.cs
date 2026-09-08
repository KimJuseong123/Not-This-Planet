using System.Collections.Generic;
using UnityEngine;

namespace Metagame.Gameplay.Parts
{
    /// <summary>발사 하중 시험의 진행 상태.</summary>
    public enum LaunchState
    {
        /// <summary>아직 시작하지 않았다.</summary>
        Idle,

        /// <summary>엔진 출력을 올리는 중이다.</summary>
        Ramping,

        /// <summary>
        /// 하중 시험을 통과해 상승하는 중이다(기획 §22.5 "약 8~10초 동안 상승").
        ///
        /// 예전에는 <see cref="Success"/> 가 곧 끝이었고, <c>Tick</c> 첫 줄이
        /// <c>State != Ramping</c> 이면 돌아가므로 <b>추력이 그 자리에서 끊겼다.</b>
        /// 우주선은 <see cref="PhysicsScale.LaunchLiftOffHeight"/>(1.5m) 만 뜨고
        /// 발사대로 다시 떨어졌다.
        /// </summary>
        Ascending,

        /// <summary>5초를 견디고 떠올랐다.</summary>
        Success,

        /// <summary>버티지 못했다. 이유는 <see cref="LaunchTest.FailureReason"/> 에 있다.</summary>
        Failed,
    }

    /// <summary>
    /// 발사 하중 시험. 기획(S15P21B202-107 §22.3)의 "실제 하중 테스트" 다.
    ///
    /// <b>하중을 계산해서 접합부에 나눠 주지 않는다.</b> 최하단(주 추진 엔진 모듈)에 위로
    /// 추력을 주면, 그 위 접합부들은 자기가 지고 있는 질량과 가속만큼의 힘을 물리로 받는다.
    /// 비뚤게 쌓였으면 무게중심이 축에서 벗어나 토크까지 걸린다. 기획이 요구하는
    /// "실제 용접 길이와 비뚤어진 배치에서 생기는 하중" 이 계산식 없이 그대로 나온다.
    ///
    /// 접합부의 강도는 <see cref="PartAssemblyLink"/> 가 용접 비율로 정한다
    /// (미접합 8000 → 완전 용접 200000). 그래서 이 시험은 강도를 건드리지 않는다 —
    /// 추력만 주고 무엇이 끊어지는지 본다. 기획 §16.2 의 "파츠를 전부 쌓기만 하면 발사를
    /// 시도할 수 있으며 발사 하중으로 품질을 검증한다" 가 이 구조에서 성립한다.
    ///
    /// <b>무작위를 쓰지 않는다</b>(기획 §22.3). 추력 곡선은 시간에만 의존하고 난수가 없다.
    /// PhysX 가 비트 단위로 결정론적이지 않으므로 성공/실패라는 <b>결과</b> 수준에서
    /// 같은 결과를 보장한다 — S15P21B202-194 가 같은 기준으로 붕괴를 검증했다.
    ///
    /// 하중 시험을 통과하면 그대로 <see cref="LaunchState.Ascending"/> 로 넘어가 기획 §22.5
    /// 의 "약 8~10초 동안 상승" 을 이어서 돌린다. 상승을 다른 컴포넌트로 떼지 않은 이유는
    /// 추력을 주는 곳이 여기 하나여야 하기 때문이다 — 두 곳에서 같은 강체를 밀면 어느 쪽이
    /// 얼마를 주는지 알 수 없게 된다.
    ///
    /// 이 컴포넌트가 하지 않는 것: 3초 카운트다운, 문 열림과 탑승 조건(§22.1),
    /// 탑승 궤도 카메라(§22.2), 실패·성공 연출과 대기권 컷신·2장 로딩(§22.4 후반 · §22.5).
    /// 그쪽에서 <see cref="Begin"/> 을 부르면 된다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LaunchTest : MonoBehaviour
    {
        readonly List<SpaceshipPart> _tower = new();
        readonly List<PartAssemblyLink> _links = new();

        Rigidbody _engine;
        float _startY;
        float _peakThrust;

        /// <summary>탑 전체 질량. 상승에서 무게를 상쇄할 때 쓴다 — 엔진 하나가 아니라
        /// 탑 전체를 들어야 한다.</summary>
        float _towerMass;

        public LaunchState State { get; private set; } = LaunchState.Idle;

        /// <summary>엔진 출력. 0 에서 1 까지 <see cref="PhysicsScale.LaunchRampSeconds"/> 에 걸쳐 오른다.</summary>
        public float Output { get; private set; }

        /// <summary>시험을 시작한 뒤 흐른 시간.</summary>
        public float Elapsed { get; private set; }

        /// <summary>실패한 이유. 성공하거나 진행 중이면 빈 문자열이다.</summary>
        public string FailureReason { get; private set; } = "";

        /// <summary>상승을 시작한 뒤 흐른 시간. 상승 중이 아니면 0 이다.</summary>
        public float AscentElapsed { get; private set; }

        /// <summary>상승 진행도 0~1. 상승 중이 아니면 0 이다.</summary>
        public float Ascent01 => PhysicsScale.AscentSeconds <= 0f
            ? 0f
            : Mathf.Clamp01(AscentElapsed / PhysicsScale.AscentSeconds);

        /// <summary>
        /// 1장 완료. 대기권 컷신과 2장 로딩이 붙을 자리다(기획 §22.5).
        ///
        /// 이벤트로 둔 이유는 [Level]·[Network] 가 이 파일을 고치지 않고 붙을 수 있어야
        /// 하기 때문이다. 여기서 씬을 로드하면 그쪽 작업이 이 파일을 건드려야 한다.
        /// </summary>
        public event System.Action<LaunchTest> Completed;

        /// <summary>시험 대상이 된 파츠. 최하단부터 위로 정렬돼 있다.</summary>
        public IReadOnlyList<SpaceshipPart> Tower => _tower;

        /// <summary>최하단이 처음 자리에서 얼마나 올라왔는지(m).</summary>
        public float Altitude => _engine != null ? _engine.position.y - _startY : 0f;

        /// <summary>
        /// 시험을 시작한다. <paramref name="engine"/> 은 탑의 최하단 파츠다.
        ///
        /// 접합 그래프(<see cref="PartGraph"/>)를 훑어 탑과 접합부를 모은다. 호출자가 파츠
        /// 목록을 넘기게 하지 않는 이유는, 그러면 탑승 흐름이 파츠 순서를 따로 관리해야
        /// 하고 그 목록이 실제 접합과 어긋날 수 있기 때문이다.
        /// </summary>
        public void Begin(SpaceshipPart engine)
        {
            _tower.Clear();
            _links.Clear();
            Output = 0f;
            Elapsed = 0f;
            AscentElapsed = 0f;
            FailureReason = "";
            State = LaunchState.Idle;

            if (engine == null)
            {
                Fail("최하단 파츠가 없다");
                return;
            }

            PartGraph.Collect(engine, _tower);
            PartGraph.CollectLinks(_tower, _links);

            _engine = engine.GetComponent<Rigidbody>();
            if (_engine == null)
            {
                Fail("최하단 파츠에 Rigidbody 가 없다");
                return;
            }

            _startY = _engine.position.y;

            // 기획 §22.3 — "우주선이 자기 무게로 서 있는지 먼저 확인한다".
            // 이미 기울고 있으면 추력을 주기 전에 실패다. 추력이 넘어짐을 가려 버리면
            // 플레이어가 왜 실패했는지 알 수 없다.
            for (var i = 0; i < _tower.Count; i++)
            {
                var part = _tower[i];
                var tilt = Vector3.Angle(part.transform.up, Vector3.up);

                if (tilt > PhysicsScale.LaunchMaxStandingTilt)
                {
                    Fail($"{part.name} 이 {tilt:0}도 기울어 서 있지 못한다");
                    return;
                }
            }

            // 추력은 탑 전체 무게에 비례해 정한다. 파츠 질량이 바뀌어도 이륙 여부가
            // 뒤집히지 않게 하려는 것이다 — 절대값으로 두면 질량을 만질 때마다 다시 재야 한다.
            var mass = 0f;
            for (var i = 0; i < _tower.Count; i++)
            {
                var body = _tower[i].GetComponent<Rigidbody>();
                if (body != null) mass += body.mass;
            }

            _towerMass = mass;
            _peakThrust = mass * -Physics.gravity.y * PhysicsScale.LaunchThrustRatio;
            State = LaunchState.Ramping;
        }

        void FixedUpdate() => Tick(Time.fixedDeltaTime);

        /// <summary>
        /// 물리 스텝마다 부른다. 런타임에서는 <c>FixedUpdate</c> 가 부르고, EditMode
        /// 테스트는 <c>Physics.Simulate</c> 와 짝지어 직접 부른다.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0f) return;

            if (State == LaunchState.Ascending)
            {
                Ascend(deltaTime);
                return;
            }

            if (State != LaunchState.Ramping) return;

            Elapsed += deltaTime;
            Output = Mathf.Clamp01(Elapsed / PhysicsScale.LaunchRampSeconds);

            if (_engine == null)
            {
                Fail("최하단 파츠가 사라졌다");
                return;
            }

            // 추력. 무게중심에 주면 토크가 생기지 않으므로, 실제 엔진처럼
            // 최하단 파츠의 밑면 가운데에서 밀어 올린다. 탑이 비뚤면 여기서 토크가 나온다.
            var nozzle = _engine.worldCenterOfMass;
            nozzle.y = _engine.position.y - PhysicsScale.LaunchNozzleDrop;
            _engine.AddForceAtPosition(Vector3.up * (_peakThrust * Output), nozzle, ForceMode.Force);

            for (var i = 0; i < _links.Count; i++)
            {
                var link = _links[i];
                if (link == null || !link.IsIntact)
                {
                    // 기획 §22.4 — 약한 접합부부터 끊어진다. 끊어진 순간 실패로 확정하고
                    // 나머지 접합은 그대로 둔다. 붙은 덩어리로 남아 무너지는 것이 연출이다.
                    Fail("접합부가 하중을 버티지 못했다");
                    return;
                }
            }

            if (Elapsed < PhysicsScale.LaunchRampSeconds) return;
            if (Altitude < PhysicsScale.LaunchLiftOffHeight) return;

            PassLoadTest();
        }

        /// <summary>
        /// 성공 처리. 접합부를 봉인해 상승 연출 중에는 파손되지 않게 한다(기획 §22.3).
        ///
        /// <see cref="PartAssemblyLink.SealForLaunch"/> 를 쓰는 이유는, 링크가 매 <c>Tick</c>
        /// 마다 용접 비율로 강도를 다시 계산해 덮어쓰기 때문이다. 조인트의 breakForce 를
        /// 여기서 직접 올려도 다음 틱에 되돌려진다(S15P21B202-194 기록 참고).
        /// </summary>
        /// <summary>
        /// 하중 시험을 통과했다. 접합을 확정하고 상승으로 넘어간다.
        ///
        /// 여기서 끝내지 않는다 — 기획 §22.5 가 "성공 후 약 8~10초 동안 우주선이
        /// 상승한다" 다. 예전에는 이 자리에서 <c>Success</c> 로 바꿨고, 그러면 <c>Tick</c>
        /// 이 다음 스텝부터 아무것도 하지 않아 추력이 끊기고 우주선이 떨어졌다.
        /// </summary>
        void PassLoadTest()
        {
            // 하중 시험을 통과했으므로 접합을 무한 강도로 확정한다. 상승 중에 다시
            // 판정할 것이 없다 — 시험이 이미 품질을 판정했다(기획 §16.2).
            for (var i = 0; i < _links.Count; i++)
                if (_links[i] != null) _links[i].SealForLaunch();

            // 하중 시험 동안 쌓인 속도는 상승 연출의 속도 상한을 넘길 수 있다.
            // 행성 중력이 바뀌어도 시험의 충격량이 연출 속도로 새어 나오지 않게,
            // 시험을 통과하는 경계에서 탑 전체의 수직 속도만 상한으로 맞춘다.
            for (var i = 0; i < _tower.Count; i++)
            {
                var body = _tower[i] != null ? _tower[i].GetComponent<Rigidbody>() : null;
                if (body == null || body.linearVelocity.y <= PhysicsScale.AscentMaxSpeed) continue;

                var velocity = body.linearVelocity;
                velocity.y = PhysicsScale.AscentMaxSpeed;
                body.linearVelocity = velocity;
            }

            Output = 1f;
            AscentElapsed = 0f;
            State = LaunchState.Ascending;
        }

        /// <summary>
        /// 상승(기획 §22.5). <see cref="PhysicsScale.AscentSeconds"/> 동안 올라간다.
        ///
        /// <b>추력을 그냥 유지하면 안 된다.</b> 추력이 무게의
        /// <see cref="PhysicsScale.LaunchThrustRatio"/>(3.2배)이므로 순가속이 중력의
        /// 2.2배로 계속 붙는다. 9초면 60m/s 를 넘고 500m 이상 올라가서 카메라가 따라갈 수
        /// 없다. 기획 §22.5 는 상승 중에 카메라를 돌려 볼 수 있어야 한다고 하므로
        /// 상한 속도에서 추력을 줄인다.
        ///
        /// 상한에서 추력을 딱 끊지 않는 이유는 중력이 즉시 이겨 상승이 멈칫하기
        /// 때문이다. 무게를 상쇄하는 만큼은 남긴다.
        /// </summary>
        void Ascend(float deltaTime)
        {
            AscentElapsed += deltaTime;

            if (_engine == null)
            {
                Fail("상승 중 최하단 파츠가 사라졌다");
                return;
            }

            if (AscentElapsed >= PhysicsScale.AscentSeconds)
            {
                Finish();
                return;
            }

            // 엔진 질량이 아니라 탑 전체 질량으로 잰다. 엔진만 상쇄하면 위 세 파츠의
            // 무게가 그대로 남아 우주선이 떨어진다.
            var weight = _towerMass * -UnityEngine.Physics.gravity.y;
            var overspeed = _engine.linearVelocity.y - PhysicsScale.AscentMaxSpeed;

            // 무게 상쇄분 + 상한까지 남은 여유만큼. 상한을 넘으면 여유가 음수가 되어
            // 무게 상쇄분에서 깎인다 — 그래서 속도가 상한 근처에 머문다.
            var extra = -overspeed * PhysicsScale.AscentSpeedDamp;
            var thrust = Mathf.Max(0f, weight + _towerMass * extra);

            var nozzle = _engine.worldCenterOfMass;
            nozzle.y = _engine.position.y - PhysicsScale.LaunchNozzleDrop;
            _engine.AddForceAtPosition(Vector3.up * thrust, nozzle, ForceMode.Force);
        }

        /// <summary>
        /// 상승이 끝났다. 1장 완료다.
        ///
        /// <b>스테이지를 초기화하지 않는다.</b> 기획 §22.5 가 "별도의 장 사이 로비와 다시
        /// 준비하는 절차는 없다 … 참가자, 연결과 호스트 상태를 유지한 채 다음 장을
        /// 로딩한다" 고 정한다. 여기서는 상태와 이벤트만 남긴다 — 대기권 컷신과 2장
        /// 로딩은 [Level]·[Network] 의 몫이고, 붙일 자리가 있어야 한다.
        /// </summary>
        void Finish()
        {
            State = LaunchState.Success;
            Completed?.Invoke(this);
        }

        void Fail(string reason)
        {
            FailureReason = reason;
            State = LaunchState.Failed;
        }
    }
}
