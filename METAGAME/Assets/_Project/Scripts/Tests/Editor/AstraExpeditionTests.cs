using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
using Metagame.Gameplay.Parts;
namespace Metagame.Gameplay.Tests
{
 public sealed class AstraExpeditionTests
 {
  Scene scene;
  [SetUp] public void Setup()=>scene=EditorSceneManager.NewPreviewScene();
  [TearDown] public void Cleanup()=>EditorSceneManager.ClosePreviewScene(scene);
  GameObject New(string name){var go=new GameObject(name);SceneManager.MoveGameObjectToScene(go,scene);return go;}
  SpaceshipPart Part(){var go=New("Part_1");go.AddComponent<BoxCollider>();go.AddComponent<Rigidbody>();var part=go.AddComponent<SpaceshipPart>();part.Initialize();return part;}
  [Test]public void AuthoredClueDescribesThisSceneWithoutChangingModuleIdentity()
  {var map=New("map").AddComponent<AuthoredMapSettings>();map.explorationClues=new[]{"Robot charging in logistics"};var p=Part();var e=New("exploration").AddComponent<Stage1Exploration>();e.Configure(new[]{p});Assert.AreEqual("Robot charging in logistics",e.Parts[0].Clue);Assert.AreEqual("SF_Stage1_Spaceship_Robot",e.Parts[0].AssetName);Assert.AreSame(p,e.Parts[0].Part);}
  [Test]public void EmptyClueKeepsDefaultHint()
  {var map=New("map").AddComponent<AuthoredMapSettings>();map.explorationClues=new[]{"  "};var e=New("exploration").AddComponent<Stage1Exploration>();e.Configure(new[]{Part()});Assert.IsFalse(string.IsNullOrWhiteSpace(e.Parts[0].Clue));Assert.That(e.Parts[0].Clue,Does.Contain("고정못"));}
  [Test]public void AnotherScenesMapCannotOverrideClue()
  {var other=EditorSceneManager.NewPreviewScene();try{var go=new GameObject("other map");SceneManager.MoveGameObjectToScene(go,other);go.AddComponent<AuthoredMapSettings>().explorationClues=new[]{"unrelated"};var e=New("exploration").AddComponent<Stage1Exploration>();e.Configure(new[]{Part()});Assert.AreNotEqual("unrelated",e.Parts[0].Clue);}finally{EditorSceneManager.ClosePreviewScene(other);}}
 }
}
