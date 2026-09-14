using Metagame.Gameplay.Parts;
using UnityEngine;

namespace Metagame.Gameplay
{
    /// <summary>Variant-only manual hauling, including the original convertible Part_4 while unoccupied.</summary>
    [RequireComponent(typeof(SpaceshipPart), typeof(PartPush), typeof(Grabbable))]
    [DefaultExecutionOrder(-50)]
    public sealed class AstraV2ManualPart : MonoBehaviour
    {
        PartPush _push;
        Grabbable _grabbable;
        PartTractorActivator _activator;
        void Awake()
        {
            _push = GetComponent<PartPush>(); _grabbable = GetComponent<Grabbable>();
            _activator = GetComponent<PartTractorActivator>();
        }
        void FixedUpdate()
        {
            if (_activator == null || !_activator.IsActivated) return;
            var vehicle = _activator.Vehicle;
            bool manual = vehicle == null || vehicle.Rider == null;
            if (_push.enabled != manual) _push.enabled = manual;
            if (_grabbable.enabled != manual) _grabbable.enabled = manual;
            _grabbable.CanGrab = manual;
            _grabbable.Climbable = false;
        }
    }
}
