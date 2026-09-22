using System.Collections.Generic;
using UnityEngine;

namespace Metagame.Gameplay
{
    /// <summary>
    /// 블렌더로 만든 우주 토끼 메시(<c>SF_Rabbit_Base</c>)를 물리 인형 위에 씌우는 어댑터.
    ///
    /// <b>물리는 하나도 건드리지 않는다.</b> 랙돌의 강체·콜라이더·조인트는 그대로 두고,
    /// 프리미티브 Renderer 만 꺼서 눈에 안 보이게 한 다음, 모델의 본을
    /// 대응하는 강체의 월드 트랜스폼으로 매 LateUpdate 마다 덮어쓴다.
    ///
    /// 이 모델은 앞의 우주비행사와 달리 <b>부드럽게 스킨된 단일 메시</b>다(정점 10,649개).
    /// 그래서 본을 억지로 늘이면 살이 늘어난다 — 랙돌 치수를 모델에서 뽑아
    /// 맞춰 둔 이유가 그것이다(<see cref="RagdollBuilder"/> 의 치수 주석 참고).
    ///
    /// 함정 두 가지.
    ///
    /// 1) <b>좌우는 이름이 아니라 좌표로 판단한다.</b> FBX 축 변환이 X 를 뒤집기 때문에
    ///    모델마다 <c>L_</c> 이 어느 쪽인지 달라진다. 이 토끼는 <c>L_</c> 이 유니티 −X 라
    ///    우리 인형의 왼쪽과 맞지만, 앞의 우주비행사는 반대였다.
    /// 2) 모델은 팔을 옆으로 뻗은 <b>T 자세</b>로 바인드돼 있는데 랙돌의 팔은 아래로 내려와 있다.
    ///    바인드 그대로 따라가게 하면 손이 몸 옆 30cm 에 떠 있는다 —
    ///    잡는 지점과 보이는 손이 어긋난다. 그래서 팔 6개만 "아래로 펴는" 보정을 넣고,
    ///    팔 길이는 모델 자신의 마디 길이로 이어 붙여 살이 끊기지 않게 했다.
    ///
    /// 본의 로컬 +Y 가 뼈를 따라간다(블렌더 규약). 방향 보정은 전부 이 축으로 한다.
    /// </summary>
    public class SpaceSuit : MonoBehaviour
    {
        public const string ResourceKey = "SF_Rabbit_Base";

        /// <summary>
        /// 모델은 0.979m 로 만들어져 있다. 귀 끝까지 1.33m 가 되도록 균등 배율을 건다.
        /// 이 값을 바꾸면 <see cref="RagdollBuilder"/> 의 부위 치수도 같이 다시 재야 한다 —
        /// 셀프테스트가 "실루엣 높이 1.33m" 로 그 어긋남을 잡는다.
        /// </summary>
        public const float ModelScale = 1.3585f;

        struct Link
        {
            public Transform Bone;
            public Rigidbody Part;
            public Vector3 OffsetPos;        // Part 로컬 좌표
            public Quaternion OffsetRot;
        }

        Link[] links;
        PlayerRig _player;
        public RabbitLocomotion Locomotion { get; private set; }
        public RabbitLocomotion.State? NetworkLocomotionState { get; set; }

        public Transform Model { get; private set; }
        public int LinkCount { get { return links != null ? links.Length : 0; } }
        /// <summary>씌운 메시 조각 수.</summary>
        public int MeshCount { get; private set; }

        static GameObject prefab;
        static bool searched;

        /// <summary>Resources 에 프리팹이 있으면 모델을 쓸 수 있다 (없으면 프리미티브 룩으로 돌아간다).</summary>
        public static GameObject Prefab
        {
            get
            {
                if (!searched)
                {
                    searched = true;
                    prefab = Resources.Load<GameObject>(ResourceKey);
                    if (prefab == null)
                        Debug.LogWarning("[SpaceSuit] Resources/" + ResourceKey
                                         + " 없음 — 프리미티브 룩으로 돈다. 에디터에서 ArtLink.BuildPrefabs 를 돌려라.");
                }
                return prefab;
            }
        }

        public static bool Available { get { return Prefab != null; } }

        /// <summary>
        /// 인형에 메시를 씌운다. <b>반드시 <see cref="Doll.Teleport"/> 전에</b> 부르는 것 —
        /// 그때가 인형이 원점의 기준 자세(회전 없음, 발바닥 y=0)로 서 있는 유일한 순간이고,
        /// 오프셋을 그 자세에서 재야 계산이 단순하다.
        /// </summary>
        public static SpaceSuit Attach(Doll doll, Transform parent)
        {
            if (doll == null || Prefab == null) return null;

            var go = Object.Instantiate(Prefab);
            go.name = "토끼 메시";
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            go.transform.localScale = Vector3.one * ModelScale;   // 본 위치를 재기 전에 걸어야 한다

            var suit = go.AddComponent<SpaceSuit>();
            suit.Model = go.transform;
            suit.Bind(doll);
            if (Application.isPlaying) suit.InitializeLocomotion();
            return suit;
        }

        // ── 뼈대 잇기 ───────────────────────────────────────────
        struct Plan
        {
            public string Bone;
            public Rigidbody Part;
            public int ArmChain;      // 0 = 그대로, 1/2/3 = 팔 사슬의 첫/둘/셋째 마디
            public float ArmSide;     // 팔 사슬의 어깨 x 부호
        }

        void Bind(Doll doll)
        {
            // 부모 → 자식 순서. 부모의 월드 트랜스폼을 쓰면 자식이 딸려 움직이므로
            // 반드시 이 순서로 덮어써야 한다.
            //
            // 이름의 Left/Right 는 FBX 변환에서 뒤집혀 있다 — LeftUpperArm 본이 +X 에 있으므로
            // 우리 인형의 '오른팔'(x = +ShoulderX)에 붙인다.
            // 부모 → 자식 순서. 부모의 월드 트랜스폼을 쓰면 자식이 딸려 움직이므로
            // 반드시 이 순서로 덮어써야 한다. 팔 사슬(1→2→3)은 반드시 연달아 놓는다.
            //
            // 이 리그(Tripo 출력)는 L_ 이 유니티 −X 라 우리 인형의 왼쪽과 맞는다.
            // 트위스트 본(*Twist01/02)은 표에 없다 — 부모를 따라가면 저절로 제자리에 온다.
            var plans = new List<Plan>
            {
                new Plan { Bone = "Root",       Part = doll.Hips },
                new Plan { Bone = "Hip",        Part = doll.Hips },
                new Plan { Bone = "Pelvis",     Part = doll.Hips },
                new Plan { Bone = "Waist",      Part = doll.Hips },
                new Plan { Bone = "Spine01",    Part = doll.Chest },
                new Plan { Bone = "Spine02",    Part = doll.Chest },
                new Plan { Bone = "NeckTwist01",Part = doll.Chest },
                new Plan { Bone = "NeckTwist02",Part = doll.Head },
                new Plan { Bone = "Head",       Part = doll.Head },

                new Plan { Bone = "L_Clavicle", Part = doll.Chest },
                new Plan { Bone = "L_Upperarm", Part = doll.Left.Upper,      ArmChain = 1, ArmSide = -1f },
                new Plan { Bone = "L_Forearm",  Part = doll.Left.Lower,      ArmChain = 2, ArmSide = -1f },
                new Plan { Bone = "L_Hand",     Part = doll.Left.Hand.Body,  ArmChain = 3, ArmSide = -1f },

                new Plan { Bone = "R_Clavicle", Part = doll.Chest },
                new Plan { Bone = "R_Upperarm", Part = doll.Right.Upper,     ArmChain = 1, ArmSide = +1f },
                new Plan { Bone = "R_Forearm",  Part = doll.Right.Lower,     ArmChain = 2, ArmSide = +1f },
                new Plan { Bone = "R_Hand",     Part = doll.Right.Hand.Body, ArmChain = 3, ArmSide = +1f },

                new Plan { Bone = "L_Thigh",    Part = doll.LeftLeg.Thigh },
                new Plan { Bone = "L_Calf",     Part = doll.LeftLeg.Shin },
                new Plan { Bone = "L_Foot",     Part = doll.LeftLeg.Foot },
                new Plan { Bone = "L_ToeBase",  Part = doll.LeftLeg.Foot },

                new Plan { Bone = "R_Thigh",    Part = doll.RightLeg.Thigh },
                new Plan { Bone = "R_Calf",     Part = doll.RightLeg.Shin },
                new Plan { Bone = "R_Foot",     Part = doll.RightLeg.Foot },
                new Plan { Bone = "R_ToeBase",  Part = doll.RightLeg.Foot },
            };

            // 1) 바인드 자세를 먼저 통째로 읽는다 (하나라도 옮기면 나머지가 딸려 움직인다)
            var bones = new Transform[plans.Count];
            var bindPos = new Vector3[plans.Count];
            var bindRot = new Quaternion[plans.Count];
            for (int i = 0; i < plans.Count; i++)
            {
                bones[i] = FindBone(Model, plans[i].Bone);
                if (bones[i] == null) { Debug.LogWarning("[SpaceSuit] 본 없음: " + plans[i].Bone); continue; }
                bindPos[i] = bones[i].position;
                bindRot[i] = bones[i].rotation;
            }

            // 2) 원하는 월드 자세를 계산한다 (아직 아무것도 안 옮긴다)
            var wantPos = new Vector3[plans.Count];
            var wantRot = new Quaternion[plans.Count];
            Vector3 nextRight = Vector3.zero, nextLeft = Vector3.zero;   // 팔 사슬의 다음 마디가 놓일 자리

            for (int i = 0; i < plans.Count; i++)
            {
                if (bones[i] == null) continue;
                wantPos[i] = bindPos[i];
                wantRot[i] = bindRot[i];

                if (plans[i].ArmChain == 0) continue;

                // 팔은 아래로 곧게 편다. 본 로컬 +Y 가 뼈 방향이다(블렌더 규약).
                Vector3 bindUp = bindRot[i] * Vector3.up;
                wantRot[i] = Quaternion.FromToRotation(bindUp, Vector3.down) * bindRot[i];

                bool right = plans[i].ArmSide > 0f;
                if (plans[i].ArmChain == 1)
                    wantPos[i] = new Vector3(plans[i].ArmSide * RagdollBuilder.ShoulderX,
                                             RagdollBuilder.ShoulderY, 0f);      // 랙돌의 어깨점
                else
                    wantPos[i] = right ? nextRight : nextLeft;                   // 앞 마디 끝에 이어 붙인다

                // 다음 마디는 이 마디의 길이(모델 자신의 치수)만큼 아래
                float len = 0f;
                if (i + 1 < plans.Count && bones[i + 1] != null
                    && plans[i + 1].ArmChain == plans[i].ArmChain + 1
                    && plans[i + 1].ArmSide == plans[i].ArmSide)
                    len = Vector3.Distance(bindPos[i], bindPos[i + 1]);

                Vector3 tail = wantPos[i] + Vector3.down * len;
                if (right) nextRight = tail; else nextLeft = tail;
            }

            // 3) 부모부터 순서대로 실제로 옮긴다
            for (int i = 0; i < plans.Count; i++)
                if (bones[i] != null) bones[i].SetPositionAndRotation(wantPos[i], wantRot[i]);

            // 4) 기준 자세에서의 상대 오프셋을 기억한다
            var list = new List<Link>();
            for (int i = 0; i < plans.Count; i++)
            {
                if (bones[i] == null || plans[i].Part == null) continue;
                var p = plans[i].Part.transform;
                list.Add(new Link
                {
                    Bone = bones[i],
                    Part = plans[i].Part,
                    OffsetPos = Quaternion.Inverse(p.rotation) * (wantPos[i] - p.position),
                    OffsetRot = Quaternion.Inverse(p.rotation) * wantRot[i],
                });
            }
            links = list.ToArray();

            // 5) 마무리 — 그림자는 켜두되 레이어는 인형과 같이 둔다 (지형 탐색에서 자기 몸을 뺀다)
            var smrs = Model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            MeshCount = smrs.Length;
            for (int i = 0; i < smrs.Length; i++)
            {
                if (smrs[i].rootBone == null && links.Length > 0) smrs[i].rootBone = links[0].Bone;
                smrs[i].gameObject.layer = RagdollBuilder.PlayerLayer;
            }
            var all = Model.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++) all[i].gameObject.layer = RagdollBuilder.PlayerLayer;

            Sync();
        }

        static Transform FindBone(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var r = FindBone(root.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }

        // ── 매 프레임 따라가기 ──────────────────────────────────
        public void InitializeLocomotion()
        {
            if (Locomotion == null) Locomotion = RabbitLocomotion.Create(Model, Prefab);
        }

        void LateUpdate()
        {
            Sync();
            if (Locomotion == null) return;
            if (_player == null) _player = GetComponentInParent<PlayerRig>();
            if (_player == null || _player.Doll == null) return;
            if (_player.NetworkDriven)
            {
                if (NetworkLocomotionState.HasValue)
                    Locomotion.Apply(NetworkLocomotionState.Value, Time.deltaTime);
            }
            else Locomotion.Apply(RabbitLocomotion.ReadState(_player), Time.deltaTime);
        }

        /// <summary>
        /// 동료를 던지는 동작을 한 번 재생한다 (S15P21B202-519).
        ///
        /// <para><b>네트워크로 걷기 상태를 받는 몸에도 그대로 먹는다.</b> 남의 몸도
        /// <see cref="Locomotion"/> 을 갖고 <see cref="LateUpdate"/> 에서 같은
        /// <c>Apply</c> 를 타므로, 부르기만 하면 참가자 화면에서도 보인다 — 그래서
        /// 던지는 그림을 위해 새 경로를 만들지 않았다.</para>
        /// </summary>
        public void PlayToss()
        {
            InitializeLocomotion();
            Locomotion?.PlayToss();
        }

        /// <summary>
        /// 춤을 처음부터 재생한다 (S15P21B202-563).
        ///
        /// <para>위 <see cref="PlayToss"/> 와 같은 이유로 남의 몸에도 그대로 먹는다 —
        /// 남의 몸도 <see cref="Locomotion"/> 을 갖고 같은 <c>Apply</c> 를 탄다.
        /// <b>감정표현은 남이 보라고 있는 것</b>이라 이 성질이 이 기능의 전부다.</para>
        /// </summary>
        public void PlayDance(byte id)
        {
            InitializeLocomotion();
            Locomotion?.PlayDance(id);
        }

        /// <summary>
        /// 방망이 스윙을 한 번 재생한다 (S15P21B202-694).
        ///
        /// <para>위 <see cref="PlayToss"/> 와 같은 이유로 남의 몸에도 그대로 먹는다.
        /// <b>스윙은 1인칭으로는 거의 안 보인다</b> — 내 화면에는 팔만 나온다.
        /// 이 동작이 값을 하는 곳은 <b>남의 화면</b>이라 그 성질이 전부다.</para>
        /// </summary>
        public void PlaySwing()
        {
            InitializeLocomotion();
            Locomotion?.PlaySwing();
        }

        /// <summary>스윙이 재생 중인가. 검증이 읽는다.</summary>
        public bool SwingActive => Locomotion != null && Locomotion.SwingActive;

        /// <summary>춤을 즉시 끝낸다.</summary>
        public void CancelDance() => Locomotion?.CancelDance();

        /// <summary>춤이 재생 중인가. 검증과 표시가 읽는다.</summary>
        public bool DanceActive => Locomotion != null && Locomotion.DanceActive;

        void OnDestroy() { Locomotion?.Dispose(); }

        public void Sync()
        {
            if (links == null) return;
            for (int i = 0; i < links.Length; i++)
            {
                var l = links[i];
                if (l.Bone == null || l.Part == null) continue;
                var p = l.Part.transform;
                l.Bone.SetPositionAndRotation(p.position + p.rotation * l.OffsetPos,
                                              p.rotation * l.OffsetRot);
            }
        }

        /// <summary>
        /// 지금 보이는 메시의 실루엣 높이 (귀 끝까지). 배율이 맞는지 재는 데 쓴다.
        ///
        /// <b>Renderer.bounds 로 재면 안 된다</b> — 스킨 메시의 바운드에는 유니티가 여유분을 붙여
        /// 0.979m 모델이 1.084m 로 나온다(11% 크게). 메시 자신의 바인드 바운드를 쓴다.
        /// </summary>
        public float SilhouetteHeight
        {
            get
            {
                if (Model == null) return 0f;
                var smrs = Model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                float lo = float.MaxValue, hi = float.MinValue;
                for (int i = 0; i < smrs.Length; i++)
                {
                    var mesh = smrs[i].sharedMesh;
                    if (mesh == null) continue;
                    // 메시 로컬 축은 블렌더 그대로일 수 있다 (이 모델은 로컬 y 가 '깊이'다).
                    // 그래서 축을 골라 읽지 말고 8모서리를 월드로 옮겨 높이를 잰다.
                    var b = mesh.bounds;
                    var m = smrs[i].transform.localToWorldMatrix;
                    for (int c = 0; c < 8; c++)
                    {
                        var p = new Vector3((c & 1) == 0 ? b.min.x : b.max.x,
                                            (c & 2) == 0 ? b.min.y : b.max.y,
                                            (c & 4) == 0 ? b.min.z : b.max.z);
                        float y = m.MultiplyPoint3x4(p).y;
                        if (y < lo) lo = y;
                        if (y > hi) hi = y;
                    }
                }
                return hi > lo ? hi - lo : 0f;
            }
        }

        /// <summary>보이는 손과 실제로 잡는 손이 얼마나 벌어져 있는가 (m).</summary>
        public float HandGap(Hand hand)
        {
            if (links == null || hand == null || hand.Body == null) return 0f;
            for (int i = 0; i < links.Length; i++)
                if (links[i].Part == hand.Body && links[i].Bone != null)
                    return Vector3.Distance(links[i].Bone.position, hand.Body.position);
            return 0f;
        }

        /// <summary>셀프테스트용 — 이 본이 대응 강체를 실제로 따라가고 있는가.</summary>
        public float WorstDrift()
        {
            if (links == null) return 0f;
            float worst = 0f;
            for (int i = 0; i < links.Length; i++)
            {
                var l = links[i];
                if (l.Bone == null || l.Part == null) continue;
                var p = l.Part.transform;
                Vector3 expect = p.position + p.rotation * l.OffsetPos;
                worst = Mathf.Max(worst, Vector3.Distance(l.Bone.position, expect));
            }
            return worst;
        }
    }
}
