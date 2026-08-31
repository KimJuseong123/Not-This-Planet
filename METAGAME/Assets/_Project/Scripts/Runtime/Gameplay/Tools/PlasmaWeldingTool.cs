using UnityEngine;
using Metagame.Core;

namespace Metagame.Gameplay
{
    public enum PlasmaBeamMode
    {
        None,
        Weld,
        Cut
    }

    /// <summary>
    /// 임시 플라즈마 용접기의 시각 전용 동작.
    /// 좌클릭은 용접색, 우클릭은 절단색으로 같은 빔을 표시한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlasmaWeldingTool : ToolBase
    {
        [Header("빔 표시")]
        [SerializeField] Color weldColor = new Color(0.08f, 0.75f, 1f, 1f);
        [SerializeField] Color cutColor = new Color(1f, 0.28f, 0.05f, 1f);
        [SerializeField] float beamRange = Tuning.ToolBeamRange;
        [SerializeField] float beamWidth = 0.035f;
        [SerializeField] int beamMask = ~0;

        const int RaycastBufferSize = 32;
        readonly RaycastHit[] hits = new RaycastHit[RaycastBufferSize];
        LineRenderer beam;
        Material runtimeMaterial;

        public PlasmaBeamMode BeamMode { get; private set; }
        public bool BeamActive { get; private set; }
        public Vector3 BeamEnd { get; private set; }
        public Color CurrentBeamColor
        {
            get
            {
                return BeamMode == PlasmaBeamMode.Cut ? cutColor : weldColor;
            }
        }

        protected override void OnEquipped()
        {
            EnsureBeam();
            BeamMode = PlasmaBeamMode.None;
            BeamActive = false;
            beam.enabled = false;
        }

        protected override void OnUnequipped()
        {
            BeamMode = PlasmaBeamMode.None;
            BeamActive = false;
            if (beam != null) beam.enabled = false;
        }

        protected override void OnToolInput(ToolInputFrame input, ToolAimContext aim)
        {
            // 용접과 절단은 하나의 노즐에서 나가는 상호 배타적 기능이다.
            // 두 버튼을 동시에 누르면 어느 기능인지 결정하지 않고 빔을 끈다.
            if (input.PrimaryHeld && !input.SecondaryHeld)
                BeamMode = PlasmaBeamMode.Weld;
            else if (input.SecondaryHeld && !input.PrimaryHeld)
                BeamMode = PlasmaBeamMode.Cut;
            else
                BeamMode = PlasmaBeamMode.None;

            BeamActive = BeamMode != PlasmaBeamMode.None;
        }

        protected override void OnToolVisualUpdate(ToolInputFrame input, ToolAimContext aim)
        {
            if (beam == null) EnsureBeam();
            if (beam == null || !BeamActive || Muzzle == null)
            {
                if (beam != null) beam.enabled = false;
                return;
            }

            Vector3 direction = aim.Direction.sqrMagnitude > 1e-4f
                ? aim.Direction.normalized
                : Muzzle.forward;
            Vector3 origin = Muzzle.position;
            float distance = beamRange;

            int count = Physics.RaycastNonAlloc(origin, direction, hits, beamRange,
                                                beamMask, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                var hit = hits[i];
                if (hit.collider == null || IsOwnedCollider(hit.collider)) continue;
                if (hit.distance < distance) distance = hit.distance;
            }

            BeamEnd = origin + direction * distance;
            Color color = CurrentBeamColor;
            beam.startColor = color;
            beam.endColor = color;
            beam.SetPosition(0, origin);
            beam.SetPosition(1, BeamEnd);
            beam.enabled = true;
        }

        void EnsureBeam()
        {
            if (beam == null)
            {
                beam = GetComponent<LineRenderer>();
                if (beam == null) beam = gameObject.AddComponent<LineRenderer>();

                beam.useWorldSpace = true;
                beam.loop = false;
                beam.positionCount = 2;
                beam.alignment = LineAlignment.View;
                beam.textureMode = LineTextureMode.Stretch;
                beam.numCapVertices = 2;
                beam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                beam.receiveShadows = false;
                beam.startWidth = beamWidth;
                beam.endWidth = beamWidth * 0.70f;
            }

            if (beam.sharedMaterial == null)
            {
                runtimeMaterial = Prim.Flat(Color.white);
                beam.sharedMaterial = runtimeMaterial;
            }

            beam.startWidth = beamWidth;
            beam.endWidth = beamWidth * 0.70f;
        }

        bool IsOwnedCollider(Collider collider)
        {
            if (collider == null) return true;
            if (collider.transform == transform || collider.transform.IsChildOf(transform))
                return true;
            return Owner != null && collider.GetComponentInParent<PlayerRig>() == Owner;
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (runtimeMaterial != null)
            {
                if (Application.isPlaying) Destroy(runtimeMaterial);
                else DestroyImmediate(runtimeMaterial);
            }
        }
    }
}
