using UnityEngine;
using Metagame.Core;

namespace Metagame.Gameplay
{
    /// <summary>
    /// 1인칭/3인칭 카메라. 팔은 화면 중앙이 가리키는 쪽으로 뻗는다.
    /// 기본 게임 모드는 캐릭터 머리 높이에 카메라를 두는 1인칭이다.
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
        public bool FirstPerson;
        public float Distance = Tuning.CamDistance;
        public float TargetHeight = Tuning.CamHeight;
        public float FirstPersonForward = 0.20f;
        public float Sensitivity = 2.4f;
        public float MinPitch = -42f;
        public float MaxPitch = 74f;
        [Header("자유 시점 (Alt)")]
        public float FreeMinPitch = -78f;
        public float FreeMaxPitch = 84f;
        public float ReturnSpeed = 7.5f;      // 클수록 빨리 제자리로 온다
        [Header("고정쇠 하중감")]
        [Range(0.05f, 1f)]
        public float ClampUpwardLookScale = 0.28f;
        public float ClampDownwardPullSpeed = 8f;

        public Camera Cam { get; private set; }

        float yaw;
        float pitch = 12f;
        float aimYaw;
        float aimPitch = 12f;
        bool returning;
        bool clampLiftResistance;
        float clampLiftResistanceBlend;
        bool clampLiftPitchLocked;
        float clampLiftPitchLock;
        bool vehicleView;
        bool savedFirstPerson;
        float savedDistance;
        float savedTargetHeight;
        float savedMinPitch;
        float savedMaxPitch;
        float currentDistance;
        Vector3 smoothFocus;
        bool focusReady;
        readonly RaycastHit[] hits = new RaycastHit[12];

        public static OrbitCamera Create(Transform target, Color skyColor, bool firstPerson = false)
        {
            var go = new GameObject("카메라");
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = 64f;
            cam.nearClipPlane = firstPerson ? 0.03f : 0.1f;
            cam.farClipPlane = 900f;      // 별밭이 반지름 300 짜리 껍데기다
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = skyColor;
            go.tag = "MainCamera";

            var orbit = go.AddComponent<OrbitCamera>();
            orbit.Cam = cam;
            orbit.Target = target;
            orbit.FirstPerson = firstPerson;
            orbit.currentDistance = firstPerson ? 0f : orbit.Distance;
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

        /// <summary>고정쇠를 잡은 동안 위쪽 시점 조작에 하중감을 켠다.</summary>
        public void SetClampLiftResistance(bool on)
        {
            clampLiftResistance = on;
        }

        /// <summary>고정쇠를 충분히 들어 올린 뒤 시점이 더 위로 올라가지 않게 한다.</summary>
        public void SetClampLiftPitchLock(bool on)
        {
            if (on == clampLiftPitchLocked) return;

            clampLiftPitchLocked = on;
            if (on) clampLiftPitchLock = pitch;
        }

        public bool VehicleView { get { return vehicleView; } }

        /// <summary>
        /// 운반 장치에 탑승하면 장치보다 높은 곳에서 내려다보는 시점으로 전환한다.
        /// 하차할 때 기존 1인칭/3인칭 설정과 시야 각도 제한을 되돌린다.
        /// </summary>
        public void SetVehicleView(bool on)
        {
            if (on == vehicleView) return;

            if (on)
            {
                savedFirstPerson = FirstPerson;
                savedDistance = Distance;
                savedTargetHeight = TargetHeight;
                savedMinPitch = MinPitch;
                savedMaxPitch = MaxPitch;

                vehicleView = true;
                FirstPerson = false;
                Distance = 10f;
                TargetHeight = 0.55f;
                // 이 카메라의 pitch는 양수일수록 높은 곳에서 아래를 본다.
                // 음수 범위를 쓰면 카메라가 탑승체 아래로 내려가므로 탑승 모드에서는
                // 고각만 허용해 운반체와 캐릭터를 위에서 내려다보게 한다.
                MinPitch = 70f;
                MaxPitch = 88f;
                FreeLook = false;
                returning = false;
                yaw = aimYaw;
                pitch = 78f;
                aimPitch = 78f;
            }
            else
            {
                vehicleView = false;
                FirstPerson = savedFirstPerson;
                Distance = savedDistance;
                TargetHeight = savedTargetHeight;
                MinPitch = savedMinPitch;
                MaxPitch = savedMaxPitch;
                aimPitch = Mathf.Clamp(aimPitch, MinPitch, MaxPitch);
                pitch = Mathf.Clamp(pitch, MinPitch, MaxPitch);
                currentDistance = FirstPerson ? 0f : Distance;
            }

            focusReady = false;
            LateUpdate();
        }

        public void Steer(Vector2 look)
        {
            if (look.sqrMagnitude > 1e-6f) returning = false;   // 되돌아오는 중 마우스를 잡으면 사람이 우선

            float lo = VehicleView ? MinPitch : (FreeLook ? FreeMinPitch : MinPitch);
            float hi = VehicleView ? MaxPitch : (FreeLook ? FreeMaxPitch : MaxPitch);

            float targetResistance = clampLiftResistance && !FreeLook ? 1f : 0f;
            clampLiftResistanceBlend = Mathf.MoveTowards(
                clampLiftResistanceBlend, targetResistance, 10f * Time.deltaTime);

            float verticalLook = look.y;
            if (verticalLook > 0f && clampLiftResistanceBlend > 0f)
            {
                float response = Mathf.Lerp(1f, ClampUpwardLookScale, clampLiftResistanceBlend);
                verticalLook *= response;
            }

            yaw += look.x * Sensitivity;
            pitch = Mathf.Clamp(pitch - verticalLook * Sensitivity, lo, hi);

            // 위로 올리려는 동안에는 시점이 조금씩 아래로 되돌아가도록 해,
            // 무거운 고정쇠를 억지로 드는 듯한 끙끙거리는 반응을 만든다.
            if (clampLiftResistanceBlend > 0f)
                pitch = Mathf.Clamp(pitch + ClampDownwardPullSpeed
                                    * clampLiftResistanceBlend * Time.deltaTime, lo, hi);

            // pitch가 작아질수록 화면이 위를 본다. 기준을 넘은 시점의 pitch보다
            // 작아지는 것만 막아, 아래로 보는 조작은 허용하고 추가 상승만 제한한다.
            if (clampLiftPitchLocked)
                pitch = Mathf.Max(pitch, clampLiftPitchLock);

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
            clampLiftResistance = false;
            clampLiftResistanceBlend = 0f;
            clampLiftPitchLocked = false;
            clampLiftPitchLock = 0f;
            yaw = aimYaw;
            pitch = aimPitch;
            currentDistance = FirstPerson ? 0f : Distance;
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

            if (FirstPerson)
            {
                // 물리 강체의 회전은 흔들릴 수 있으므로 위치는 골반의 월드 위치와
                // 세계의 위쪽만 사용한다. 수평 시선 방향으로만 조금 내밀어 얼굴 메시가 막지 않게 한다.
                Vector3 flatForward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                transform.position = raw + flatForward * FirstPersonForward;
                transform.rotation = rot;
                currentDistance = 0f;
                return;
            }

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
