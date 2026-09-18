using System;
using System.IO;
using System.Linq;
using Metagame.EditorTools;
using Metagame.Gameplay.Stage2;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Metagame.Gameplay.Tests
{
    public sealed class Stage2ReviewTests
    {
        const string TerrainFolder="Assets/_Project/Terrain/Stage2";
        string[] originalFolders;
        string temporaryScene;
        [SetUp] public void Setup()
        {
            originalFolders=AssetDatabase.GetSubFolders(TerrainFolder);
            Undo.ClearAll();
        }
        [TearDown] public void Cleanup()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            Undo.ClearAll();
            foreach(var folder in AssetDatabase.GetSubFolders(TerrainFolder).Except(originalFolders))
                if(Path.GetFileName(folder).StartsWith("Preview-"))AssetDatabase.DeleteAsset(folder);
            if(temporaryScene!=null)AssetDatabase.DeleteAsset(temporaryScene);
        }
        static Scene Open()=>EditorSceneManager.OpenScene(Stage2SceneBuilder.ScenePath);
        static Stage2Target[] Targets(Scene scene)=>scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Stage2Target>(true)).ToArray();

        [TestCase(0)] [TestCase(2)] [TestCase(4)]
        public void RejectsMissingOrExtraPressureTargets(int count)
        {
            var scene=Open();
            var valves=Targets(scene).Where(t=>t.Kind==YardTargetKind.PressureRelief).ToArray();
            if(count<3)foreach(var t in valves.Skip(count))Object.DestroyImmediate(t);
            else
            {
                var extra=new GameObject("Extra valve").AddComponent<Stage2Target>();
                extra.Configure(103,YardTargetKind.PressureRelief,3,"Extra");
            }
            Assert.Throws<InvalidOperationException>(()=>Stage2ArtIntegration.Validate());
            Assert.That(File.ReadAllText("../artifacts/stage2-art/scene-validation.json"),Does.Contain("Pressure relief interaction count"));
        }
        [Test]
        public void RejectsUniqueButChangedInteractionId()
        {
            var t=Targets(Open()).First(t=>t.Kind!=YardTargetKind.PressureRelief);
            t.Configure(999,t.Kind,t.Index,t.Label);
            Assert.Throws<InvalidOperationException>(()=>Stage2ArtIntegration.Validate());
            Assert.That(File.ReadAllText("../artifacts/stage2-art/scene-validation.json"),Does.Contain("Interaction IDs changed"));
        }
        [Test]
        public void RejectsUnregisteredSceneTarget()
        {
            var yard=Open().GetRootGameObjects().Select(r=>r.GetComponent<Stage2Yard>()).Single(y=>y!=null);
            var serialized=new SerializedObject(yard);
            var targets=serialized.FindProperty("_targets");
            targets.GetArrayElementAtIndex(targets.arraySize-1).objectReferenceValue=targets.GetArrayElementAtIndex(0).objectReferenceValue;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Assert.Throws<InvalidOperationException>(()=>Stage2ArtIntegration.Validate());
            Assert.That(File.ReadAllText("../artifacts/stage2-art/scene-validation.json"),Does.Contain("Registered interaction targets differ from scene"));
        }
        [Test]
        public void FailedRefinementRollsBackAndRemovesOnlyItsGeneratedAssets()
        {
            var scene=Open();
            var terrain=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Terrain>()).Single();
            var original=terrain.terrainData;
            var art=terrain.transform.parent;
            // A malformed input fails after the data copy has been created and assigned.
            Object.DestroyImmediate(art.Find("06 Distant industrial skyline").gameObject);
            temporaryScene=AssetDatabase.GenerateUniqueAssetPath("Assets/Stage2TerrainReview.unity");
            Assert.That(EditorSceneManager.SaveScene(scene,temporaryScene),Is.True);
            var bytes=File.ReadAllBytes(temporaryScene);
            Assert.That(()=>Stage2ArtIntegration.RefineTerrain(scene),Throws.Exception);
            Assert.That(terrain.terrainData,Is.SameAs(original));
            Assert.That(terrain.GetComponent<TerrainCollider>().terrainData,Is.SameAs(original));
            CollectionAssert.AreEquivalent(originalFolders,AssetDatabase.GetSubFolders(TerrainFolder));
            CollectionAssert.AreEqual(bytes,File.ReadAllBytes(temporaryScene));
        }
        [Test]
        public void RefinementPreservesSavedDataDrainageAndSupportsUndoRedo()
        {
            var scene=Open();
            var terrain=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Terrain>()).Single();
            var original=terrain.terrainData;
            var sceneBytes=File.ReadAllBytes(scene.path);
            var dataBytes=File.ReadAllBytes(AssetDatabase.GetAssetPath(original));
            var transforms=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Transform>(true)).ToArray();
            var positions=transforms.Select(t=>t.position).ToArray();
            int x=Mathf.RoundToInt((33-terrain.transform.position.x)/original.size.x*(original.heightmapResolution-1));
            int z=Mathf.RoundToInt((0-terrain.transform.position.z)/original.size.z*(original.heightmapResolution-1));
            float bed=original.GetHeight(x,z);
            Stage2ArtIntegration.RefineTerrain(scene);
            var preview=terrain.terrainData;
            Assert.That(preview,Is.Not.SameAs(original));
            Assert.That(preview.GetHeight(x,z),Is.EqualTo(bed).Within(.001f));
            Assert.That(scene.isDirty,Is.True);
            CollectionAssert.AreEqual(sceneBytes,File.ReadAllBytes(scene.path));
            CollectionAssert.AreEqual(dataBytes,File.ReadAllBytes(AssetDatabase.GetAssetPath(original)));
            Undo.PerformUndo();
            Assert.That(terrain.terrainData,Is.SameAs(original));
            Assert.That(terrain.GetComponent<TerrainCollider>().terrainData,Is.SameAs(original));
            for(int i=0;i<transforms.Length;i++)Assert.That(transforms[i].position,Is.EqualTo(positions[i]));
            Undo.PerformRedo();
            Assert.That(terrain.terrainData,Is.SameAs(preview));
            Assert.That(terrain.GetComponent<TerrainCollider>().terrainData,Is.SameAs(preview));
        }
        [Test]
        public void ConversionPreservesLegacyGroundAndDoesNotSaveScene()
        {
            var scene=Open();
            var art=scene.GetRootGameObjects().Single(r=>r.name=="Stage2 Earth Art - Reclaimed Launch Yard").transform;
            Object.DestroyImmediate(art.GetComponentInChildren<Terrain>().gameObject);
            Object.DestroyImmediate(art.Find("08 Terrain vegetation - outer banks").gameObject);
            var ground=GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name="03 Reclaimed earth outside apron";
            ground.transform.SetParent(art);
            temporaryScene=AssetDatabase.GenerateUniqueAssetPath("Assets/Stage2TerrainReview.unity");
            Assert.That(EditorSceneManager.SaveScene(scene,temporaryScene),Is.True);
            var before=File.ReadAllBytes(temporaryScene);
            Stage2ArtIntegration.ConvertToTerrain(scene);
            Assert.That(ground,Is.Not.Null);
            Assert.That(ground.activeSelf,Is.False);
            Assert.That(art.GetComponentInChildren<Terrain>(),Is.Not.Null);
            CollectionAssert.AreEqual(before,File.ReadAllBytes(temporaryScene));
            Undo.PerformUndo();
            Assert.That(ground.activeSelf,Is.True);
            Assert.That(art.GetComponentInChildren<Terrain>(),Is.Null);
            Assert.That(art.Find("08 Terrain vegetation - outer banks"),Is.Null);
            CollectionAssert.AreEqual(before,File.ReadAllBytes(temporaryScene));
        }
        [Test]
        public void DirtySceneIsRejectedWithoutChangingTerrain()
        {
            var scene=Open();
            var terrain=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Terrain>()).Single();
            var original=terrain.terrainData;
            EditorSceneManager.MarkSceneDirty(scene);
            Assert.Throws<InvalidOperationException>(()=>Stage2ArtIntegration.RefineTerrain(scene));
            Assert.That(terrain.terrainData,Is.SameAs(original));
        }
        [Test]
        public void InactiveTerrainStillBlocksConversion()
        {
            var scene=Open();
            var terrain=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Terrain>()).Single();
            terrain.gameObject.SetActive(false);
            temporaryScene=AssetDatabase.GenerateUniqueAssetPath("Assets/Stage2TerrainReview.unity");
            Assert.That(EditorSceneManager.SaveScene(scene,temporaryScene),Is.True);
            var error=Assert.Throws<InvalidOperationException>(()=>Stage2ArtIntegration.ConvertToTerrain(scene));
            Assert.That(error.Message,Does.Contain("Terrain already exists"));
            Assert.That(terrain.gameObject.activeSelf,Is.False);
        }
    }
}
