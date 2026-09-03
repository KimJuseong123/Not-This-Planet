using UnityEngine;

namespace Metagame.Gameplay
{
    /// <summary>
    /// 보이는 모델에서 <b>빔이 나갈 끝점</b>을 구한다(S15P21B202-277).
    ///
    /// <b>왜 좌표를 계산하는가.</b> 용접 건 프리팹의 <c>AttachPoints/Muzzle</c> 은
    /// 코드로 만든 임시 총(<c>PlaceholderVisual</c>)의 끝에 맞춰 놓은 값인데, 장착하면
    /// <see cref="PlasmaWeldingTool"/> 이 <b>실제 모델만</b> 옮긴다. Muzzle 은 그 모델의
    /// 형제라 따라가지 않아, 빔이 나가는 점이 보이는 총에서 <b>1.34m</b> 떨어진다.
    /// <see cref="Core.Tuning.ToolBeamRange"/>(0.80m)보다 멀어서 <b>보이는 총이 닿는
    /// 곳과 빔이 닿는 곳이 겹칠 수가 없었다.</b>
    ///
    /// <b>FBX 노드 이름에 의존하지 않는다.</b> <c>ResolveHeldVisual</c> 의 기존 주석이
    /// "FBX를 다시 내보내 루트 이름이 바뀌더라도" 를 이미 걱정하고 있다. 이름 대신
    /// 렌더러의 경계 상자를 쓰면 다시 내보내도 살아남는다.
    ///
    /// 상태를 갖지 않는다 — 검증이 Transform 과 경계 상자를 직접 넣어 잠글 수 있다.
    /// </summary>
    public static class NozzleGeometry
    {
        /// <summary>
        /// <paramref name="renderersOf"/> 아래 모든 렌더러를 감싸는 경계 상자를
        /// <b>그 Transform 의 지역 좌표</b>로 돌려준다.
        ///
        /// 지역 좌표로 두는 이유는 <b>한 번만 계산하면 되기 때문</b>이다. 총이 손에서
        /// 어떻게 돌아가든 모델 안에서의 모양은 바뀌지 않는다.
        /// </summary>
        public static bool TryLocalBounds(Transform root, out Bounds bounds)
        {
            bounds = default;
            if (root == null) return false;

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            var found = false;

            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null) continue;

                // ParticleSystem·Trail 은 크기가 매 프레임 변해 끝점을 흔든다.
                if (renderer is ParticleSystemRenderer || renderer is TrailRenderer) continue;

                var mesh = renderer.bounds;   // 세계 좌표
                var local = new Bounds(root.InverseTransformPoint(mesh.center), Vector3.zero);

                // 세계 크기를 지역으로 되돌린다. 회전이 섞이면 정확하지 않지만, 여기서는
                // 여러 조각을 합치는 것이 목적이라 이 정도로 충분하다.
                var scale = root.lossyScale;
                local.extents = new Vector3(
                    scale.x != 0f ? mesh.extents.x / Mathf.Abs(scale.x) : mesh.extents.x,
                    scale.y != 0f ? mesh.extents.y / Mathf.Abs(scale.y) : mesh.extents.y,
                    scale.z != 0f ? mesh.extents.z / Mathf.Abs(scale.z) : mesh.extents.z);

                if (!found)
                {
                    bounds = local;
                    found = true;
                    continue;
                }

                bounds.Encapsulate(local);
            }

            return found;
        }

        /// <summary>
        /// 경계 상자를 <paramref name="direction"/> 으로 밀었을 때 <b>가장 멀리 나간
        /// 자리</b>. 그것이 그 방향에서 본 모델의 끝이다.
        ///
        /// <b>조준 방향을 쓰는 것이 핵심이다.</b> 총의 어느 축이 총구인지 추측하지
        /// 않는다 — FBX 마다 다르고 휴대 자세가 90도 돌리기 때문에, 축을 정해 두면
        /// 모델이 바뀔 때마다 어긋난다. 빔은 어차피 조준 방향으로 나가므로(§16.3),
        /// <b>그 방향에서 가장 앞선 점</b>이 눈에 맞는 시작점이다.
        ///
        /// 상자의 <b>모서리</b>가 아니라 지지면의 중심을 쓴다. 모서리를 쓰면 총 옆으로
        /// 비켜난 자리에서 빔이 나간다.
        /// </summary>
        public static Vector3 TipAlong(Transform space, Bounds localBounds, Vector3 direction)
        {
            if (space == null) return localBounds.center;

            var aim = direction.sqrMagnitude > 1e-6f ? direction.normalized : space.forward;

            var center = space.TransformPoint(localBounds.center);
            var extents = localBounds.extents;
            var scale = space.lossyScale;

            // 회전된 각 축이 조준 방향으로 얼마나 뻗는가를 더한다. 이것이 그 방향의
            // 반지름이다(경계 상자의 지지 거리).
            var reach =
                Mathf.Abs(Vector3.Dot(space.right * (extents.x * scale.x), aim)) +
                Mathf.Abs(Vector3.Dot(space.up * (extents.y * scale.y), aim)) +
                Mathf.Abs(Vector3.Dot(space.forward * (extents.z * scale.z), aim));

            return center + aim * reach;
        }
    }
}
