using System.Collections.Generic;
using UnityEngine;

namespace Metagame.Gameplay.Parts
{
    /// <summary>
    /// 파츠의 접합 지점. 파츠의 자식 오브젝트에 붙인다.
    ///
    /// 규약: <c>transform.forward</c> 는 접합면이 바라보는 바깥 방향이다.
    /// 위쪽 소켓은 위를, 아래쪽 소켓은 아래를 향한다. 두 소켓이 맞물리면
    /// 위치가 겹치고 두 forward 가 정반대가 된다.
    ///
    /// 콜라이더가 필요 없다. 물리 쿼리로 찾지 않고 <see cref="All"/> 목록을 훑는다.
    /// 소켓 수가 파츠당 몇 개뿐이라 목록 순회가 구체 캐스트보다 단순하고 결정적이다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PartSocket : MonoBehaviour
    {
        /// <summary>씬에 활성화된 모든 소켓.</summary>
        public static readonly List<PartSocket> All = new();

        [SerializeField] SocketRole _role = SocketRole.Top;

        [SerializeField]
        [Tooltip("비우면 부모에서 SpaceshipPart 를 찾는다.")]
        SpaceshipPart _owner;

        bool _initialized;

        public SocketRole Role => _role;

        public SpaceshipPart Owner
        {
            get
            {
                Initialize();
                return _owner;
            }
        }

        /// <summary>맞물려 있는 상대 소켓. 없으면 null.</summary>
        public PartSocket ConnectedTo { get; private set; }

        public bool IsOccupied => ConnectedTo != null;

        /// <summary>
        /// 위에 파츠를 받을 수 있는 상태인지.
        /// 아직 조립에 들어가지 않은 파츠의 위쪽 소켓에는 쌓을 수 없다.
        /// 이것이 "아래에서 위로" 순서를 강제하는 규칙이다.
        /// </summary>
        public bool CanReceive =>
            _role == SocketRole.Top
            && !IsOccupied
            && Owner != null
            && Owner.State is PartState.Placed or PartState.Welded;

        void OnEnable()
        {
            Initialize();
            if (!All.Contains(this)) All.Add(this);
        }

        void OnDisable() => All.Remove(this);

        /// <summary>
        /// 소유 파츠를 찾는다. 여러 번 불러도 안전하다.
        /// Awake 와 OnEnable 은 EditMode 에서 보장되지 않으므로 테스트와 파츠가 직접 부른다.
        /// </summary>
        public void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            if (_owner == null) _owner = GetComponentInParent<SpaceshipPart>();
        }

        /// <summary>역할을 코드에서 지정한다. 레벨 구성 스크립트와 테스트가 쓴다.</summary>
        public void SetRole(SocketRole role) => _role = role;

        /// <summary>씬 목록에 직접 등록한다. OnEnable 이 돌지 않는 EditMode 에서 쓴다.</summary>
        public void Register()
        {
            Initialize();
            if (!All.Contains(this)) All.Add(this);
        }

        public void Unregister() => All.Remove(this);

        /// <summary>두 소켓을 서로 맞물린 것으로 표시한다.</summary>
        public void Connect(PartSocket other)
        {
            if (other == null || other == this) return;

            ConnectedTo = other;
            other.ConnectedTo = this;
        }

        /// <summary>맞물림을 푼다. 상대 쪽 표시도 함께 지운다.</summary>
        public void Disconnect()
        {
            var other = ConnectedTo;
            ConnectedTo = null;

            if (other != null && other.ConnectedTo == this) other.ConnectedTo = null;
        }
    }
}
