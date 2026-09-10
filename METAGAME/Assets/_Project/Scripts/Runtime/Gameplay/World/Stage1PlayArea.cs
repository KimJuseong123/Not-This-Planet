using UnityEngine;

namespace Metagame.Gameplay
{
    /// <summary>Optional scene-authored flight limits. Old scenes retain their 42m arena defaults.</summary>
    [DisallowMultipleComponent]
    public sealed class Stage1PlayArea : MonoBehaviour
    {
        [SerializeField] private Vector2 _flightHalfSize = new Vector2(51f, 47f);
        [SerializeField] private float _maximumAltitude = 20f;
        public Vector2 FlightHalfSize => _flightHalfSize;
        public float MaximumAltitude => _maximumAltitude;

        public Vector3 ClampFlight(Vector3 position, ref Vector3 velocity)
        {
            Vector3 center = transform.position;
            float minX = center.x - _flightHalfSize.x, maxX = center.x + _flightHalfSize.x;
            float minZ = center.z - _flightHalfSize.y, maxZ = center.z + _flightHalfSize.y;
            if ((position.x <= minX && velocity.x < 0f) || (position.x >= maxX && velocity.x > 0f)) velocity.x = 0f;
            if ((position.z <= minZ && velocity.z < 0f) || (position.z >= maxZ && velocity.z > 0f)) velocity.z = 0f;
            position.x = Mathf.Clamp(position.x, minX, maxX);
            position.z = Mathf.Clamp(position.z, minZ, maxZ);
            return position;
        }

        public float VehicleFloor(Vector3 position)
        {
            // Dedicated Ground layer excludes the craft, cargo and players. Start nearby so a
            // roof above the craft is not mistaken for its floor.
            return Physics.Raycast(position + Vector3.up * 1f, Vector3.down, out var hit,
                100f, GameLayers.GroundMask, QueryTriggerInteraction.Ignore)
                ? hit.point.y + Parts.TransportVehicle.GroundY : Parts.TransportVehicle.GroundY;
        }
    }
}
