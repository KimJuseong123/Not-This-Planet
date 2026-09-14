using System;
using UnityEngine;
using Metagame.Core;

namespace Metagame.Gameplay
{
    /// <summary>Server chooses the hit; a bounded local pool draws the glob and temporary splatter.</summary>
    public sealed class AstraInkLauncher : AstraHandTool
    {
        [SerializeField] Material inkMaterial;
        public const float ShotInterval = .45f;
        public const int MaxMarks = 16;
        const float ProjectileSpeed = 26f;
        const float MarkSeconds = 20f;
        [Serializable] public struct ShotState
        {
            public uint Sequence;
            public Vector3 Start, End, Normal;
            public float Age;
            public bool Hit;
        }
        ShotState shot;
        float cooldown;
        Transform effects, glob;
        readonly Transform[] marks = new Transform[MaxMarks];
        readonly float[] expiry = new float[MaxMarks];
        int nextMark;
        bool landed;
        public ShotState Snapshot => shot;
        public override string UseHint => "클릭 · 먹물 발사 · 자국은 잠시 뒤 사라집니다";
        public override byte ViewAction => cooldown > 0f ? (byte)1 : (byte)0;
        public override float ViewProgress => cooldown / ShotInterval;
        public void ConfigureInk(Material material) => inkMaterial = material;

        public bool Fire(Vector3 aim)
        {
            if (WorldEvents.RemoteDecides || !IsEquipped || Owner == null || Owner.Downed || cooldown > 0f) return false;
            aim = aim.sqrMagnitude > .001f ? aim.normalized : Vector3.forward;
            bool hit = FindSurface(aim, 36f, out var surface);
            shot = new ShotState
            {
                Sequence = shot.Sequence + 1,
                Start = AimOrigin + aim * .7f,
                End = hit ? surface.point : AimOrigin + aim * 36f,
                Normal = hit ? surface.normal : -aim,
                Hit = hit
            };
            cooldown = ShotInterval;
            BeginVisual();
            return true;
        }
        protected override void OnToolInput(ToolInputFrame input, ToolAimContext aim)
        {
            if (input.PrimaryPressed) Fire(aim.Direction);
        }

        public void ApplySnapshot(ShotState state)
        {
            if (state.Sequence == 0 || state.Sequence == shot.Sequence) return;
            shot = state;
            BeginVisual();
        }
        void BeginVisual()
        {
            EnsureEffects();
            landed = false;
            if (glob != null) glob.gameObject.SetActive(true);
        }
        void EnsureEffects()
        {
            if (effects != null || inkMaterial == null) return;
            effects = new GameObject(name + " - ink effects").transform;
            glob = Blob("Flying ink", effects, Vector3.zero, Vector3.one * .24f);
            for (int i = 0; i < MaxMarks; i++)
            {
                marks[i] = new GameObject("Ink splatter " + i).transform;
                marks[i].SetParent(effects, false);
                Blob("Ink pool", marks[i], Vector3.zero, new Vector3(.85f, .018f, .72f));
                for (int j = 0; j < 5; j++)
                {
                    float angle = j * 2.39996f + i;
                    Blob("Ink droplet", marks[i], new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * .48f,
                        new Vector3(.18f + j * .04f, .015f, .21f));
                }
                marks[i].gameObject.SetActive(false);
            }
            glob.gameObject.SetActive(false);
        }
        Transform Blob(string label, Transform parent, Vector3 local, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = label;
            var collider = go.GetComponent<Collider>(); collider.enabled = false; Destroy(collider);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local; go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = inkMaterial;
            return go.transform;
        }
        protected override void LateUpdate()
        {
            base.LateUpdate();
            cooldown = Mathf.Max(0f, cooldown - Time.deltaTime);
            if (shot.Sequence == 0) return;
            shot.Age += Time.deltaTime;
            if (glob == null) return;
            float duration = Mathf.Max(.03f, Vector3.Distance(shot.Start, shot.End) / ProjectileSpeed);
            glob.position = Vector3.Lerp(shot.Start, shot.End, Mathf.Clamp01(shot.Age / duration));
            if (!landed && shot.Age >= duration)
            {
                landed = true;
                glob.gameObject.SetActive(false);
                if (shot.Hit && shot.Age < MarkSeconds)
                {
                    var mark = marks[nextMark];
                    mark.SetPositionAndRotation(shot.End + shot.Normal * .018f, Quaternion.FromToRotation(Vector3.up, shot.Normal));
                    mark.gameObject.SetActive(true);
                    expiry[nextMark] = Time.time + MarkSeconds - shot.Age;
                    nextMark = (nextMark + 1) % MaxMarks;
                }
            }
            for (int i = 0; i < MaxMarks; i++)
                if (marks[i] != null && marks[i].gameObject.activeSelf && Time.time >= expiry[i]) marks[i].gameObject.SetActive(false);
        }
        protected override void OnDestroy()
        {
            if (effects != null) Destroy(effects.gameObject);
            base.OnDestroy();
        }
    }
}
