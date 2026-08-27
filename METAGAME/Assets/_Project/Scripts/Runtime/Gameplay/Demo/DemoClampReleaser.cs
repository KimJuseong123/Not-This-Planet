using System.Collections.Generic;
using Metagame.Gameplay.Parts;
using UnityEngine;

namespace Metagame.Gameplay.Demo
{
    /// <summary>
    /// 지정한 파츠의 고정쇠를 시간이 지나면 스스로 풀어 준다.
    ///
    /// 입력 바인딩(S15P21B202-13)이 붙기 전까지는 플레이어가 고정쇠를 누를 수 없어
    /// Play 를 눌러도 퍼즐 루프가 시작되지 않는다. 그 사이 동안 흐름을 눈으로
    /// 확인할 수 있게 하는 시연용 컴포넌트다.
    ///
    /// 입력이 붙으면 이 컴포넌트를 씬에서 지우면 된다. 게임 로직은 이것에 의존하지 않는다.
    /// </summary>
    public sealed class DemoClampReleaser : MonoBehaviour
    {
        [System.Serializable]
        public sealed class Entry
        {
            [Tooltip("고정쇠를 풀 파츠.")]
            public SpaceshipPart Part;

            [Tooltip("Play 후 몇 초 뒤에 풀지.")]
            [Min(0f)] public float DelaySeconds = 2f;
        }

        [SerializeField] List<Entry> _entries = new();

        readonly List<bool> _done = new();

        float _elapsed;

        public void Configure(IEnumerable<Entry> entries)
        {
            _entries.Clear();
            _entries.AddRange(entries);
        }

        void FixedUpdate()
        {
            _elapsed += Time.fixedDeltaTime;

            while (_done.Count < _entries.Count) _done.Add(false);

            for (var i = 0; i < _entries.Count; i++)
            {
                if (_done[i]) continue;

                var entry = _entries[i];
                if (entry?.Part == null) { _done[i] = true; continue; }
                if (_elapsed < entry.DelaySeconds) continue;

                _done[i] = true;

                foreach (var clamp in entry.Part.Clamps)
                    if (clamp != null)
                        clamp.Release();

                Debug.Log($"[Demo] {entry.Part.name} 고정쇠 해제 → {entry.Part.State}", entry.Part);
            }
        }
    }
}
