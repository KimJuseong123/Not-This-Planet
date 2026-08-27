using System.Collections.Generic;
using UnityEngine;

namespace Metagame.Gameplay
{
    /// <summary>표지판에 적힌 글자 — HUD 가 가까이 갔을 때 화면에 띄운다.</summary>
    public class WorldLabel : MonoBehaviour
    {
        public static readonly List<WorldLabel> All = new List<WorldLabel>();
        public string Text = "";
        public float ShowRange = 6.5f;

        void OnEnable() { All.Add(this); }
        void OnDisable() { All.Remove(this); }

        public static WorldLabel Nearest(Vector3 p, out float distance)
        {
            WorldLabel best = null;
            distance = float.MaxValue;
            for (int i = 0; i < All.Count; i++)
            {
                var l = All[i];
                if (l == null) continue;
                float d = Vector3.Distance(p, l.transform.position);
                if (d > l.ShowRange || d >= distance) continue;
                distance = d;
                best = l;
            }
            return best;
        }
    }
}
