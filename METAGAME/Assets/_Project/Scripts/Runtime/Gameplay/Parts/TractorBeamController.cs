using System.Collections.Generic;
using UnityEngine;
using Metagame.Core;
using Metagame.Gameplay.Interaction;

namespace Metagame.Gameplay.Parts
{
    /// <summary>
    /// 운반 장치의 탑승·운전·견인 입력을 연결한다.
    ///
    /// 활성화된 운반 장치에 탑승·운전·견인 입력을 연결한다. 탑승 중 좌·우클릭과 F는
    /// 장치 아래 광역 저중력장을 켜고 끈다. Shift는 기존 가속 이동에만 사용한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TractorBeamController : MonoBehaviour
    {
        const float ArenaLimit = 21f;

        PlayerRig player;
        TransportVehicle[] vehicles = System.Array.Empty<TransportVehicle>();
        InputFrame input;
        TransportVehicle mountedVehicle;
        string statusMessage = "";
        float statusUntil;
        bool previousGrabLeft;
        bool previousGrabRight;
        bool fieldToggleRequested;
        bool physicsAuthority = true;

        /// <summary>
        /// 실제로 태우고·끌고·움직일 수 있는가. 매치에 붙은 클라이언트에서는 꺼 둔다
        /// (S15P21B202-149).
        ///
        /// 물리는 서버가 돈다. 클라이언트가 자기 화면에서 탑승·견인을 실행하면 서버가
        /// 보낸 결과와 싸운다 — 잠깐 탔다가 도로 내려지는 그림이 된다. 꺼 두어도
        /// 조준 대상(<see cref="FocusedVehicle"/>)은 계속 갱신되므로 HUD 안내는 나온다.
        ///
        /// 혼자 실행할 때는 켜져 있다 — 그때는 내 PC 가 곧 서버다.
        /// </summary>
        public bool Authoritative = true;

        /// <summary>
        /// Physics ownership is separate from local input ownership. A dedicated server
        /// has no local player and therefore disables <see cref="Authoritative"/> input,
        /// but it still must simulate the shared low-gravity field.
        /// </summary>
        public bool PhysicsAuthority
        {
            get => physicsAuthority;
            set
            {
                physicsAuthority = value;
                ApplyPhysicsAuthority();
            }
        }

        public TransportVehicle FocusedVehicle { get; private set; }
        public TransportVehicle MountedVehicle => mountedVehicle;
        public TractorBeam ActiveBeam => mountedVehicle != null ? mountedVehicle.Beam : null;
        public TractorBeam FocusedBeam => FocusedVehicle != null ? FocusedVehicle.Beam : null;
        public bool IsControlling => mountedVehicle != null;
        public string StatusMessage => Time.unscaledTime <= statusUntil ? statusMessage : "";

        void OnEnable() => TransportVehicle.AnyActivated += RegisterActivatedVehicle;

        void OnDisable() => TransportVehicle.AnyActivated -= RegisterActivatedVehicle;

        /// <summary>
        /// 명시적으로 전달된 레거시 빔의 위치만 실행 시 맵의 빈 지점으로 섞는다.
        /// Main 씬의 Part_1은 고정못을 뽑은 자리에서 활성화되므로 임의로 옮기지 않는다.
        /// EditMode 테스트는 randomizeVehicles=false로 고정된 배치를 사용한다.
        /// </summary>
        public void Configure(PlayerRig localPlayer, TractorBeam[] beams, bool randomizeVehicles = true)
        {
            player = localPlayer;
            beams = beams ?? System.Array.Empty<TractorBeam>();
            vehicles = System.Array.Empty<TransportVehicle>();

            for (var i = 0; i < beams.Length; i++)
            {
                var beam = beams[i];
                if (beam == null) continue;

                var vehicle = beam.GetComponent<TransportVehicle>();
                if (vehicle == null) vehicle = beam.gameObject.AddComponent<TransportVehicle>();
                vehicle.Initialize(beam, TransportVehicle.BeamHoldDistance);
                RegisterVehicle(vehicle);
            }

            // 컨트롤러보다 먼저 활성화된 Part_1도 등록한다. 보통은 해제 순간의 이벤트가
            // 처리하지만, 씬 저장 상태나 서버 초기화 순서가 달라도 동일하게 동작해야 한다.
            var activators = FindObjectsByType<PartTractorActivator>(FindObjectsInactive.Include);
            for (var i = 0; i < activators.Length; i++)
                if (activators[i] != null) RegisterVehicle(activators[i].Vehicle);

            player?.ConfigureTransportInteractionTargets(vehicles);
            ApplyPhysicsAuthority();
            if (randomizeVehicles && beams.Length > 0) PlaceVehiclesRandomly();
            RefreshFocus();
        }

        void RegisterActivatedVehicle(TransportVehicle vehicle)
        {
            RegisterVehicle(vehicle);
            vehicle?.SetFieldPhysicsAuthority(physicsAuthority);
            player?.ConfigureTransportInteractionTargets(vehicles);
            RefreshFocus();
        }

        void RegisterVehicle(TransportVehicle vehicle)
        {
            if (vehicle == null) return;

            for (var i = 0; i < vehicles.Length; i++)
                if (vehicles[i] == vehicle) return;

            var next = new TransportVehicle[vehicles.Length + 1];
            System.Array.Copy(vehicles, next, vehicles.Length);
            next[next.Length - 1] = vehicle;
            vehicles = next;
        }

        void ApplyPhysicsAuthority()
        {
            for (var i = 0; i < vehicles.Length; i++)
                if (vehicles[i] != null)
                    vehicles[i].SetFieldPhysicsAuthority(physicsAuthority);
        }

        public void SetInput(InputFrame frame)
        {
            input = frame;
            RefreshFocus();

            // 조준까지만 하고 실행은 하지 않는다 — 서버가 할 일이다.
            if (!Authoritative)
            {
                previousGrabLeft = frame.GrabLeft;
                previousGrabRight = frame.GrabRight;
                return;
            }

            var leftPressed = frame.GrabLeft && !previousGrabLeft;
            var rightPressed = frame.GrabRight && !previousGrabRight;
            var beamPressed = leftPressed || rightPressed;
            if (mountedVehicle != null && ((beamPressed && !frame.TransportAction)
                                           || (frame.FieldToggle && !frame.TransportAction)))
                fieldToggleRequested = true;

            if (frame.TransportAction)
            {
                if (mountedVehicle != null)
                {
                    if (mountedVehicle.Beam != null && mountedVehicle.Beam.IsHolding)
                        mountedVehicle.Beam.Release();
                    mountedVehicle.BeginDismount();
                    if (!mountedVehicle.IsOccupied) mountedVehicle = null;
                    SetStatus("하차 중 · 운반 장치가 바닥으로 내려갑니다");
                }
                else
                {
                    BoardFocusedVehicle();
                }
            }

            previousGrabLeft = frame.GrabLeft;
            previousGrabRight = frame.GrabRight;
        }

        /// <summary>개인 복구 시 탑승만 해제한다. 다른 운반체와 파츠 위치는 유지한다.</summary>
        public void ReleaseForRespawn()
        {
            if (mountedVehicle != null && mountedVehicle.Beam != null && mountedVehicle.Beam.IsHolding)
                mountedVehicle.Beam.Release();
            if (mountedVehicle != null) mountedVehicle.ReleaseRiderForRespawn();
            mountedVehicle = null;
            input = new InputFrame();
            previousGrabLeft = false;
            previousGrabRight = false;
            fieldToggleRequested = false;
        }

        public void Reset()
        {
            if (mountedVehicle != null && mountedVehicle.Beam != null && mountedVehicle.Beam.IsHolding)
                mountedVehicle.Beam.Release();

            for (var i = 0; i < vehicles.Length; i++)
                if (vehicles[i] != null) vehicles[i].ForceReset();

            mountedVehicle = null;
            input = new InputFrame();
            previousGrabLeft = false;
            previousGrabRight = false;
            fieldToggleRequested = false;
            RefreshFocus();
        }

        void Update()
        {
            RefreshFocus();
        }

        void FixedUpdate() => Tick(Time.fixedDeltaTime);

        /// <summary>
        /// 탑승 운전을 한 물리 스텝 처리한다. FixedUpdate와 자동 검증이 같은 경로를
        /// 사용하도록 공개해 카메라 기준 이동·선체 정렬을 결정론적으로 점검할 수 있다.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (GameSession.Instance != null && GameSession.Instance.EndingActive) return;
            if (!Authoritative) return;
            if (mountedVehicle == null) return;

            if (player == null)
            {
                Reset();
                return;
            }

            var wish = player.RelativeMove(input.Move);
            mountedVehicle.SetDriveVector(wish);
            mountedVehicle.SetFacingDirection(player.PlanarForward);
            mountedVehicle.SetBoost(input.Run);   // Shift — 가속 이동(기획 §14.1)

            // Space 상승 · Ctrl 하강. 탑승 중에는 PlayerRig 가 점프·팔을 돌리지 않으므로
            // (PlayerRig.FixedUpdate 가 일찍 반환한다) 이 두 키를 고도에 그대로 쓴다.
            var vertical = (input.Jump ? 1f : 0f) - (input.ArmsUp ? 1f : 0f);
            mountedVehicle.SetVerticalInput(vertical);

            // 좌·우클릭과 F는 같은 rising-edge 토글 경로를 공유한다. SetInput에서
            // 요청을 합치므로 같은 프레임에 두 입력이 들어와도 한 번만 바뀐다.
            if (fieldToggleRequested)
            {
                fieldToggleRequested = false;
                mountedVehicle.Field.Toggle();
                SetStatus(mountedVehicle.Field.IsActive ? "저중력장 켬" : "저중력장 끔");
            }

            if (mountedVehicle.Beam != null)
                mountedVehicle.Beam.Tick(deltaTime);

            if (!mountedVehicle.IsOccupied)
                mountedVehicle = null;
        }

        void BoardFocusedVehicle()
        {
            if (FocusedVehicle == null || player == null
                || player.CurrentInteractionTarget.Kind != PlayerInteractionKind.Transport
                || player.CurrentInteractionTarget.Transport != FocusedVehicle)
            {
                SetStatus("먼저 운반 장치를 조준하세요");
                return;
            }

            if (!FocusedVehicle.TryBoard(player))
            {
                SetStatus("운반 장치에 더 가까이 다가가세요");
                return;
            }

            mountedVehicle = FocusedVehicle;
            mountedVehicle.SetFacingDirection(player.PlanarForward);
            SetStatus("탑승 완료 · WASD로 운전 · 좌/우클릭 또는 F로 저중력장 · E 하차");
        }

        void RefreshFocus()
        {
            if (mountedVehicle != null)
            {
                FocusedVehicle = mountedVehicle;
                return;
            }

            player?.RefreshInteractionFocus();
            var target = player != null ? player.CurrentInteractionTarget : PlayerInteractionTarget.None;
            FocusedVehicle = target.Kind == PlayerInteractionKind.Transport ? target.Transport : null;
        }


        void PlaceVehiclesRandomly()
        {
            var occupied = new List<Vector3>(vehicles.Length + 8);
            var parts = FindObjectsByType<SpaceshipPart>(FindObjectsInactive.Include);
            for (var i = 0; i < parts.Length; i++)
                if (parts[i] != null) occupied.Add(parts[i].transform.position);

            if (player != null) occupied.Add(player.Position);
            occupied.Add(new Vector3(0f, 0f, 10f)); // 조립 지점

            // 예전에는 TickCount 로 시드를 만들었다. 그래서 참가자마다 운반체가 다른
            // 곳에 있었다 — 각 PC 가 자기 시계로 따로 뽑았기 때문이다(S15P21B202-152).
            //
            // 이제 이 함수는 서버에서만 돈다. 결과 위치는 NetworkTransform 을 타고
            // 그대로 내려가므로 시드를 맞출 필요가 없다 — 서버가 뽑은 값이 곧 정답이다.
            var random = new System.Random();
            for (var i = 0; i < vehicles.Length; i++)
            {
                var vehicle = vehicles[i];
                if (vehicle == null) continue;

                Vector3 chosen = Vector3.zero;
                var found = false;
                for (var attempt = 0; attempt < 80; attempt++)
                {
                    var x = Mathf.Lerp(-ArenaLimit + 2f, ArenaLimit - 2f, (float)random.NextDouble());
                    var z = Mathf.Lerp(-ArenaLimit + 2f, ArenaLimit - 2f, (float)random.NextDouble());
                    var candidate = new Vector3(x, TransportVehicle.GroundY, z);

                    var clear = true;
                    for (var j = 0; j < occupied.Count; j++)
                    {
                        var delta = candidate - occupied[j];
                        delta.y = 0f;
                        if (delta.sqrMagnitude < 5.8f * 5.8f)
                        {
                            clear = false;
                            break;
                        }
                    }

                    if (!clear) continue;
                    chosen = candidate;
                    found = true;
                    break;
                }

                if (!found)
                    chosen = new Vector3((i == 0 ? -10f : 10f), TransportVehicle.GroundY, -17f);

                occupied.Add(chosen);
                var yaw = (float)random.NextDouble() * 360f;
                vehicle.PlaceOnGround(chosen, yaw);
            }
        }

        void SetStatus(string message)
        {
            statusMessage = message ?? "";
            statusUntil = Time.unscaledTime + 4f;
        }

    }
}
