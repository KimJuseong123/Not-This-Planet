using UnityEngine;
using Metagame.Core;
using Metagame.Gameplay.Parts;

namespace Metagame.Gameplay
{
    public enum PlasmaBeamMode
    {
        None,
        Weld,
        Cut
    }

    /// <summary>
    /// 플라즈마 용접기. 좌클릭은 용접, 우클릭은 절단이다.
    ///
    /// 빔 표시와 입력은 S15P21B202-168([Art])이 만들었고 <b>효과가 없었다</b> —
    /// 좌클릭하면 색만 바뀌었다. S15P21B202-206 에서 원형 접합선
    /// (<see cref="WeldSeam"/>)에 연결했다.
    ///
    /// 절단(우클릭)은 S15P21B202-209 에서 붙였다. 용접과 절단은 하나의 노즐에서 나가는
    /// 상호 배타적 기능이므로 한쪽이 도는 동안 다른 쪽은 진행되지 않는다.
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

        /// <summary>
        /// 지금 용접 중인 접합선. 없으면 null.
        ///
        /// <b>진행도를 밖으로 내보내지 않는다.</b> 기획 §16.2 가 "용접률, 완료 문구,
        /// 진행 바" 를 금지한다. 무엇을 겨누고 있는지는 불꽃 위치를 정하는 데 필요하므로
        /// 접합선 자체만 알려 준다.
        /// </summary>
        public WeldSeam WeldingSeam { get; private set; }

        /// <summary>이번 틱에 용접이 실제로 진행됐는가. 불꽃·소리가 붙을 신호다.</summary>
        public bool WeldingProgressed { get; private set; }

        /// <summary>지금 절단 중인 접합선. 없으면 null.</summary>
        public WeldSeam CuttingSeam { get; private set; }

        /// <summary>이번 틱에 절단이 실제로 진행됐는가.</summary>
        public bool CuttingProgressed { get; private set; }
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
            WeldingSeam = null;
            WeldingProgressed = false;
            CuttingSeam = null;
            CuttingProgressed = false;
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

            // PlayerRig 가 물리 틱마다 부르므로 간격은 고정 스텝이다.
            WeldTick(Time.fixedDeltaTime, BeamMode);
            CutTick(Time.fixedDeltaTime, BeamMode);
        }

        /// <summary>
        /// 겨누고 있는 원형 접합선을 채운다(기획 §16.1).
        ///
        /// 조준 방향이 아니라 <b>노즐 위치</b>로 접합선을 찾는다. 기획의 "용접기 최대
        /// 사거리는 약 0.8m" 가 뜻하는 것이 노즐과 접합선 사이의 거리이고, 그래서
        /// 플레이어는 서서 돌아보는 것이 아니라 우주선을 <b>돌아야</b> 한다 — §16.1 의
        /// "원형 접합부 전체를 따라간다" 가 그 뜻이다.
        ///
        /// 조준 방향까지 보게 하면 0.8m 앞의 접합선을 겨누지 못하는 각도가 생겨
        /// "닿았는데 안 되는" 상태가 된다.
        ///
        /// 모드를 인수로 받는다. <see cref="BeamMode"/> 를 읽으면 검증이 입력 프레임과
        /// 장착 절차를 통째로 흉내내야 하고, 그러면 정작 보고 싶은 기하 판정이 묻힌다.
        /// </summary>
        /// <returns>실제로 용접이 진행됐는가.</returns>
        public bool WeldTick(float deltaTime, PlasmaBeamMode mode)
        {
            WeldingProgressed = false;

            if (mode != PlasmaBeamMode.Weld || Muzzle == null)
            {
                WeldingSeam = null;
                return false;
            }

            WeldingSeam = WeldSeam.FindNearest(Muzzle.position);
            if (WeldingSeam == null) return false;

            var point = WeldingSeam.ClosestPointOnSeam(Muzzle.position);
            WeldingProgressed = WeldingSeam.TryWeld(point, deltaTime);
            return WeldingProgressed;
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

        /// <summary>
        /// 겨누고 있는 원형 접합선을 자른다(기획 §16.3).
        ///
        /// 접합선을 찾는 방식과 사거리는 <see cref="WeldTick"/> 과 같아야 한다 —
        /// 용접은 되는데 절단은 안 되는 각도가 생기면 플레이어가 이유를 알 수 없다.
        /// </summary>
        /// <returns>실제로 절단이 진행됐는가.</returns>
        public bool CutTick(float deltaTime, PlasmaBeamMode mode)
        {
            CuttingProgressed = false;

            if (mode != PlasmaBeamMode.Cut || Muzzle == null)
            {
                CuttingSeam = null;
                return false;
            }

            CuttingSeam = WeldSeam.FindNearest(Muzzle.position);
            if (CuttingSeam == null) return false;

            var point = CuttingSeam.ClosestPointOnSeam(Muzzle.position);
            CuttingProgressed = CuttingSeam.TryCut(point, deltaTime);

            // 완전히 잘리면 접합이 해제되고 접합선의 진행분도 되돌아간다.
            // 다음 틱에 다시 찾으면 되므로 여기서 붙잡아 두지 않는다.
            return CuttingProgressed;
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
