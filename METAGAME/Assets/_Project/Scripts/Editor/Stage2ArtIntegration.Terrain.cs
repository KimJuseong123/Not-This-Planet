using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Metagame.EditorTools
{
    public static partial class Stage2ArtIntegration
    {
        const string TerrainFolder = "Assets/_Project/Terrain/Stage2";
        const string TerrainName = "Stage2 Earth Terrain";

        // The apron remains level; low reclaimed banks lead into the industrial basin rim.
        static float Ramp(float a,float b,float value)=>Mathf.SmoothStep(0,1,Mathf.InverseLerp(a,b,value));
        static float EarthHeight(float x, float z)
        {
            float edge = Mathf.Max(Mathf.Abs(x) / 43f, Mathf.Abs(z) / 39f);
            float blend = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.96f, 2.5f, edge));
            float hills = 3 + 13 * Mathf.PerlinNoise(x * .013f + 12, z * .013f + 8);
            float bank = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.79f, 1.02f, edge)) * .45f;
            // The southwest arrival corridor stays open and nearly level.
            float arrival = (1 - Ramp(6, 15, Mathf.Abs(x + 27))) * (1 - Ramp(-44, -28, z));
            return Mathf.Lerp(bank + blend * hills, 0, arrival * (1 - blend));
        }

        static TerrainLayer EarthLayer(string name, Color a, Color b, float tile)
        {
            const int n = 256;
            var texture = new Texture2D(n, n, TextureFormat.RGB24, false);
            var pixels = new Color[n * n];
            // Blend wrapped noise samples for seamless, non-gridlike ground variation.
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                float u = (float)x / n, v = (float)y / n;
                float Noise(float scale) => Mathf.Lerp(
                    Mathf.Lerp(Mathf.PerlinNoise(20+u*scale,20+v*scale),Mathf.PerlinNoise(20+(u-1)*scale,20+v*scale),u),
                    Mathf.Lerp(Mathf.PerlinNoise(20+u*scale,20+(v-1)*scale),Mathf.PerlinNoise(20+(u-1)*scale,20+(v-1)*scale),u),v);
                float noise = Noise(7)*.65f+Noise(43)*.35f;
                pixels[y * n + x] = Color.Lerp(a, b, noise);
            }
            texture.SetPixels(pixels); texture.Apply();
            string path = TerrainFolder + "/" + name + ".png";
            File.WriteAllBytes(path, texture.EncodeToPNG()); Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.wrapMode = TextureWrapMode.Repeat; importer.mipmapEnabled = true; importer.SaveAndReimport();
            string layerPath=TerrainFolder + "/" + name + ".terrainlayer";
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(layerPath);
            bool create=layer==null; if(create)layer=new TerrainLayer {name=name};
            layer.diffuseTexture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);layer.tileSize=Vector2.one*tile;layer.smoothness=.05f;
            if(create)AssetDatabase.CreateAsset(layer,layerPath);else EditorUtility.SetDirty(layer);
            return layer;
        }

        [MenuItem("Not This Planet/Scenes/Convert Stage2 Earth to Terrain")]
        public static void ConvertToTerrain()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Exit Play Mode first.");
            var loaded = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(ScenePath);
            Require(!loaded.IsValid() || !loaded.isDirty, "Save scene edits before conversion.");
            var scene = loaded.IsValid() ? loaded : EditorSceneManager.OpenScene(ScenePath);
            Require(!scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Terrain>()).Any(), "Terrain already exists; edit it directly.");
            Folder(TerrainFolder);
            var art = scene.GetRootGameObjects().Single(g => g.name == ArtRoot).transform;
            var data = new TerrainData { name = "TD_Stage2_Earth", heightmapResolution = 513, alphamapResolution = 512, baseMapResolution = 512, size = new Vector3(320, 32, 320) };
            var heights = new float[513, 513];
            for (int z = 0; z < 513; z++) for (int x = 0; x < 513; x++) heights[z, x] = (EarthHeight(x * 320f / 512 - 160, z * 320f / 512 - 160) + 4) / 32;
            data.SetHeights(0, 0, heights);
            data.terrainLayers = new[] {
                EarthLayer("EarthSoil", new Color(.28f,.23f,.16f), new Color(.43f,.36f,.25f), 5),
                EarthLayer("DryGrass", new Color(.30f,.32f,.15f), new Color(.46f,.43f,.24f), 5),
                EarthLayer("IndustrialGravel", new Color(.29f,.30f,.28f), new Color(.47f,.46f,.40f), 3),
                EarthLayer("DampDrainage", new Color(.13f,.18f,.12f), new Color(.25f,.27f,.17f), 4) };
            var weights = new float[512,512,4];
            for (int z = 0; z < 512; z++) for (int x = 0; x < 512; x++)
            {
                float px = x * 320f / 511 - 160, pz = z * 320f / 511 - 160;
                float edge = Mathf.Max(Mathf.Abs(px)/43, Mathf.Abs(pz)/39);
                float grass = Mathf.PerlinNoise(px*.065f+17,pz*.065f+21) * .75f;
                float gravel = (1-Ramp(.72f,1.15f,edge))*.8f;
                float damp = (1-Ramp(1,4,Mathf.Abs(px-33))) * (1-Ramp(28,34,Mathf.Abs(pz))) * .9f;
                weights[z,x,3]=damp; weights[z,x,2]=gravel*(1-damp);
                weights[z,x,1]=grass*(1-gravel)*(1-damp); weights[z,x,0]=(1-grass)*(1-gravel)*(1-damp);
            }
            data.SetAlphamaps(0,0,weights);
            AssetDatabase.CreateAsset(data,TerrainFolder+"/TD_Stage2_Earth.asset");
            var terrainObject = Terrain.CreateTerrainGameObject(data); terrainObject.name = TerrainName;
            terrainObject.transform.SetParent(art,false); terrainObject.transform.position = new Vector3(-160,-4,-160);
            var terrain = terrainObject.GetComponent<Terrain>(); terrain.drawInstanced = true; terrain.heightmapPixelError = 5; terrain.basemapDistance = 200;
            var material = new Material(Shader.Find("Universal Render Pipeline/Terrain/Lit")) { name = "Stage2 Terrain URP" };
            AssetDatabase.CreateAsset(material,TerrainFolder+"/Terrain.mat"); terrain.materialTemplate = material;
            Object.DestroyImmediate(art.Find("03 Reclaimed earth outside apron").gameObject);
            var yard = scene.GetRootGameObjects().Single(g=>g.GetComponent<Metagame.Gameplay.Stage2.Stage2Yard>()!=null);
            yard.transform.Find("01 Ground and wayfinding/Foundation").GetComponent<Renderer>().enabled=false;
            // Expose terrain around the perimeter instead of covering the entire map with concrete.
            foreach(Transform slab in art.Find("02 Apron slabs and route markings"))
                if(slab.name=="Art Slab")
                {
                    var p=slab.position;
                    bool work = Mathf.Abs(p.x)<=24 && Mathf.Abs(p.z)<=27;
                    bool preparation = p.x<=-18 && p.z<=-12;
                    slab.gameObject.SetActive(work||preparation);
                }
            void GroundOnTerrain(Transform t)
            {
                var bounds=Bounds(t.gameObject);
                float y=Mathf.Max(0,terrain.SampleHeight(t.position)+terrain.transform.position.y);
                t.position += Vector3.up*(y-bounds.min.y-.025f);
            }
            foreach(Transform t in art.Find("06 Distant industrial skyline")) GroundOnTerrain(t);
            foreach(Transform cluster in art.Find("07 Earth vegetation - edge clusters")) foreach(Transform plant in cluster) GroundOnTerrain(plant);
            var outer=Group(art,"08 Terrain vegetation - outer banks"); random=new System.Random(260917);
            for(int i=0;i<240;i++)
            {
                float angle=Range(0,Mathf.PI*2),radius=Range(48,100);
                var p=new Vector3(Mathf.Cos(angle)*radius,0,Mathf.Sin(angle)*radius);
                if(p.x>-36&&p.x<-18&&p.z<-30)continue;
                var model=Model(i%5==0?"DeadSapling":i%3==0?"Shrub":"GrassDry",outer,p,Range(0,360),Vector3.one*Range(1.4f,3));
                GroundOnTerrain(model.transform);
            }
            terrain.Flush(); EditorUtility.SetDirty(data); AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene); Require(EditorSceneManager.SaveScene(scene),"Terrain scene save failed");
            RefineTerrain();
        }

        public static void RefineTerrain()
        {
            var scene=EditorSceneManager.OpenScene(ScenePath);
            var art=scene.GetRootGameObjects().Single(g=>g.name==ArtRoot).transform;
            var terrain=art.GetComponentInChildren<Terrain>();var data=terrain.terrainData;
            var pads=art.Find("06 Distant industrial skyline").Cast<Transform>().Select(t=>Bounds(t.gameObject)).ToArray();
            var heights=new float[513,513];
            for(int z=0;z<513;z++)for(int x=0;x<513;x++)
            {
                float px=x*320f/512-160,pz=z*320f/512-160,h=EarthHeight(px,pz);
                foreach(var pad in pads)
                {
                    float dx=Mathf.Max(0,Mathf.Abs(px-pad.center.x)-pad.extents.x),dz=Mathf.Max(0,Mathf.Abs(pz-pad.center.z)-pad.extents.z);
                    float weight=1-Ramp(0,3,Mathf.Max(dx,dz));
                    h=Mathf.Lerp(h,EarthHeight(pad.center.x,pad.center.z),weight);
                }
                heights[z,x]=(h+4)/32;
            }
            data.SetHeights(0,0,heights);
            var foundation=scene.GetRootGameObjects().Single(g=>g.GetComponent<Metagame.Gameplay.Stage2.Stage2Yard>()!=null).transform.Find("01 Ground and wayfinding/Foundation");
            foundation.GetComponent<Collider>().enabled=false;
            foreach(Transform slab in art.Find("02 Apron slabs and route markings"))
                if(slab.name=="Art Slab"&&slab.gameObject.activeSelf)
                    slab.position+=Vector3.up*(.02f-Bounds(slab.gameObject).max.y);
            data.terrainLayers=new[]{
                EarthLayer("EarthSoil",new Color(.28f,.23f,.16f),new Color(.43f,.36f,.25f),5),
                EarthLayer("DryGrass",new Color(.30f,.32f,.15f),new Color(.46f,.43f,.24f),5),
                EarthLayer("IndustrialGravel",new Color(.29f,.30f,.28f),new Color(.47f,.46f,.40f),3),
                EarthLayer("DampDrainage",new Color(.13f,.18f,.12f),new Color(.25f,.27f,.17f),4)};
            foreach(var name in new[]{"06 Distant industrial skyline","07 Earth vegetation - edge clusters","08 Terrain vegetation - outer banks"})
            {
                var group=art.Find(name);
                foreach(var t in group.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("Art ")))
                {var b=Bounds(t.gameObject);float y=Mathf.Max(0,terrain.SampleHeight(t.position)+terrain.transform.position.y);t.position+=Vector3.up*(y-b.min.y-.025f);}
            }
            terrain.Flush();EditorUtility.SetDirty(data);AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            Validate();ValidateTerrain();Render();
        }

        public static void ValidateTerrain()
        {
            var scene=UnityEngine.SceneManagement.SceneManager.GetSceneByPath(ScenePath);
            if(!scene.IsValid())scene=EditorSceneManager.OpenScene(ScenePath);
            var terrain=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Terrain>()).Single();
            Require(terrain.GetComponent<TerrainCollider>().terrainData==terrain.terrainData,"Terrain collision data mismatch");
            Require(terrain.terrainData.terrainLayers.Length==4,"Four terrain paint layers required");
            Require(terrain.materialTemplate.shader.name=="Universal Render Pipeline/Terrain/Lit","Terrain URP shader missing");
            Require(!scene.GetRootGameObjects().Single(g=>g.GetComponent<Metagame.Gameplay.Stage2.Stage2Yard>()!=null).transform.Find("01 Ground and wayfinding/Foundation").GetComponent<Collider>().enabled,"Legacy flat collider must not mask Terrain sculpting");
            foreach(var p in new[]{new Vector3(9,0,9),new Vector3(-8,0,5),new Vector3(-8,0,22),new Vector3(8,0,24),new Vector3(16,0,-1),new Vector3(-27,0,-18)})
                Require(terrain.SampleHeight(p)+terrain.transform.position.y<.02f,"Terrain rises through a work area");
            Physics.SyncTransforms();
            Require(terrain.GetComponent<TerrainCollider>().Raycast(new Ray(new Vector3(80,40,40),Vector3.down),out var hit,60),"Outer terrain has no collision");
            File.WriteAllText(Output+"/terrain-validation.json",JsonUtility.ToJson(new TerrainReport{passed=true,heightmap=513,layers=4,size=320,colliderHeight=hit.point.y},true));
            Debug.Log("[Stage2 Terrain] Heightmap, four paint layers, collider and level work areas verified.");
        }
        [Serializable] sealed class TerrainReport { public bool passed; public int heightmap,layers,size; public float colliderHeight; }
    }
}
