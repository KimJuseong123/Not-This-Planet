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
        public bool randomizeVehicles;
        [TextArea(2, 5)] public string[] explorationClues = System.Array.Empty<string>();

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
