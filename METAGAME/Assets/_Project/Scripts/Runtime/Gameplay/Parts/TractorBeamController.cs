using System.Collections.Generic;
using UnityEngine;
using Metagame.Core;

namespace Metagame.Gameplay.Parts
{
    /// <summary>
    /// 운반 장치의 탑승·운전·견인 입력을 연결한다.
    ///
    /// 운반 장치는 파츠와 무관하게 먼저 탑승할 수 있다. 탑승 중 좌·우클릭을 누르면
    /// 카메라가 겨눈 파츠를 선택하고, 실제로 장치 바로 아래에 들어왔을 때만 TractorBeam이
    /// 같은 물리 규칙으로 들어 올린다. 따라서 고정쇠가 남은 파츠는 탑승을 막지 않지만,
    /// 견인 순간에는 기존 고정쇠 규칙의 안내를 받는다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TractorBeamController : MonoBehaviour
    {
        const float FocusRange = 34f;
        const float FocusHalfAngle = 38f;
        const float ArenaLimit = 21f;
        const float PartAimRange = 60f;
        const float VehicleFocusHeight = 1.2f;
        const int PartHitBufferSize = 12;

        PlayerRig player;
        TransportVehicle[] vehicles = System.Array.Empty<TransportVehicle>();
        InputFrame input;
        TransportVehicle mountedVehicle;
        string statusMessage = "";
        float statusUntil;
        bool previousGrabLeft;
        bool previousGrabRight;
        readonly RaycastHit[] partHits = new RaycastHit[PartHitBufferSize];

        public TransportVehicle FocusedVehicle { get; private set; }
        public TransportVehicle MountedVehicle => mountedVehicle;
        public TractorBeam ActiveBeam => mountedVehicle != null ? mountedVehicle.Beam : null;
        public TractorBeam FocusedBeam => FocusedVehicle != null ? FocusedVehicle.Beam : null;
        public bool IsControlling => mountedVehicle != null;
        public string StatusMessage => Time.unscaledTime <= statusUntil ? statusMessage : "";

        /// <summary>
        /// 장치가 씬에 배치되는 위치는 실행할 때마다 맵의 빈 지점으로 섞는다.
        /// EditMode 테스트는 randomizeVehicles=false로 고정된 배치를 사용한다.
        /// </summary>
        public void Configure(PlayerRig localPlayer, TractorBeam[] beams, bool randomizeVehicles = true)
        {
            player = localPlayer;
            beams = beams ?? System.Array.Empty<TractorBeam>();
            vehicles = new TransportVehicle[beams.Length];

            for (var i = 0; i < beams.Length; i++)
            {
                var beam = beams[i];
                if (beam == null) continue;

                var vehicle = beam.GetComponent<TransportVehicle>();
                if (vehicle == null) vehicle = beam.gameObject.AddComponent<TransportVehicle>();
                vehicle.Initialize(beam, TransportVehicle.BeamHoldDistance);
                vehicles[i] = vehicle;
            }

            if (randomizeVehicles) PlaceVehiclesRandomly();
            RefreshFocus();
        }

        public void SetInput(InputFrame frame)
        {
            input = frame;
            RefreshFocus();

            var leftPressed = frame.GrabLeft && !previousGrabLeft;
            var rightPressed = frame.GrabRight && !previousGrabRight;
            var beamPressed = leftPressed || rightPressed;

            if (frame.TransportAction)
            {
                if (mountedVehicle != null)
                {
                    if (mountedVehicle.Beam != null && mountedVehicle.Beam.IsHolding)
                        mountedVehicle.Beam.Release();
                    mountedVehicle.BeginDismount();
                    SetStatus("하차 중 · 운반 장치가 바닥으로 내려갑니다");
                }
                else
                {
                    BoardFocusedVehicle();
                }
            }

            // E로 막 탑승한 같은 프레임에는 클릭을 소비하지 않는다. 장치를 띄운 뒤
            // 조준하고 누르면 의도한 견인 입력이 된다.
            if (mountedVehicle != null && beamPressed && !frame.TransportAction)
                ToggleBeamFromAim();

            previousGrabLeft = frame.GrabLeft;
            previousGrabRight = frame.GrabRight;
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
            if (mountedVehicle == null) return;

            if (player == null || player.Cam == null)
            {
                Reset();
                return;
            }

            var wish = player.Cam.CameraRelativeMove(input.Move);
            mountedVehicle.SetDriveVector(wish);
            mountedVehicle.SetFacingDirection(player.Cam.PlanarForward);
            mountedVehicle.SetBoost(input.Run);   // Shift — 가속 이동(기획 §14.1)

            // Space 상승 · Ctrl 하강. 탑승 중에는 PlayerRig 가 점프·팔을 돌리지 않으므로
            // (PlayerRig.FixedUpdate 가 일찍 반환한다) 이 두 키를 고도에 그대로 쓴다.
            var vertical = (input.Jump ? 1f : 0f) - (input.ArmsUp ? 1f : 0f);
            mountedVehicle.SetVerticalInput(vertical);

            // F 로 광역 저중력장을 켜고 끈다(기획 §14.1).
            if (input.FieldToggle)
            {
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
            if (FocusedVehicle == null)
            {
                SetStatus("먼저 운반 장치를 조준하세요");
                return;
            }

            if (player == null)
            {
                SetStatus("운반 장치를 조작할 플레이어가 없습니다");
                return;
            }

            if (!FocusedVehicle.TryBoard(player))
            {
                SetStatus("운반 장치에 더 가까이 다가가세요");
                return;
            }

            mountedVehicle = FocusedVehicle;
            if (player.Cam != null)
                mountedVehicle.SetFacingDirection(player.Cam.PlanarForward);
            SetStatus("탑승 완료 · WASD로 운전 · 좌/우클릭으로 견인 · E 하차");
        }

        void ToggleBeamFromAim()
        {
            if (mountedVehicle == null || mountedVehicle.Beam == null) return;

            if (!mountedVehicle.IsHovering)
            {
                SetStatus("운반 장치가 부유 높이에 오르는 중입니다");
                return;
            }

            var beam = mountedVehicle.Beam;
            if (beam.IsHolding)
            {
                beam.Release();
                SetStatus("파츠를 내려놓았습니다");
                return;
            }

            var candidate = FindAimedPart();
            if (candidate == null)
            {
                SetStatus("조준한 곳에 운반할 파츠가 없습니다");
                return;
            }

            var result = beam.TryGrab(candidate);
            SetStatus(result == BeamGrabResult.Grabbed
                ? "견인 시작 · 좌/우클릭으로 내려놓기"
                : FailureMessage(result));
        }

        void RefreshFocus()
        {
            if (mountedVehicle != null)
            {
                FocusedVehicle = mountedVehicle;
                return;
            }

            FocusedVehicle = FindFocusedVehicle();
        }

        TransportVehicle FindFocusedVehicle()
        {
            if (player == null || player.Cam == null) return null;

            var origin = player.Cam.transform.position;
            var forward = player.Cam.transform.forward;
            TransportVehicle best = null;
            var bestAngle = float.MaxValue;
            var bestDistance = float.MaxValue;

            for (var i = 0; i < vehicles.Length; i++)
            {
                var vehicle = vehicles[i];
                if (vehicle == null || !vehicle.isActiveAndEnabled || vehicle.IsReturning) continue;

                var target = vehicle.transform.position + Vector3.up * VehicleFocusHeight;
                var offset = target - origin;
                var distance = offset.magnitude;
                if (distance > FocusRange || distance < 0.001f) continue;

                var angle = Vector3.Angle(forward, offset);
                if (angle > FocusHalfAngle) continue;

                if (angle < bestAngle - 0.01f
                    || Mathf.Abs(angle - bestAngle) <= 0.01f && distance < bestDistance)
                {
                    best = vehicle;
                    bestAngle = angle;
                    bestDistance = distance;
                }
            }

            return best;
        }

        SpaceshipPart FindAimedPart()
        {
            if (player == null || player.Cam == null) return null;

            var origin = player.Cam.transform.position;
            var direction = player.Cam.transform.forward;
            var count = Physics.SphereCastNonAlloc(origin, 0.35f, direction, partHits,
                                                   PartAimRange, GameLayers.PartMask,
                                                   QueryTriggerInteraction.Collide);

            SpaceshipPart best = null;
            var bestDistance = float.MaxValue;
            for (var i = 0; i < count; i++)
            {
                var collider = partHits[i].collider;
                if (collider == null) continue;

                var part = collider.GetComponentInParent<SpaceshipPart>();
                if (part == null || partHits[i].distance >= bestDistance) continue;

                best = part;
                bestDistance = partHits[i].distance;
            }

            return best;
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

        static string FailureMessage(BeamGrabResult result)
        {
            return result switch
            {
                BeamGrabResult.NoTarget => "운반 장치가 파츠 바로 위에 있어야 합니다",
                BeamGrabResult.Clamped => "고정쇠를 모두 해제해야 파츠를 들어 올릴 수 있습니다",
                BeamGrabResult.Welded => "용접된 파츠는 운반할 수 없습니다",
                BeamGrabResult.AlreadyCarried => "다른 운반 작업이 진행 중입니다",
                BeamGrabResult.BeamBusy => "이 견인 빔은 이미 파츠를 들고 있습니다",
                _ => "견인 빔을 작동할 수 없습니다",
            };
        }
    }
}
