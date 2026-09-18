using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Metagame.AICompanion
{
    /// <summary>
    /// AI 가 함께 쓰는 길찾기 바닥. <b>판에 한 장만 둔다.</b>
    ///
    /// <para><b>왜 모았나.</b> 예전에는 <see cref="CompanionMotor"/> 가 저마다
    /// <c>NavMesh.AddNavMeshData</c> 를 했다. AI 가 하나뿐인 실험실에서는 문제가 없었지만
    /// 빈 자리를 셋이 채우면 같은 자리에 바닥이 세 장 겹치고, 각자 따로 다시 굽는다.
    /// 낭비이기도 하고, 겹친 바닥 위에서는 서로 피하기(로컬 회피)도 제대로 돌지 않는다.</para>
    ///
    /// <para><b>서버에서만 쓴다.</b> AI 는 서버에서만 움직인다.</para>
    /// </summary>
    public static class CompanionNavMesh
    {
        /// <summary>한 번에 굽는 영역(m). 한 장만 구우므로 넉넉히 잡는다.</summary>
        static readonly Vector3 Size = new Vector3(80, 20, 80);

        /// <summary>가장자리에서 이만큼 안으로 들어오면 다시 굽는다(m).</summary>
        const float Margin = 15f;

        /// <summary>다시 굽기까지의 최소 간격(초). 굽는 일은 싸지 않다.</summary>
        const float RebakeInterval = 2f;

        static NavMeshData _data;
        static NavMeshDataInstance _instance;
        static Vector3 _center;
        static bool _built;
        static float _nextBake;

        /// <summary>
        /// 이 지점이 길찾기 바닥 위에 오도록 한다. 이미 넉넉히 덮고 있으면 아무것도 하지 않는다.
        /// </summary>
        public static void EnsureCovers(Vector3 point)
        {
            if (_built && !NeedsRebake(point)) return;
            Bake(point);
        }

        /// <summary>두 지점을 모두 덮게 한다. 경로는 양 끝이 다 바닥 위에 있어야 난다.</summary>
        public static void EnsureCovers(Vector3 from, Vector3 to)
        {
            if (_built && !NeedsRebake(from) && !NeedsRebake(to)) return;
            Bake(Vector3.Lerp(from, to, 0.5f));
        }

        /// <summary>판이 끝날 때 치운다. 남겨 두면 다음 판의 바닥과 겹친다.</summary>
        public static void Clear()
        {
            if (_instance.valid) NavMesh.RemoveNavMeshData(_instance);
            _instance = default;
            _data = null;
            _built = false;
            _nextBake = 0f;
        }

        static bool NeedsRebake(Vector3 point)
        {
            Vector3 offset = point - _center;
            offset.y = 0;
            float slack = Mathf.Min(Size.x, Size.z) * 0.5f - Margin;
            return Mathf.Abs(offset.x) > slack || Mathf.Abs(offset.z) > slack;
        }

        static void Bake(Vector3 around)
        {
            // 간격을 두지 않으면 두 AI 가 번갈아 부탁할 때 매 프레임 굽는다.
            if (_built && Time.time < _nextBake) return;
            _nextBake = Time.time + RebakeInterval;
            _center = around;

            var bounds = new Bounds(around, Size);
            var sources = new List<NavMeshBuildSource>();
            NavMeshBuilder.CollectSources(bounds, ~0, NavMeshCollectGeometry.PhysicsColliders, 0,
                new List<NavMeshBuildMarkup>(), sources);

            // 트리거는 바닥이 아니다. 넣으면 허공에 길이 난다.
            //
            // <b>움직이는 물체만 뺀다.</b> 예전에는 리지드바디가 붙은 것을 모두 뺐는데,
            // 그러면 문·해치·조립된 구조물처럼 <b>키네마틱</b> 인 것까지 빠져 실내에 길이
            // 나지 않았다 — AI 가 건물에서 못 나오던 이유다. 굴러다니는 것(운석·도구)만
            // 빼면 된다.
            sources.RemoveAll(s => s.component is Collider c
                && (c.isTrigger || (c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic)));

            var settings = NavMesh.GetSettingsByID(0);
            settings.agentRadius = CompanionMotor.AgentRadius;
            settings.agentHeight = CompanionMotor.AgentHeight;
            settings.agentClimb = 0.25f;
            settings.agentSlope = 40;

            if (_built && _data != null)
            {
                // 있는 것을 갱신한다 — 새로 붙였다 떼면 그 순간 길이 사라진다.
                NavMeshBuilder.UpdateNavMeshData(_data, settings, sources, bounds);
                return;
            }

            _data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, around, Quaternion.identity);
            if (_data == null) return;

            _instance = NavMesh.AddNavMeshData(_data);
            _built = true;
        }
    }
}
