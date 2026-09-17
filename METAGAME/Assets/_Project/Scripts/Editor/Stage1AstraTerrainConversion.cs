using System;
using System.IO;
using System.Linq;
using Metagame.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Metagame.EditorTools
{
    /// <summary>One-time mesh heightfield conversion. Never rebuilds an edited Terrain.</summary>
    public static class Stage1AstraTerrainConversion
    {
        public const string TerrainName = "Basin - Editable Terrain";
        const string ScenePath = "Assets/Scenes/Stage1_Astra.unity";
        const string DataPath = "Assets/_Project/Terrain/Stage1/TerrainData/TD_Stage1_AstraBasin.asset";
        const string LayerPath = "Assets/_Project/Terrain/Stage1/TerrainLayers/TL_Stage1_AstraSand.terrainlayer";
        const string MaterialPath = "Assets/_Project/Materials/Environment/Stage1/M_Stage1_AstraTerrain.mat";
        const int Resolution = 2049;

        [Serializable]
        class Report
        {
            public string status;
            public int heightResolution, surfaceSamples, vegetationSamples, routeSamples, paintLayers, physicsDropSamples;
            public float maximumHeightError, meanHeightError, maximumVegetationError;
            public Vector3 terrainPosition, terrainSize;
            public bool legacyRendererDisabled, legacyColliderDisabled, heightBrushRoundtrip, textureBrushRoundtrip;
        }

        static void Check(bool valid, string message)
        {
            if (!valid) throw new InvalidOperationException(message);
        }

        static Transform[] All(Scene scene) => scene.GetRootGameObjects()
            .SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();

        static MeshCollider Source(Scene scene) => All(scene)
            .Single(t => t.name.Contains("sculpted ground")).GetComponent<MeshCollider>();

        static Ray RayAt(float x, float z) => new Ray(new Vector3(x, 60f, z), Vector3.down);

        static RaycastHit Sample(Collider collider, float x, float z)
        {
            Check(collider.Raycast(RayAt(x,z), out var hit, 120f), $"Missing surface at {x:F3},{z:F3}: {collider.name}");
            return hit;
        }

        public static void ConvertIsolated()
        {
            Check(Application.isBatchMode && File.Exists("../ISOLATED_ASTRA_TERRAIN"), "Use the isolated conversion project.");
            var scene = EditorSceneManager.OpenScene(ScenePath);
            Check(!All(scene).Any(t => t.GetComponent<Terrain>()), "Terrain already exists; refusing to overwrite sculpting or painting.");
            Check(!File.Exists(DataPath), "TerrainData already exists; refusing to overwrite authored data.");
            var source = Source(scene);
            Check(source && source.enabled && source.gameObject.activeInHierarchy, "Missing active source floor.");
            Check(source.transform.childCount == 0, "Unexpected children on source floor.");
            Physics.SyncTransforms();
            var bounds = source.bounds;
            var position = new Vector3(bounds.min.x, Mathf.Floor(bounds.min.y)-1f, bounds.min.z);
            var size = new Vector3(bounds.size.x, Mathf.Max(16f, Mathf.Ceil(bounds.max.y-position.y)+4f), bounds.size.z);
            var heights = new float[Resolution, Resolution];
            for (int z=0; z<Resolution; z++)
            for (int x=0; x<Resolution; x++)
            {
                float wx = Mathf.Clamp(position.x + size.x*x/(Resolution-1), bounds.min.x+.0005f, bounds.max.x-.0005f);
                float wz = Mathf.Clamp(position.z + size.z*z/(Resolution-1), bounds.min.z+.0005f, bounds.max.z-.0005f);
                heights[z,x] = (Sample(source,wx,wz).point.y-position.y)/size.y;
            }
            var data = new TerrainData
            {
                name = "TD_Stage1_AstraBasin",
                heightmapResolution = Resolution,
                alphamapResolution = 512,
                baseMapResolution = 1024,
                size = size
            };
            data.SetHeights(0,0,heights);
            var originalMaterial = source.GetComponent<Renderer>().sharedMaterial;
            var texture = originalMaterial.GetTexture("_BaseMap") as Texture2D;
            Check(texture, "Source floor texture missing.");
            var uv = source.sharedMesh.uv;
            Check(uv.Length > 0, "Source floor has no UVs.");
            var scale = originalMaterial.GetTextureScale("_BaseMap");
            var offset = originalMaterial.GetTextureOffset("_BaseMap");
            float u0=uv.Min(p=>p.x)*scale.x+offset.x, v0=uv.Min(p=>p.y)*scale.y+offset.y;
            float ur=(uv.Max(p=>p.x)-uv.Min(p=>p.x))*scale.x;
            float vr=(uv.Max(p=>p.y)-uv.Min(p=>p.y))*scale.y;
            Check(ur>0f && vr>0f, "Unsupported mirrored or constant source UVs.");
            var layer = new TerrainLayer
            {
                name = "TL_Stage1_AstraSand",
                diffuseTexture = texture,
                tileSize = new Vector2(size.x/ur,size.z/vr),
                tileOffset = new Vector2(u0*size.x/ur,v0*size.z/vr),
                metallic = originalMaterial.GetFloat("_Metallic"),
                smoothness = originalMaterial.GetFloat("_Smoothness"),
                diffuseRemapMin = Vector4.zero,
                diffuseRemapMax = originalMaterial.GetColor("_BaseColor")
            };
            AssetDatabase.CreateAsset(layer,LayerPath);
            var alien = AssetDatabase.LoadAssetAtPath<TerrainLayer>("Assets/_Project/Terrain/Stage1/TerrainLayers/TL_Stage1_AlienGround.asset");
            var stone = AssetDatabase.LoadAssetAtPath<TerrainLayer>("Assets/_Project/Terrain/Stage1/TerrainLayers/TL_Stage1_Stone.asset");
            Check(alien && alien.diffuseTexture && stone && stone.diffuseTexture, "Missing paint layers.");
            data.terrainLayers = new[] { layer, alien, stone };
            var weights = new float[512,512,3];
            for(int z=0;z<512;z++)for(int x=0;x<512;x++)weights[z,x,0]=1f;
            data.SetAlphamaps(0,0,weights);
            AssetDatabase.CreateAsset(data,DataPath);
            var shader=Shader.Find("Universal Render Pipeline/Terrain/Lit");
            Check(shader,"URP Terrain Lit shader missing.");
            var material=new Material(shader) { name="M_Stage1_AstraTerrain", enableInstancing=true };
            AssetDatabase.CreateAsset(material,MaterialPath);
            var go=Terrain.CreateTerrainGameObject(data);
            go.name=TerrainName;
            go.transform.SetParent(source.transform.parent,true);
            go.transform.position=position;
            go.layer=GameLayers.Ground;
            var terrain=go.GetComponent<Terrain>();
            terrain.materialTemplate=material;
            terrain.drawInstanced=true;
            terrain.heightmapPixelError=1f;
            terrain.basemapDistance=300f;
            terrain.shadowCastingMode=ShadowCastingMode.On;
            terrain.allowAutoConnect=false;
            go.GetComponent<TerrainCollider>().sharedMaterial=source.sharedMaterial;
            // Retain the original mesh for reversible comparison, but never double-collide.
            source.enabled=false;
            source.GetComponent<Renderer>().enabled=false;
            terrain.Flush();
            Physics.SyncTransforms();
            ValidateScene(scene,true);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("ASTRA_TERRAIN_CONVERSION_PASS");
        }

        public static void ValidateSaved()
        {
            Check(Application.isBatchMode && File.Exists("../ISOLATED_ASTRA_TERRAIN"), "Use the isolated validation project.");
            var scene=EditorSceneManager.OpenScene(ScenePath);
            ValidateScene(scene,false);
            Stage1VegetationBuilder.Validate();
            var terrain=All(scene).Single(t=>t.name==TerrainName).GetComponent<Terrain>();
            var report=JsonUtility.FromJson<Report>(File.ReadAllText("../terrain-reload.json"));
            report.physicsDropSamples=ValidatePhysicsDrops(terrain);
            File.WriteAllText("../terrain-reload.json",JsonUtility.ToJson(report,true));
            EditorSceneManager.OpenScene(ScenePath);
        }

        static void ValidateScene(Scene scene, bool testBrushes)
        {
            var terrain=All(scene).Single(t=>t.name==TerrainName).GetComponent<Terrain>();
            var data=terrain.terrainData;
            var collider=terrain.GetComponent<TerrainCollider>();
            var source=Source(scene);
            var report=new Report
            {
                heightResolution=data.heightmapResolution,
                terrainPosition=terrain.transform.position,
                terrainSize=data.size,
                paintLayers=data.terrainLayers.Length,
                legacyRendererDisabled=!source.GetComponent<Renderer>().enabled,
                legacyColliderDisabled=!source.enabled
            };
            Check(report.legacyRendererDisabled&&report.legacyColliderDisabled,"Old floor still renders/collides.");
            Check(collider.enabled&&collider.terrainData==data&&terrain.gameObject.layer==GameLayers.Ground,"Invalid Terrain collision.");
            Check(data.terrainLayers.Length==3&&data.terrainLayers.All(l=>l&&l.diffuseTexture),"Missing paint layers.");
            Check(terrain.materialTemplate&&terrain.materialTemplate.shader.name=="Universal Render Pipeline/Terrain/Lit","Incorrect Terrain shader.");
            source.enabled=true;
            Physics.SyncTransforms();
            try
            {
                var b=source.bounds;
                double totalError=0;
                void Compare(float x,float z)
                {
                    x=Mathf.Clamp(x,b.min.x+.002f,b.max.x-.002f);
                    z=Mathf.Clamp(z,b.min.z+.002f,b.max.z-.002f);
                    float error=Mathf.Abs(Sample(source,x,z).point.y-Sample(collider,x,z).point.y);
                    report.maximumHeightError=Mathf.Max(report.maximumHeightError,error);
                    totalError+=error; report.surfaceSamples++;
                }
                // Sample original vertices and independent sub-cell positions, not only copied height samples.
                foreach(var vertex in source.sharedMesh.vertices)
                {var p=source.transform.TransformPoint(vertex);Compare(p.x,p.z);}
                var random=new System.Random(917);
                for(int i=0;i<12000;i++)Compare(Mathf.Lerp(b.min.x,b.max.x,(float)random.NextDouble()),Mathf.Lerp(b.min.z,b.max.z,(float)random.NextDouble()));
                foreach(var plant in All(scene).Where(t=>t.GetComponent<PlantRustle>()))
                {
                    var p=plant.position;
                    float expected=Sample(source,p.x,p.z).point.y;
                    float actual=Sample(collider,p.x,p.z).point.y;
                    report.maximumVegetationError=Mathf.Max(report.maximumVegetationError,Mathf.Abs(expected-actual));
                    report.vegetationSamples++;
                }
                foreach(var t in All(scene).Where(t=>t.name.StartsWith("Astra Floor Guidance")||t.name.Contains("Player Spawn")))
                {Compare(t.position.x,t.position.z);report.routeSamples++;}
                report.meanHeightError=(float)(totalError/report.surfaceSamples);
                Check(report.maximumHeightError<.05f,$"Terrain height deviation too large: {report.maximumHeightError}");
                Check(report.maximumVegetationError<.025f,$"Vegetation height deviation: {report.maximumVegetationError}");
            }
            finally { source.enabled=false; Physics.SyncTransforms(); }
            if(testBrushes)
            {
                // Verify both authoring channels and restore them before the initial save.
                var patch=data.GetHeights(10,10,1,1);
                float original=patch[0,0];
                patch[0,0]=original+.1f;
                data.SetHeights(10,10,patch);
                Check(Mathf.Abs(data.GetHeights(10,10,1,1)[0,0]-patch[0,0])<.0001f,"Height brush roundtrip failed.");
                patch[0,0]=original;data.SetHeights(10,10,patch);
                var paint=data.GetAlphamaps(10,10,1,1);
                var originalPaint=(float[,,])paint.Clone();
                paint[0,0,0]=0;paint[0,0,1]=0;paint[0,0,2]=1;
                data.SetAlphamaps(10,10,paint);
                Check(data.GetAlphamaps(10,10,1,1)[0,0,2]>.99f,"Texture brush roundtrip failed.");
                data.SetAlphamaps(10,10,originalPaint);
                terrain.Flush();
                report.heightBrushRoundtrip=true;report.textureBrushRoundtrip=true;
            }
            report.status="PASS";
            File.WriteAllText(testBrushes?"../terrain-conversion.json":"../terrain-reload.json",JsonUtility.ToJson(report,true));
            Debug.Log("ASTRA_TERRAIN_VALIDATION_PASS "+JsonUtility.ToJson(report));
        }

        static int ValidatePhysicsDrops(Terrain terrain)
        {
            var data=terrain.terrainData;
            var origin=terrain.transform.position;
            // Editor simulation must not advance any authored gameplay rigidbodies.
            // The saved scene is reopened after this disposable empty test scene.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var previousMode=Physics.simulationMode;
            Physics.simulationMode=SimulationMode.Script;
            try
            {
                var floor=new GameObject("Isolated TerrainCollider");
                floor.transform.position=origin;
                floor.AddComponent<TerrainCollider>().terrainData=data;
                var positions=new[] {new Vector2(-20,-20),new Vector2(0,-20),new Vector2(20,0),new Vector2(28,19),new Vector2(-35,35)};
                var bodies=new Rigidbody[positions.Length];
                var expected=new float[positions.Length];
                for(int i=0;i<positions.Length;i++)
                {
                    var p=new Vector3(positions[i].x,0f,positions[i].y);
                    expected[i]=data.GetInterpolatedHeight((p.x-origin.x)/data.size.x,(p.z-origin.z)/data.size.z)+origin.y+.3f;
                    var probe=new GameObject("Terrain contact probe");
                    probe.transform.position=new Vector3(p.x,expected[i]+2f,p.z);
                    probe.AddComponent<SphereCollider>().radius=.3f;
                    bodies[i]=probe.AddComponent<Rigidbody>();
                    bodies[i].constraints=RigidbodyConstraints.FreezePositionX|RigidbodyConstraints.FreezePositionZ|RigidbodyConstraints.FreezeRotation;
                }
                Physics.SyncTransforms();
                for(int step=0;step<240;step++)Physics.Simulate(.02f);
                for(int i=0;i<bodies.Length;i++)
                    Check(Mathf.Abs(bodies[i].position.y-expected[i])<.04f,$"Terrain contact failed at probe {i}: {bodies[i].position.y} vs {expected[i]}");
                return positions.Length;
            }
            finally
            {
                Physics.simulationMode=previousMode;
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            }
        }
    }
}
