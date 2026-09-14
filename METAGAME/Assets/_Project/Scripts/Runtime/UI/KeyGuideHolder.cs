using System;
using System.Collections.Generic;
using UnityEngine;

namespace Metagame.UI
{
    /// <summary>로컬 HUD 안내 목록. 키 조회와 표시 조건을 등록하면 재바인딩도 반영한다.</summary>
    public sealed class KeyGuideHolder
    {
        public sealed class Entry
        {
            public string Id { get; }
            public string Label { get; }
            public string KeyText { get; private set; } = string.Empty;
            public bool Visible { get; private set; }
            readonly Func<KeyCode> _binding;
            readonly Func<bool> _available;
            KeyCode _lastKey = KeyCode.None;

            internal Entry(string id, string label, Func<KeyCode> binding, Func<bool> available)
            {
                Id = id;
                Label = label;
                _binding = binding;
                _available = available;
            }

            internal void Refresh()
            {
                var key = _binding();
                if (_lastKey != key)
                {
                    _lastKey = key;
                    KeyText = key == KeyCode.None ? string.Empty : key.ToString().ToUpperInvariant();
                    if (KeyText.StartsWith("ALPHA", StringComparison.Ordinal))
                        KeyText = KeyText.Substring(5);
                }
                Visible = key != KeyCode.None && _available();
            }
        }

        readonly List<Entry> _entries = new List<Entry>();
        public IReadOnlyList<Entry> Entries => _entries;
        internal int Revision { get; private set; }

        public void Register(string id, string label, Func<KeyCode> binding, Func<bool> available)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("Guide id is required.", nameof(id));
            if (binding == null) throw new ArgumentNullException(nameof(binding));
            if (available == null) throw new ArgumentNullException(nameof(available));
            var entry = new Entry(id, label, binding, available);
            Revision++;
            for (int i = 0; i < _entries.Count; i++)
                if (_entries[i].Id == id) { _entries[i] = entry; return; }
            _entries.Add(entry);
        }

        public void Remove(string id)
        {
            for (int i = _entries.Count - 1; i >= 0; i--)
                if (_entries[i].Id == id) { _entries.RemoveAt(i); Revision++; }
        }

        public void Refresh()
        {
            for (int i = 0; i < _entries.Count; i++) _entries[i].Refresh();
        }
    }
}
