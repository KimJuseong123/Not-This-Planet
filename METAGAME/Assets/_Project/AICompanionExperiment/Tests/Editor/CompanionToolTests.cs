using System.Collections;
using Metagame.AICompanion;
using Metagame.Gameplay;
using Metagame.Gameplay.Hazards;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Metagame.AIExperiment.Tests
{
    public sealed class CompanionToolTests
    {
        /// <summary>
        /// <b>연 씬을 반드시 닫는다</b> (S15P21B202-604). 이유는
        /// <see cref="CompanionTests.CloseTheRoom"/> 에 적어 뒀다 — 열어 둔 채 끝나면
        /// 뒤이어 도는 검증들이 이 방의 콜라이더를 만나 무너진다.
        /// </summary>
        [OneTimeTearDown]
        public void CloseTheRoom()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        [Category("Heavy")]
        [UnityTest]
        public IEnumerator RealToolsPickUpSwapAndDropWithoutFiring()
        {
            EditorSceneManager.OpenScene(AIExperimentManager.RoomScene);
            yield return new EnterPlayMode();
            for (float end = Time.time + 2; Time.time < end;) yield return null;
            var trial = Object.FindAnyObjectByType<AICompanionTrial>();
            var session = Object.FindAnyObjectByType<GameSession>();
            session.ScriptedInput = true;
            var bat = Object.FindAnyObjectByType<MeteorBat>();
            var welder = Object.FindAnyObjectByType<PlasmaWeldingTool>();
            Assert.NotNull(bat); Assert.NotNull(welder);
            session.LocalPlayer.Teleport(new Vector3(bat.transform.position.x, .2f, bat.transform.position.z + 2), Quaternion.identity);
            Assert.IsTrue(trial.ReceiveIntent("몽둥이 주워줘", "pickup_bat"), trial.Receipt);
            var deadline = Time.time + 36;
            while (Time.time < deadline && trial.Companion.Order == CompanionOrder.PickUpBat) yield return null;
            Assert.AreSame(bat, trial.Companion.Rig.ActiveTool, trial.Companion.Status);
            Assert.AreEqual(0, bat.RhythmSequence, "Picking up must not swing.");
            Assert.IsFalse(CompanionMotor.ToolIsFree(bat.GetComponent<Grabbable>()));
            Assert.IsFalse(trial.ReceiveIntent("못 빼줘", "pull_pin"), "A held tool must not accidentally fire at a pin.");
            Assert.AreEqual(0, bat.RhythmSequence);
            session.LocalPlayer.Teleport(new Vector3(welder.transform.position.x, .2f, welder.transform.position.z - 2), Quaternion.identity);
            Assert.IsTrue(trial.ReceiveIntent("용접건 주워줘", "pickup_welder"));
            deadline = Time.time + 36;
            while (Time.time < deadline && trial.Companion.Order == CompanionOrder.PickUpWelder) yield return null;
            Assert.AreSame(welder, trial.Companion.Rig.ActiveTool, trial.Companion.Status);
            Assert.IsFalse(bat.IsEquipped);
            Assert.IsTrue(trial.ReceiveIntent("내려놔", "drop_tool"));
            for (float end = Time.time + .5f; Time.time < end;) yield return null;
            Assert.IsNull(trial.Companion.Rig.HeldTool, trial.Companion.Status + " order=" + trial.Companion.Order);
            Assert.IsFalse(welder.IsEquipped);
            Assert.IsFalse(welder.GetComponent<Rigidbody>().isKinematic);
            Assert.IsTrue(trial.ReceiveIntent("내려놔", "drop_tool"));
            for (float end = Time.time + .1f; Time.time < end;) yield return null;
            Assert.AreEqual(CompanionOrder.Wait, trial.Companion.Order);
            yield return new ExitPlayMode();
        }

        [Category("Heavy")]
        [UnityTest]
        public IEnumerator RealBatHitsPracticeBallThenDefendsAndStops()
        {
            EditorSceneManager.OpenScene(AIExperimentManager.RoomScene);
            yield return new EnterPlayMode();
            for (float end = Time.time + 2; Time.time < end;) yield return null;
            var trial = Object.FindAnyObjectByType<AICompanionTrial>();
            var session = Object.FindAnyObjectByType<GameSession>();
            session.ScriptedInput = true;
            var target = Object.FindAnyObjectByType<PracticeMeteorTarget>();
            session.LocalPlayer.Teleport(new Vector3(1, .2f, -3), Quaternion.identity);
            Assert.IsTrue(trial.ReceiveIntent("공 때려줘", "hit_target"));
            session.LocalPlayer.Teleport(new Vector3(-7, .2f, -2), Quaternion.identity);
            var deadline = Time.time + 36;
            while (Time.time < deadline && trial.Companion.Order == CompanionOrder.HitTarget) yield return null;
            Assert.Greater(target.HitCount, 0, trial.Companion.Status);
            var bat = trial.Companion.Rig.ActiveTool as MeteorBat;
            Assert.NotNull(bat);
            Assert.IsTrue(trial.ReceiveIntent("운석 막아줘", "defend_meteors"));
            session.LocalPlayer.Teleport(new Vector3(5, .2f, 1), Quaternion.identity);
            deadline = Time.time + 35;
            while (Time.time < deadline && !trial.Companion.Status.StartsWith("근거리 운석 방어")) yield return null;
            StringAssert.StartsWith("근거리 운석 방어", trial.Companion.Status);
            trial.SpawnPracticeMeteor();
            var meteor = Object.FindAnyObjectByType<Meteor>();
            Assert.NotNull(meteor, "The authored F6 sample prefab must be available in the trial scene.");
            deadline = Time.time + 3;
            while (Time.time < deadline && !meteor.WasDeflected) yield return null;
            Assert.IsTrue(meteor.WasDeflected, trial.Companion.Status);
            Assert.AreSame(meteor, bat.LastDeflected);
            trial.StopImmediately();
            var sequence = bat.RhythmSequence;
            for (float end = Time.time + .5f; Time.time < end;) yield return null;
            Assert.AreEqual(sequence, bat.RhythmSequence);
            Assert.AreSame(bat, trial.Companion.Rig.ActiveTool, "Stop keeps the tool; drop is a separate command.");
            Object.Destroy(meteor.gameObject);
            Assert.IsTrue(trial.ReceiveIntent("내려놔", "drop_tool"));
            for (float end = Time.time + .5f; Time.time < end;) yield return null;
            Assert.IsNull(trial.Companion.Rig.HeldTool, trial.Companion.Status + " order=" + trial.Companion.Order);
            yield return new ExitPlayMode();
        }
    }
}
