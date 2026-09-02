using System;
using System.Collections.Generic;
using UnityEngine;

namespace Metagame.Gameplay.Parts
{
    /// <summary>
    /// 맞물린 두 파츠를 잡고 있는 조인트 하나를 관리한다.
    /// 조립할 때 위에 올라가는 파츠 쪽에 붙는다. 조인트와 같은 오브젝트에 있어야
    /// <c>OnJointBreak</c> 를 받을 수 있다.
    ///
    /// 조인트가 끊어지면 소켓 맞물림을 풀어 그 자리에 다시 쌓을 수 있게 한다.
    /// 끊어졌다는 사실을 두 경로로 확인한다.
    ///
    ///  1) <c>OnJointBreak</c> — 런타임에서 즉시 온다.
    ///  2) <see cref="Tick"/> 의 조인트 생존 확인 — 콜백이 오지 않는 경우의 안전망이다.
    ///     EditMode 의 Physics.Simulate 는 물리 콜백을 보내지 않으므로 테스트도 이 경로를 쓴다.
    ///
    /// <b>파츠 하나에 접합이 여럿일 수 있다</b>(S15P21B202-229). 비드로 옆에도 이을 수
    /// 있으므로 아래 하나로는 표현되지 않는다. 그래서 예전의
    /// <c>DisallowMultipleComponent</c> 를 떼었다.
    ///
    /// 뗄 수 있었던 근거는 위의 두 경로다. <c>OnJointBreak</c> 는 조인트와 같은
    /// 오브젝트에 있어야 오지만, 그것이 <b>유일한 경로가 아니다</b> — 조인트가 끊어지면
    /// Unity 가 Joint 컴포넌트를 지우므로 <see cref="Tick"/> 이 다음 틱에 잡는다.
    /// 콜백은 즉시성일 뿐이고, 정확성은 안전망이 갖고 있다.
    ///
    /// <b>쌍마다 하나</b>라는 규칙은 남는다. 두 파츠 사이에 접합이 둘이면 강도가 두 배가
    /// 되고 하중 시험이 같은 자리를 두 번 센다. <see cref="FindBetween"/> 이 그것을 막는다.
    /// </summary>
    public sealed class PartAssemblyLink : MonoBehaviour
    {
        /// <summary>
        /// 살아 있는 접합 전부. <see cref="PartSocket.All"/> · <see cref="WeldBead.All"/> 과
        /// 같은 방식이다.
        ///
        /// 목록이 필요한 이유는 접합이 <b>두 파츠 중 한쪽에만</b> 붙어 있기 때문이다.
        /// 파츠에서 <c>GetComponents</c> 로는 자기가 가진 접합만 보이고, 상대가 가진
        /// 접합은 보이지 않는다 — 그러면 그래프의 변 절반을 잃는다.
        ///
        /// EditMode 에서는 <c>OnEnable</c> 이 호출되지 않으므로 <see cref="Bind"/> ·
        /// <see cref="BindBeadWeld"/> 가 직접 등록한다.
        /// </summary>
        public static readonly List<PartAssemblyLink> All = new();

        // 직렬화한다. 씬이나 프리팹으로 저장된 조립 상태가 로드된 뒤에도 유지되어야 한다.
        // 저장하지 않으면 로드 직후 _joint 가 null 로 보여 Tick 이 곧바로 맞물림을 풀어 버린다.
        [SerializeField] PartSocket _movingSocket;
        [SerializeField] PartSocket _targetSocket;
        [SerializeField] Joint _joint;

        /// <summary>
        /// 이어진 두 파츠. <see cref="_movingPart"/> 가 조인트를 가진 쪽이다.
        ///
        /// <b>소켓에서 파츠를 다시 찾지 않고 여기 둔다.</b> 비드로 이은 접합에는 소켓이
        /// 없기 때문이다(S15P21B202-228). 예전에는 <c>_movingSocket.Owner</c> 로 찾았고,
        /// 그러면 소켓 없는 접합에서 파츠를 잃어 용접이 끝나도 상태가 바뀌지 않는다.
        /// </summary>
        [SerializeField] SpaceshipPart _movingPart;

        [SerializeField] SpaceshipPart _targetPart;

        /// <summary>
        /// 비드가 만든 강도 0~1. 원형 접합선의 진행분과 <b>더해진다</b>(S15P21B202-228).
        ///
        /// 직렬화한다. 비드는 GameObject 로 씬에 남으므로 로드 뒤에도 강도가 유지되어야
        /// 한다 — 저장하지 않으면 용접한 탑이 로드 직후 미접합 강도로 돌아가 충격에
        /// 떨어진다(접합선 진행분을 저장하는 것과 같은 이유다).
        /// </summary>
        [SerializeField] float _beadRatio;

        bool _released;

        /// <summary>발사 성공으로 봉인되었는가. 봉인되면 강도를 다시 계산하지 않는다.</summary>
        bool _sealed;

        /// <summary>맞물림이 풀렸을 때. 조인트가 끊어진 경우와 직접 해제한 경우 모두 포함한다.</summary>
        public event Action<PartAssemblyLink> Released;

        public PartSocket MovingSocket => _movingSocket;

        public PartSocket TargetSocket => _targetSocket;

        /// <summary>조인트를 가진 쪽의 파츠.</summary>
        public SpaceshipPart MovingPart => _movingPart;

        /// <summary><c>connectedBody</c> 쪽의 파츠.</summary>
        public SpaceshipPart TargetPart => _targetPart;

        /// <summary>소켓으로 맞물린 접합인가. 비드로 이은 접합은 소켓이 없다.</summary>
        public bool HasSockets => _movingSocket != null && _targetSocket != null;

        public bool IsIntact => !_released && _joint != null;

        /// <summary>
        /// 두 파츠를 잇는 접합. 없으면 null.
        ///
        /// 순서를 가리지 않는다. 소켓 조립은 링크를 <b>위에 올라가는</b> 파츠에 붙이므로
        /// 비드가 아래에서 위로 자란 경우 소유자와 방향이 뒤집힌다 — 그때 같은 쌍을
        /// 못 찾으면 이미 있는 접합 위에 조인트를 하나 더 만든다.
        /// </summary>
        public static PartAssemblyLink FindBetween(SpaceshipPart a, SpaceshipPart b)
        {
            if (a == null || b == null) return null;

            for (var i = 0; i < All.Count; i++)
            {
                var link = All[i];
                if (link == null || link._released) continue;

                if ((link._movingPart == a && link._targetPart == b) ||
                    (link._movingPart == b && link._targetPart == a))
                    return link;
            }

            return null;
        }

        /// <summary>
        /// <paramref name="part"/> 가 아래쪽 소켓으로 맞물려 만든 접합. 없으면 null.
        ///
        /// <b>비드 보강과 구분해야 하는 자리다.</b> 파츠에 접합이 여럿일 수 있게 된 뒤
        /// (S15P21B202-229) <c>GetComponent&lt;PartAssemblyLink&gt;</c> 는 "어느 접합?" 이
        /// 되었다. 원형 접합선과 재조립은 <b>소켓으로 맞물린 그 접합</b>만 봐야 한다 —
        /// 옆에 비드로 이은 것을 함께 풀면 남의 접합이 사라진다.
        /// </summary>
        public static PartAssemblyLink SocketLinkOf(SpaceshipPart part)
        {
            if (part == null) return null;

            for (var i = 0; i < All.Count; i++)
            {
                var link = All[i];
                if (link == null || link._released) continue;
                if (link._movingPart == part && link.HasSockets) return link;
            }

            return null;
        }

        /// <summary>
        /// <paramref name="part"/> 에 이어진 접합 전부를 <paramref name="into"/> 에 담는다.
        /// 자기가 가진 것과 상대가 가진 것을 모두 본다.
        /// </summary>
        public static void CollectLinksOf(SpaceshipPart part, List<PartAssemblyLink> into)
        {
            if (part == null || into == null) return;

            for (var i = 0; i < All.Count; i++)
            {
                var link = All[i];
                if (link == null || link._released || !link.IsIntact) continue;
                if (link._movingPart != part && link._targetPart != part) continue;

                if (!into.Contains(link)) into.Add(link);
            }
        }

        /// <summary>이 접합의 반대쪽 파츠. <paramref name="from"/> 이 이 접합에 없으면 null.</summary>
        public SpaceshipPart Across(SpaceshipPart from)
        {
            if (from == _movingPart) return _targetPart;
            if (from == _targetPart) return _movingPart;
            return null;
        }

        /// <summary>
        /// 용접 완료 비율 0~1. 아무것도 잇지 않았으면 0 이다.
        ///
        /// 완료 여부가 아니라 비율을 쓴다. 절반만 용접했으면 절반만 튼튼해야 운석이
        /// 떨어질 때 용접하지 않은 층이 무너지는 긴장이 생긴다.
        ///
        /// 기획 §16.2 "처음 조립하는 접합부의 강도는 실제로 용접한 길이에 비례한다" —
        /// 비드 방식에서는 <b>닿아 있는 비드의 부피</b>가 그 길이를 대신한다
        /// (S15P21B202-228).
        /// </summary>
        public float WeldRatio => Mathf.Clamp01(_beadRatio);

        /// <summary>
        /// 비드가 만든 강도. <see cref="WeldBead"/> 가 접촉 부피에서 계산해 넘긴다.
        ///
        /// S15P21B202-230 에서 원형 접합선이 사라진 뒤 <see cref="WeldRatio"/> 와 같은
        /// 값이다. 이름을 남겨 둔 이유는 <b>어디서 온 강도인지</b>를 읽는 쪽에서 알 수
        /// 있게 하려는 것이다 — 나중에 다른 출처가 생기면 여기서 갈린다.
        /// </summary>
        public float BeadWeldRatio => _beadRatio;

        /// <summary>
        /// 비드 강도를 갈아 끼운다. <see cref="WeldBead"/> 만 부른다.
        ///
        /// 누적하지 않고 <b>갈아 끼운다</b> — 비드를 깎으면 내려가야 하므로, 부르는 쪽이
        /// 지금 닿아 있는 양을 그대로 다시 계산해 넘기는 편이 어긋나지 않는다.
        /// </summary>
        public void SetBeadWeldRatio(float ratio)
        {
            var next = Mathf.Clamp01(ratio);
            if (Mathf.Approximately(next, _beadRatio)) return;

            _beadRatio = next;
            if (_joint != null && !_sealed) ApplyJointStrength();
        }

        public bool IsFullyWelded => WeldRatio >= 1f;

        /// <summary>
        /// 두 접합면 사이의 간격(m). 축 방향 거리다.
        ///
        /// 기획 §16.1 "파츠가 움직여 접합면 간격이 벌어지면 작업이 중단된다" 의 판정에
        /// 쓰였다. S15P21B202-230 에서 원형 접합선이 사라진 뒤 <b>읽는 곳이 없다</b> —
        /// 비드는 벌어지는 것을 간격이 아니라 <b>비드가 끊어지는 것</b>으로 표현한다.
        ///
        /// 그래도 지우지 않는다. 소켓 두 개를 가진 곳이 여기뿐이고, "두 접합면이 얼마나
        /// 벌어져 있는가" 는 조립 상태를 진단하는 값이다(QA 도구가 쓸 자리다).
        /// </summary>
        public float Gap
        {
            get
            {
                // 비드로 이은 접합에는 "접합면 간격" 이라는 개념이 없다(S15P21B202-228).
                // 벌어지는 것은 비드가 끊어지는 것으로 표현되므로 간격으로 중단시킬 것이
                // 없다 — 0 을 준다. 예전에는 여기서 float.MaxValue 를 줬는데, 그러면
                // 비드 접합에서 §16.1 의 간격 판정이 언제나 실패해 용접을 아예 못 한다.
                if (!HasSockets) return _joint != null ? 0f : float.MaxValue;

                var normal = _targetSocket.transform.forward;
                var delta = _movingSocket.transform.position - _targetSocket.transform.position;
                return Mathf.Abs(Vector3.Dot(delta, normal));
            }
        }

        /// <summary>
        /// 상호작용 없이 용접을 끝낸다. 레벨 초기 상태 구성과 검증에서 쓴다.
        ///
        /// <b>"용접을 끝낸다" 를 표현하는 방법은 이것 하나다.</b> 예전에는 검증마다
        /// 용접점을 모아 하나씩 <c>ForceComplete</c> 했고, 그래서 표현을 원형 접합선으로
        /// 바꿀 때 여덟 군데를 고쳐야 했다.
        ///
        /// <b>그 선택이 이번에 값을 했다.</b> S15P21B202-230 이 접합선을 걷어낼 때
        /// 이 메서드의 <b>안</b>만 바뀌었고, 이것을 부르는 다섯 스위트
        /// (<c>LaunchTestTests</c> · <c>MeteorTests</c> · <c>ShipAscentTests</c> ·
        /// <c>ShipRideTests</c> · <c>TowerCollapseTests</c>)는 한 줄도 고치지 않았다.
        /// 그 스위트들은 용접을 검증하는 것이 아니라 "완전히 용접된 탑" 이라는 상태를
        /// 필요로 할 뿐이다.
        /// </summary>
        public void ForceWeldComplete() => SetBeadWeldRatio(1f);

        /// <summary>용접이 모두 끝나 접합이 확정되었을 때.</summary>
        public event Action<PartAssemblyLink> Welded;

        public void Bind(PartSocket movingSocket, PartSocket targetSocket, Joint joint)
        {
            _movingSocket = movingSocket;
            _targetSocket = targetSocket;
            _movingPart = movingSocket != null ? movingSocket.Owner : null;
            _targetPart = targetSocket != null ? targetSocket.Owner : null;
            _joint = joint;

            Register();
            ApplyJointStrength();

            // 이미 이 쌍을 잇고 있는 비드가 있으면 강도를 되찾는다.
            //
            // 조인트가 운석에 끊어졌다가 다시 조립되는 경우가 있다. 비드는 파츠에
            // 그대로 남아 있는데 새 접합의 강도는 0 에서 시작하므로, 물어보지 않으면
            // <b>눈에 보이는 비드와 실제 강도가 어긋난다</b>.
            WeldBead.RefreshBetween(_movingPart, _targetPart);
        }

        /// <summary>
        /// 비드로 이은 접합을 맺는다. 소켓이 없다(S15P21B202-228).
        /// 강도는 <see cref="SetBeadWeldRatio"/> 하나로만 들어온다.
        /// </summary>
        public void BindBeadWeld(SpaceshipPart owner, SpaceshipPart other, Joint joint)
        {
            _movingSocket = null;
            _targetSocket = null;
            _movingPart = owner;
            _targetPart = other;
            _joint = joint;

            Register();
            ApplyJointStrength();
        }

        void OnEnable() => Register();

        void OnDisable() => Unregister();

        public void Register()
        {
            if (!_released && !All.Contains(this)) All.Add(this);
        }

        public void Unregister() => All.Remove(this);

        void FixedUpdate() => Tick();

        /// <summary>
        /// 조인트가 아직 살아 있는지 확인한다. 끊어졌으면 맞물림을 푼다.
        /// 런타임에서는 FixedUpdate 가 부르고, 테스트에서는 직접 부른다.
        /// </summary>
        /// <summary>
        /// 접합부를 봉인해 더 이상 끊어지지 않게 한다. 발사가 성공한 뒤 상승 연출 중에
        /// 파손되지 않아야 하기 때문이다(기획 §22.3).
        ///
        /// 조인트의 <c>breakForce</c> 를 올리는 것만으로는 안 된다.
        /// <see cref="ApplyJointStrength"/> 가 매 <see cref="Tick"/> 마다 용접 비율로 강도를
        /// 다시 계산해 덮어쓰기 때문이다(S15P21B202-194 에서 이 함정에 빠졌다).
        /// 그래서 재계산 자체를 멈춘다.
        /// </summary>
        public void SealForLaunch()
        {
            _sealed = true;

            if (_joint == null) return;

            _joint.breakForce = float.PositiveInfinity;
            _joint.breakTorque = float.PositiveInfinity;
        }

        public void Tick()
        {
            if (_released) return;

            if (_joint == null)
            {
                Release();
                return;
            }

            if (_sealed) return;

            ApplyJointStrength();
        }

        /// <summary>
        /// 용접 완료 비율에 따라 조인트 강도를 올린다.
        /// 전부 채워지면 파츠를 Welded 로 전환하고 다시 들 수 없게 한다.
        /// </summary>
        void ApplyJointStrength()
        {
            var ratio = WeldRatio;

            _joint.breakForce = Mathf.Lerp(
                PhysicsScale.UnweldedBreakForce, PhysicsScale.WeldedBreakForce, ratio);
            _joint.breakTorque = Mathf.Lerp(
                PhysicsScale.UnweldedBreakTorque, PhysicsScale.WeldedBreakTorque, ratio);

            if (ratio < 1f) return;

            // 소켓 조립은 <b>위에 올라간 파츠 하나만</b> 올린다. 예전부터 그랬고 이
            // 이슈에서 바꾸지 않는다 — 아래 파츠까지 Welded 로 만들면 용접한 탑의 밑단을
            // 다시 들 수 없게 되고, 그것은 §16.3 재조립과 얽힌 별개의 결정이다.
            //
            // 비드로 이은 접합은 <b>둘 다</b> 올린다. 옆으로 이었으면 어느 쪽이 "위" 인지가
            // 없고, 이제 하나로 움직이므로 한쪽만 들 수 있으면 안 된다.
            var promoted = Promote(_movingPart);
            if (!HasSockets && Promote(_targetPart)) promoted = true;

            if (promoted) Welded?.Invoke(this);
        }

        static void Demote(SpaceshipPart part)
        {
            if (part != null && part.State == PartState.Welded)
                part.TryTransitionTo(PartState.Placed);
        }

        static bool Promote(SpaceshipPart part)
        {
            if (part == null || part.State != PartState.Placed) return false;
            return part.TryTransitionTo(PartState.Welded);
        }

        void OnJointBreak(float breakForce) => Release();

        /// <summary>
        /// EditMode 에서는 Destroy 가 다음 프레임까지 지연되지 않으므로 즉시 파괴를 써야 한다.
        /// 테스트가 같은 파츠를 다시 조립할 때 이전 컴포넌트가 남아 있으면 안 된다.
        /// </summary>
        static void DestroyObject(UnityEngine.Object target)
        {
            if (target == null) return;

            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }

        /// <summary>
        /// 맞물림을 풀고 이 컴포넌트를 지운다. 여러 번 불러도 한 번만 동작한다.
        /// 용접 티켓에서 조인트를 교체할 때도 이 경로를 쓴다.
        /// </summary>
        public void Release()
        {
            if (_released) return;
            _released = true;
            Unregister();

            if (_movingSocket != null) _movingSocket.Disconnect();

            // 접합이 풀렸으면 이 접합의 용접도 없던 일이 된다. 비드 자체는 파츠에
            // 남는다 — 자국이 남는 것이 기획 §16.2 이고, 다시 조립하면 <see cref="Bind"/>
            // 가 그 비드를 다시 물어본다.
            _beadRatio = 0f;

            // 용접까지 끝낸 파츠가 절단·파손으로 떨어졌으면 다시 놓인 상태로 돌린다.
            // 그러지 않으면 Welded 로 남아 다시 들 수 없고, 기획 §16.3 의 "절단한 …
            // 재조립" 이 성립하지 않는다.
            //
            // 올린 것만 되돌린다 — ApplyJointStrength 와 짝을 맞춘다. 소켓 접합은
            // 위쪽 하나, 비드 접합은 둘 다다.
            Demote(_movingPart);
            if (!HasSockets) Demote(_targetPart);

            if (_joint != null)
            {
                var joint = _joint;
                _joint = null;
                DestroyObject(joint);
            }

            Released?.Invoke(this);
            DestroyObject(this);
        }
    }
}
