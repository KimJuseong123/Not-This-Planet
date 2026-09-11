using Metagame.Gameplay.Hazards;
using UnityEngine;

namespace Metagame.Gameplay
{
    /// <summary>
    /// Stage1의 게임플레이 운석 예고를 분위기 연출에만 전달한다.
    ///
    /// Pending은 읽기 전용으로 관찰하며, 스포너의 Tick·간격·착탄·물리·데미지에는
    /// 손대지 않는다. 다른 Stage는 이 브리지를 생성하지 않으면 기존 흐름을 그대로 쓴다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Stage1AtmosphereAlert : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f)] float normalMeteorIntensity = 0.55f;

        AtmosphereController atmosphere;
        MeteorSpawner spawner;
        float lastAlertIntensity = -1f;

        public void Configure(AtmosphereController controller, MeteorSpawner meteorSpawner)
        {
            atmosphere = controller;
            spawner = meteorSpawner;
            lastAlertIntensity = -1f;

            if (atmosphere == null) return;
            atmosphere.SetMeteorShowerIntensity(normalMeteorIntensity);
            UpdateAtmosphereAlert(true);
        }

        void Update()
        {
            UpdateAtmosphereAlert(false);
        }

        void OnDisable()
        {
            if (atmosphere != null)
                atmosphere.SetAlertIntensity(0f);
        }

        void UpdateAtmosphereAlert(bool force)
        {
            if (atmosphere == null || spawner == null) return;

            float alert = 0f;
            var pending = spawner.Pending;
            for (int i = 0; i < pending.Count; i++)
                alert = Mathf.Max(alert, pending[i].Progress01);

            if (!force && Mathf.Abs(alert - lastAlertIntensity) < 0.01f) return;
            lastAlertIntensity = alert;
            atmosphere.SetAlertIntensity(alert);
        }
    }
}
