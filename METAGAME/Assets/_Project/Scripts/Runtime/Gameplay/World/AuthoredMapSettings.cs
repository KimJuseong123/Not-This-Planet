using UnityEngine;

namespace Metagame.Gameplay
{
    /// <summary>Optional scene configuration. No movement forces or part state rules live here.</summary>
    public sealed class AuthoredMapSettings : MonoBehaviour
    {
        public Transform playerSpawn;
        public Vector2 flightHalfSize = new Vector2(51f, 47f);
        public float maximumAltitude = 20f;
        public bool preserveSceneLighting = true;

        /// <summary>
        /// 수제 맵의 조명은 지키되 <b>하늘의 별만은 코드가 뿌린다</b> (S15P21B202-546).
        ///
        /// <para><b>왜 갈랐나.</b> 예전에는 <see cref="preserveSceneLighting"/> 하나가
        /// 조명·안개·스카이박스·별을 통째로 껐다. 그래서 Stage1_Astra 에서는
        /// <c>AtmosphereBackdrop</c> 도 <c>Vacuum</c> 도 <c>Starfield</c> 도 만들어지지
        /// 않았고, 하늘의 별은 씬에 박아 둔 아트 메시 하나가 전부였다. 그 메시는 방위각의
        /// 절반과 고도 60도 위가 비어 있다(실측: 면적이 같은 전천 96칸 중 <b>84칸이 빈
        /// 칸</b>). 코드 별하늘은 같은 자로 <b>0칸</b>이다.</para>
        ///
        /// <para>"수제 맵의 조명을 보존한다" 와 "하늘에 별을 뿌리지 않는다" 는 서로 다른
        /// 결정인데 문지기 하나에 묶여 있었다. 조명은 그대로 두고 별만 켠다 —
        /// <c>AtmosphereBackdrop.ApplyStarsOnly</c> 는 스카이박스도 조명도 건드리지 않는다.</para>
        /// </summary>
        public bool useCodeStarfield = true;

        public bool randomizeVehicles;

        public void ConfigureCamera(Camera camera)
        {
            if (camera == null) return;
            var data = camera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            if (data == null)
                data = camera.gameObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            camera.allowHDR = true;
            data.renderPostProcessing = true;
        }

        public Bounds Bounds => new Bounds(transform.position + Vector3.up * 10f,
            new Vector3(flightHalfSize.x * 2f, 20f, flightHalfSize.y * 2f));

        public Vector3 ClampFlight(Vector3 position, ref Vector3 velocity)
        {
            var min = Bounds.min;
            var max = Bounds.max;
            if ((position.x <= min.x && velocity.x < 0) || (position.x >= max.x && velocity.x > 0)) velocity.x = 0;
            if ((position.z <= min.z && velocity.z < 0) || (position.z >= max.z && velocity.z > 0)) velocity.z = 0;
            position.x = Mathf.Clamp(position.x, min.x, max.x);
            position.z = Mathf.Clamp(position.z, min.z, max.z);
            return position;
        }

        public float GroundHeight(Vector3 position, float fallback)
        {
            return Physics.Raycast(position + Vector3.up, Vector3.down, out var hit, 100f,
                GameLayers.GroundMask, QueryTriggerInteraction.Ignore) ? hit.point.y : fallback;
        }
    }
}
