using System;
using UnityEngine;
using Metagame.Gameplay.Parts;

namespace Metagame.Gameplay
{
    /// <summary>맵 이탈 복구. 네트워크 어댑터가 권한이 있는 세계에서만 Tick을 호출한다.</summary>
    public sealed class MapRecovery : MonoBehaviour
    {
        [SerializeField, Min(0f)] float edgeMargin = 3f;
        [SerializeField, Min(0f)] float belowFloorMargin = 5f;

        GameSession session;
        Bounds bounds;
        SpaceshipPart[] parts = Array.Empty<SpaceshipPart>();
        ToolSpawn[] tools = Array.Empty<ToolSpawn>();
        LaunchTest[] launches = Array.Empty<LaunchTest>();

        struct ToolSpawn
        {
            public ToolBase Tool;
            public Rigidbody Body;
            public Vector3 Position;
            public Quaternion Rotation;
            public bool Kinematic;
            public bool Gravity;
            public RigidbodyConstraints Constraints;
        }

        public bool ResetPending { get; private set; }
        public event Action SceneResetRequested;

        public void Configure(GameSession owner, Bounds playableBounds,
                              SpaceshipPart[] coreParts, ToolBase[] recoverableTools,
                              LaunchTest[] launchTests)
        {
            session = owner;
            bounds = playableBounds;
            parts = coreParts ?? Array.Empty<SpaceshipPart>();
            launches = launchTests ?? Array.Empty<LaunchTest>();
            recoverableTools = recoverableTools ?? Array.Empty<ToolBase>();
            tools = new ToolSpawn[recoverableTools.Length];
            for (int i = 0; i < tools.Length; i++)
            {
                var tool = recoverableTools[i];
                var body = tool != null ? tool.GetComponentInParent<Rigidbody>() : null;
                if (body == null) continue;
                tools[i] = new ToolSpawn
                {
                    Tool = tool, Body = body, Position = body.position, Rotation = body.rotation,
                    Kinematic = body.isKinematic, Gravity = body.useGravity,
                    Constraints = body.constraints
                };
            }
            ResetPending = false;
        }

        // 상한 높이는 없다. 점프·저중력·발사는 정상적인 맵 위 활동이다.
        public static bool IsOutside(Vector3 position, Bounds playableBounds,
                                     float horizontalMargin, float floorMargin)
        {
            if (!IsFinite(position.x) || !IsFinite(position.y) || !IsFinite(position.z))
                return true;
            var min = playableBounds.min;
            var max = playableBounds.max;
            return position.x < min.x - horizontalMargin || position.x > max.x + horizontalMargin
                || position.z < min.z - horizontalMargin || position.z > max.z + horizontalMargin
                || position.y < min.y - floorMargin;
        }

        static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        public bool IsOutside(Vector3 position) =>
            IsOutside(position, bounds, edgeMargin, belowFloorMargin);

        public bool RecoverySuspended
        {
            get
            {
                if (ResetPending || (session != null && (session.Cleared || session.EndingActive || session.CinematicInputBlocked))) return true;
                for (int i = 0; i < launches.Length; i++)
                    if (launches[i] != null &&
                        (launches[i].State == LaunchState.Ascending || launches[i].State == LaunchState.Success))
                        return true;
                return false;
            }
        }

        public void Tick()
        {
            if (RecoverySuspended) return;

            // 도구나 플레이어를 먼저 되돌려 파츠 유실을 숨기지 않는다.
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] == null || !IsOutside(parts[i].transform.position)) continue;
                ResetPending = true;
                Debug.LogWarning($"[MapRecovery] 핵심 파츠 이탈: {parts[i].name}. 게임 씬을 초기화합니다.");
                SceneResetRequested?.Invoke();
                return;
            }

            // Esc 메뉴 중에도 세계 물리는 계속 흐른다. 입력 루프와 무관하게 복구한다.
            if (session != null && !session.RecoveryFromServer && session.LocalPlayer != null
                && IsOutside(session.LocalPlayer.Position))
                session.RecoverFallenPlayer(session.LocalPlayer, session.Transport);

            for (int i = 0; i < tools.Length; i++)
            {
                var spawn = tools[i];
                if (spawn.Tool == null || spawn.Body == null || !IsOutside(spawn.Body.position)) continue;
                // 손·등 장비 소유 상태를 먼저 해제해야 다음 물리 틱에 다시 끌려가지 않는다.
                if (spawn.Tool.Owner != null) spawn.Tool.Owner.DropEquipment();
                spawn.Tool.Unequip();
                spawn.Body.isKinematic = false;
                spawn.Body.linearVelocity = Vector3.zero;
                spawn.Body.angularVelocity = Vector3.zero;
                spawn.Body.position = spawn.Position;
                spawn.Body.rotation = spawn.Rotation;
                spawn.Body.transform.SetPositionAndRotation(spawn.Position, spawn.Rotation);
                spawn.Body.useGravity = spawn.Gravity;
                spawn.Body.constraints = spawn.Constraints;
                spawn.Body.isKinematic = spawn.Kinematic;
                if (!spawn.Kinematic) spawn.Body.WakeUp();
            }
        }
    }
}
