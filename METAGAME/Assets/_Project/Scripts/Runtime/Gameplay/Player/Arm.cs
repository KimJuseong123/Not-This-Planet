using UnityEngine;

namespace Metagame.Gameplay
{
    /// <summary>인형의 팔 한 짝.</summary>
    public class Arm
    {
        public bool IsLeft;
        public Rigidbody Upper, Lower;
        public Hand Hand;
        public ConfigurableJoint Shoulder, Elbow;
        public Transform ShoulderAnchor;      // 어깨 위치를 재는 기준 (가슴의 자식)
        public Quaternion ShoulderStart = Quaternion.identity;
        public Quaternion ElbowStart = Quaternion.identity;
    }
}
