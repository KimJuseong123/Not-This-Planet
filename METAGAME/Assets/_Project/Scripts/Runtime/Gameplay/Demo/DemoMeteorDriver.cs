using Metagame.Gameplay.Hazards;
using UnityEngine;

namespace Metagame.Gameplay.Demo
{
    /// <summary>
    /// 운석 스포너를 매 물리 스텝 돌린다.
    ///
    /// 게임플레이 코드에서는 스포너의 Tick 을 상위 시스템이 명시적으로 호출한다.
    /// 호스트 권위 물리에서 틱 순서를 통제하기 위한 것이다.
    /// 그 상위 시스템이 아직 없으므로 씬에서 확인할 수 있게 여기서 대신 돌린다.
    /// </summary>
    [RequireComponent(typeof(MeteorSpawner))]
    public sealed class DemoMeteorDriver : MonoBehaviour
    {
        MeteorSpawner _spawner;

        void Awake() => _spawner = GetComponent<MeteorSpawner>();

        void FixedUpdate()
        {
            if (_spawner != null) _spawner.Tick(Time.fixedDeltaTime);
        }
    }
}
