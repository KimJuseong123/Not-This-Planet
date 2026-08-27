using UnityEngine;
using Metagame.Core;

namespace Metagame.Gameplay
{
    /// <summary>전원이 들어오면 열리는 에어록 해치. 열림 정도만큼 위로 올라간다.</summary>
    public class SlidingDoor : MonoBehaviour
    {
        public static SlidingDoor Instance;

        public Vector3 ClosedPos;
        public Vector3 OpenPos;
        public float Target01;
        public float Current01 { get; private set; }
        public float Speed = 0.55f;

        Rigidbody body;

        public bool IsOpen { get { return Current01 > 0.95f; } }

        public static SlidingDoor Create(Transform parent, Vector3 center, Vector3 size, Vector3 openOffset)
        {
            // 해치는 벽보다 어둡게 — 벽과 같은 색이면 "여기가 문이다" 가 안 읽힌다
            var go = Prim.Box("에어록 해치", center, size, new Color(0.34f, 0.36f, 0.42f), 0.2f, parent);
            go.GetComponent<Collider>().material = Prim.GroundPhysics;

            // 줄무늬는 사람이 걸어오는 쪽(±X) 면에 붙인다. 두께 방향(±Z)에 붙이면 옆에서만 보인다.
            var d = Prim.DecoRoot(go.transform);
            Color warn = new Color(0.95f, 0.66f, 0.14f);
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sy = -1; sy <= 1; sy += 2)
                    Prim.FitBox(d, "해치 줄무늬",
                                new Vector3(sx * size.x * 0.52f, sy * size.y * 0.30f, 0f),
                                new Vector3(0.06f, 0.22f, size.z * 0.86f), warn, 0.2f);
            Prim.FitBox(d, "해치 손잡이", new Vector3(-size.x * 0.55f, 0f, 0f),
                        new Vector3(0.10f, 0.14f, 1.1f), new Color(0.72f, 0.76f, 0.82f), 0.2f);

            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            var door = go.AddComponent<SlidingDoor>();
            door.body = rb;
            door.ClosedPos = center;
            door.OpenPos = center + openOffset;
            Instance = door;
            return door;
        }

        void FixedUpdate()
        {
            Current01 = Mathf.MoveTowards(Current01, Mathf.Clamp01(Target01), Speed * Time.fixedDeltaTime);
            body.MovePosition(Vector3.Lerp(ClosedPos, OpenPos, Current01));
        }
    }
}
