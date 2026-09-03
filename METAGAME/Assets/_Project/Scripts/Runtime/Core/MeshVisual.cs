using UnityEngine;

namespace Metagame.Core
{
    /// <summary>
    /// Keeps mesh-based visual children renderable when a generated object or an imported
    /// model is missing the renderer that belongs next to its MeshFilter.
    ///
    /// ParticleSystemRenderer and LineRenderer are intentionally left alone. They are not
    /// MeshFilter renderers and adding an empty MeshRenderer to those effect roots would not
    /// make the effect visible.
    /// </summary>
    public static class MeshVisual
    {
        /// <summary>
        /// Ensures the supplied GameObject's own MeshFilter has a MeshRenderer.
        /// Returns null when the object is not a usable mesh holder.
        /// </summary>
        public static MeshRenderer EnsureRenderer(GameObject meshObject)
        {
            if (!IsAlive(meshObject)) return null;

            var filter = meshObject.GetComponent<MeshFilter>();
            if (!IsAlive(filter) || !IsAlive(filter.sharedMesh)) return null;

            var renderer = meshObject.GetComponent<MeshRenderer>();
            return renderer != null ? renderer : meshObject.AddComponent<MeshRenderer>();
        }

        /// <summary>
        /// Repairs every usable MeshFilter below a visual root and returns the number of
        /// MeshRenderer components added. Existing renderer components and materials are kept.
        /// </summary>
        public static int EnsureRenderers(GameObject visualRoot)
        {
            if (!IsAlive(visualRoot)) return 0;

            MeshFilter[] filters;
            try
            {
                filters = visualRoot.GetComponentsInChildren<MeshFilter>(true);
            }
            catch (MissingReferenceException)
            {
                return 0;
            }

            var added = 0;
            for (var i = 0; i < filters.Length; i++)
            {
                var filter = filters[i];
                if (!IsAlive(filter) || !IsAlive(filter.sharedMesh)) continue;

                // A MeshFilter is normally paired with MeshRenderer. If an unusual authored
                // object already owns another renderer type, do not create a second renderer
                // and alter its intended rendering path.
                if (filter.GetComponent<Renderer>() != null) continue;

                filter.gameObject.AddComponent<MeshRenderer>();
                added++;
            }

            return added;
        }

        static bool IsAlive(Object target) =>
            !ReferenceEquals(target, null) && target != null;
    }
}
