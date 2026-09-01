using System;
using UnityEngine;

namespace Metagame.Gameplay
{
    /// <summary>
    /// 플라즈마 건 총구의 다섯 힌지를 기계식 꽃잎처럼 여닫는다.
    /// 메시를 변형하지 않고 FBX에 잡힌 Hinge_01~05 피벗만 회전하므로
    /// 용접(닫힘)과 절단(열림) 전환이 입력에 즉시 반응한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlasmaGunPetalAnimator : MonoBehaviour
    {
        const int RequiredHingeCount = 5;

        [Header("힌지 배치")]
        [Tooltip("총구에서 몸체 쪽을 향하는 축. 제작 모델은 프리팹 기준 -Z입니다.")]
        [SerializeField] Vector3 ringAxis = Vector3.back;

        [Header("모드별 각도")]
        [SerializeField] float idleAngle;
        [SerializeField] float weldClosedAngle = 50f;
        [SerializeField] float cutOpenAngle = -50f;
        [SerializeField, Min(1f)] float angularSpeed = 420f;
        [SerializeField, Min(0.01f)] float readyTolerance = 2f;

        readonly Transform[] hinges = new Transform[RequiredHingeCount];
        readonly Quaternion[] baseRotations = new Quaternion[RequiredHingeCount];
        readonly Vector3[] tangentAxes = new Vector3[RequiredHingeCount];

        bool initialized;
        float currentAngle;

        public int HingeCount { get; private set; }
        public float CurrentAngle => currentAngle;

        /// <summary>
        /// 현재 모드의 목표 자세로 이동하고, 자세가 완성됐는지 반환한다.
        /// 힌지가 없는 임시 모델은 기존 빔을 막지 않는다.
        /// </summary>
        public bool TickAndIsReady(PlasmaBeamMode mode, float deltaTime)
        {
            EnsureInitialized();
            if (HingeCount == 0) return true;

            var target = TargetAngle(mode);
            currentAngle = Mathf.MoveTowards(currentAngle, target,
                angularSpeed * Mathf.Max(0f, deltaTime));
            ApplyPose();
            return Mathf.Abs(Mathf.DeltaAngle(currentAngle, target)) <= readyTolerance;
        }

        /// <summary>프리팹 미리보기와 테스트에서 지정한 모드 자세를 즉시 적용한다.</summary>
        public void SnapToMode(PlasmaBeamMode mode)
        {
            EnsureInitialized();
            currentAngle = TargetAngle(mode);
            ApplyPose();
        }

        /// <summary>에셋 교체나 계층 변경 뒤 힌지 기준 자세를 다시 읽는다.</summary>
        public void Rebind()
        {
            initialized = false;
            HingeCount = 0;
            EnsureInitialized();
        }

        void OnEnable()
        {
            EnsureInitialized();
            currentAngle = idleAngle;
            ApplyPose();
        }

        void OnValidate()
        {
            if (ringAxis.sqrMagnitude < 1e-4f) ringAxis = Vector3.back;
            angularSpeed = Mathf.Max(1f, angularSpeed);
            readyTolerance = Mathf.Max(0.01f, readyTolerance);
        }

        void EnsureInitialized()
        {
            if (initialized) return;
            initialized = true;
            HingeCount = 0;

            Array.Clear(hinges, 0, hinges.Length);
            var center = Vector3.zero;
            for (var i = 0; i < RequiredHingeCount; i++)
            {
                var hinge = FindDescendant(transform, "Hinge_" + (i + 1).ToString("00"));
                if (hinge == null) continue;

                hinges[i] = hinge;
                baseRotations[i] = hinge.localRotation;
                center += transform.InverseTransformPoint(hinge.position);
                HingeCount++;
            }

            if (HingeCount == 0) return;
            center /= HingeCount;

            var axis = ringAxis.normalized;
            for (var i = 0; i < RequiredHingeCount; i++)
            {
                var hinge = hinges[i];
                if (hinge == null) continue;

                var pivot = transform.InverseTransformPoint(hinge.position);
                var radial = Vector3.ProjectOnPlane(pivot - center, axis);
                if (radial.sqrMagnitude < 1e-5f)
                {
                    tangentAxes[i] = Vector3.up;
                    continue;
                }

                var tangentInRoot = Vector3.Cross(axis, radial.normalized).normalized;
                var tangentWorld = transform.TransformDirection(tangentInRoot);
                tangentAxes[i] = hinge.parent != null
                    ? hinge.parent.InverseTransformDirection(tangentWorld).normalized
                    : tangentWorld.normalized;
            }

            currentAngle = idleAngle;
        }

        void ApplyPose()
        {
            for (var i = 0; i < RequiredHingeCount; i++)
            {
                if (hinges[i] == null) continue;
                hinges[i].localRotation = Quaternion.AngleAxis(currentAngle, tangentAxes[i]) *
                                          baseRotations[i];
            }
        }

        float TargetAngle(PlasmaBeamMode mode)
        {
            return mode switch
            {
                PlasmaBeamMode.Weld => weldClosedAngle,
                PlasmaBeamMode.Cut => cutOpenAngle,
                _ => idleAngle,
            };
        }

        static Transform FindDescendant(Transform root, string wantedName)
        {
            if (root == null) return null;
            var all = root.GetComponentsInChildren<Transform>(true);
            foreach (var candidate in all)
                if (candidate != null && candidate.name == wantedName)
                    return candidate;
            return null;
        }
    }
}
