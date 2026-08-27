using UnityEngine;
using Metagame.Core;

namespace Metagame.Gameplay
{
    /// <summary>
    /// 3인칭 궤도 카메라. 팔은 화면 중앙이 가리키는 쪽으로 뻗는다.
    /// 인형의 골반을 따라다니되, 골반이 물리로 덜덜 떨리므로 위치는 부드럽게 쫓아간다.
    ///
    /// <b>각도를 두 벌 들고 있다.</b>
    ///  · yaw/pitch       — 실제로 보는 각도 (카메라 트랜스폼)
    ///  · aimYaw/aimPitch — 조작의 기준이 되는 각도 (WASD 방향, 팔이 뻗는 방향)
    ///
    /// 평소엔 둘이 같다. <b>Alt 를 누르고 있는 동안만</b> 보는 각도만 움직이고 조작 기준은 얼어붙는다 —
    /// 그래서 캐릭터 정면으로 돌아가 얼굴을 봐도 걷는 방향과 팔이 흔들리지 않는다.
    /// 손을 떼면 보는 각도가 조작 기준으로 부드럽게 되돌아온다.
    /// </summary>
    public class OrbitCamera : MonoBehaviour
    {
        public Transform Target;
        public float Distance = Tuning.CamDistance;
        public float TargetHeight = Tuning.CamHeight;
        public float Sensitivity = 2.4f;
        public float MinPitch = -42f;
        public float MaxPitch = 74f;
        [Header("자유 시점 (Alt)")]
        public float FreeMinPitch = -78f;
        public float FreeMaxPitch = 84f;
        public float ReturnSpeed = 7.5f;      // 클수록 빨리 제자리로 온다

        public Camera Cam { get; private set; }

        float yaw;
        float pitch = 12f;
        float aimYaw;
        float aimPitch = 12f;
        bool returning;
        float currentDistance;
        Vector3 smoothFocus;
        bool focusReady;
        readonly RaycastHit[] hits = new RaycastHit[12];

        public static OrbitCamera Create(Transform target, Color skyColor)
        {
            var go = new GameObject("카메라");
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = 64f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 900f;      // 별밭이 반지름 300 짜리 껍데기다
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = skyColor;
            go.tag = "MainCamera";

            var orbit = go.AddComponent<OrbitCamera>();
            orbit.Cam = cam;
            orbit.Target = target;
            orbit.currentDistance = orbit.Distance;
            return orbit;
        }

        /// <summary>Alt 를 누르고 있는가. 켜지는 순간 조작 기준 각도가 얼어붙는다.</summary>
        public bool FreeLook { get; private set; }

        public void SetFreeLook(bool on)
        {
            if (on == FreeLook) return;
            FreeLook = on;
            returning = !on;          // 놓는 순간부터 보는 각도를 조작 기준으로 되돌린다
        }

        public void Steer(Vector2 look)
        {
            if (look.sqrMagnitude > 1e-6f) returning = false;   // 되돌아오는 중 마우스를 잡으면 사람이 우선

            float lo = FreeLook ? FreeMinPitch : MinPitch;
            float hi = FreeLook ? FreeMaxPitch : MaxPitch;

            yaw += look.x * Sensitivity;
            pitch = Mathf.Clamp(pitch - look.y * Sensitivity, lo, hi);

            if (!FreeLook)
            {
                // 평소에는 조작 기준이 보는 각도를 그대로 따라간다
                aimYaw = yaw;
                aimPitch = Mathf.Clamp(pitch, MinPitch, MaxPitch);
            }
        }

        void ReturnToAim(float dt)
        {
            if (FreeLook || !returning) return;

            float k = 1f - Mathf.Exp(-ReturnSpeed * dt);
            yaw = Mathf.LerpAngle(yaw, aimYaw, k);
            pitch = Mathf.Lerp(pitch, aimPitch, k);

            if (Mathf.Abs(Mathf.DeltaAngle(yaw, aimYaw)) < 0.15f && Mathf.Abs(pitch - aimPitch) < 0.15f)
            {
                yaw = aimYaw;
                pitch = aimPitch;
                returning = false;
            }
        }

        /// <summary>순간이동(부활) 뒤에 카메라가 세계를 가로질러 날아가지 않게.</summary>
        public void SnapToTarget()
        {
            focusReady = false;
            FreeLook = false;
            returning = false;
            yaw = aimYaw;
            pitch = aimPitch;
            LateUpdate();
        }

        void LateUpdate()
        {
            if (Target == null) return;
            ReturnToAim(Time.deltaTime);

            Vector3 raw = Target.position + Vector3.up * TargetHeight;
            if (!focusReady) { smoothFocus = raw; focusReady = true; }
            else smoothFocus = Vector3.Lerp(smoothFocus, raw, 1f - Mathf.Exp(-16f * Time.deltaTime));

            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 back = rot * Vector3.back;

            float want = Distance;
            int n = Physics.SphereCastNonAlloc(smoothFocus, 0.20f, back, hits, Distance, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var col = hits[i].collider;
                if (col == null) continue;
                if (col.GetComponentInParent<PlayerRig>() != null) continue;   // 자기 몸은 시야를 막지 않는다
                float d = hits[i].distance;
                if (d <= 0.01f) continue;
                want = Mathf.Min(want, Mathf.Max(1.0f, d - 0.12f));
            }

            currentDistance = Mathf.Lerp(currentDistance, want, (want < currentDistance ? 24f : 6f) * Time.deltaTime);

            transform.position = smoothFocus + back * currentDistance;
            transform.rotation = rot;
        }

        // 아래 셋은 전부 조작 기준 각도에서 뽑는다. 카메라 트랜스폼에서 뽑으면
        // Alt 로 시점을 돌리는 동안 걷는 방향과 팔이 같이 돌아가 버린다.
        Quaternion AimRotation { get { return Quaternion.Euler(aimPitch, aimYaw, 0f); } }

        public Vector3 PlanarForward
        {
            get { Vector3 f = AimRotation * Vector3.forward; f.y = 0f; return f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward; }
        }

        public Vector3 PlanarRight
        {
            get { Vector3 r = AimRotation * Vector3.right; r.y = 0f; return r.sqrMagnitude > 1e-4f ? r.normalized : Vector3.right; }
        }

        public Vector3 AimDirection { get { return AimRotation * Vector3.forward; } }

        /// <summary>조작 기준 각도. 설정하면 보는 각도도 같이 맞춘다.</summary>
        public float Yaw
        {
            get { return aimYaw; }
            set { aimYaw = value; if (!FreeLook) yaw = value; }
        }

        public float Pitch
        {
            get { return aimPitch; }
            set { aimPitch = Mathf.Clamp(value, MinPitch, MaxPitch); if (!FreeLook) pitch = aimPitch; }
        }

        /// <summary>지금 보고 있는 각도 (자유 시점 동안만 조작 기준과 다르다).</summary>
        public float ViewYaw { get { return yaw; } }
        public float ViewPitch { get { return pitch; } }
    }
}
