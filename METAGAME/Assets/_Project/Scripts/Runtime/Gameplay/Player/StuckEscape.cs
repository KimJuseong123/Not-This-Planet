using UnityEngine;

namespace Metagame.Gameplay
{
    /// <summary>
    /// 끼임 탈출(기획 §21). <c>T</c> 를 3초 유지하면 캐릭터만 가까운 안전 지면으로 옮긴다.
    ///
    /// <b>기능은 이미 있었지만 §21 을 다섯 군데에서 어겼다</b>(S15P21B202-248).
    ///
    /// <code>
    /// GetKeyDown            → 즉시 발동. §21 은 "3초 동안 유지하면"
    /// Teleport(SpawnFoot)   → 스테이지 처음 자리. §21 은 "가까운 안전 지면"
    /// Revive(MaxHealth)     → 스스로 일어난다. §21 은 "기절 중에는 사용할 수 없다"
    /// (장비 처리 없음)      → §21 은 "이동 전 위치에 떨어진다"
    /// Transport.Reset()     → §21 은 "파츠, 비행체와 장비를 복구하는 기능은 아니다"
    /// </code>
    ///
    /// 그중 <c>Revive</c> 가 §21 뿐 아니라 §20 을 통째로 무너뜨렸다 — §20.3 의 "동료가
    /// 구조할 때까지", 솔로 6초 대기, §20.5 의 전원 기절 실패가 모두 <c>T</c> 한 번으로
    /// 우회됐다.
    ///
    /// <see cref="MonoBehaviour"/> 가 아니다. 소유자가 <see cref="Tick"/> 을 부르므로
    /// EditMode 에서 시간을 직접 넣어 검증할 수 있다 — 3초를 실제로 기다릴 수는 없다.
    /// </summary>
    public sealed class StuckEscape
    {
        readonly PlayerRig _player;
        float _held;

        public StuckEscape(PlayerRig player)
        {
            _player = player;
        }

        /// <summary>유지 진행도 0~1. 화면 표시가 이 값을 쓴다.</summary>
        public float Progress01 =>
            Mathf.Clamp01(_held / PhysicsScale.StuckEscapeSeconds);

        /// <summary>몇 번 옮겼는가. 검증과 진단용이다.</summary>
        public int EscapeCount { get; private set; }

        /// <summary>
        /// 안전한 자리를 못 찾아 기다리는 중인가.
        ///
        /// 진행도는 다 찼는데 옮기지 못한 상태다. 화면에 이유를 보여줄 자리다 —
        /// 안 그러면 플레이어는 3초를 눌렀는데 아무 일도 없는 것으로만 보인다.
        /// </summary>
        public bool WaitingForRoom { get; private set; }

        /// <summary>
        /// 매 프레임 호출한다. 이번 호출에 실제로 옮겼으면 <c>true</c>.
        ///
        /// <b>키를 놓으면 진행분을 버린다.</b> 고정못(§12.1)처럼 유지하면 끼지 않았을 때
        /// 조금씩 눌러 모아 둘 수 있고, 그러면 "3초 유지" 가 아니라 "합쳐서 3초" 가 된다.
        /// </summary>
        /// <summary>
        /// 끼어 보이는가 — <b>T 를 알려 줄 때다</b> (S15P21B202-669).
        ///
        /// <para>가고 싶다는 입력이 있는데 실제로 안 가는 상태가
        /// <see cref="PhysicsScale.StuckNoticeSeconds"/> 동안 이어지면 참이 된다.</para>
        ///
        /// <para><b>제자리가 정상인 구간을 빼는 것이 이 판정의 본체다.</b> 파츠를 밀 때와
        /// 고정못을 당길 때는 원래 거의 안 움직인다(S15P21B202-667). 거기서 "끼었습니다"
        /// 가 뜨면 <b>게임이 제 기능을 오작동으로 알리는 것</b>이 된다.</para>
        /// </summary>
        public bool LooksStuck { get; private set; }

        float _stuckSeconds;

        /// <summary>
        /// 지금 끼었다고 볼 수 있는 상태인가. 제자리가 정상인 구간은 전부 뺀다.
        /// </summary>
        bool CouldBeStuck()
        {
            if (_player == null || _player.Downed || _player.Ragdolled) return false;
            if (!_player.Grounded) return false;          // 떨어지는 중은 낀 것이 아니다
            if (_player.IsRiding) return false;           // 탈것은 몸이 안 움직인다
            if (_player.Prone) return false;              // 기어가는 속도가 원래 느리다
            if (_player.Hauling) return false;            // 파츠를 미는 중 — 0.73m/s 가 정상
            if (_player.ClampGrabbed) return false;       // 고정못을 당기는 중 — 제자리가 기획이다

            var wish = _player.MoveWish;
            wish.y = 0f;
            if (wish.sqrMagnitude < 1e-4f) return false;  // 갈 생각이 없으면 낀 것이 아니다

            return _player.Speed < PhysicsScale.StuckNoticeSpeed;
        }

        void TickStuckNotice(float deltaTime)
        {
            if (!CouldBeStuck())
            {
                _stuckSeconds = 0f;
                LooksStuck = false;
                return;
            }
            _stuckSeconds += deltaTime;
            LooksStuck = _stuckSeconds >= PhysicsScale.StuckNoticeSeconds;
        }

        public bool Tick(float deltaTime, bool held)
        {
            if (_player == null || deltaTime <= 0f) return false;

            TickStuckNotice(deltaTime);

            // 기절 중에는 쌓이지 않는다(§21). 진행분도 버린다 — 맞기 직전에 눌러 두고
            // 기절한 뒤에 채우는 것이 되면 §20.3 의 구조 대기가 의미를 잃는다.
            if (!held || _player.Downed)
            {
                _held = 0f;
                WaitingForRoom = false;
                return false;
            }

            _held += deltaTime;
            if (_held < PhysicsScale.StuckEscapeSeconds)
            {
                WaitingForRoom = false;
                return false;
            }

            // 다 찼다. 자리를 못 찾으면 진행도를 그대로 두고 다음 프레임에 다시 본다 —
            // 3초를 다시 세게 하면 움직이는 장애물 때문에 놓친 한 프레임이 벌이 된다.
            _held = PhysicsScale.StuckEscapeSeconds;

            if (!TryEscape())
            {
                WaitingForRoom = true;
                return false;
            }

            _held = 0f;
            WaitingForRoom = false;
            EscapeCount++;
            return true;
        }

        /// <summary>
        /// 캐릭터만 옮긴다. 파츠·운반체·장비는 건드리지 않는다(§21).
        /// </summary>
        bool TryEscape()
        {
            if (!SafeGround.TryFind(_player.FootPosition, out var foot)) return false;

            // <b>순간이동보다 먼저 놓는다.</b> §21 "들고 있던 장비는 이동 전 위치에
            // 떨어진다" — 뒤에 놓으면 도구가 새 자리까지 따라온다.
            _player.DropEquipment();

            var facing = _player.FacingDirection;
            facing.y = 0f;
            var rotation = facing.sqrMagnitude < 1e-6f
                ? Quaternion.identity
                : Quaternion.LookRotation(facing, Vector3.up);

            _player.Teleport(foot, rotation);
            return true;
        }
    }
}
