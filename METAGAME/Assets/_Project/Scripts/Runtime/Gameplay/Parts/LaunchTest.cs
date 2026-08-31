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
    /// 이 컴포넌트가 하지 않는 것: 3초 카운트다운, 문 열림과 탑승 조건(§22.1),
    /// 탑승 궤도 카메라(§22.2), 실패·성공 연출(§22.4 후반 · §22.5).
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

        public LaunchState State { get; private set; } = LaunchState.Idle;

        /// <summary>엔진 출력. 0 에서 1 까지 <see cref="PhysicsScale.LaunchRampSeconds"/> 에 걸쳐 오른다.</summary>
        public float Output { get; private set; }

        /// <summary>시험을 시작한 뒤 흐른 시간.</summary>
        public float Elapsed { get; private set; }

        /// <summary>실패한 이유. 성공하거나 진행 중이면 빈 문자열이다.</summary>
        public string FailureReason { get; private set; } = "";

        /// <summary>시험 대상이 된 파츠. 최하단부터 위로 정렬돼 있다.</summary>
        public IReadOnlyList<SpaceshipPart> Tower => _tower;

        /// <summary>최하단이 처음 자리에서 얼마나 올라왔는지(m).</summary>
        public float Altitude => _engine != null ? _engine.position.y - _startY : 0f;

        /// <summary>
        /// 시험을 시작한다. <paramref name="engine"/> 은 탑의 최하단 파츠다.
        ///
        /// 소켓 사슬(<see cref="PartSocket.ConnectedTo"/>)을 따라 위로 올라가며 탑을 모은다.
        /// 호출자가 파츠 목록을 넘기게 하지 않는 이유는, 그러면 탑승 흐름이 파츠 순서를
        /// 따로 관리해야 하고 그 목록이 실제 접합과 어긋날 수 있기 때문이다.
        /// </summary>
        public void Begin(SpaceshipPart engine)
        {
            _tower.Clear();
            _links.Clear();
            Output = 0f;
            Elapsed = 0f;
            FailureReason = "";
            State = LaunchState.Idle;

            if (engine == null)
            {
                Fail("최하단 파츠가 없다");
                return;
            }

            CollectTower(engine);

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

            _peakThrust = mass * -Physics.gravity.y * PhysicsScale.LaunchThrustRatio;
            State = LaunchState.Ramping;
        }

        /// <summary>
        /// 최하단부터 소켓 사슬을 따라 위로 올라가며 탑과 접합부를 모은다.
        /// 접합부는 위에 올라가는 파츠 쪽에 붙어 있다(<see cref="PartAssembler"/>).
        /// </summary>
        void CollectTower(SpaceshipPart engine)
        {
            var current = engine;

            while (current != null)
            {
                _tower.Add(current);

                PartSocket top = null;
                var sockets = current.Sockets;
                for (var i = 0; i < sockets.Count; i++)
                    if (sockets[i] != null && sockets[i].Role == SocketRole.Top)
                        top = sockets[i];

                var next = top != null && top.ConnectedTo != null ? top.ConnectedTo.Owner : null;
                if (next == null || _tower.Contains(next)) break;

                var link = next.GetComponent<PartAssemblyLink>();
                if (link != null) _links.Add(link);

                current = next;
            }
        }

        void FixedUpdate() => Tick(Time.fixedDeltaTime);

        /// <summary>
        /// 물리 스텝마다 부른다. 런타임에서는 <c>FixedUpdate</c> 가 부르고, EditMode
        /// 테스트는 <c>Physics.Simulate</c> 와 짝지어 직접 부른다.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (State != LaunchState.Ramping || deltaTime <= 0f) return;

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

            Succeed();
        }

        /// <summary>
        /// 성공 처리. 접합부를 봉인해 상승 연출 중에는 파손되지 않게 한다(기획 §22.3).
        ///
        /// <see cref="PartAssemblyLink.SealForLaunch"/> 를 쓰는 이유는, 링크가 매 <c>Tick</c>
        /// 마다 용접 비율로 강도를 다시 계산해 덮어쓰기 때문이다. 조인트의 breakForce 를
        /// 여기서 직접 올려도 다음 틱에 되돌려진다(S15P21B202-194 기록 참고).
        /// </summary>
        void Succeed()
        {
            for (var i = 0; i < _links.Count; i++)
                if (_links[i] != null) _links[i].SealForLaunch();

            Output = 1f;
            State = LaunchState.Success;
        }

        void Fail(string reason)
        {
            FailureReason = reason;
            State = LaunchState.Failed;
        }
    }
}
