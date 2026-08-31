using System.Collections.Generic;
using UnityEngine;

namespace Metagame.Gameplay
{
    /// <summary>잡았을 때의 상호작용 역할. 운반물과 도구의 처리 경로를 분리한다.</summary>
    public enum GrabbableRole
    {
        Transport = 0,
        Tool = 1
    }

    /// <summary>
    /// 손으로 잡을 수 있는 것. 이게 붙어 있지 않으면 손이 미끄러진다
    /// (아무 데나 붙을 수 있으면 모든 퍼즐을 벽 타기로 넘어버린다).
    /// </summary>
    public class Grabbable : MonoBehaviour
    {
        public static readonly List<Grabbable> All = new List<Grabbable>();

        public string Label = "";
        public bool Climbable = false;      // 매달려 기어오를 수 있는 고정물(레일·케이블)인가
        public float GripStrength = 1f;     // 1보다 작으면 잘 미끄러진다
        /// <summary>자석 손이 고를 때의 가산점. 퍼즐에 꼭 필요한 물건을 1 로 두면 손이 그쪽으로 간다.</summary>
        public float Priority = 0f;
        /// <summary>false 면 지금은 잡히지 않는다 (소켓에 꽂혀 고정된 배터리 등).</summary>
        public bool CanGrab = true;
        /// <summary>
        /// 운반체는 손의 운반 상태를 사용하고, 도구는 양손 고정 그립과 전방 조준 자세를 사용한다.
        /// 기존 Grabbable은 기본값 Transport라서 기존 씬의 동작을 보존한다.
        /// </summary>
        [Tooltip("Transport는 일반 운반, Tool은 양손 고정 및 전방 조준 도구입니다.")]
        public GrabbableRole Role = GrabbableRole.Transport;
        public bool IsTransport { get { return Role == GrabbableRole.Transport; } }
        public bool IsTool { get { return Role == GrabbableRole.Tool; } }

        /// <summary>지금 이걸 쥐고 있는 손의 개수 (0~2).</summary>
        public int HandCount { get; private set; }

        void OnEnable() { All.Add(this); }
        void OnDisable() { All.Remove(this); }

        public void NotifyGrabbed(Hand h) { HandCount++; }
        public void NotifyReleased(Hand h) { HandCount = Mathf.Max(0, HandCount - 1); }

        public static Grabbable Mark(GameObject go, string label, bool climbable = false,
                                     float grip = 1f, float priority = 0f,
                                     GrabbableRole role = GrabbableRole.Transport)
        {
            var g = go.GetComponent<Grabbable>();
            if (g == null) g = go.AddComponent<Grabbable>();
            g.Label = label;
            g.Climbable = climbable;
            g.GripStrength = grip;
            g.Priority = priority;
            g.Role = role;
            return g;
        }

        public static Grabbable Find(string label)
        {
            for (int i = 0; i < All.Count; i++)
                if (All[i] != null && All[i].Label == label) return All[i];
            return null;
        }
    }
}
