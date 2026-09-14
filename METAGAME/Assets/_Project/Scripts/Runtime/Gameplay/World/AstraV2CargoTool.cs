using UnityEngine;

namespace Metagame.Gameplay
{
    /// <summary>Authored roller/lever body marker for WorldSync; never adds runtime tracked bodies.</summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class AstraV2CargoTool : MonoBehaviour
    {
        Vector3 _home;
        Quaternion _rotation;
        Rigidbody _body;
        void Awake() { _body = GetComponent<Rigidbody>(); _home = transform.position; _rotation = transform.rotation; }
        void FixedUpdate()
        {
            if (WorldEvents.RemoteDecides || _body == null || _body.position.y > -4f) return;
            _body.position = _home; _body.rotation = _rotation;
            _body.linearVelocity = Vector3.zero; _body.angularVelocity = Vector3.zero;
        }
    }
}
