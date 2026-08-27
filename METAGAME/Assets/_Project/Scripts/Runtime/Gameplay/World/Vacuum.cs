using UnityEngine;

namespace Metagame.Gameplay
{
    /// <summary>
    /// 진공의 빛. 지구 하늘과 정반대로 맞춘다.
    ///
    ///  · 안개 없음 — 공기가 없으니 먼 것도 또렷하다. 그래서 거리감이 이상해진다(맞는 얘기다)
    ///  · 하늘빛 없음 — 그림자가 새까맣다. 대신 발밑 레골리스의 반사광만 조금 넣는다
    ///  · 해는 아주 세고 하얗다. 그림자 경계가 칼같다
    /// </summary>
    [ExecuteAlways]
    public class Vacuum : MonoBehaviour
    {
        [Header("해 — 대기가 없어 하얗고 세다")]
        public Color sunColor = new Color(1.00f, 0.99f, 0.96f);
        [Range(0f, 3f)] public float sunIntensity = 1.75f;
        public Vector3 sunAngles = new Vector3(34f, -46f, 0f);
        [Range(0f, 1f)] public float shadowStrength = 0.94f;

        [Header("행성 반사광 — 그림자가 완전히 새까맣지 않게 하는 유일한 빛")]
        public Color fillColor = new Color(0.42f, 0.56f, 0.86f);
        [Range(0f, 1f)] public float fillIntensity = 0.16f;
        public Vector3 fillAngles = new Vector3(-24f, 128f, 0f);

        [Header("앰비언트 — 위는 까맣고 아래(레골리스)만 조금 밝다")]
        public Color ambientSky = new Color(0.05f, 0.06f, 0.10f);
        public Color ambientEquator = new Color(0.11f, 0.11f, 0.13f);
        public Color ambientGround = new Color(0.26f, 0.23f, 0.19f);

        [Header("우주 배경")]
        public Color spaceColor = new Color(0.015f, 0.017f, 0.032f);

        Light sun, fill;

        public Color SkyColor { get { return spaceColor; } }
        /// <summary>해가 있는 쪽 (빛이 오는 방향의 반대).</summary>
        public Vector3 SunDirection { get { return -(Quaternion.Euler(sunAngles) * Vector3.forward); } }

        void OnEnable() { Apply(); }
        void OnValidate() { if (isActiveAndEnabled) Apply(); }

        public void Apply()
        {
            EnsureLights();

            sun.type = LightType.Directional;
            sun.color = sunColor;
            sun.intensity = sunIntensity;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = shadowStrength;
            sun.transform.rotation = Quaternion.Euler(sunAngles);

            fill.type = LightType.Directional;
            fill.color = fillColor;
            fill.intensity = fillIntensity;
            fill.shadows = LightShadows.None;
            fill.transform.rotation = Quaternion.Euler(fillAngles);

            RenderSettings.skybox = null;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = ambientSky;
            RenderSettings.ambientEquatorColor = ambientEquator;
            RenderSettings.ambientGroundColor = ambientGround;
            RenderSettings.ambientIntensity = 1f;

            RenderSettings.fog = false;          // 진공에는 안개가 없다

            ApplyToCameras();
        }

        public void ApplyToCameras()
        {
            var cams = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
            for (int i = 0; i < cams.Length; i++)
            {
                cams[i].clearFlags = CameraClearFlags.SolidColor;
                cams[i].backgroundColor = spaceColor;
            }
        }

        void EnsureLights()
        {
            if (sun == null)
            {
                var go = new GameObject("해");
                go.transform.SetParent(transform, false);
                sun = go.AddComponent<Light>();
            }
            if (fill == null)
            {
                var go = new GameObject("행성 반사광");
                go.transform.SetParent(transform, false);
                fill = go.AddComponent<Light>();
            }
        }
    }
}
