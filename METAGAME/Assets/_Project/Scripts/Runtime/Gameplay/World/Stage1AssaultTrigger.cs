using UnityEngine;

namespace Metagame.Gameplay
{
    /// <summary>기존 씬 참조 보존용. 공세 시작은 Stage1Sequence가 첫 고정못 해제로 결정한다.</summary>
    [RequireComponent(typeof(BoxCollider))]
    public sealed class Stage1AssaultTrigger : MonoBehaviour
    {
        void Reset() => GetComponent<BoxCollider>().isTrigger = true;
    }
}
