using System;
using System.Collections.Generic;
using UnityEngine;

namespace Metagame.Gameplay
{
    public sealed class AstraMinefield : MonoBehaviour
    {
        static readonly List<AstraMinefield> fields = new List<AstraMinefield>();
        [SerializeField] Vector3[] mines = Array.Empty<Vector3>();
        [SerializeField] Transform[] scars = Array.Empty<Transform>();
        [SerializeField] Transform flash;
        uint spent;
        float age = 99;
        Vector3 explosion;
        public const float TriggerRadius = .65f;
        public Vector3[] MinePositions => (Vector3[])mines.Clone();
        [Serializable] public struct State { public uint Spent; public float Age; public Vector3 Explosion; }
        public State Snapshot => new State { Spent = spent, Age = age, Explosion = explosion };
        public void Configure(Vector3[] positions, Transform[] marks, Transform effect)
        { if (positions.Length > 32) throw new ArgumentException("Maximum 32 mines"); mines = positions; scars = marks; flash = effect; }
        void OnEnable() { if (!fields.Contains(this)) fields.Add(this); }
        void OnDisable() => fields.Remove(this);
        public bool IsSpent(int index) => (spent & (1u << index)) != 0;
        public static float Nearest(Vector3 foot, UnityEngine.SceneManagement.Scene scene, float range)
        {
            float nearest = range;
            foreach (var field in fields)
            {
                if (field == null || field.gameObject.scene != scene) continue;
                for (int i = 0; i < field.mines.Length; i++)
                {
                    var delta = field.mines[i] - foot;
                    if (field.IsSpent(i) || Mathf.Abs(delta.y) > 3) continue;
                    delta.y = 0; nearest = Mathf.Min(nearest, delta.magnitude);
                }
            }
            return nearest;
        }
        public bool TryTrigger(PlayerRig player)
        {
            if (WorldEvents.RemoteDecides || player == null || player.Doll == null || !player.ServerAuthority || player.IsDead || player.Downed || player.IsRiding || player.gameObject.scene != gameObject.scene) return false;
            for (int i = 0; i < mines.Length; i++)
            {
                Vector3 delta = player.FootPosition - mines[i];
                if (IsSpent(i) || Mathf.Abs(delta.y) > .45f) continue;
                delta.y = 0;
                if (delta.sqrMagnitude > TriggerRadius * TriggerRadius) continue;
                spent |= 1u << i; age = 0; explosion = mines[i];
                player.ApplyDamage(35, "지뢰 폭발");
                player.KnockDown((delta + Vector3.up).normalized, 6);
                return true;
            }
            return false;
        }
        void FixedUpdate() { if (!WorldEvents.RemoteDecides) foreach (var player in PlayerRig.All) TryTrigger(player); }
        public void ApplySnapshot(State value) { spent = value.Spent; age = Mathf.Max(0, value.Age); explosion = value.Explosion; }
        void Update()
        {
            age += Time.deltaTime;
            for (int i = 0; i < scars.Length; i++) if (scars[i] != null) scars[i].gameObject.SetActive(IsSpent(i));
            if (flash == null) return;
            flash.gameObject.SetActive(age < .4f); flash.position = explosion + Vector3.up * .2f;
            flash.localScale = Vector3.one * Mathf.Lerp(.1f, 3, Mathf.Clamp01(age / .4f));
        }
    }
}
