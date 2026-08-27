using UnityEngine;

namespace Metagame.Gameplay
{
    /// <summary>인형의 다리 한 짝.</summary>
    public class Leg
    {
        public bool IsLeft;
        public Rigidbody Thigh, Shin, Foot;
        public ConfigurableJoint Hip, Knee, Ankle;
        public Quaternion HipStart = Quaternion.identity;
        public Quaternion KneeStart = Quaternion.identity;
        public Quaternion AnkleStart = Quaternion.identity;
    }
}
