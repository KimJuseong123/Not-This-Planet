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

        [Header("장착 기준점")]
        [Tooltip("비우면 AttachPoints/HandGrip 자식을 자동으로 찾습니다.")]
        [SerializeField] Transform handGrip;

        const int RaycastBufferSize = 32;
        readonly RaycastHit[] hits = new RaycastHit[RaycastBufferSize];
        LineRenderer beam;
        Material runtimeMaterial;
        PlasmaGunPetalAnimator petalAnimator;
        Transform heldVisual;

        // --- 노즐 끝 (S15P21B202-277) ---
        Transform nozzleTip;
        Bounds visibleLocalBounds;
        bool visibleBoundsReady;

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

        protected override void Awake()
        {
            base.Awake();
            ResolveHandGrip();
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            ResolveHandGrip();
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

            // 참가자 화면에서는 세계를 스스로 바꾸지 않는다. 여기서도 용접하면 서버가 보낸
            // 것과 겹쳐 비드가 두 배로 자란다 — 판정은 서버, 여기는 받아 그리기만 한다.
            if (WorldEvents.RemoteDecides) { GrowingBead = null; return false; }

            WeldingProgressed = WeldBead.TryGrowNear(Muzzle.position, deltaTime, out var bead);
            GrowingBead = bead;

            // 다른 사람 화면에도 같은 비드가 자라야 한다. 비드는 덩어리 사슬이라 상태를
            // 통째로 보내기 어렵지만, 부품이 이미 같은 자리에 맞춰져 있으므로 같은 자리에서
            // 같은 양만큼 자라게 하면 같은 모양이 된다(-152).
            if (WeldingProgressed) WorldEvents.WeldGrown?.Invoke(Muzzle.position, deltaTime);

            return WeldingProgressed;
        }

        protected override void OnToolVisualUpdate(ToolInputFrame input, ToolAimContext aim)
        {
            // 조준 방향이 바뀌면 "그 방향에서 본 총의 끝" 도 바뀐다(S15P21B202-277).
            ResolveNozzleTip(aim.Direction);

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

            if (WorldEvents.RemoteDecides) { ShrinkingBead = null; return false; }

            CuttingProgressed = WeldBead.TryShrinkNear(Muzzle.position, deltaTime, out var bead);
            ShrinkingBead = bead;

            if (CuttingProgressed) WorldEvents.WeldShrunk?.Invoke(Muzzle.position, deltaTime);

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
            ResolveHandGrip();
            if (heldVisual == null || handGrip == null) return;

            if (!droppedVisualPoseCaptured)
            {
                droppedVisualLocalPosition = heldVisual.localPosition;
                droppedVisualLocalRotation = heldVisual.localRotation;
                droppedVisualLocalScale = heldVisual.localScale;
                droppedVisualPoseCaptured = true;
            }

            // 장착 포즈는 별도 숫자 세트가 아니라 HandGrip 마커를 단일 기준으로 사용한다.
            // 씬/프리팹에서 HandGrip을 조정하면 다음 장착 시 보이는 모델도 같은 포즈를 따른다.
            heldVisual.SetLocalPositionAndRotation(handGrip.localPosition, handGrip.localRotation);
            heldVisual.localScale = handGrip.localScale;

            // 자세가 바뀌면 끝점도 따라와야 한다(S15P21B202-277).
            ResolveNozzleTip(heldVisual.forward);
        }

        void RestoreDroppedVisualPose()
        {
            if (!droppedVisualPoseCaptured || heldVisual == null) return;

            heldVisual.SetLocalPositionAndRotation(
                droppedVisualLocalPosition, droppedVisualLocalRotation);
            heldVisual.localScale = droppedVisualLocalScale;

            ResolveNozzleTip(heldVisual.forward);
        }

        /// <summary>
        /// 빔이 나갈 자리를 <b>보이는 모델의 끝</b>으로 옮긴다(S15P21B202-277).
        ///
        /// <b>프리팹의 <c>AttachPoints/Muzzle</c> 은 임시 총에 맞춰 놓은 값이었다.</b>
        /// 장착하면 <see cref="ApplyHeldVisualPose"/> 가 <c>ReplacementModel</c> 아래의
        /// 실제 모델을 <c>HandGrip</c>의 로컬 포즈로 옮기는데, Muzzle 은 그 모델의
        /// 형제라 제자리에 남는다. 두 점의 거리가 <b>1.34m</b> 이고
        /// <see cref="Tuning.ToolBeamRange"/> 는 0.80m 이므로, <b>보이는 총이 닿는 곳과
        /// 빔이 닿는 곳이 겹칠 수가 없었다.</b>
        ///
        /// 연출만의 문제가 아니었다 — <see cref="WeldTick"/> 과 <see cref="CutTick"/> 도
        /// 같은 좌표를 쓰므로 <b>용접·절단 판정이 총에서 1.34m 떨어진 허공에서</b>
        /// 이뤄졌다. 기획 §16 의 조립이 여기에 걸려 있다.
        ///
        /// 끝점을 <see cref="ToolBase.SetMuzzle"/> 로 넣어 <b>읽는 쪽을 하나도 고치지
        /// 않는다.</b> Muzzle 을 읽는 자리가 빔·용접·절단·파티클로 여러 군데라, 읽는
        /// 쪽마다 고치면 하나를 빼먹는다.
        /// </summary>
        /// <returns>끝점을 실제로 옮겼는가. 보이는 모델이 없으면 <c>false</c>.</returns>
        public bool ResolveNozzleTip(Vector3 direction)
        {
            ResolveHeldVisual();
            if (heldVisual == null) return false;

            if (!visibleBoundsReady)
                visibleBoundsReady =
                    NozzleGeometry.TryLocalBounds(heldVisual, out visibleLocalBounds);

            if (!visibleBoundsReady) return false;

            if (nozzleTip == null)
            {
                // 도구 루트에 매단다. 모델 아래에 매달면 자세를 되돌릴 때
                // (RestoreDroppedVisualPose) 같이 끌려가 한 프레임 어긋난다.
                var go = new GameObject("노즐 끝");
                go.transform.SetParent(transform, false);
                nozzleTip = go.transform;

                SetMuzzle(nozzleTip);
            }

            nozzleTip.position =
                NozzleGeometry.TipAlong(heldVisual, visibleLocalBounds, direction);

            return true;
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

        void ResolveHandGrip()
        {
            if (handGrip != null) return;
            handGrip = transform.Find("AttachPoints/HandGrip");
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
