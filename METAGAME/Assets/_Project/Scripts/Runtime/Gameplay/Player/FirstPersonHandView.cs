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
        /// 위치·회전은 카메라 로컬 기준이다. 대기 Euler는 손목 래퍼에,
        /// 잡기 Euler·스케일은 그 아래 손 메시 래퍼에 각각 적용된다.
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

            [Header("잡기 손 포즈")]
            [Tooltip("왼손목을 고정한 채 왼손 메시를 이동하는 오프셋")]
            public Vector3 leftGrabOffset;
            [Tooltip("오른손목을 고정한 채 오른손 메시를 이동하는 오프셋")]
            public Vector3 rightGrabOffset;
            [Tooltip("왼손 잡기 시 손 메시의 상대 Euler 각도. 손목에는 적용되지 않음")]
            public Vector3 leftGrabEuler;
            [Tooltip("오른손 잡기 시 손 메시의 상대 Euler 각도. 손목에는 적용되지 않음")]
            public Vector3 rightGrabEuler;
            [Tooltip("왼손 잡기 시 손 메시에 곱하는 상대 스케일. 손가락을 움켜쥔 인상을 조정")]
            public Vector3 leftGrabScale;
            [Tooltip("오른손 잡기 시 손 메시에 곱하는 상대 스케일. 손가락을 움켜쥔 인상을 조정")]
            public Vector3 rightGrabScale;

            [Header("표시 크기")]
            [Tooltip("FBX의 가장 긴 축을 맞출 길이(m)")]
            [Min(0.001f)]
            public float visualLength;
            [Tooltip("원본 FBX의 좌우 방향을 왼손에 맞게 반전할지 여부")]
            public bool mirrorLeftHand;
            [Tooltip("원본 FBX의 좌우 방향을 오른손에 맞게 반전할지 여부")]
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
                        leftIdlePosition = new Vector3(-0.34f, -0.24f, 0.42f),
                        rightIdlePosition = new Vector3(0.34f, -0.24f, 0.42f),
                        leftIdleEuler = new Vector3(168f, 25f, -12f),
                        rightIdleEuler = new Vector3(168f, -25f, 12f),
                        // The mesh points along local -Z; reach forward in wrist space.
                        leftGrabOffset = new Vector3(0f, 0f, -0.06f),
                        rightGrabOffset = new Vector3(0f, 0f, -0.06f),
                        leftGrabEuler = new Vector3(-6f, 0f, 0f),
                        rightGrabEuler = new Vector3(-6f, 0f, 0f),
                        leftGrabScale = new Vector3(1f, 0.96f, 1.03f),
                        rightGrabScale = new Vector3(1f, 0.96f, 1.03f),
                        visualLength = 0.22f,
                        mirrorLeftHand = false,
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

#if UNITY_EDITOR
        // Preview only: player builds use the serialized scene settings during Create.
        [ContextMenu("Apply Lower Corner Hand Preset")]
        public void ApplyLowerCornerHandPreset()
        {
            settings = Settings.Default;
            if (player == null || orbitCamera == null) return;
            ClearVisuals();
            Bind(player, orbitCamera, settings);
        }
#endif

        PlayerRig player;
        OrbitCamera orbitCamera;
        Transform viewRoot;
        Transform leftHand;
        Transform rightHand;
        Transform leftVisual;
        Transform rightVisual;

        Vector3 leftPositionVelocity;
        Vector3 rightPositionVelocity;
        Vector3 leftVisualPositionVelocity;
        Vector3 rightVisualPositionVelocity;
        Vector3 leftVisualBaseScale = Vector3.one;
        Vector3 rightVisualBaseScale = Vector3.one;
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
        /// <summary>손목 래퍼와 분리된 왼손 메시 래퍼.</summary>
        public Transform LeftHandVisual { get { return leftVisual; } }
        /// <summary>손목 래퍼와 분리된 오른손 메시 래퍼.</summary>
        public Transform RightHandVisual { get { return rightVisual; } }

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

            leftHand = CreateHand(prefab, "왼손", -1f, out leftVisual, out leftVisualBaseScale);
            rightHand = CreateHand(prefab, "오른손", 1f, out rightVisual, out rightVisualBaseScale);
            PlayerAppearance.Tint(viewRoot.GetComponentsInChildren<Renderer>(true),
                CustomizationData.TintColor(CustomizationData.Load().Tint));
            UpdateVisibility();
        }

        Transform CreateHand(GameObject prefab, string name, float side,
                             out Transform visual, out Vector3 visualBaseScale)
        {
            var hand = new GameObject(name).transform;
            hand.SetParent(viewRoot, false);
            bool mirror = side < 0f ? settings.mirrorLeftHand : settings.mirrorRightHand;
            hand.localScale = new Vector3(mirror ? -1f : 1f, 1f, 1f);

            var model = Instantiate(prefab);
            model.name = name + " 메시";
            model.transform.SetParent(hand, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            NormalizeModel(model.transform, settings.visualLength);
            RemovePhysicsComponents(model);
            ConfigureRenderers(model);
            visual = model.transform;
            visualBaseScale = visual.localScale;
            // Set the initial pose after size normalization to keep scaling independent of pose.
            hand.localPosition = side < 0f ? settings.leftIdlePosition : settings.rightIdlePosition;
            hand.localRotation = Quaternion.Euler(side < 0f
                ? settings.leftIdleEuler : settings.rightIdleEuler);
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
            bool toolHeld = player.HeldTool != null || player.PresentationTool != null;
            var bat = player.PresentationTool as MeteorBat;
            if (bat == null && player.HeldTool != null) bat = player.HeldTool.GetComponentInChildren<MeteorBat>();
            // 개별 손의 기본 배치는 유지하고 공통 부모에 스윙만 적용한다.
            // 다음 프레임의 손 보간값에 스윙을 누적하지 않는다.
            viewRoot.localRotation = bat != null
                ? bat.FirstPersonSwingRotation(orbitCamera.transform)
                : Quaternion.identity;
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
                       leftVisual, leftVisualBaseScale, settings.leftIdleEuler,
                       settings.leftGrabEuler, settings.leftGrabScale, LeftGrabPoseActive,
                       swayPosition, swayRotation, ref leftPositionVelocity,
                       ref leftVisualPositionVelocity, deltaTime);
            UpdateHand(rightHand, settings.rightIdlePosition, settings.rightGrabOffset,
                       rightVisual, rightVisualBaseScale, settings.rightIdleEuler,
                       settings.rightGrabEuler, settings.rightGrabScale, RightGrabPoseActive,
                       swayPosition, swayRotation, ref rightPositionVelocity,
                       ref rightVisualPositionVelocity, deltaTime);
        }

        static bool IsGripping(Hand hand)
        {
            return hand != null && (hand.Gripping || hand.Carrying);
        }

        void UpdateHand(Transform wrist, Vector3 idlePosition, Vector3 grabOffset,
                        Transform visual, Vector3 visualBaseScale,
                        Vector3 wristEuler, Vector3 grabEuler, Vector3 grabScale,
                        bool grabbing, Vector3 swayPosition, Quaternion swayRotation,
                        ref Vector3 positionVelocity,
                        ref Vector3 visualPositionVelocity, float deltaTime)
        {
            if (wrist == null) return;

            // 이동·시점 흔들림과 대기 자세는 손목 래퍼에만 적용한다.
            // 잡기 입력이 들어와도 손목이 꺾이지 않도록 잡기 오프셋은 아래
            // 메시 래퍼로 분리한다.
            Vector3 targetPosition = idlePosition + swayPosition;
            wrist.localPosition = Vector3.SmoothDamp(wrist.localPosition, targetPosition,
                                                    ref positionVelocity, settings.poseSmoothTime,
                                                    Mathf.Infinity, deltaTime);

            Quaternion targetRotation = swayRotation * Quaternion.Euler(wristEuler);
            wrist.localRotation = Quaternion.Slerp(wrist.localRotation, targetRotation,
                                                  1f - Mathf.Exp(-settings.rotationSmooth * deltaTime));

            if (visual == null) return;

            Vector3 visualTargetPosition = grabbing ? grabOffset : Vector3.zero;
            visual.localPosition = Vector3.SmoothDamp(visual.localPosition, visualTargetPosition,
                                                       ref visualPositionVelocity,
                                                       settings.poseSmoothTime,
                                                       Mathf.Infinity, deltaTime);

            Quaternion visualTargetRotation = grabbing
                ? Quaternion.Euler(grabEuler)
                : Quaternion.identity;
            visual.localRotation = Quaternion.Slerp(visual.localRotation, visualTargetRotation,
                                                     1f - Mathf.Exp(-settings.rotationSmooth * deltaTime));

            Vector3 visualTargetScale = visualBaseScale;
            if (grabbing)
                visualTargetScale = Vector3.Scale(visualBaseScale, grabScale);
            visual.localScale = Vector3.Lerp(visual.localScale, visualTargetScale,
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
            leftVisual = null;
            rightVisual = null;
            leftPositionVelocity = Vector3.zero;
            rightPositionVelocity = Vector3.zero;
            leftVisualPositionVelocity = Vector3.zero;
            rightVisualPositionVelocity = Vector3.zero;
            leftVisualBaseScale = Vector3.one;
            rightVisualBaseScale = Vector3.one;
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
            if (!IsValidScale(value.leftGrabScale)) value.leftGrabScale = defaults.leftGrabScale;
            if (!IsValidScale(value.rightGrabScale)) value.rightGrabScale = defaults.rightGrabScale;
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

        static bool IsValidScale(Vector3 value)
        {
            return IsFinite(value) && value.x > 0.001f && value.y > 0.001f && value.z > 0.001f;
        }
    }
}
