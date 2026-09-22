using System;
using System.Collections.Generic;
using UnityEngine;

namespace Metagame.Network
{
    /// <summary>Position/index-only copies of imported collision meshes, included in player builds.</summary>
    public sealed class AiNavigationGeometry : ScriptableObject
    {
        [Serializable] public struct Entry { public Mesh source; public Mesh readable; }
        [SerializeField] Entry[] entries = Array.Empty<Entry>();
        Dictionary<Mesh, Mesh> lookup;

        public Mesh Get(Mesh source)
        {
            if (lookup == null)
            {
                lookup = new Dictionary<Mesh, Mesh>();
                foreach (var entry in entries)
                    if (entry.source != null && entry.readable != null) lookup[entry.source] = entry.readable;
            }
            return lookup.TryGetValue(source, out var mesh) ? mesh : null;
        }
#if UNITY_EDITOR
        public void SetEntries(Entry[] value) { entries = value; lookup = null; }
#endif
    }
}
