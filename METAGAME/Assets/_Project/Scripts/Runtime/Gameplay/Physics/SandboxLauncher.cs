using UnityEngine;

namespace Metagame.Gameplay
{
    /// <summary>
    /// Sandbox_Physics 씬 전용 디버그 컴포넌트.
    /// 견인 도구가 아직 없으므로, 파츠가 "옮겨지는 중"인 상황을 흉내내기 위해
    /// Play 시작 시 지정한 속도를 한 번 준다. 실제 게임 로직에는 사용하지 않는다.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class SandboxLauncher : MonoBehaviour
    {
        [SerializeField] Vector3 _launchVelocity = new(4f, 0f, 0f);

        public Vector3 LaunchVelocity
        {
            get => _launchVelocity;
            set => _launchVelocity = value;
        }

        void Start() => GetComponent<Rigidbody>().linearVelocity = _launchVelocity;
    }
}
