using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using Metagame.Gameplay;
using Metagame.Gameplay.Parts;

namespace Metagame.Network
{
    /// <summary>
    /// 세계에서 움직이는 것들(부품·운반체)의 자세를 서버에서 모두에게 내려보낸다
    /// (S15P21B202-152).
    ///
    /// <b>왜 오브젝트마다 NetworkIdentity 를 달지 않았나.</b> 부품과 운반체는 코드가
    /// 만드는 것이 아니라 <b>씬에 배치돼 있다</b>. 씬에 놓인 오브젝트를 Mirror 가
    /// 짝지으려면 sceneId 가 필요한데, 그 값은 Unity 에디터가 씬을 저장할 때 부여한다.
    /// 손으로 넣을 수 있는 값이 아니라, 씬 파일을 직접 고치는 방법으로는 안전하게
    /// 만들 수 없다. 그래서 중계용 오브젝트 하나를 서버가 띄우고, 그것이 씬 오브젝트들의
    /// 자세를 실어 나른다.
    ///
    /// 서버와 클라이언트는 <b>같은 씬</b>을 열고 있으므로 같은 오브젝트를 갖고 있다.
    /// 양쪽이 같은 규칙으로 줄을 세우면(<see cref="Collect"/>) 번호만으로 짝지을 수 있다.
    ///
    /// 물리는 서버에서만 돈다(-145). 클라이언트의 같은 오브젝트는 키네마틱으로 두고
    /// 받은 자세를 따라가기만 한다 — 양쪽이 각자 굴리면 결과가 갈린다.
    /// </summary>
    public sealed class WorldSync : NetworkBehaviour
    {
        /// 초당 20번. 사람과 같은 주기다(-148 §5-1).
        const float SendInterval = 1f / 20f;

        /// 받은 자세로 옮겨 가는 속도. 20Hz 로 받은 사이를 메운다.
        const float Smoothing = 20f;

        [Serializable]
        public struct Pose
        {
            public Vector3 Position;
            public Quaternion Rotation;
        }

        readonly List<Rigidbody> tracked = new List<Rigidbody>();
        Pose[] targets = Array.Empty<Pose>();
        float sendTimer;
        bool ready;

        public override void OnStartServer()
        {
            Collect();
            ready = true;
            Debug.Log($"[WorldSync] 서버가 {tracked.Count}개를 맡는다");
        }

        public override void OnStartClient()
        {
            if (isServer) return;   // 호스트 모드면 서버 쪽에서 이미 준비됐다

            Collect();
            foreach (var body in tracked)
            {
                if (body == null) continue;
                body.isKinematic = true;   // 서버가 보낸 자세만 따라간다
            }

            targets = new Pose[tracked.Count];
            for (int i = 0; i < tracked.Count; i++)
                targets[i] = Read(tracked[i]);

            ready = true;
            Debug.Log($"[WorldSync] 클라이언트가 {tracked.Count}개를 받는다");
        }

        /// <summary>
        /// 움직이는 것들을 <b>양쪽이 같은 순서로</b> 줄 세운다.
        ///
        /// 이름만으로는 같은 이름이 여럿일 때 순서가 흔들린다. 씬이 정해 준 처음
        /// 위치는 서버와 클라이언트가 똑같으므로, 이름이 같으면 그 위치로 가른다.
        /// </summary>
        void Collect()
        {
            tracked.Clear();

            foreach (var part in FindObjectsByType<SpaceshipPart>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Add(part != null ? part.GetComponent<Rigidbody>() : null);

            foreach (var vehicle in FindObjectsByType<TransportVehicle>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Add(vehicle != null ? vehicle.GetComponent<Rigidbody>() : null);

            tracked.Sort(Compare);
        }

        void Add(Rigidbody body)
        {
            if (body != null && !tracked.Contains(body)) tracked.Add(body);
        }

        static int Compare(Rigidbody a, Rigidbody b)
        {
            int byName = string.CompareOrdinal(a.name, b.name);
            if (byName != 0) return byName;

            Vector3 pa = a.transform.position, pb = b.transform.position;
            int byX = pa.x.CompareTo(pb.x);
            if (byX != 0) return byX;
            int byZ = pa.z.CompareTo(pb.z);
            return byZ != 0 ? byZ : pa.y.CompareTo(pb.y);
        }

        static Pose Read(Rigidbody body)
        {
            return body == null
                ? default
                : new Pose { Position = body.position, Rotation = body.rotation };
        }

        void Update()
        {
            if (!ready) return;

            if (isServer) SendFromServer();
            else FollowOnClient();
        }

        void SendFromServer()
        {
            sendTimer += Time.deltaTime;
            if (sendTimer < SendInterval) return;
            sendTimer = 0f;

            var poses = new Pose[tracked.Count];
            for (int i = 0; i < tracked.Count; i++)
                poses[i] = Read(tracked[i]);

            RpcApply(poses);
        }

        [ClientRpc]
        void RpcApply(Pose[] poses)
        {
            if (isServer) return;               // 호스트 모드에서는 내 물리가 곧 정답이다
            if (poses == null) return;

            // 개수가 다르면 서로 다른 세계를 보고 있다는 뜻이다. 억지로 맞추면
            // 엉뚱한 물체가 엉뚱한 자리로 간다 — 그럴 때는 손대지 않고 알린다.
            if (poses.Length != tracked.Count)
            {
                Debug.LogWarning($"[WorldSync] 개수가 다르다 — 서버 {poses.Length} · 여기 {tracked.Count}");
                return;
            }

            targets = poses;
        }

        void FollowOnClient()
        {
            float t = 1f - Mathf.Exp(-Smoothing * Time.deltaTime);

            for (int i = 0; i < tracked.Count && i < targets.Length; i++)
            {
                var body = tracked[i];
                if (body == null) continue;

                body.transform.SetPositionAndRotation(
                    Vector3.Lerp(body.transform.position, targets[i].Position, t),
                    Quaternion.Slerp(body.transform.rotation, targets[i].Rotation, t));
            }
        }
    }
}
