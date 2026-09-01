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
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PartAssemblyLink : MonoBehaviour
    {
        // 직렬화한다. 씬이나 프리팹으로 저장된 조립 상태가 로드된 뒤에도 유지되어야 한다.
        // 저장하지 않으면 로드 직후 _joint 가 null 로 보여 Tick 이 곧바로 맞물림을 풀어 버린다.
        [SerializeField] PartSocket _movingSocket;
        [SerializeField] PartSocket _targetSocket;
        [SerializeField] Joint _joint;

        [SerializeField] WeldSeam _seam;

        bool _released;

        /// <summary>발사 성공으로 봉인되었는가. 봉인되면 강도를 다시 계산하지 않는다.</summary>
        bool _sealed;
        bool _seamCollected;

        /// <summary>맞물림이 풀렸을 때. 조인트가 끊어진 경우와 직접 해제한 경우 모두 포함한다.</summary>
        public event Action<PartAssemblyLink> Released;

        public PartSocket MovingSocket => _movingSocket;

        public PartSocket TargetSocket => _targetSocket;

        public bool IsIntact => !_released && _joint != null;

        /// <summary>이 접합의 원형 용접선(기획 §16.1). 없으면 용접할 수 없다.</summary>
        public WeldSeam Seam
        {
            get
            {
                EnsureSeam();
                return _seam;
            }
        }

        /// <summary>
        /// 용접 완료 비율 0~1. 접합선이 없으면 0 이다.
        ///
        /// 완료 여부가 아니라 비율을 쓴다. 절반만 용접했으면 절반만 튼튼해야 운석이
        /// 떨어질 때 용접하지 않은 층이 무너지는 긴장이 생긴다.
        ///
        /// 기획 §16.2 "처음 조립하는 접합부의 강도는 실제로 용접한 길이에 비례한다" —
        /// 접합선이 조각 길이로 재므로 이 값이 곧 용접한 길이의 비율이다.
        /// </summary>
        public float WeldRatio
        {
            get
            {
                EnsureSeam();
                return _seam != null ? _seam.Ratio : 0f;
            }
        }

        public bool IsFullyWelded => Seam != null && WeldRatio >= 1f;

        /// <summary>
        /// 두 접합면 사이의 간격(m). 축 방향 거리다.
        ///
        /// 기획 §16.1 "파츠가 움직여 접합면 간격이 벌어지면 작업이 중단된다" 의 판정에
        /// <see cref="WeldSeam"/> 이 이 값을 쓴다. 여기서 재는 이유는 소켓 두 개를 가진
        /// 곳이 여기이고, 접합선이 소켓을 다시 찾으면 두 곳이 어긋날 수 있기 때문이다.
        /// </summary>
        public float Gap
        {
            get
            {
                if (_movingSocket == null || _targetSocket == null) return float.MaxValue;

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
        /// </summary>
        public void ForceWeldComplete()
        {
            EnsureSeam();
            if (_seam != null) _seam.ForceComplete();
        }

        /// <summary>용접이 모두 끝나 접합이 확정되었을 때.</summary>
        public event Action<PartAssemblyLink> Welded;

        public void Bind(PartSocket movingSocket, PartSocket targetSocket, Joint joint)
        {
            _movingSocket = movingSocket;
            _targetSocket = targetSocket;
            _joint = joint;

            _seamCollected = false;
            EnsureSeam();
            ApplyJointStrength();
        }

        /// <summary>파츠에 달린 접합선을 찾는다. 런타임에 붙이거나 떼어낸 뒤 호출한다.</summary>
        public void RefreshSeam()
        {
            _seamCollected = true;

            _seam = GetComponentInChildren<WeldSeam>(true);
            if (_seam != null) _seam.Initialize();
        }

        void EnsureSeam()
        {
            if (_seamCollected) return;
            RefreshSeam();
        }

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

            if (ratio < 1f || _seam == null) return;

            var part = _movingSocket != null ? _movingSocket.Owner : null;
            if (part == null || part.State != PartState.Placed) return;

            if (part.TryTransitionTo(PartState.Welded)) Welded?.Invoke(this);
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

            if (_movingSocket != null) _movingSocket.Disconnect();

            // 접합이 풀렸으면 용접도 없던 일이 된다. 다시 올려 처음부터 용접해야 한다.
            EnsureSeam();
            if (_seam != null) _seam.ResetProgress();

            // 용접까지 끝낸 파츠가 절단·파손으로 떨어졌으면 다시 놓인 상태로 돌린다.
            // 그러지 않으면 Welded 로 남아 다시 들 수 없고, 기획 §16.3 의 "절단한 …
            // 재조립" 이 성립하지 않는다.
            var owner = _movingSocket != null ? _movingSocket.Owner : null;
            if (owner != null && owner.State == PartState.Welded)
                owner.TryTransitionTo(PartState.Placed);

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
