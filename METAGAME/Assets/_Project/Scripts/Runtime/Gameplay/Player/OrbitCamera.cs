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
    ///  · aimYaw/aimPitch — 기존 호출부와의 호환을 위한 조작 기준 미러
    ///
    /// 이동·조준·캐릭터 정면은 항상 현재 카메라 시점을 기준으로 한다. Alt 는 시야를
    /// 자유롭게 돌리는 기능만 바꾸며, 시점과 조작 기준을 따로 얼리거나 놓을 때 되돌리지 않는다.
    /// </summary>
    public class OrbitCamera : MonoBehaviour
    {
        public Transform Target;
        public bool FirstPerson;
        public float Distance = Tuning.CamDistance;
        public float TargetHeight = Tuning.CamHeight;

        /// <summary>
        /// 따라갈 지점을 얼마나 빨리 좇는가. 클수록 빠르고, 그만큼 목표의 떨림이 그대로 보인다.
        /// 시정수는 <c>1/FocusSmoothing</c> 초다 — 기본값 16 이면 62ms 뒤처진다.
        ///
        /// <para><b>왜 밖에서 바꿀 수 있게 두는가</b> (S15P21B202-328). 이 스무딩은 목표가
        /// 물리로 직접 움직이는 몸일 때를 위한 것이다. 그런데 참가자 화면에서 내 몸은
        /// 이미 두 번 다듬어진 뒤에 온다 — 서버가 보낸 자세를 Mirror 의 스냅샷 보간이
        /// 잇고, 골반 강체가 다시 물리 스텝 사이를 보간한다. 그 위에 62ms 를 또 얹으면
        /// 남는 것은 부드러움이 아니라 지연뿐이라, 그 경우에는 <see cref="RemoteAvatar"/>
        /// 가 이 값을 올려 잡는다.</para>
        /// </summary>
        public float FocusSmoothing = 16f;
        public float FirstPersonForward = 0.20f;
        public float Sensitivity = 2.4f;
        public bool InvertLookY;
        public float MinPitch = -42f;
        public float MaxPitch = 74f;
        [Header("자유 시점 (Alt)")]
        public float FreeMinPitch = -78f;
        public float FreeMaxPitch = 84f;
        public float ReturnSpeed = 7.5f;      // 기존 직렬화 호환용. 현재는 Alt 해제 복귀를 사용하지 않는다
        [Header("고정쇠 하중감")]
        [Range(0.05f, 1f)]
        public float ClampUpwardLookScale = 0.28f;
        public float ClampDownwardPullSpeed = 8f;

        public Camera Cam { get; private set; }

        float yaw;
        float pitch = 12f;
        float aimYaw;
        float aimPitch = 12f;
        bool clampLiftResistance;
        float clampLiftResistanceBlend;
        bool clampLiftPitchLocked;
        float clampLiftPitchLock;
        bool vehicleView;
        bool downedView;
        Transform downedEyes;
        float downedSeconds;
        bool savedFirstPerson;
        float savedDistance;
        float savedTargetHeight;
        float savedMinPitch;
        float savedMaxPitch;
        float savedAimPitch;
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

        /// <summary>Alt 를 누르고 있는가. 시점만 자유롭게 조작한다.</summary>
        public bool FreeLook { get; private set; }

        /// <summary>기절 시점인가(기획 §10.3).</summary>
        public bool DownedView => downedView;

        /// <summary>
        /// 화면 전체를 기울이는 기준 회전. 평소에는 항등이다.
        ///
        /// <see cref="Metagame.Gameplay.Parts.TransportVehicle.LevelOffset"/> 을
        /// <see cref="PlayerRig"/> 가 매 물리 스텝 넣어 준다.
        /// </summary>
        public Quaternion ViewTilt => viewTilt;

        Quaternion viewTilt = Quaternion.identity;

        /// <summary>
        /// 기준 회전을 정한다. 항등이면 평소와 같다.
        ///
        /// <b>부드럽게 만들지 않는다.</b> 기체의 자세가 이미 물리로 연속이고, 굴림은
        /// 수평에서 시작해 수평으로 끝나므로 들어올 때와 나갈 때 튀지 않는다.
        /// </summary>
        public void SetViewTilt(Quaternion tilt) => viewTilt = tilt;

        /// <summary>
        /// 화면을 갱신한다. EditMode 검증에서 <c>LateUpdate</c> 가 돌지 않으므로
        /// 갈라 뒀다 — 이 저장소가 <c>Initialize()</c> · <c>Tick()</c> 로 맞춰 온 방식이다.
        /// </summary>
        public void Tick() => LateUpdate();

        /// <summary>
        /// 아직 시야가 몸과 함께 넘어가는 중인가.
        ///
        /// 이 동안에는 마우스가 시점을 돌리지 않는다 — 몸이 넘어지는 회전과 마우스 회전이
        /// 섞이면 무엇이 넘어지는 것인지 알 수 없다.
        /// </summary>
        public bool DownedFalling =>
            downedView && downedSeconds < PhysicsScale.DownedFirstPersonSeconds;

        /// <summary>기절한 뒤 지난 시간(초). 표시와 검증이 쓴다.</summary>
        public float DownedSeconds => downedSeconds;

        /// <summary>
        /// 기절 시점을 켜고 끈다(기획 §10.1 · §10.3).
        ///
        /// <see cref="SetVehicleView"/> 와 같은 구조다 — 상태를 저장하고 모드를 강제한 뒤
        /// 나갈 때 되돌린다. §10.3 "구조되면 1인칭으로 돌아온다" 가 그 복원이다.
        ///
        /// <paramref name="eyes"/> 는 <b>머리 본</b>이다. 그 회전을 따라가야 시야가 몸과
        /// 함께 넘어간다 — 평소에는 일부러 쓰지 않는 값이다(<see cref="LateUpdate"/> 의
        /// 주석 참고).
        /// </summary>
        public void SetDownedView(bool on, Transform eyes = null)
        {
            if (on == downedView) return;

            if (on)
            {
                // 탑승 중이었다면 그쪽이 이미 값을 들고 있다. 두 번 저장하면 복원이 어긋난다.
                if (!vehicleView)
                {
                    savedFirstPerson = FirstPerson;
                    savedDistance = Distance;
                    savedTargetHeight = TargetHeight;
                    savedMinPitch = MinPitch;
                    savedMaxPitch = MaxPitch;
                    savedAimPitch = aimPitch;
                }

                downedView = true;
                downedEyes = eyes;
                downedSeconds = 0f;

                FirstPerson = true;
                FreeLook = false;
            }
            else
            {
                downedView = false;
                downedEyes = null;
                downedSeconds = 0f;

                if (!vehicleView)
                {
                    FirstPerson = savedFirstPerson;
                    Distance = savedDistance;
                    TargetHeight = savedTargetHeight;
                    MinPitch = savedMinPitch;
                    MaxPitch = savedMaxPitch;
                    aimPitch = Mathf.Clamp(savedAimPitch, MinPitch, MaxPitch);
                    pitch = Mathf.Clamp(pitch, MinPitch, MaxPitch);
                    currentDistance = FirstPerson ? 0f : Distance;
                }
            }

            focusReady = false;
        }

        /// <summary>
        /// 기절 시점의 시간을 센다. 소유자가 매 프레임 부른다.
        ///
        /// <c>Time</c> 을 직접 읽지 않는 이유는 저장소의 다른 것들과 같다 — 검증이 5초를
        /// 실제로 기다릴 수는 없다.
        /// </summary>
        public void TickDownedView(float deltaTime)
        {
            if (!downedView || deltaTime <= 0f) return;

            var wasFalling = DownedFalling;
            downedSeconds += deltaTime;

            // 넘어지기가 끝나면 3인칭으로 물러난다(§10.3 "쓰러진 캐릭터 <b>중심</b>의
            // 3인칭 자유 시점").
            if (!wasFalling || DownedFalling) return;

            FirstPerson = false;
            currentDistance = 0f;   // 몸에서 서서히 물러난다
            FrameDownedBody();
        }

        /// <summary>
        /// 쓰러진 몸이 화면에 들어오게 시점을 잡는다(기획 §10.3, S15P21B202-267).
        ///
        /// <b>쓰러진 머리의 회전을 이어받으면 안 된다.</b> 예전에는 그렇게 했다.
        ///
        /// <code>
        /// var euler = downedEyes.rotation.eulerAngles;   // 땅에 처박힌 머리
        /// yaw   = euler.y;
        /// pitch = Clamp(NormalizePitch(euler.x), MinPitch, MaxPitch);
        /// </code>
        ///
        /// 얼굴이 땅을 보고 있으면 각도가 극단으로 가 <see cref="MinPitch"/>(-42도)에서
        /// 잘리고 카메라가 지면 아래를 향한다. 옆으로 누웠으면 <c>eulerAngles</c> 의 x·y
        /// 분해에 roll 이 섞여 엉뚱한 방향이 된다 — 어느 쪽이든 <b>몸이 프레임에 없고</b>,
        /// 그러면 S15P21B202-267 이 렌더링을 살려도 보이지 않는다.
        ///
        /// <b>고정하는 것은 전환 순간뿐이다.</b> 그 뒤에는 마우스로 자유롭게 돌린다 —
        /// §10.3 의 "자유 시점" 을 지킨다.
        ///
        /// 좌우는 머리가 <b>누운 방향</b>에서 가져온다. 머리의 회전이 아니라 골반에서
        /// 머리로 향하는 벡터라, 어떤 자세로 넘어져도 신뢰할 수 있다. 그 방향을 등지고
        /// 서면 몸이 세로로 화면에 들어온다.
        /// </summary>
        void FrameDownedBody()
        {
            pitch = PhysicsScale.DownedThirdPersonPitchDegrees;

            if (Target == null || downedEyes == null) return;

            var along = downedEyes.position - Target.position;
            along.y = 0f;

            // 완전히 접혀 누워 골반과 머리가 겹치면 지금 좌우를 그대로 둔다.
            if (along.sqrMagnitude < 1e-4f) return;

            yaw = Mathf.Atan2(along.x, along.z) * Mathf.Rad2Deg;
        }

        public void SetFreeLook(bool on)
        {
            if (on == FreeLook) return;
            FreeLook = on;
        }

        /// <summary>개인 설정의 감도와 Y축 반전을 이 카메라에 즉시 반영한다.</summary>
        public void ApplySettings(GameSettingsData.ControlSettings settings)
        {
            if (settings == null) return;
            Sensitivity = Mathf.Clamp(settings.lookSensitivity, 0.1f, 5f);
            InvertLookY = settings.invertLookY;
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
        /// 운반 장치에 탑승하면 조종석 1인칭으로 전환한다.
        /// 하차할 때 기존 1인칭/3인칭 설정과 시야 각도 제한을 되돌린다.
        ///
        /// 예전에는 장치 위 10m 에서 내려다보는 3인칭이었다. 탑승자를 위에서 보는 화면이라
        /// "무엇에 타고 있다"가 전달되지 않았다. 지금은 탑승자의 머리에서 보고, 탑승자는
        /// 감춰지며(<see cref="Doll.SetVisible"/>) 조종석 지오메트리가 시야를 감싼다.
        ///
        /// 시야 각도 제한은 플레이어가 쓰던 값을 그대로 쓴다. 조종석 안에서 아래를 보면
        /// 기체 내부가 보이는데, 그 범위를 막는 것은 카메라가 아니라 조종석 지오메트리의
        /// 몫이다 — 실제 에셋이 들어오면 그쪽에서 자연스럽게 정리된다.
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
                savedAimPitch = aimPitch;   // 탑승 중 0 으로 눕히므로 원래 값을 들고 있어야 한다

                vehicleView = true;
                FirstPerson = true;
                Distance = 0f;
                TargetHeight = Tuning.CamHeight;   // 골반 기준 머리 높이 — 걸을 때와 같다
                FreeLook = false;
                yaw = aimYaw;
                pitch = 0f;                        // 조종석에서 수평을 본다
                aimPitch = 0f;
            }
            else
            {
                vehicleView = false;
                FirstPerson = savedFirstPerson;
                Distance = savedDistance;
                TargetHeight = savedTargetHeight;
                MinPitch = savedMinPitch;
                MaxPitch = savedMaxPitch;
                aimPitch = Mathf.Clamp(savedAimPitch, MinPitch, MaxPitch);
                pitch = Mathf.Clamp(pitch, MinPitch, MaxPitch);
                currentDistance = FirstPerson ? 0f : Distance;
            }

            focusReady = false;
            LateUpdate();
        }

        public void Steer(Vector2 look)
        {
            // 넘어지는 동안에는 마우스가 시점을 돌리지 않는다(기획 §10.3,
            // S15P21B202-259). 몸이 넘어지는 회전과 마우스 회전이 섞이면 무엇이
            // 넘어지는 것인지 알 수 없다. 3인칭으로 바뀌면 다시 열린다.
            if (DownedFalling) return;

            float lo = VehicleView ? MinPitch : (FreeLook ? FreeMinPitch : MinPitch);
            float hi = VehicleView ? MaxPitch : (FreeLook ? FreeMaxPitch : MaxPitch);

            float targetResistance = clampLiftResistance && !FreeLook ? 1f : 0f;
            clampLiftResistanceBlend = Mathf.MoveTowards(
                clampLiftResistanceBlend, targetResistance, 10f * Time.deltaTime);

            float sensitivity = Mathf.Max(0.01f, Sensitivity);
            float verticalLook = InvertLookY ? -look.y : look.y;
            if (verticalLook > 0f && clampLiftResistanceBlend > 0f)
            {
                float response = Mathf.Lerp(1f, ClampUpwardLookScale, clampLiftResistanceBlend);
                verticalLook *= response;
            }

            yaw += look.x * sensitivity;
            pitch = Mathf.Clamp(pitch - verticalLook * sensitivity, lo, hi);

            // 위로 올리려는 동안에는 시점이 조금씩 아래로 되돌아가도록 해,
            // 무거운 고정쇠를 억지로 드는 듯한 끙끙거리는 반응을 만든다.
            if (clampLiftResistanceBlend > 0f)
                pitch = Mathf.Clamp(pitch + ClampDownwardPullSpeed
                                    * clampLiftResistanceBlend * Time.deltaTime, lo, hi);

            // pitch가 작아질수록 화면이 위를 본다. 기준을 넘은 시점의 pitch보다
            // 작아지는 것만 막아, 아래로 보는 조작은 허용하고 추가 상승만 제한한다.
            if (clampLiftPitchLocked)
                pitch = Mathf.Max(pitch, clampLiftPitchLock);

            // Alt 중에도 현재 시점을 곧바로 조작 기준으로 쓴다. aim 필드는 공개 API와
            // 탑승 시야 전환의 기존 상태 보존을 위해 남겨 두되 항상 현재 시점을 미러링한다.
            aimYaw = yaw;
            aimPitch = Mathf.Clamp(pitch, MinPitch, MaxPitch);
        }

        /// <summary>순간이동(부활) 뒤에 카메라가 세계를 가로질러 날아가지 않게.</summary>
        public void SnapToTarget()
        {
            focusReady = false;
            FreeLook = false;
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

            Vector3 raw = Target.position + Vector3.up * TargetHeight;
            if (!focusReady) { smoothFocus = raw; focusReady = true; }
            else smoothFocus = Vector3.Lerp(smoothFocus, raw,
                                            1f - Mathf.Exp(-FocusSmoothing * Time.deltaTime));

            // <b>기준 회전을 앞에 곱한다</b>(S15P21B202-353). 탑승체가 운석에 맞아
            // 돌면 화면도 함께 돌아야 한다 — 그러지 않으면 기체는 도는데 세상은
            // 수평이라 무엇이 일어났는지 알 수 없다.
            //
            // <see cref="ViewRotation"/> 은 건드리지 않는다. 이동·조준은 계속 수평
            // 기준이어야 한다 — 굴는 동안 조작이 뒤집히면 복구가 불가능해진다.
            Quaternion rot = viewTilt * Quaternion.Euler(pitch, yaw, 0f);

            // 기절해 넘어지는 동안에는 <b>머리 본의 회전</b>을 그대로 쓴다(기획 §10.3,
            // S15P21B202-259). 아래 1인칭 경로가 회전을 세계 기준으로만 잡는 것은 걸을 때
            // 래그돌 떨림이 시야에 들어오지 않게 하려는 것인데, 넘어질 때는 그 떨림이
            // 곧 정보다.
            if (DownedFalling && downedEyes != null)
            {
                transform.position = downedEyes.position;
                transform.rotation = downedEyes.rotation;
                currentDistance = 0f;
                return;
            }

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

        // 아래 방향은 모두 현재 카메라 시점에서 뽑는다. 수평 이동은 pitch를 버리고,
        // 조준 방향은 pitch를 포함한다. 따라서 Alt 중에도 시점·이동·조준이 같은 축을 쓴다.
        Quaternion ViewRotation { get { return Quaternion.Euler(pitch, yaw, 0f); } }

        public Vector3 PlanarForward
        {
            get { Vector3 f = ViewRotation * Vector3.forward; f.y = 0f; return f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward; }
        }

        public Vector3 PlanarRight
        {
            get { Vector3 r = ViewRotation * Vector3.right; r.y = 0f; return r.sqrMagnitude > 1e-4f ? r.normalized : Vector3.right; }
        }

        public Vector3 AimDirection { get { return ViewRotation * Vector3.forward; } }

        /// <summary>
        /// 입력 벡터를 현재 카메라의 수평 축으로 변환한다. 모든 WASD 이동 호출부가
        /// 같은 변환을 사용하도록 카메라에 한 곳으로 모아 둔다.
        /// </summary>
        public Vector3 CameraRelativeMove(Vector2 input)
        {
            if (input.sqrMagnitude > 1f) input.Normalize();

            Vector3 move = PlanarRight * input.x + PlanarForward * input.y;
            return move.sqrMagnitude > 1f ? move.normalized : move;
        }

        /// <summary>현재 카메라 yaw. 설정하면 조작 기준 미러도 같이 맞춘다.</summary>
        public float Yaw
        {
            get { return yaw; }
            set { yaw = value; aimYaw = value; }
        }

        public float Pitch
        {
            get { return pitch; }
            set { pitch = Mathf.Clamp(value, MinPitch, MaxPitch); aimPitch = pitch; }
        }

        /// <summary>지금 보고 있는 각도.</summary>
        public float ViewYaw { get { return yaw; } }
        public float ViewPitch { get { return pitch; } }
    }
}
