using UnityEngine;

namespace Metagame.Gameplay
{
    /// <summary>
    /// ConfigurableJoint 다루기. targetRotation 은 "조인트 프레임에서 본, 시작 자세로부터의
    /// 역회전" 이라 직관과 반대다 — 여기서 한 번 감싸두고 나머지 코드는 그냥
    /// "부모 기준으로 이 자세가 되어라" 만 말한다.
    ///
    /// 자세를 만드는 힘은 전부 조인트 드라이브로 넣는다. AddTorque 는 솔버 밖에서
    /// 더해지는 힘이라, 조인트 드라이브(솔버 안)와 겨루면 언제나 진다.
    /// </summary>
    public static class JointTools
    {
        public static void SetTargetLocalRotation(ConfigurableJoint joint,
                                                  Quaternion targetLocalRotation,
                                                  Quaternion startLocalRotation)
        {
            if (joint == null) return;

            Vector3 right = joint.axis;
            Vector3 forward = Vector3.Cross(joint.axis, joint.secondaryAxis).normalized;
            Vector3 up = Vector3.Cross(forward, right).normalized;
            Quaternion worldToJoint = Quaternion.LookRotation(forward, up);

            Quaternion result = Quaternion.Inverse(worldToJoint);
            result *= Quaternion.Inverse(targetLocalRotation) * startLocalRotation;
            result *= worldToJoint;

            joint.targetRotation = result;
        }

        public static void SetDrive(ConfigurableJoint joint, float spring, float damper, float maxForce = 1.0e6f)
        {
            var d = new JointDrive();
            d.positionSpring = spring;
            d.positionDamper = damper;
            d.maximumForce = maxForce;
            joint.rotationDriveMode = RotationDriveMode.Slerp;
            joint.slerpDrive = d;
        }

        public static void Limit(ConfigurableJoint j, float xLow, float xHigh, float yLimit, float zLimit)
        {
            j.angularXMotion = ConfigurableJointMotion.Limited;
            j.angularYMotion = yLimit > 0.01f ? ConfigurableJointMotion.Limited : ConfigurableJointMotion.Locked;
            j.angularZMotion = zLimit > 0.01f ? ConfigurableJointMotion.Limited : ConfigurableJointMotion.Locked;

            var low = j.lowAngularXLimit; low.limit = xLow; j.lowAngularXLimit = low;
            var high = j.highAngularXLimit; high.limit = xHigh; j.highAngularXLimit = high;
            var y = j.angularYLimit; y.limit = Mathf.Max(yLimit, 0.1f); j.angularYLimit = y;
            var z = j.angularZLimit; z.limit = Mathf.Max(zLimit, 0.1f); j.angularZLimit = z;
        }

        public static bool IsFinite(Vector3 v)
        {
            return !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z)
                  || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));
        }

        public static Vector3 Clamp(Vector3 v, float max)
        {
            float m = v.magnitude;
            return m > max && m > 1e-5f ? v * (max / m) : v;
        }
    }
}
