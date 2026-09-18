using UnityEngine;

namespace Metagame.Gameplay
{
    /// <summary>Cosmetic response to the real bat's successful prop hit, not a second hit test.</summary>
    public sealed class PracticeMeteorTarget : MonoBehaviour
    {
        [SerializeField] Rigidbody body;
        [SerializeField] Transform ceilingAnchor;
        [SerializeField] Renderer face;
        [SerializeField] LineRenderer tether;
        MeteorBat[] bats;
        MaterialPropertyBlock block;
        float smileUntil;
        public int HitCount { get; private set; }
        void Start()
        {
            block = new MaterialPropertyBlock();
            bats = FindObjectsByType<MeteorBat>();
            foreach (var bat in bats) bat.Swung += OnSwung;
        }
        void OnSwung(MeteorBat bat, bool connected)
        {
            if (!connected || bat.LastPushed != body) return;
            HitCount++;
            smileUntil = Time.time + 0.7f;
        }
        void LateUpdate()
        {
            if (tether != null && ceilingAnchor != null && body != null)
            {
                tether.SetPosition(0, ceilingAnchor.position);
                tether.SetPosition(1, body.position + Vector3.up * 0.5f);
            }
            if (face == null || block == null) return;
            var color = Time.time < smileUntil ? new Color(1f, 0.65f, 0.73f) : new Color(0.27f, 0.95f, 0.81f);
            block.SetColor("_BaseColor", color);
            block.SetColor("_Color", color);
            block.SetColor("_EmissionColor", color * 0.3f);
            face.SetPropertyBlock(block);
        }
        void OnDestroy()
        {
            if (bats != null) foreach (var bat in bats) if (bat != null) bat.Swung -= OnSwung;
        }
    }
}
