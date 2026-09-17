using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Metagame.Tests.Editor
{
    /// <summary>
    /// 월드 동기화가 "게임 씬이 떴다" 를 무엇으로 판단하는지 지킨다 (S15P21B202-315).
    ///
    /// <para><b>왜 이 테스트가 있나.</b> 예전에는 <see cref="TransportVehicle"/> 이 있는지로
    /// 판단했다. 그런데 견인 장치는 <b>씬에 없는 것이 정상</b>이다 — 사람이 Part_1 을 해제한 뒤
    /// 런타임에 <c>AddComponent</c> 로 생긴다. 그래서 그 문지기는 <b>영영 열리지 않았고</b>,
    /// WorldSync 가 한 번도 스폰되지 않아 월드 동기화가 통째로 빠진 채 게임이 돌았다.</para>
    ///
    /// <para>두 사람이 각자 물리를 굴리니 물체가 서로 다르게 보였고 상호작용도 전달되지 않았다.
    /// 2026-09-07 실측 — 방장 로그에 <c>[WorldSync] 20초째 옮길 것을 찾지 못했다</c> 만 남고
    /// 그 뒤 아무 일도 없었다.</para>
    ///
    /// <para><b>혼자 켜서는 절대 안 보이는 결함이다.</b> 호스트는 자기 물리가 곧 정답이라
    /// 화면이 멀쩡하다. 그래서 사람이 아니라 이 테스트가 지켜야 한다.</para>
    /// </summary>
    public class WorldSyncReadinessTests
    {
        /// <summary>
        /// <b>연 씬을 반드시 닫는다</b> (S15P21B202-604). 열어 둔 채 끝나면 뒤이어 도는
        /// 검증들이 이 씬의 콜라이더를 만나 무너진다 — 2026-09-17 에 그렇게
        /// <c>CraftMeteorImpactTests</c> 9건이 한꺼번에 깨졌다(기체가 남의 씬 바닥
        /// 4m 에 걸렸다). <c>test_scene_cleanup.py</c> 가 이 정리를 강제한다.
        /// </summary>
        [OneTimeTearDown]
        public void CloseAnyOpenScene()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        const string ScenePath = "Assets/Scenes/Main.unity";

        static T[] FindInScene<T>() where T : Component
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var found = new System.Collections.Generic.List<T>();
            foreach (var root in scene.GetRootGameObjects())
                found.AddRange(root.GetComponentsInChildren<T>(true));
            return found.ToArray();
        }

        [Test]
        public void TheGameSceneHasPartsToSync()
        {
            // 문지기가 이것으로 열린다. 하나도 없으면 월드 동기화가 시작되지 못한다.
            Assert.Greater(FindInScene<SpaceshipPart>().Length, 0,
                "게임 씬에 SpaceshipPart 가 없다 — WorldSync 의 준비 판정이 영영 열리지 않는다.");
        }

        [Test]
        public void TheGameSceneHasNoTransportVehicle_SoItMustNotBeTheReadinessGate()
        {
            // 이것이 0 이라는 사실 자체가, 견인 장치를 문지기로 쓰면 안 되는 이유다.
            // (같은 사실을 Stage1QaAudit 도 확인하지만 그건 사람이 눌러야 도는 도구다.)
            Assert.AreEqual(0, FindInScene<TransportVehicle>().Length,
                "견인 장치가 씬에 생겼다. 그렇다면 Part_1 해제 전 별도 장치가 없어야 한다는 기획이 바뀐 것이니 "
                + "WorldSync 의 준비 판정과 Stage1QaAudit 을 함께 다시 본다.");
        }
    }
}
