using Metagame.Gameplay;
using UnityEngine;

namespace Metagame.UI
{
    /// <summary>
    /// Tab 태블릿을 1인칭 카메라 앞에 실제 3D 물체로 들어 올린다.
    /// 화면 내용은 Stage1TabletScreen이 모델의 화면 재질에 직접 렌더링한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Stage1TabletView : MonoBehaviour
    {
        public const string ResourceKey = "PF_Stage1_Tablet";

        [Header("화면 면")]
        [SerializeField] Transform screenAnchor;
        [SerializeField] Vector2 screenSize = new Vector2(0.25f, 0.13f);

        [Header("1인칭 휴대 포즈")]
        [SerializeField] Vector3 openLocalPosition = new Vector3(-0.05f, -0.08f, 0.29f);
        [SerializeField] Vector3 openLocalEuler = new Vector3(8f, -12f, 8f);
        [SerializeField] Vector3 closedLocalPosition = new Vector3(0.18f, -0.55f, 0.42f);
        [SerializeField] Vector3 closedLocalEuler = new Vector3(34f, -18f, -10f);
        [SerializeField, Min(0.01f)] float poseSpeed = 12f;

        Camera viewCamera;
        Stage1Exploration exploration;
        Renderer[] renderers;
        float visibility;

        public bool IsOpen => exploration != null && exploration.TabletOpen;

        public static Stage1TabletView Create(Camera camera, Stage1Exploration state)
        {
            if (camera == null || state == null) return null;

            var prefab = Resources.Load<GameObject>(ResourceKey);
            if (prefab == null)
            {
                Debug.LogWarning("[Stage1TabletView] 태블릿 프리팹을 찾지 못했습니다: Resources/"
                                 + ResourceKey);
                return null;
            }

            var instance = Instantiate(prefab, camera.transform, false);
            instance.name = "1인칭 태블릿";
            var view = instance.GetComponent<Stage1TabletView>();
            if (view == null) view = instance.AddComponent<Stage1TabletView>();
            view.Initialize(camera, state);
            return view;
        }

        public void Initialize(Camera camera, Stage1Exploration state)
        {
            viewCamera = camera;
            exploration = state;
            renderers = GetComponentsInChildren<Renderer>(true);
            var screenRenderer = transform.Find("ScreenAnchor/DynamicScreenSurface")
                ?.GetComponent<Renderer>();
            var tabletScreen = GetComponent<Stage1TabletScreen>();
            if (tabletScreen == null) tabletScreen = gameObject.AddComponent<Stage1TabletScreen>();
            tabletScreen.Initialize(exploration, screenRenderer);
            visibility = IsOpen ? 1f : 0f;
            ApplyPose(visibility);
            SetRenderersVisible(visibility > 0.001f);
        }

        /// <summary>에디터 프리팹 빌더가 실제 모델의 화면 면과 휴대 포즈를 기록한다.</summary>
        public void ConfigurePrefab(Transform displayAnchor, Vector2 displaySize)
        {
            screenAnchor = displayAnchor;
            screenSize = displaySize;
        }

        void LateUpdate()
        {
            var target = IsOpen ? 1f : 0f;
            visibility = Mathf.MoveTowards(visibility, target, poseSpeed * Time.deltaTime);
            SetRenderersVisible(visibility > 0.001f);
            ApplyPose(SmoothStep(visibility));
        }

        void ApplyPose(float amount)
        {
            transform.localPosition = Vector3.Lerp(closedLocalPosition, openLocalPosition, amount);
            transform.localRotation = Quaternion.Slerp(
                Quaternion.Euler(closedLocalEuler), Quaternion.Euler(openLocalEuler), amount);
        }

        void SetRenderersVisible(bool visible)
        {
            if (renderers == null) return;
            foreach (var item in renderers)
                if (item != null) item.enabled = visible;
        }

        /// <summary>
        /// 모델 화면의 네 모서리를 현재 게임 화면에 투영한다.
        /// 반환 Rect는 OnGUI 좌표계(왼쪽 위가 원점)다.
        /// </summary>
        public bool TryGetGuiScreenRect(out Rect rect)
        {
            rect = default;
            if (!IsOpen || visibility < 0.15f || viewCamera == null || screenAnchor == null)
                return false;

            var half = screenSize * 0.5f;
            var corners = new[]
            {
                screenAnchor.TransformPoint(new Vector3(-half.x, -half.y, 0f)),
                screenAnchor.TransformPoint(new Vector3(-half.x,  half.y, 0f)),
                screenAnchor.TransformPoint(new Vector3( half.x, -half.y, 0f)),
                screenAnchor.TransformPoint(new Vector3( half.x,  half.y, 0f)),
            };

            var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            foreach (var world in corners)
            {
                var screen = viewCamera.WorldToScreenPoint(world);
                if (screen.z <= 0f) return false;
                min = Vector2.Min(min, new Vector2(screen.x, screen.y));
                max = Vector2.Max(max, new Vector2(screen.x, screen.y));
            }

            if (max.x - min.x < 32f || max.y - min.y < 24f) return false;
            rect = new Rect(min.x, Screen.height - max.y, max.x - min.x, max.y - min.y);
            return true;
        }

        static float SmoothStep(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * (3f - 2f * value);
        }
    }
}
