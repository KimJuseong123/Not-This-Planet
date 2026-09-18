using System;
using System.Linq;
using System.Reflection;
using Metagame.EditorTools;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Metagame.Gameplay.Tests
{
    public sealed class TinyFlowerPatchBuilderTests
    {
        string _folder;
        [SetUp] public void Setup()
        {
            string name="TinyFlowerRegression_"+Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets/_Project",name);
            _folder="Assets/_Project/"+name;
        }
        [TearDown] public void Cleanup() => AssetDatabase.DeleteAsset(_folder);

        [Test] public void BuilderHasNoAutomaticEditorLoadHook()
        {
            var type=typeof(TinyFlowerPatchBuilder);
            Assert.That(type.GetCustomAttribute<InitializeOnLoadAttribute>(),Is.Null);
            Assert.That(type.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static)
                .Any(m=>m.GetCustomAttribute<InitializeOnLoadMethodAttribute>()!=null),Is.False);
        }

        [Test] public void RebuildingMeshAndPalettePreservesSavedPrefabAndMaterialReferences()
        {
            string meshPath=_folder+"/mesh.asset",texturePath=_folder+"/palette.asset";
            var palette=(Texture2D)Invoke("CreateOrReplacePalette",texturePath);
            Invoke("SaveOrReplaceMesh",Triangle(1),meshPath);
            var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mesh,out string meshGuid,out long meshId);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(palette,out string paletteGuid,out long paletteId);
            var material=new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            material.mainTexture=palette;
            string materialPath=_folder+"/material.mat",prefabPath=_folder+"/flower.prefab";
            AssetDatabase.CreateAsset(material,materialPath);
            var go=new GameObject("Referenced flower");
            try
            {
                go.AddComponent<MeshFilter>().sharedMesh=mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial=material;
                PrefabUtility.SaveAsPrefabAsset(go,prefabPath);
            }
            finally { Object.DestroyImmediate(go); }
            AssetDatabase.SaveAssets();
            palette.SetPixel(0,0,Color.red);palette.Apply();
            Invoke("CreateOrReplacePalette",texturePath);
            Invoke("SaveOrReplaceMesh",Triangle(2),meshPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(meshPath,ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(texturePath,ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(materialPath,ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(prefabPath,ImportAssetOptions.ForceUpdate);
            var saved=AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var referencedMesh=saved.GetComponent<MeshFilter>().sharedMesh;
            var referencedPalette=saved.GetComponent<MeshRenderer>().sharedMaterial.mainTexture as Texture2D;
            Assert.That(referencedMesh,Is.Not.Null);
            Assert.That(referencedPalette,Is.Not.Null);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(referencedMesh,out string newMeshGuid,out long newMeshId);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(referencedPalette,out string newPaletteGuid,out long newPaletteId);
            Assert.That(newMeshGuid,Is.EqualTo(meshGuid));Assert.That(newMeshId,Is.EqualTo(meshId));
            Assert.That(newPaletteGuid,Is.EqualTo(paletteGuid));Assert.That(newPaletteId,Is.EqualTo(paletteId));
            Assert.That(referencedMesh.vertices[1],Is.EqualTo(Vector3.right*2));
            Assert.That((Color32)referencedPalette.GetPixel(0,0),Is.EqualTo(new Color32(71,111,66,255)));
        }
        static object Invoke(string name,params object[] args) => typeof(TinyFlowerPatchBuilder)
            .GetMethod(name,BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,args);
        static Mesh Triangle(float size)
        {
            var mesh=new Mesh();mesh.vertices=new[]{Vector3.zero,Vector3.right*size,Vector3.up*size};
            mesh.normals=new[]{Vector3.forward,Vector3.forward,Vector3.forward};
            mesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.up};mesh.triangles=new[]{0,1,2};
            return mesh;
        }
    }
}
