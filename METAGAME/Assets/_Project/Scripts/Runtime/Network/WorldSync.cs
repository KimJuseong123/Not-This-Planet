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

        /// <summary>고정쇠. 위치가 아니라 <b>진행도</b>가 사람마다 달라지는 것들이다.</summary>
        readonly List<PartClamp> clamps = new List<PartClamp>();
        ClampState[] clampTargets = Array.Empty<ClampState>();
        Pose[] targets = Array.Empty<Pose>();
        float sendTimer;
        bool ready;
        bool warnedMismatch;
        bool warnedClampMismatch;

        /// <summary>클라이언트로서 챙길 것이 남았는가. 스폰이 등록된 뒤에만 참이 된다.</summary>
        bool needsClientPrepare;

        public override void OnStartServer()
        {
            Collect();
            ready = true;

            // 용접은 서버에서만 판정된다. 그 사건을 받아 모두에게 중계한다.
            WorldEvents.WeldGrown += RpcWeldGrown;
            WorldEvents.WeldShrunk += RpcWeldShrunk;

            Debug.Log($"[WorldSync] 서버가 {tracked.Count}개를 맡는다 · 고정쇠 {clamps.Count}개 · {Order()}");
        }

        public override void OnStartClient()
        {
            // 호스트 모드면 서버 쪽에서 이미 준비됐다.
            if (isServer) return;

            // 여기서 챙기지 않는다. 이 스폰은 방장의 씬이 준비되면 오는데, 내 게임 씬은
            // 아직 뜨는 중일 수 있다. 그때 챙기면 0개를 챙긴 채 "준비 완료" 가 되고,
            // 이후로는 서버가 보내는 개수와 영원히 어긋나 아무것도 따라가지 않는다
            // (2026-09-03 실측 — 참가자만 오브젝트가 동기화되지 않았다).
            // Update 가 씬이 올라올 때까지 기다렸다 챙긴다.
            needsClientPrepare = true;
        }

        /// <summary>
        /// 클라이언트가 따라갈 것들을 챙긴다. 게임 씬이 아직이면 거짓을 돌려주고 다음에 다시 본다.
        /// </summary>
        bool PrepareClient()
        {
            // 씬이 올라왔는지는 "옮길 것이 있는가" 로 판단한다(WorldSyncSpawner 와 같은 기준).
            if (FindAnyObjectByType<TransportVehicle>() == null) return false;

            Collect();
            if (tracked.Count == 0) return false;

            foreach (var body in tracked)
            {
                if (body == null) continue;
                body.isKinematic = true;   // 서버가 보낸 자세만 따라간다
            }

            targets = new Pose[tracked.Count];
            for (int i = 0; i < tracked.Count; i++)
                targets[i] = Read(tracked[i]);

            // 이 화면은 그림이다. 세계를 스스로 바꾸면 서버가 보낸 것과 겹친다.
            WorldEvents.RemoteDecides = true;

            ready = true;
            Debug.Log($"[WorldSync] 클라이언트가 {tracked.Count}개를 받는다 · 고정쇠 {clamps.Count}개 · {Order()}");
            return true;
        }

        /// <summary>
        /// 움직이는 것들을 <b>양쪽이 같은 순서로</b> 줄 세운다.
        ///
        /// 이름만으로는 같은 이름이 여럿일 때 순서가 흔들린다. 씬이 정해 준 처음
        /// 위치는 서버와 클라이언트가 똑같으므로, 이름이 같으면 그 위치로 가른다.
        /// </summary>
        /// <summary>고정쇠 하나의 상태. 위치로는 알 수 없어 따로 실어 보낸다.</summary>
        public struct ClampState
        {
            public float Progress;      // 0~1
            public bool Released;
        }

        void Collect()
        {
            tracked.Clear();

            foreach (var part in FindObjectsByType<SpaceshipPart>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Add(part != null ? part.GetComponent<Rigidbody>() : null);

            foreach (var vehicle in FindObjectsByType<TransportVehicle>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Add(vehicle != null ? vehicle.GetComponent<Rigidbody>() : null);

            tracked.Sort(Compare);

            clamps.Clear();
            foreach (var clamp in FindObjectsByType<PartClamp>(FindObjectsInactive.Include,
                                                               FindObjectsSortMode.None))
                if (clamp != null) clamps.Add(clamp);

            clamps.Sort(CompareByPath);
        }

        static int CompareByPath(Component a, Component b)
        {
            int byPath = string.CompareOrdinal(PathOf(a.transform), PathOf(b.transform));
            return byPath != 0
                ? byPath
                : a.transform.GetSiblingIndex().CompareTo(b.transform.GetSiblingIndex());
        }

        void Add(Rigidbody body)
        {
            if (body != null && !tracked.Contains(body)) tracked.Add(body);
        }

        /// <summary>
        /// 양쪽이 반드시 같은 순서를 내야 한다 — 번호 하나가 밀리면 그 뒤 전부가 남의 자리로 간다.
        ///
        /// 예전에는 이름이 같을 때 <b>지금 위치</b>로 동점을 갈랐다. 그런데 서버는 시작하자마자
        /// 목록을 만들고 클라이언트는 자기 게임 씬이 다 뜬 뒤에 만든다. 그 사이에 서버가 물체를
        /// 움직였으면 위치가 달라져 순서가 갈리고, 탈것 자리에 다른 물체의 좌표가 들어간다 —
        /// 탈것은 그대로인데 사람만 날아다니는 것처럼 보인다(2026-09-03).
        ///
        /// 그래서 <b>시간이 지나도 변하지 않는 것</b>으로만 가른다. 계층 경로와 형제 순번은
        /// 같은 씬을 같은 코드로 세우는 한 양쪽이 똑같다.
        /// </summary>
        static int Compare(Rigidbody a, Rigidbody b)
        {
            int byPath = string.CompareOrdinal(PathOf(a.transform), PathOf(b.transform));
            if (byPath != 0) return byPath;

            return a.transform.GetSiblingIndex().CompareTo(b.transform.GetSiblingIndex());
        }

        /// <summary>
        /// 지금 줄 세운 순서를 이름으로 늘어놓는다. 양쪽 로그를 나란히 놓으면 어긋남이 바로 보인다 —
        /// 개수만 같고 순서가 다르면 좌표가 남의 자리로 가는데, 그건 개수 검사로 못 잡는다.
        /// </summary>
        string Order()
        {
            var names = new string[tracked.Count];
            for (int i = 0; i < tracked.Count; i++)
                names[i] = tracked[i] != null ? tracked[i].name : "(없음)";
            return string.Join(", ", names);
        }

        /// <summary>뿌리부터의 계층 경로. 이름이 겹쳐도 부모가 다르면 갈린다.</summary>
        static string PathOf(Transform t)
        {
            var path = new System.Text.StringBuilder(t.name);
            for (var parent = t.parent; parent != null; parent = parent.parent)
                path.Insert(0, parent.name).Insert(0, '/');
            return path.ToString();
        }

        static Pose Read(Rigidbody body)
        {
            return body == null
                ? default
                : new Pose { Position = body.position, Rotation = body.rotation };
        }

        public override void OnStopClient()
        {
            if (!isServer) WorldEvents.RemoteDecides = false;
        }

        public override void OnStopServer()
        {
            // 남겨 두면 다음 매치에서 이미 사라진 오브젝트를 향해 사건이 날아간다.
            WorldEvents.WeldGrown -= RpcWeldGrown;
            WorldEvents.WeldShrunk -= RpcWeldShrunk;
        }

        void Update()
        {
            if (!ready)
            {
                // 여기서 isServer 를 보면 안 된다 — 스폰 등록 전에는 netIdentity 가 없어
                // 그 자리에서 NullReference 가 난다(실측). OnStartClient 가 세운 표시만 본다.
                if (!needsClientPrepare) return;
                if (!PrepareClient()) return;
            }

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

            var states = new ClampState[clamps.Count];
            for (int i = 0; i < clamps.Count; i++)
            {
                var clamp = clamps[i];
                states[i] = clamp == null
                    ? default
                    : new ClampState { Progress = clamp.Progress, Released = clamp.IsReleased };
            }
            RpcApplyClamps(states);
        }

        /// <summary>
        /// 고정쇠의 진행도와 해제 여부를 맞춘다. 판정은 서버가 하고 여기서는 따라만 간다.
        /// </summary>
        [ClientRpc]
        void RpcApplyClamps(ClampState[] states)
        {
            if (isServer) return;               // 호스트는 자기 물리가 정답이다
            if (states == null || !ready) return;

            if (states.Length != clamps.Count)
            {
                if (!warnedClampMismatch)
                {
                    warnedClampMismatch = true;
                    Debug.LogWarning($"[WorldSync] 고정쇠 개수가 다르다 — 서버 {states.Length} · 여기 {clamps.Count}");
                }
                return;
            }
            warnedClampMismatch = false;

            for (int i = 0; i < clamps.Count; i++)
                if (clamps[i] != null) clamps[i].ApplyNetworkState(states[i].Progress, states[i].Released);
        }

        // ── 용접 — 상태가 아니라 행동을 실어 나른다 ─────────────

        /// <summary>
        /// 서버에서 용접이 진행되면 그 사실을 모두에게 알린다.
        /// 부품이 이미 같은 자리에 맞춰져 있으므로, 같은 자리에서 같은 양만큼 자라게 하면
        /// 같은 모양이 나온다 — 덩어리 사슬을 통째로 보내지 않아도 된다(-152).
        /// </summary>
        [ClientRpc]
        void RpcWeldGrown(Vector3 nozzle, float amount)
        {
            if (isServer) return;
            WeldBead.TryGrowNear(nozzle, amount, out _);
        }

        [ClientRpc]
        void RpcWeldShrunk(Vector3 nozzle, float amount)
        {
            if (isServer) return;
            WeldBead.TryShrinkNear(nozzle, amount, out _);
        }

        [ClientRpc]
        void RpcApply(Pose[] poses)
        {
            if (isServer) return;               // 호스트 모드에서는 내 물리가 곧 정답이다
            if (poses == null) return;

            // 아직 챙기는 중이다. 이때의 개수 차이는 정상이라 알릴 것이 없다 —
            // 내 게임 씬이 방장보다 늦게 뜨면 반드시 한 번은 지나가는 자리다.
            if (!ready) return;

            // 개수가 다르면 서로 다른 세계를 보고 있다는 뜻이다. 억지로 맞추면
            // 엉뚱한 물체가 엉뚱한 자리로 간다 — 그럴 때는 손대지 않고 알린다.
            if (poses.Length != tracked.Count)
            {
                // 매 전송마다 찍으면 로그가 덮인다. 처음 한 번만 알린다.
                if (!warnedMismatch)
                {
                    warnedMismatch = true;
                    Debug.LogWarning($"[WorldSync] 개수가 다르다 — 서버 {poses.Length} · 여기 {tracked.Count}");
                }
                return;
            }
            warnedMismatch = false;

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
