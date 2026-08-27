using System.Collections.Generic;
using UnityEngine;
using Metagame.Core;

namespace Metagame.Gameplay
{
    /// <summary>지나가면 부활 지점이 여기로 옮겨진다.</summary>
    public class Checkpoint : MonoBehaviour
    {
        public static readonly List<Checkpoint> All = new List<Checkpoint>();

        public int Index;
        public string Title = "";
        public Vector3 SpawnFoot;
        public bool Reached { get; private set; }

        Renderer glow;

        void OnEnable() { All.Add(this); }
        void OnDisable() { All.Remove(this); }

        public static Checkpoint Create(Transform parent, int index, string title, Vector3 footPos)
        {
            var go = new GameObject("표지 " + index + " " + title);
            go.transform.SetParent(parent, false);
            go.transform.position = footPos;

            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(3.4f, 3.0f, 5.0f);
            box.center = new Vector3(0f, 1.5f, 0f);

            var cp = go.AddComponent<Checkpoint>();
            cp.Index = index;
            cp.Title = title;
            cp.SpawnFoot = footPos;

            // 착륙 표지등 — 도달하면 초록으로 바뀐다
            Prim.Decor(Prim.Pipe("표지 기둥", footPos + new Vector3(0f, 0.55f, 2.0f), 0.10f, 1.1f,
                                 new Color(0.40f, 0.42f, 0.46f), 0.2f, go.transform));
            var lamp = Prim.Decor(Prim.Ball("표지등", footPos + new Vector3(0f, 1.20f, 2.0f), 0.34f,
                                            Color.white, 0.5f, go.transform));
            lamp.GetComponent<Renderer>().sharedMaterial = Prim.Ghost(new Color(0.98f, 0.42f, 0.30f, 0.85f), 1.8f, 1f);
            cp.glow = lamp.GetComponent<Renderer>();
            return cp;
        }

        public void MarkReached()
        {
            if (Reached) return;
            Reached = true;
            if (glow != null) glow.sharedMaterial = Prim.Ghost(new Color(0.30f, 0.98f, 0.52f, 0.90f), 1.8f, 1f);
        }

        void OnTriggerEnter(Collider other)
        {
            var rig = other.GetComponentInParent<PlayerRig>();
            if (rig == null || GameSession.Instance == null) return;
            GameSession.Instance.ReachCheckpoint(this);
        }
    }
}
