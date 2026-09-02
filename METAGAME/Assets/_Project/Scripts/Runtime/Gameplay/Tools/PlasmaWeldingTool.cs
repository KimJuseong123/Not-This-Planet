using System;
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
    /// 좌클릭하면 색만 바뀌었다. S15P21B202-206 에서 원형 접합선에 연결했고,
    /// S15P21B202-230 에서 그 접합선을 걷어내 <b>용접 비드</b>(<see cref="WeldBead"/>)
    /// 하나만 남겼다.
    ///
    /// 절단(우클릭)은 S15P21B202-209 에서 붙였다. 용접과 절단은 하나의 노즐에서 나가는
    /// 상호 배타적 기능이므로 한쪽이 도는 동안 다른 쪽은 진행되지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlasmaWeldingTool : ToolBase
    {
        public static event Action<PlasmaWeldingTool> AnyWeldProgressed;
        public static event Action<PlasmaWeldingTool> AnyCutProgressed;

        [Header("빔 표시")]
        [SerializeField] Color weldColor = new Color(0.08f, 0.75f, 1f, 1f);
        [SerializeField] Color cutColor = new Color(1f, 0.28f, 0.05f, 1f);
        [SerializeField] float beamRange = Tuning.ToolBeamRange;
        [SerializeField] float beamWidth = 0.035f;
        [SerializeField] int beamMask = ~0;

        [Header("1인칭 휴대 포즈")]
        [SerializeField] Vector3 heldVisualLocalPosition = new Vector3(0.7f, -0.9f, 0.25f);
        [SerializeField] Vector3 heldVisualLocalEuler = new Vector3(-90f, -80f, 10f);
        [SerializeField] Vector3 heldVisualLocalScale = Vector3.one;

        const int RaycastBufferSize = 32;
        readonly RaycastHit[] hits = new RaycastHit[RaycastBufferSize];
        LineRenderer beam;
        Material runtimeMaterial;
        PlasmaGunPetalAnimator petalAnimator;
        Transform heldVisual;
        Vector3 droppedVisualLocalPosition;
        Quaternion droppedVisualLocalRotation;
        Vector3 droppedVisualLocalScale;
        bool droppedVisualPoseCaptured;

        public PlasmaBeamMode BeamMode { get; private set; }
        public bool BeamActive { get; private set; }
        public bool BeamHasContact { get; private set; }
        public Vector3 BeamEnd { get; private set; }

        /// <summary>
        /// 지금 비드를 쌓고 있는 파츠. 없으면 null.
        ///
        /// <b>진행도를 밖으로 내보내지 않는다.</b> 기획 §16.2 가 "용접률, 완료 문구,
        /// 진행 바" 를 금지한다. 무엇을 겨누고 있는지는 불꽃 위치를 정하는 데 필요하므로
        /// 대상만 알려 준다.
        /// </summary>
        public WeldBead GrowingBead { get; private set; }

        /// <summary>
        /// 이번 틱에 용접이 실제로 진행됐는가. <b>불꽃·소리가 붙을 신호다.</b>
        ///
        /// 다리가 다른 몸체에 걸리는 순간 <c>false</c> 가 된다 — 비드가 더 뻗지 않기
        /// 때문이다(S15P21B202-228). 진행 바가 없는 설계에서 <b>불꽃이 멎는 것</b>이
        /// "이어졌다" 를 알리는 신호가 된다.
        /// </summary>
        public bool WeldingProgressed { get; private set; }

        /// <summary>지금 비드를 깎고 있는 파츠. 없으면 null.</summary>
        public WeldBead ShrinkingBead { get; private set; }

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
            ApplyHeldVisualPose();

            // 휴대 중 충돌 끄기는 ToolBase 가 한다(S15P21B202-237). 예전에는 여기서
            // 직접 껐고, 그래서 S15P21B202-233 이 방망이를 만들 때 같은 처리를 빠뜨렸다.
            EnsureBeam();
            ResolvePetalAnimator();
            BeamMode = PlasmaBeamMode.None;
            BeamActive = false;
            BeamHasContact = false;
            if (petalAnimator != null) petalAnimator.SnapToMode(PlasmaBeamMode.None);
            beam.enabled = false;
        }

        protected override void OnUnequipped()
        {
            RestoreDroppedVisualPose();

            BeamMode = PlasmaBeamMode.None;
            BeamActive = false;
            BeamHasContact = false;
            WeldingProgressed = false;
            CuttingProgressed = false;
            GrowingBead = null;
            ShrinkingBead = null;
            if (petalAnimator != null) petalAnimator.SnapToMode(PlasmaBeamMode.None);
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
        /// 겨누고 있는 우주선 몸체에 비드를 쌓는다(기획 §16.2).
        ///
        /// 조준 방향이 아니라 <b>노즐 위치</b>로 대상을 찾는다. 기획의 "용접기 최대
        /// 사거리는 약 0.8m" 가 뜻하는 것이 노즐과 몸체 사이의 거리이고, 그래서
        /// 플레이어는 서서 돌아보는 것이 아니라 우주선을 <b>돌아야</b> 한다.
        ///
        /// 조준 방향까지 보게 하면 0.8m 앞을 겨누지 못하는 각도가 생겨 "닿았는데 안 되는"
        /// 상태가 된다. 기둥이 뻗는 방향은 대신 표면의 바깥 방향으로 정한다.
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
                GrowingBead = null;
                return false;
            }

            WeldingProgressed = WeldBead.TryGrowNear(Muzzle.position, deltaTime, out var bead);
            GrowingBead = bead;
            if (WeldingProgressed) AnyWeldProgressed?.Invoke(this);
            return WeldingProgressed;
        }

        protected override void OnToolVisualUpdate(ToolInputFrame input, ToolAimContext aim)
        {
            if (beam == null) EnsureBeam();
            ResolvePetalAnimator();
            var visualReady = petalAnimator == null ||
                              petalAnimator.TickAndIsReady(BeamMode, Time.deltaTime);
            if (beam == null || !BeamActive || !visualReady || Muzzle == null)
            {
                BeamHasContact = false;
                if (beam != null) beam.enabled = false;
                return;
            }

            Vector3 direction = aim.Direction.sqrMagnitude > 1e-4f
                ? aim.Direction.normalized
                : Muzzle.forward;
            Vector3 origin = Muzzle.position;
            float distance = beamRange;
            BeamHasContact = false;

            int count = Physics.RaycastNonAlloc(origin, direction, hits, beamRange,
                                                beamMask, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                var hit = hits[i];
                if (hit.collider == null || IsOwnedCollider(hit.collider)) continue;
                BeamHasContact = true;
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
        /// 겨누고 있는 비드를 깎는다(기획 §16.3).
        ///
        /// 대상을 찾는 방식과 사거리는 <see cref="WeldTick"/> 과 같아야 한다 —
        /// 용접은 되는데 절단은 안 되는 각도가 생기면 플레이어가 이유를 알 수 없다.
        ///
        /// 쌓을 수만 있고 되돌릴 수 없으면 잘못 쏜 한 번이 파츠에 영구히 남는다.
        /// 잇는 비드가 다 사라지면 접합도 풀린다(S15P21B202-228).
        /// </summary>
        /// <returns>실제로 절단이 진행됐는가.</returns>
        public bool CutTick(float deltaTime, PlasmaBeamMode mode)
        {
            CuttingProgressed = false;

            if (mode != PlasmaBeamMode.Cut || Muzzle == null)
            {
                ShrinkingBead = null;
                return false;
            }

            CuttingProgressed = WeldBead.TryShrinkNear(Muzzle.position, deltaTime, out var bead);
            ShrinkingBead = bead;
            if (CuttingProgressed) AnyCutProgressed?.Invoke(this);
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

        void ResolvePetalAnimator()
        {
            if (petalAnimator == null)
                petalAnimator = GetComponentInChildren<PlasmaGunPetalAnimator>(true);
        }

        void ApplyHeldVisualPose()
        {
            ResolveHeldVisual();
            if (heldVisual == null) return;

            if (!droppedVisualPoseCaptured)
            {
                droppedVisualLocalPosition = heldVisual.localPosition;
                droppedVisualLocalRotation = heldVisual.localRotation;
                droppedVisualLocalScale = heldVisual.localScale;
                droppedVisualPoseCaptured = true;
            }

            heldVisual.SetLocalPositionAndRotation(
                heldVisualLocalPosition, Quaternion.Euler(heldVisualLocalEuler));
            heldVisual.localScale = heldVisualLocalScale;
        }

        void RestoreDroppedVisualPose()
        {
            if (!droppedVisualPoseCaptured || heldVisual == null) return;

            heldVisual.SetLocalPositionAndRotation(
                droppedVisualLocalPosition, droppedVisualLocalRotation);
            heldVisual.localScale = droppedVisualLocalScale;
        }

        void ResolveHeldVisual()
        {
            if (heldVisual != null) return;
            heldVisual = transform.Find(
                "AttachPoints/ReplacementModel/SF_Stage1_PlasmaWeldingGun");

            // FBX를 다시 내보내 루트 이름이 바뀌더라도 ReplacementModel의
            // 첫 번째 자식을 휴대용 비주얼로 사용한다.
            if (heldVisual == null)
            {
                var replacement = transform.Find("AttachPoints/ReplacementModel");
                if (replacement != null && replacement.childCount > 0)
                    heldVisual = replacement.GetChild(0);
            }
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
