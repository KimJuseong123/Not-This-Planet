using UnityEngine;
using Metagame.Core;

namespace Metagame.Gameplay
{
    /// <summary>Shared presentation and aim queries for the Astra pocket tools.</summary>
    public abstract class AstraHandTool : ToolBase
    {
        [SerializeField] Transform model;
        readonly RaycastHit[] hits = new RaycastHit[48];
        public abstract string UseHint { get; }
        public void ConfigureModel(Transform value) => model = value;

        protected Vector3 AimOrigin => Owner != null && Owner.Doll != null
            ? Owner.AimOrigin : Muzzle.position;

        protected bool FindSurface(Vector3 direction, float range, out RaycastHit nearest)
        {
            nearest = default;
            float distance = float.PositiveInfinity;
            int count = Physics.RaycastNonAlloc(AimOrigin, direction, hits, range,
                ~GameLayers.InteractableMask & ~GameLayers.BeamQueryMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var hit = hits[i];
                if (hit.collider.transform.IsChildOf(transform)) continue;
                if (Owner != null && hit.collider.GetComponentInParent<PlayerRig>() == Owner) continue;
                if (hit.distance >= distance) continue;
                nearest = hit;
                distance = hit.distance;
            }
            return distance < float.PositiveInfinity;
        }

        protected override void OnToolVisualUpdate(ToolInputFrame input, ToolAimContext aim)
        {
            if (model != null)
                UpdateFirstPersonVisual(model, new Vector3(.32f, -.30f, .64f),
                    Quaternion.identity, Vector3.one * .75f, new Vector3(0f, -.18f, -.12f));
        }
    }
}
