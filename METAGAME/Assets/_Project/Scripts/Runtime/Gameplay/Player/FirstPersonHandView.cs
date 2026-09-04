using UnityEngine;
using UnityEngine.Rendering;

namespace Metagame.Gameplay
{
    /// <summary>
    /// 로컬 플레이어에게만 보이는 1인칭 손 표시.
    ///
    /// 물리 손은 PlayerRig가 계속 소유한다. 이 컴포넌트는 카메라 아래에
    /// 충돌 없는 메시를 별도로 두고, 입력·그립 상태와 카메라 움직임을 읽어
    /// 화면 표현만 만든다. 따라서 원격 아바타나 서버 물리에 영향을 주지 않는다.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class FirstPersonHandView : MonoBehaviour
    {
        public const string ResourceKey = "CharacterHand";

        /// <summary>
        /// 메인 씬의 SpaceBootstrap에서 조정하는 1인칭 손 표시 설정.
        /// 위치·회전은 카메라 로컬 기준이며, Euler 각도는 손 래퍼에 적용된다.
        /// </summary>
        [System.Serializable]
        public struct Settings
        {
            [Header("기본 위치")]
            [Tooltip("카메라 기준 왼손 대기 위치")]
            public Vector3 leftIdlePosition;
            [Tooltip("카메라 기준 오른손 대기 위치")]
            public Vector3 rightIdlePosition;

            [Header("대기 각도")]
            [Tooltip("왼손 대기 Euler 각도")]
            public Vector3 leftIdleEuler;
            [Tooltip("오른손 대기 Euler 각도")]
            public Vector3 rightIdleEuler;

            [Header("잡기 포즈")]
            [Tooltip("왼손 잡기 포즈에서 대기 위치에 더하는 오프셋")]
            public Vector3 leftGrabOffset;
            [Tooltip("오른손 잡기 포즈에서 대기 위치에 더하는 오프셋")]
            public Vector3 rightGrabOffset;
            [Tooltip("왼손 잡기 Euler 각도")]
            public Vector3 leftGrabEuler;
            [Tooltip("오른손 잡기 Euler 각도")]
            public Vector3 rightGrabEuler;

            [Header("표시 크기")]
            [Tooltip("FBX의 가장 긴 축을 맞출 길이(m)")]
            [Min(0.001f)]
            public float visualLength;
            [Tooltip("오른손 메시를 좌우 반전할지 여부")]
            public bool mirrorRightHand;

            [Header("보간")]
            [Tooltip("위치 포즈가 목표에 도달하는 시간(초)")]
            [Min(0.001f)]
            public float poseSmoothTime;
            [Tooltip("회전 포즈가 목표에 도달하는 속도")]
            [Min(0.001f)]
            public float rotationSmooth;

            [Header("이동 흔들림")]
            [Tooltip("플레이어 로컬 속도에 곱하는 위치 흔들림 계수")]
            public Vector3 movementSwayScale;
            [Tooltip("시점 yaw/pitch 변화에 곱하는 회전 흔들림 계수")]
            public float lookSwayMultiplier;
            [Tooltip("수평 이동 속도에 곱하는 roll 흔들림 계수")]
            public float movementRollSwayMultiplier;
            [Tooltip("위치 흔들림 최대 거리(m)")]
            [Min(0f)]
            public float maxSwayPosition;
            [Tooltip("회전 흔들림 최대 각도")]
            [Min(0f)]
            public float maxSwayAngle;

            public static Settings Default
            {
                get
                {
                    return new Settings
                    {
                        leftIdlePosition = new Vector3(-0.18f, -0.14f, 0.37f),
                        rightIdlePosition = new Vector3(0.18f, -0.14f, 0.37f),
                        leftIdleEuler = new Vector3(-78f, -7f, -3f),
                        rightIdleEuler = new Vector3(-78f, 7f, 3f),
                        leftGrabOffset = new Vector3(0.025f, 0.018f, 0.028f),
                        rightGrabOffset = new Vector3(-0.025f, 0.018f, 0.028f),
                        leftGrabEuler = new Vector3(-40f, -18f, -22f),
                        rightGrabEuler = new Vector3(-40f, 18f, 22f),
                        visualLength = 0.28f,
                        mirrorRightHand = true,
                        poseSmoothTime = 0.085f,
                        rotationSmooth = 16f,
                        movementSwayScale = new Vector3(0.010f, 0.004f, 0.004f),
                        lookSwayMultiplier = 0.55f,
                        movementRollSwayMultiplier = 1.1f,
                        maxSwayPosition = 0.055f,
                        maxSwayAngle = 9f
                    };
                }
            }
        }

        [SerializeField]
        Settings settings = Settings.Default;

        PlayerRig player;
        OrbitCamera orbitCamera;
        Transform viewRoot;
        Transform leftHand;
        Transform rightHand;

        Vector3 leftPositionVelocity;
        Vector3 rightPositionVelocity;
        Vector3 previousCameraPosition;
        float previousYaw;
        float previousPitch;
        bool initialized;
        bool warnedMissingPrefab;

        /// <summary>현재 왼손이 잡기 포즈를 표시하는가.</summary>
        public bool LeftGrabPoseActive { get; private set; }

        /// <summary>현재 오른손이 잡기 포즈를 표시하는가.</summary>
        public bool RightGrabPoseActive { get; private set; }

        /// <summary>테스트와 진단에서 표시용 손의 존재를 확인하는 읽기 전용 루트.</summary>
        public Transform LeftHandRoot { get { return leftHand; } }
        public Transform RightHandRoot { get { return rightHand; } }

        /// <summary>
        /// 카메라에 로컬 손을 붙인다. 같은 카메라에 이미 붙어 있으면 재사용해
        /// 씬 재초기화나 네트워크 스폰 순서가 중복 생성으로 이어지지 않게 한다.
        /// </summary>
        public static FirstPersonHandView Create(PlayerRig player, OrbitCamera camera)
        {
            return Create(player, camera, Settings.Default);
        }

        /// <summary>지정한 설정으로 카메라에 로컬 손을 붙인다.</summary>
        public static FirstPersonHandView Create(PlayerRig player, OrbitCamera camera, Settings settings)
        {
            if (player == null || camera == null) return null;

            var view = camera.GetComponent<FirstPersonHandView>();
            if (view == null) view = camera.gameObject.AddComponent<FirstPersonHandView>();
            view.Bind(player, camera, settings);
            return view;
        }

        void Bind(PlayerRig owner, OrbitCamera camera, Settings requestedSettings)
        {
            settings = NormalizeSettings(requestedSettings);
            if (player == owner && orbitCamera == camera && viewRoot != null) return;

            ClearVisuals();
            player = owner;
            orbitCamera = camera;
            previousCameraPosition = camera.transform.position;
            previousYaw = camera.Yaw;
            previousPitch = camera.Pitch;
            initialized = true;

            viewRoot = new GameObject("~1인칭 손").transform;
            viewRoot.SetParent(transform, false);

            var prefab = Resources.Load<GameObject>(ResourceKey);
            if (prefab == null)
            {
                if (!warnedMissingPrefab)
                {
                    warnedMissingPrefab = true;
                    Debug.LogWarning("[FirstPersonHandView] CharacterHand 리소스를 찾지 못했다. "
                                   + "에디터에서 ArtLink.BuildPrefabs 를 실행해야 한다.");
                }
                return;
            }

            leftHand = CreateHand(prefab, "왼손", -1f);
            rightHand = CreateHand(prefab, "오른손", 1f);
            UpdateVisibility();
        }

        Transform CreateHand(GameObject prefab, string name, float side)
        {
            var hand = new GameObject(name).transform;
            hand.SetParent(viewRoot, false);
            hand.localScale = new Vector3(side < 0f || !settings.mirrorRightHand ? 1f : -1f,
                                          1f, 1f);

            var model = Instantiate(prefab);
            model.name = name + " 메시";
            model.transform.SetParent(hand, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            NormalizeModel(model.transform, settings.visualLength);
            RemovePhysicsComponents(model);
            ConfigureRenderers(model);
            return hand;
        }

        static void NormalizeModel(Transform model, float visualLength)
        {
            var renderers = model.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return;

            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

            float longest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            if (longest > 0.001f && IsFinite(longest))
                model.localScale = Vector3.one * (visualLength / longest);
        }

        static void RemovePhysicsComponents(GameObject model)
        {
            foreach (var collider in model.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = false;
                Remove(collider);
            }

            foreach (var body in model.GetComponentsInChildren<Rigidbody>(true)) Remove(body);
            foreach (var joint in model.GetComponentsInChildren<Joint>(true)) Remove(joint);

            foreach (var animator in model.GetComponentsInChildren<Animator>(true))
            {
                animator.enabled = false;
                Remove(animator);
            }
        }

        static void Remove(Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }

        static void ConfigureRenderers(GameObject model)
        {
            foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }

        void LateUpdate()
        {
            if (!initialized || player == null || orbitCamera == null) return;

            float deltaTime = Mathf.Clamp(Time.deltaTime, 0.0001f, 0.05f);
            Vector3 cameraPosition = orbitCamera.transform.position;
            Vector3 cameraVelocity = (cameraPosition - previousCameraPosition) / deltaTime;
            Vector3 localVelocity = orbitCamera.transform.InverseTransformDirection(cameraVelocity);
            localVelocity = Vector3.ClampMagnitude(localVelocity, 6f);

            float yawDelta = Mathf.Clamp(Mathf.DeltaAngle(previousYaw, orbitCamera.Yaw), -30f, 30f);
            float pitchDelta = Mathf.Clamp(orbitCamera.Pitch - previousPitch, -30f, 30f);

            previousCameraPosition = cameraPosition;
            previousYaw = orbitCamera.Yaw;
            previousPitch = orbitCamera.Pitch;

            UpdateVisibility();
            if (viewRoot == null || !viewRoot.gameObject.activeSelf) return;

            var input = player.CurrentInput;
            bool toolHeld = player.HeldTool != null;
            LeftGrabPoseActive = input.GrabLeft || toolHeld || IsGripping(player.LeftHand);
            RightGrabPoseActive = input.GrabRight || toolHeld || IsGripping(player.RightHand);

            Vector3 swayPosition = new Vector3(-localVelocity.x * settings.movementSwayScale.x,
                                               -localVelocity.y * settings.movementSwayScale.y,
                                               -localVelocity.z * settings.movementSwayScale.z);
            swayPosition = Vector3.ClampMagnitude(swayPosition, settings.maxSwayPosition);

            Quaternion swayRotation = Quaternion.Euler(
                Mathf.Clamp(pitchDelta * settings.lookSwayMultiplier,
                            -settings.maxSwayAngle, settings.maxSwayAngle),
                Mathf.Clamp(-yawDelta * settings.lookSwayMultiplier,
                            -settings.maxSwayAngle, settings.maxSwayAngle),
                Mathf.Clamp(-localVelocity.x * settings.movementRollSwayMultiplier,
                            -settings.maxSwayAngle, settings.maxSwayAngle));

            UpdateHand(leftHand, settings.leftIdlePosition, settings.leftGrabOffset,
                       settings.leftIdleEuler, settings.leftGrabEuler, LeftGrabPoseActive,
                       swayPosition, swayRotation, ref leftPositionVelocity, deltaTime);
            UpdateHand(rightHand, settings.rightIdlePosition, settings.rightGrabOffset,
                       settings.rightIdleEuler, settings.rightGrabEuler, RightGrabPoseActive,
                       swayPosition, swayRotation, ref rightPositionVelocity, deltaTime);
        }

        static bool IsGripping(Hand hand)
        {
            return hand != null && (hand.Gripping || hand.Carrying);
        }

        void UpdateHand(Transform hand, Vector3 idlePosition, Vector3 grabOffset,
                               Vector3 idleEuler, Vector3 grabEuler, bool grabbing,
                               Vector3 swayPosition, Quaternion swayRotation,
                               ref Vector3 positionVelocity, float deltaTime)
        {
            if (hand == null) return;

            Vector3 poseOffset = grabbing ? grabOffset : Vector3.zero;
            Vector3 targetPosition = idlePosition + poseOffset + swayPosition;
            hand.localPosition = Vector3.SmoothDamp(hand.localPosition, targetPosition,
                                                    ref positionVelocity, settings.poseSmoothTime,
                                                    Mathf.Infinity, deltaTime);

            Quaternion idleRotation = Quaternion.Euler(idleEuler);
            Quaternion grabRotation = Quaternion.Euler(grabEuler);
            Quaternion targetRotation = swayRotation * (grabbing ? grabRotation : idleRotation);
            hand.localRotation = Quaternion.Slerp(hand.localRotation, targetRotation,
                                                  1f - Mathf.Exp(-settings.rotationSmooth * deltaTime));
        }

        void UpdateVisibility()
        {
            if (viewRoot == null) return;

            bool visible = player != null && player.IsLocal
                        && orbitCamera != null && orbitCamera.FirstPerson
                        && !player.IsRiding && !player.Downed;
            viewRoot.gameObject.SetActive(visible);
        }

        void ClearVisuals()
        {
            if (viewRoot == null) return;
            if (Application.isPlaying) Destroy(viewRoot.gameObject);
            else DestroyImmediate(viewRoot.gameObject);
            viewRoot = null;
            leftHand = null;
            rightHand = null;
        }

        void OnDestroy() => ClearVisuals();

        static Settings NormalizeSettings(Settings value)
        {
            var defaults = Settings.Default;
            // 새 필드가 추가된 기존 씬은 직렬화 데이터가 없는 동안 구조체가
            // 통째로 zero 값으로 들어올 수 있다. 세 가지 필수 수치가 모두
            // 비어 있을 때만 전체 기본값으로 복구하고, 나머지 0 값은 개발자가
            // 의도적으로 조정한 값으로 보존한다.
            bool missing = IsMissing(value.visualLength, 0.001f)
                        && IsMissing(value.poseSmoothTime, 0.001f)
                        && IsMissing(value.rotationSmooth, 0.001f);
            if (missing) return defaults;

            if (!IsFinite(value.leftIdlePosition)) value.leftIdlePosition = defaults.leftIdlePosition;
            if (!IsFinite(value.rightIdlePosition)) value.rightIdlePosition = defaults.rightIdlePosition;
            if (!IsFinite(value.leftIdleEuler)) value.leftIdleEuler = defaults.leftIdleEuler;
            if (!IsFinite(value.rightIdleEuler)) value.rightIdleEuler = defaults.rightIdleEuler;
            if (!IsFinite(value.leftGrabOffset)) value.leftGrabOffset = defaults.leftGrabOffset;
            if (!IsFinite(value.rightGrabOffset)) value.rightGrabOffset = defaults.rightGrabOffset;
            if (!IsFinite(value.leftGrabEuler)) value.leftGrabEuler = defaults.leftGrabEuler;
            if (!IsFinite(value.rightGrabEuler)) value.rightGrabEuler = defaults.rightGrabEuler;
            if (!IsFinite(value.visualLength) || value.visualLength <= 0.001f)
                value.visualLength = defaults.visualLength;
            if (!IsFinite(value.poseSmoothTime) || value.poseSmoothTime <= 0.001f)
                value.poseSmoothTime = defaults.poseSmoothTime;
            if (!IsFinite(value.rotationSmooth) || value.rotationSmooth <= 0.001f)
                value.rotationSmooth = defaults.rotationSmooth;
            if (!IsFinite(value.movementSwayScale)) value.movementSwayScale = defaults.movementSwayScale;
            if (!IsFinite(value.lookSwayMultiplier)) value.lookSwayMultiplier = defaults.lookSwayMultiplier;
            if (!IsFinite(value.movementRollSwayMultiplier))
                value.movementRollSwayMultiplier = defaults.movementRollSwayMultiplier;
            if (!IsFinite(value.maxSwayPosition) || value.maxSwayPosition < 0f)
                value.maxSwayPosition = defaults.maxSwayPosition;
            if (!IsFinite(value.maxSwayAngle) || value.maxSwayAngle < 0f)
                value.maxSwayAngle = defaults.maxSwayAngle;
            return value;
        }

        static bool IsMissing(float value, float minimum)
        {
            return !IsFinite(value) || value <= minimum;
        }

        static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }
    }
}
