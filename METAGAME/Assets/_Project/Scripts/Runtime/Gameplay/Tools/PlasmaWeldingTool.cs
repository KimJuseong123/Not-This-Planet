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
    [DefaultExecutionOrder(110)] // FirstPersonHandView(100) 뒤에서 이번 프레임의 손 위치를 읽는다.
    public sealed class PlasmaWeldingTool : ToolBase
    {
        public static event Action<PlasmaWeldingTool> AnyWeldProgressed;
        public static event Action<PlasmaWeldingTool> AnyCutProgressed;

        [Header("빔 표시")]
        [SerializeField] Color weldColor = new Color(1f, 0.12f, 0.035f, 1f);
        [SerializeField] Color cutColor = new Color(1f, 0.28f, 0.05f, 1f);
        [SerializeField] float beamRange = Tuning.ToolBeamRange;
        [SerializeField] float beamWidth = 0.035f;
        [SerializeField] int beamMask = ~0;

        [Header("용접 재료 / 조준 가이드")]
        [Min(0.01f)] [SerializeField] float materialCapacitySeconds = 60f;
        [Min(0.005f)] [SerializeField] float guideRadius = 0.045f;
        [Tooltip("콜라이더 표면에서 법선 방향으로 가이드를 띄우는 거리(m).")]
        [Min(0.002f)] [SerializeField] float guideSurfaceOffset = 0.02f;
        [SerializeField] Color guideColor = new Color(0.75f, 0.22f, 1f, 1f);
        float usedMaterialSeconds;
        Vector3 aimDirection;
        ToolAimContext aimContext;
        WeldingSurfaceGuide surfaceGuide;
        [Header("접합 추천 가이드")]
        [Min(0.1f)] [SerializeField] float jointGuideRange = 4f;
        [Min(0.002f)] [SerializeField] float jointGuideSurfaceOffset = 0.012f;
        [SerializeField] Color jointGuideColor = new Color(1f, 0.82f, 0.12f, 0.32f);
        WeldingJointGuide jointGuide;
        public bool JointGuideVisible => jointGuide != null && jointGuide.Visible;
        public float RemainingFraction => Mathf.Clamp01(1f - usedMaterialSeconds / Mathf.Max(0.01f, materialCapacitySeconds));
        public override float ViewProgress => RemainingFraction;
        public bool GuideVisible { get; private set; }

        [Header("용접 접촉 효과")]
        [SerializeField] Color weldHotColor = new Color(1f, 0.10f, 0.025f, 0.90f);
        [SerializeField] Color weldSparkColor = new Color(1f, 0.28f, 0.035f, 0.62f);
        [Min(0.01f)] [SerializeField] float weldGlowSize = 0.052f;
        [Min(0.05f)] [SerializeField] float weldLightRange = 0.48f;
        [Min(0f)] [SerializeField] float weldLightIntensity = 1.15f;
        [Min(0.01f)] [SerializeField] float weldSparkInterval = 0.065f;
        [Range(1, 12)] [SerializeField] int weldSparksPerBurst = 2;
        [Min(0.04f)] [SerializeField] float weldSmokeInterval = 0.14f;

        [Header("월드 휴대 포즈")]
        [Tooltip("3인칭/원격 화면에서 장착 중 보이는 용접건 모델의 로컬 위치입니다.")]
        [SerializeField] Vector3 heldVisualLocalPosition = new Vector3(0.18f, -0.14f, 0.12f);
        [Tooltip("3인칭/원격 화면에서 장착 중 보이는 용접건 모델의 로컬 회전(Euler)입니다.")]
        [SerializeField] Vector3 heldVisualLocalEuler = new Vector3(-90f, -80f, 10f);
        [Tooltip("3인칭/원격 화면에서 장착 중에만 적용할 모델 크기입니다.")]
        [SerializeField] Vector3 heldVisualLocalScale = Vector3.one * 0.55f;

        [Header("로컬 1인칭 화면 포즈")]
        [Tooltip("카메라 기준 용접건 모델 위치입니다. Play 중 Inspector에서 바로 조절할 수 있습니다.")]
        [SerializeField] Vector3 firstPersonVisualLocalPosition = new Vector3(0.20f, -0.17f, 0.52f);
        [Tooltip("카메라 기준 용접건 모델 회전(Euler)입니다.")]
        [SerializeField] Vector3 firstPersonVisualLocalEuler = new Vector3(-90f, -80f, 10f);
        [Tooltip("카메라 아래로 분리된 용접건 모델의 크기입니다.")]
        [SerializeField] Vector3 firstPersonVisualLocalScale = Vector3.one * 0.275f;

        [Header("1인칭 손잡이 맞춤")]
        [Tooltip("제작 모델 로컬 좌표의 손잡이 중심. 표시 모델에만 적용하며 실제 총구 판정은 바꾸지 않습니다.")]
        [SerializeField] Vector3 firstPersonGripLocalPoint = new Vector3(-0.22f, 0.10f, 0.28f);

        const int RaycastBufferSize = 32;
        readonly RaycastHit[] hits = new RaycastHit[RaycastBufferSize];
        LineRenderer beam;
        Material runtimeMaterial;
        Transform weldContactFx;
        Transform weldContactGlow;
        ParticleSystem weldSparks;
        ParticleSystem weldSmoke;
        Light weldContactLight;
        Material weldSparkMaterial;
        float nextWeldSparkAt;
        float nextWeldSmokeAt;
        WeldBead brushStrokeBead;
        Vector3 brushStrokeLocalPoint;
        bool hasBrushStrokePoint;
        PlasmaGunPetalAnimator petalAnimator;
        Transform heldVisual;

        // --- 노즐 끝 (S15P21B202-277) ---
        Transform nozzleTip;
        Bounds visibleLocalBounds;
        bool visibleBoundsReady;

        Vector3 droppedVisualLocalPosition;
        Quaternion droppedVisualLocalRotation;
        Vector3 droppedVisualLocalScale;
        Transform droppedVisualParent;
        int droppedVisualSiblingIndex;
        bool droppedVisualPoseCaptured;
        public override byte ViewAction => (byte)BeamMode;
        protected override void ApplyRemoteVisualState(byte action, float progress, Vector3 aim)
        {
            BeamMode = action <= (byte)PlasmaBeamMode.Cut ? (PlasmaBeamMode)action : PlasmaBeamMode.None;
            BeamActive = BeamMode != PlasmaBeamMode.None;
            usedMaterialSeconds = (1f - Mathf.Clamp01(progress)) * Mathf.Max(0.01f, materialCapacitySeconds);
        }

        public PlasmaBeamMode BeamMode { get; private set; }
        public bool BeamActive { get; private set; }
        public bool BeamHasContact { get; private set; }
        public Vector3 BeamEnd { get; private set; }
        RaycastHit lastCutContact;
        bool hasLastCutContact;

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

        protected override void OnValidate()
        {
            base.OnValidate();

            // Play 중 Inspector에서 값을 조절하면 다시 내려놓고 집을 필요 없이
            // 현재 보이는 모델과 노즐 기준점에 바로 반영한다.
            if (Application.isPlaying && IsEquipped) ApplyHeldVisualPose();
        }

        protected override void OnEquipped()
        {
            aimContext = default;
            aimDirection = Vector3.zero;
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
            HideWeldContactFx(clearSparks: true);
        }

        protected override void OnUnequipped()
        {
            jointGuide?.Hide();
            HideGuide();
            RestoreDroppedVisualPose();

            BeamMode = PlasmaBeamMode.None;
            BeamActive = false;
            BeamHasContact = false;
            WeldingProgressed = false;
            CuttingProgressed = false;
            GrowingBead = null;
            ShrinkingBead = null;
            ResetBrushStroke();
            if (petalAnimator != null) petalAnimator.SnapToMode(PlasmaBeamMode.None);
            if (beam != null) beam.enabled = false;
            HideWeldContactFx(clearSparks: true);
        }

        protected override void OnToolInput(ToolInputFrame input, ToolAimContext aim)
        {
            aimContext = aim;
            aimDirection = aim.Direction;
            ResolveNozzleTip(aim.Direction);
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
        /// 크로스헤어 레이가 처음 맞힌 표면에 비드를 쌓는다.
        /// 실제 진행한 시간만 이 용접건의 재료에서 소모한다.
        /// </summary>
        public bool WeldTick(float deltaTime, PlasmaBeamMode mode)
        {
            WeldingProgressed = false;

            if (mode != PlasmaBeamMode.Weld || Muzzle == null || deltaTime <= 0f || RemainingFraction <= 0f)
            {
                GrowingBead = null;
                ResetBrushStroke();
                return false;
            }

            // 참가자 화면에서는 세계를 스스로 바꾸지 않는다. 여기서도 용접하면 서버가 보낸
            // 것과 겹쳐 비드가 두 배로 자란다 — 판정은 서버, 여기는 받아 그리기만 한다.
            if (WorldEvents.RemoteDecides) { GrowingBead = null; ResetBrushStroke(); return false; }

            GrowingBead = null;
            float amount = Mathf.Min(deltaTime, Mathf.Max(0f, materialCapacitySeconds - usedMaterialSeconds));
            if (TryGetBeamHit(aimDirection, out var hit))
            {
                var hitsBodySurface = !WeldBead.IsBeadCollider(hit.collider);
                var previousBrushPoint = hasBrushStrokePoint && brushStrokeBead != null
                    ? brushStrokeBead.transform.TransformPoint(brushStrokeLocalPoint)
                    : Vector3.zero;
                WeldingProgressed = WeldBead.TryApplyAt(
                    hit, amount, false, out var bead,
                    hitsBodySurface && hasBrushStrokePoint ? brushStrokeBead : null,
                    previousBrushPoint);
                GrowingBead = bead;
                if (WeldingProgressed)
                {
                    usedMaterialSeconds += amount;
                    if (hitsBodySurface && bead != null)
                    {
                        brushStrokeBead = bead;
                        // 파츠가 움직여도 이전 점이 표면과 함께 움직이도록 파츠 로컬 좌표로 보관한다.
                        brushStrokeLocalPoint = bead.transform.InverseTransformPoint(hit.point);
                        hasBrushStrokePoint = true;
                    }
                }
                else if (hitsBodySurface)
                {
                    ResetBrushStroke();
                }
            }
            else ResetBrushStroke();
            // WorldSync의 비드 스냅샷으로 복제한다. 예전 근접 탐색 RPC를 함께 보내면
            // 클라이언트에서 조준하지 않은 다른 사슬을 키울 수 있다.

            return WeldingProgressed;
        }

        void ResetBrushStroke()
        {
            brushStrokeBead = null;
            brushStrokeLocalPoint = Vector3.zero;
            hasBrushStrokePoint = false;
        }

        protected override void OnToolVisualUpdate(ToolInputFrame input, ToolAimContext aim)
        {
            aimContext = aim;
            UpdateHeldVisualPose();

            // 조준 방향이 바뀌면 "그 방향에서 본 총의 끝" 도 바뀐다(S15P21B202-277).
            ResolveNozzleTip(aim.Direction);

            UpdateGuide(input, aim.Direction);
            UpdateJointGuide();

            if (beam == null) EnsureBeam();
            ResolvePetalAnimator();
            var visualReady = petalAnimator == null ||
                              petalAnimator.TickAndIsReady(BeamMode, Time.deltaTime);
            firstPersonVisual?.SyncAnimation();
            if (beam == null || !BeamActive || !visualReady || Muzzle == null)
            {
                BeamHasContact = false;
                if (beam != null) beam.enabled = false;
                HideWeldContactFx(clearSparks: false);
                return;
            }

            Vector3 direction = aim.Camera != null ? aim.Camera.transform.forward : aim.Direction.sqrMagnitude > 1e-4f
                ? aim.Direction.normalized
                : Muzzle.forward;
            Vector3 origin = Muzzle.position;
            BeamHasContact = false;

            if (TryGetBeamHit(direction, out var contact))
            {
                BeamHasContact = true;
            }

            Color color = CurrentBeamColor;
            beam.startColor = color;
            beam.endColor = color;
            // 빔은 화면 총구에서 크로스헤어의 실제 접촉점으로 연결한다.
            // 가이드에만 적용하는 표면 오프셋을 비드/빔 판정에는 섞지 않는다.
            var view = FirstPersonModel;
            var viewOrigin = view != null && view.gameObject.activeInHierarchy && visibleBoundsReady
                ? NozzleGeometry.TipAlong(view, visibleLocalBounds, direction) : origin;
            BeamEnd = BeamHasContact ? contact.point : viewOrigin + direction * beamRange;
            beam.SetPosition(0, viewOrigin);
            beam.SetPosition(1, BeamEnd);
            beam.enabled = true;

            // 좌클릭 용접뿐 아니라 우클릭 절단도 실제 작업 가능한 표면에 닿으면
            // 같은 접촉 발광을 보여 준다. 빔 선의 밝기가 아니라 이 지점광·불꽃이
            // 플레이어에게 "지금 작업 중"임을 알려 주는 피드백이다.
            var effectContact = contact;
            var showWeldContact = BeamHasContact &&
                                  (BeamMode == PlasmaBeamMode.Weld
                                      ? RemainingFraction > 0f && WeldBead.CanApplyAt(contact, false)
                                      : BeamMode == PlasmaBeamMode.Cut &&
                                        (CuttingProgressed || WeldBead.CanApplyAt(contact, true)));
            // 절단 물리 틱이 비드를 먼저 줄이면 같은 프레임의 재레이캐스트는
            // 아래 파츠 표면을 맞힐 수 있다. 실제로 절단한 원래 접촉점에 효과를 남긴다.
            if (BeamMode == PlasmaBeamMode.Cut && CuttingProgressed && hasLastCutContact)
                effectContact = lastCutContact;
            if (showWeldContact) ShowWeldContactFx(effectContact);
            else HideWeldContactFx(clearSparks: false);
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
            hasLastCutContact = false;

            if (mode != PlasmaBeamMode.Cut || Muzzle == null)
            {
                ShrinkingBead = null;
                return false;
            }

            if (WorldEvents.RemoteDecides) { ShrinkingBead = null; return false; }

            ShrinkingBead = null;
            if (TryGetBeamHit(aimDirection, out var hit))
            {
                CuttingProgressed = WeldBead.TryApplyAt(hit, deltaTime, true, out var bead);
                ShrinkingBead = bead;
                if (CuttingProgressed)
                {
                    lastCutContact = hit;
                    hasLastCutContact = true;
                }
                // 실제 절단이 진행된 시간만 이 용접건에 회수한다. 빈 공간에서는 충전하지 않는다.
                if (CuttingProgressed) usedMaterialSeconds = Mathf.Max(0f, usedMaterialSeconds - deltaTime);
            }

            return CuttingProgressed;
        }

        /// <summary>발사와 예고가 공유하는 첫 외부 표면. 벽 뒤의 유효 파츠를 고르지 않는다.</summary>
        public bool TryGetBeamHit(Vector3 direction, out RaycastHit contact)
        {
            contact = default;
            if (Muzzle == null) return false;
            direction = aimContext.Camera != null ? aimContext.Camera.transform.forward :
                direction.sqrMagnitude > 1e-4f ? direction.normalized : Muzzle.forward;
            float range = Mathf.Min(beamRange, PhysicsScale.WeldRange);
            var player = aimContext.Player != null ? aimContext.Player : ViewOwner;
            Vector3 origin = aimContext.Camera != null ? aimContext.Camera.transform.position :
                player != null ? player.AimOrigin : Muzzle.position;
            // 카메라가 총구 뒤에 있어도 조준할 수 있지만 실제 작업 거리는 총구 기준으로 유지한다.
            float rayRange = Vector3.Distance(origin, Muzzle.position) + range;
            int count = Physics.RaycastNonAlloc(origin, direction, hits, rayRange,
                beamMask, QueryTriggerInteraction.Ignore);
            // NonAlloc이 가득 찼을 때는 가장 가까운 충돌이 버퍼 밖일 수 있다.
            var results = count == hits.Length
                ? Physics.RaycastAll(origin, direction, rayRange, beamMask, QueryTriggerInteraction.Ignore)
                : hits;
            if (results != hits) count = results.Length;
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
                if (!IsOwnedCollider(results[i].collider) && results[i].distance < nearest)
                { contact = results[i]; nearest = contact.distance; }
            return contact.collider != null && (contact.point - Muzzle.position).sqrMagnitude <= range * range;
        }

        void UpdateGuide(ToolInputFrame input, Vector3 direction)
        {
            var owner = ViewOwner;
            bool canShow = owner != null && owner.IsLocal && !owner.Downed && !owner.IsRiding &&
                           !(input.PrimaryHeld && input.SecondaryHeld);
            if (!canShow || !TryGetBeamHit(direction, out var hit) ||
                !(BeamMode == PlasmaBeamMode.Cut ? WeldBead.CanApplyAt(hit, true) :
                  BeamMode == PlasmaBeamMode.Weld ? RemainingFraction > 0f && WeldBead.CanApplyAt(hit, false) :
                  (RemainingFraction > 0f && WeldBead.CanApplyAt(hit, false)) || WeldBead.CanApplyAt(hit, true)))
            { HideGuide(); return; }
            if (surfaceGuide == null) surfaceGuide = new WeldingSurfaceGuide(transform, guideColor);
            surfaceGuide.Show(hit, guideRadius, guideSurfaceOffset);
            GuideVisible = true;
        }

        void HideGuide()
        {
            GuideVisible = false;
            surfaceGuide?.Hide();
        }

        void UpdateJointGuide()
        {
            var owner = ViewOwner;
            if (!IsHeldForView || owner == null || !owner.IsLocal || owner.Downed || owner.IsRiding ||
                RemainingFraction <= 0f || Muzzle == null)
            { jointGuide?.Hide(); return; }
            if (jointGuide == null) jointGuide = new WeldingJointGuide(jointGuideColor);
            jointGuide.Show(Muzzle.position, jointGuideRange, jointGuideSurfaceOffset);
        }

        protected override void OnDisable()
        {
            jointGuide?.Hide();
            HideGuide();
            if (beam != null) beam.enabled = false;
            HideWeldContactFx(clearSparks: true);
            base.OnDisable();
        }

        void ShowWeldContactFx(RaycastHit hit)
        {
            EnsureWeldContactFx();
            if (weldContactFx == null) return;

            var normal = hit.normal.sqrMagnitude > 1e-6f ? hit.normal.normalized : Vector3.up;
            weldContactFx.SetPositionAndRotation(
                hit.point + normal * Mathf.Max(0.004f, weldGlowSize * 0.08f),
                Quaternion.LookRotation(normal));

            var pulse = 0.88f + Mathf.Sin(Time.unscaledTime * 46f) * 0.12f;
            if (weldContactGlow != null)
            {
                weldContactGlow.gameObject.SetActive(true);
                weldContactGlow.localScale = Vector3.one * (weldGlowSize * pulse);
            }

            if (weldContactLight != null)
            {
                weldContactLight.enabled = true;
                weldContactLight.color = Color.Lerp(weldSparkColor, weldHotColor, 0.82f);
                weldContactLight.range = weldLightRange;
                weldContactLight.intensity = weldLightIntensity * pulse;
            }

            if (weldSparks != null && Time.unscaledTime >= nextWeldSparkAt)
            {
                nextWeldSparkAt = Time.unscaledTime + weldSparkInterval;
                if (!weldSparks.isPlaying) weldSparks.Play();
                weldSparks.Emit(weldSparksPerBurst);
            }
            if (weldSmoke != null && Time.unscaledTime >= nextWeldSmokeAt)
            {
                nextWeldSmokeAt = Time.unscaledTime + weldSmokeInterval;
                if (!weldSmoke.isPlaying) weldSmoke.Play();
                weldSmoke.Emit(1);
            }
        }

        void HideWeldContactFx(bool clearSparks)
        {
            if (weldContactGlow != null) weldContactGlow.gameObject.SetActive(false);
            if (weldContactLight != null) weldContactLight.enabled = false;
            if (clearSparks && weldSparks != null)
                weldSparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (clearSparks && weldSmoke != null)
                weldSmoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        void EnsureWeldContactFx()
        {
            if (weldContactFx != null) return;

            var root = new GameObject("WeldContactFx");
            weldContactFx = root.transform;
            weldContactFx.SetParent(transform, false);

            var glow = Prim.Decor(Prim.Ball("HotMetal", Vector3.zero, 1f, weldHotColor, 0.05f));
            glow.transform.SetParent(weldContactFx, false);
            weldContactGlow = glow.transform;
            var glowRenderer = glow.GetComponent<Renderer>();
            if (glowRenderer != null)
            {
                glowRenderer.sharedMaterial = Prim.Flat(weldHotColor);
                glowRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                glowRenderer.receiveShadows = false;
            }

            weldContactLight = root.AddComponent<Light>();
            weldContactLight.type = LightType.Point;
            weldContactLight.shadows = LightShadows.None;
            weldContactLight.renderMode = LightRenderMode.ForcePixel;

            var sparksObject = new GameObject("Sparks");
            sparksObject.transform.SetParent(weldContactFx, false);
            weldSparks = sparksObject.AddComponent<ParticleSystem>();

            var main = weldSparks.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Shape;
            main.maxParticles = 64;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.08f, 0.18f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.45f, 1.25f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.0035f, 0.010f);
            main.startColor = new ParticleSystem.MinMaxGradient(weldHotColor, weldSparkColor);
            main.gravityModifier = 0.35f;

            var emission = weldSparks.emission;
            emission.enabled = false;
            var shape = weldSparks.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 58f;
            shape.radius = 0.012f;
            shape.length = 0.025f;

            var colorOverLifetime = weldSparks.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(
                new[]
                {
                    new GradientColorKey(weldHotColor, 0f),
                    new GradientColorKey(weldSparkColor, 0.42f),
                    new GradientColorKey(new Color(0.28f, 0.04f, 0.01f), 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0.62f, 0f),
                    new GradientAlphaKey(0.42f, 0.55f),
                    new GradientAlphaKey(0f, 1f),
                });
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(fade);

            var sparksRenderer = weldSparks.GetComponent<ParticleSystemRenderer>();
            sparksRenderer.renderMode = ParticleSystemRenderMode.Stretch;
            sparksRenderer.lengthScale = 1.15f;
            sparksRenderer.velocityScale = 0.08f;
            sparksRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            sparksRenderer.receiveShadows = false;
            var sparkShader = Resources.Load<Shader>("WeldingParticle");
            if (sparkShader == null) sparkShader = Shader.Find("NotThisPlanet/WeldingParticle");
            weldSparkMaterial = new Material(sparkShader != null ? sparkShader : Prim.FlatShader);
            weldSparkMaterial.name = "WeldingSpark";
            if (weldSparkMaterial.HasProperty("_Color"))
                weldSparkMaterial.SetColor("_Color", new Color(1f, 0.30f, 0.08f, 0.68f));
            sparksRenderer.sharedMaterial = weldSparkMaterial;

            var smokeObject = new GameObject("Smoke");
            smokeObject.transform.SetParent(weldContactFx, false);
            weldSmoke = smokeObject.AddComponent<ParticleSystem>();

            var smokeMain = weldSmoke.main;
            smokeMain.loop = false;
            smokeMain.playOnAwake = false;
            smokeMain.simulationSpace = ParticleSystemSimulationSpace.World;
            smokeMain.scalingMode = ParticleSystemScalingMode.Shape;
            smokeMain.maxParticles = 24;
            smokeMain.startLifetime = new ParticleSystem.MinMaxCurve(0.42f, 0.78f);
            smokeMain.startSpeed = new ParticleSystem.MinMaxCurve(0.06f, 0.18f);
            smokeMain.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.055f);
            smokeMain.startColor = new Color(0.32f, 0.35f, 0.40f, 0.24f);

            var smokeEmission = weldSmoke.emission;
            smokeEmission.enabled = false;
            var smokeShape = weldSmoke.shape;
            smokeShape.enabled = true;
            smokeShape.shapeType = ParticleSystemShapeType.Cone;
            smokeShape.angle = 20f;
            smokeShape.radius = 0.01f;
            smokeShape.length = 0.015f;

            var smokeSize = weldSmoke.sizeOverLifetime;
            smokeSize.enabled = true;
            smokeSize.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(new Keyframe(0f, 0.35f), new Keyframe(1f, 1.35f)));
            var smokeColor = weldSmoke.colorOverLifetime;
            smokeColor.enabled = true;
            var smokeFade = new Gradient();
            smokeFade.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.48f, 0.52f, 0.58f), 0f),
                    new GradientColorKey(new Color(0.18f, 0.20f, 0.24f), 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.24f, 0.18f),
                    new GradientAlphaKey(0f, 1f),
                });
            smokeColor.color = new ParticleSystem.MinMaxGradient(smokeFade);

            var smokeRenderer = weldSmoke.GetComponent<ParticleSystemRenderer>();
            smokeRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            smokeRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            smokeRenderer.receiveShadows = false;
            smokeRenderer.sharedMaterial = Prim.Ghost(new Color(0.34f, 0.37f, 0.42f, 0.22f),
                rimBoost: 0.25f, pulse: 0f, fogExempt: true);

            HideWeldContactFx(clearSparks: true);
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
            if (petalAnimator == null && heldVisual != null)
                petalAnimator = heldVisual.GetComponentInChildren<PlasmaGunPetalAnimator>(true);
            if (petalAnimator == null)
                petalAnimator = GetComponentInChildren<PlasmaGunPetalAnimator>(true);
        }

        void ApplyHeldVisualPose()
        {
            ResolveHeldVisual();
            if (heldVisual == null) return;

            CaptureDroppedVisualPose();
            UpdateHeldVisualPose();

            // 자세가 바뀌면 끝점도 따라와야 한다(S15P21B202-277).
            ResolveNozzleTip(heldVisual.forward);
        }

        void RestoreDroppedVisualPose()
        {
            if (!droppedVisualPoseCaptured || heldVisual == null) return;

            RestoreVisualParent();
            heldVisual.SetLocalPositionAndRotation(
                droppedVisualLocalPosition, droppedVisualLocalRotation);
            heldVisual.localScale = droppedVisualLocalScale;

            ResolveNozzleTip(heldVisual.forward);
        }

        void CaptureDroppedVisualPose()
        {
            if (droppedVisualPoseCaptured || heldVisual == null) return;

            droppedVisualParent = heldVisual.parent;
            droppedVisualSiblingIndex = heldVisual.GetSiblingIndex();
            droppedVisualLocalPosition = heldVisual.localPosition;
            droppedVisualLocalRotation = heldVisual.localRotation;
            droppedVisualLocalScale = heldVisual.localScale;
            droppedVisualPoseCaptured = true;
        }

        /// <summary>
        /// 실제 모델은 캐릭터 손에 맞추고, 로컬 카메라에는 표시 전용 복사본을 둔다.
        /// 1인칭 위치/회전/크기를 바꿔도 물리 본체와 실제 총구에는 적용하지 않는다.
        /// </summary>
        void UpdateHeldVisualPose()
        {
            if (heldVisual == null) return;
            heldVisual.SetLocalPositionAndRotation(heldVisualLocalPosition, Quaternion.Euler(heldVisualLocalEuler));
            heldVisual.localScale = heldVisualLocalScale;
            AlignWorldGrip(heldVisual, firstPersonGripLocalPoint);
            UpdateFirstPersonVisual(heldVisual, firstPersonVisualLocalPosition,
                Quaternion.Euler(firstPersonVisualLocalEuler), firstPersonVisualLocalScale, firstPersonGripLocalPoint);
        }

        void RestoreVisualParent() { HideFirstPersonVisual(); }

        /// <summary>
        /// 빔이 나갈 자리를 <b>보이는 모델의 끝</b>으로 옮긴다(S15P21B202-277).
        ///
        /// <b>프리팹의 <c>AttachPoints/Muzzle</c> 은 임시 총에 맞춰 놓은 값이었다.</b>
        /// 장착하면 <see cref="ApplyHeldVisualPose"/> 가 <c>ReplacementModel</c> 아래의
        /// 실제 모델만 1인칭 휴대 위치로 옮기고 돌리는데, Muzzle 은 그 모델의 형제라
        /// 제자리에 남는다. 따라서 고정된 Muzzle 을 그대로 쓰면 <b>보이는 총이 닿는
        /// 곳과 빔이 닿는 곳이 어긋난다.</b>
        ///
        /// 연출만의 문제가 아니었다 — <see cref="WeldTick"/> 과 <see cref="CutTick"/> 도
        /// 같은 좌표를 쓰므로 <b>용접·절단 판정도 보이는 총과 다른 곳에서</b> 이뤄진다.
        /// 기획 §16 의 조립이 여기에 걸려 있다.
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

        bool IsOwnedCollider(Collider collider)
        {
            if (collider == null) return true;
            if (collider.transform == transform || collider.transform.IsChildOf(transform))
                return true;
            if (heldVisual != null &&
                (collider.transform == heldVisual || collider.transform.IsChildOf(heldVisual)))
                return true;
            return ViewOwner != null && collider.GetComponentInParent<PlayerRig>() == ViewOwner;
        }

        protected override void OnDestroy()
        {
            jointGuide?.Dispose();
            surfaceGuide?.Dispose();
            // 표시를 숨긴 뒤 base에서 카메라의 전용 복사본을 정리한다.
            RestoreVisualParent();
            base.OnDestroy();
            if (runtimeMaterial != null)
            {
                if (Application.isPlaying) Destroy(runtimeMaterial);
                else DestroyImmediate(runtimeMaterial);
            }
            if (weldSparkMaterial != null)
            {
                if (Application.isPlaying) Destroy(weldSparkMaterial);
                else DestroyImmediate(weldSparkMaterial);
            }
        }
    }
}
