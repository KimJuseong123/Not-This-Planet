using System.Collections.Generic;
using Metagame.Gameplay.Parts;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 소켓 관련 테스트가 공유하는 그레이박스 파츠 조립 도구.
    ///
    /// 파츠는 밑면과 윗면 가운데에 소켓을 하나씩 갖는다.
    /// 소켓의 forward 는 접합면이 바라보는 바깥 방향이므로 밑면은 아래, 윗면은 위를 향한다.
    /// </summary>
    static class SocketTestRig
    {
        public static SpaceshipPart CreatePart(List<GameObject> spawned, Vector3 position)
        {
            var go = new GameObject("Part");
            go.transform.position = position;
            go.transform.localScale = new Vector3(
                PhysicsScale.PartWidth, PhysicsScale.PartHeight, PhysicsScale.PartWidth);
            go.AddComponent<BoxCollider>();

            var part = go.AddComponent<SpaceshipPart>();
            spawned.Add(go);

            AddSocket(part, SocketRole.Base);
            AddSocket(part, SocketRole.Top);

            part.Initialize();
            part.RefreshSockets();
            UnityEngine.Physics.SyncTransforms();
            return part;
        }

        /// <summary>고정쇠가 없어 바로 조립 상태(Placed)까지 올린 파츠.</summary>
        public static SpaceshipPart Placed(List<GameObject> spawned, Vector3 position)
        {
            var part = CreatePart(spawned, position);
            part.TryTransitionTo(PartState.Free);
            part.TryTransitionTo(PartState.Carried);
            part.TryTransitionTo(PartState.Placed);
            return part;
        }

        /// <summary>고정쇠가 없어 바로 Free 로 올린 파츠. 빔이 붙잡을 수 있는 상태다.</summary>
        public static SpaceshipPart Freed(List<GameObject> spawned, Vector3 position)
        {
            var part = CreatePart(spawned, position);
            part.TryTransitionTo(PartState.Free);
            return part;
        }

        public static PartSocket BaseSocketOf(SpaceshipPart part) => SocketOf(part, SocketRole.Base);

        public static PartSocket TopSocketOf(SpaceshipPart part) => SocketOf(part, SocketRole.Top);

        static PartSocket SocketOf(SpaceshipPart part, SocketRole role)
        {
            foreach (var socket in part.Sockets)
                if (socket != null && socket.Role == role)
                    return socket;

            return null;
        }

        static void AddSocket(SpaceshipPart part, SocketRole role)
        {
            var direction = role == SocketRole.Base ? Vector3.down : Vector3.up;

            var go = new GameObject(role.ToString());
            go.transform.SetParent(part.transform, false);
            go.transform.localPosition = direction * 0.5f;
            go.transform.localRotation = Quaternion.LookRotation(direction, Vector3.forward);

            go.AddComponent<PartSocket>().SetRole(role);
        }
    }
}
