using System.Collections.Generic;
using UnityEngine;

namespace SpaceFlop
{
    /// <summary>매달려 흔들리는 계류 케이블. 캡슐을 조인트로 이어 붙인 것이 전부다.</summary>
    public static class RopeBuilder
    {
        public static List<Rigidbody> Build(Transform parent, Vector3 top, int links, float linkLength,
                                            float diameter, Color color, string label, float linkMass = 1.0f)
        {
            var made = new List<Rigidbody>();
            var cols = new List<Collider>();
            Rigidbody prev = null;

            for (int i = 0; i < links; i++)
            {
                Vector3 c = top - Vector3.up * (linkLength * (i + 0.5f));
                var go = Prim.Capsule(label + " " + i, c, diameter, linkLength, color, 0.45f, parent);

                var rb = go.AddComponent<Rigidbody>();
                rb.mass = linkMass;
                rb.linearDamping = 0.10f;          // 진공이라 저절로 잦아들지 않는다 — 흔들면 오래 흔들린다
                rb.angularDamping = 0.8f;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.maxDepenetrationVelocity = 2f;

                var j = go.AddComponent<ConfigurableJoint>();
                j.autoConfigureConnectedAnchor = false;
                j.connectedBody = prev;
                j.anchor = new Vector3(0f, 1f, 0f);                        // 캡슐 로컬 y=1 이 윗끝
                j.connectedAnchor = prev == null
                    ? top
                    : prev.transform.InverseTransformPoint(c + Vector3.up * (linkLength * 0.5f));
                j.xMotion = j.yMotion = j.zMotion = ConfigurableJointMotion.Locked;
                j.enablePreprocessing = false;
                j.projectionMode = JointProjectionMode.PositionAndRotation;
                j.projectionDistance = 0.03f;
                JointTools.Limit(j, -42f, 42f, 42f, 20f);

                Grabbable.Mark(go, label, true, 1f, 0.4f);

                var col = go.GetComponent<Collider>();
                if (col != null) cols.Add(col);

                made.Add(rb);
                prev = rb;
            }

            // 이웃한 마디끼리 부딪히면 케이블이 부들부들 떤다
            for (int i = 0; i < cols.Count; i++)
                for (int k = i + 1; k < cols.Count; k++)
                    Physics.IgnoreCollision(cols[i], cols[k], true);

            return made;
        }
    }
}
