using System;
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

        bool _released;

        /// <summary>맞물림이 풀렸을 때. 조인트가 끊어진 경우와 직접 해제한 경우 모두 포함한다.</summary>
        public event Action<PartAssemblyLink> Released;

        public PartSocket MovingSocket => _movingSocket;

        public PartSocket TargetSocket => _targetSocket;

        public bool IsIntact => !_released && _joint != null;

        public void Bind(PartSocket movingSocket, PartSocket targetSocket, Joint joint)
        {
            _movingSocket = movingSocket;
            _targetSocket = targetSocket;
            _joint = joint;
        }

        void FixedUpdate() => Tick();

        /// <summary>
        /// 조인트가 아직 살아 있는지 확인한다. 끊어졌으면 맞물림을 푼다.
        /// 런타임에서는 FixedUpdate 가 부르고, 테스트에서는 직접 부른다.
        /// </summary>
        public void Tick()
        {
            if (_released) return;
            if (_joint != null) return;

            Release();
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
