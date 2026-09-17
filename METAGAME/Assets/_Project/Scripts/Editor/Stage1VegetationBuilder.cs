using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Metagame.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Metagame.EditorTools
{
    /// <summary>Additive authoring: no existing scene object or source model is modified.</summary>
    public static class Stage1VegetationBuilder
    {
        public const string RootName = "Stage1 Vegetation - Numbered Flora";
        const string ScenePath = "Assets/Scenes/Stage1_Astra.unity";
        const string Models = "Assets/_Project/Models/Environment/Stage1";
        const string Prefabs = "Assets/_Project/Prefabs/Environment/Stage1/Vegetation";
        const string Materials = "Assets/_Project/Materials/Environment/Stage1/Vegetation";
        static readonly string[] Species = { "01-ribbon-grass", "02-starflower-moss", "03-curled-rosette",
            "04-mushroom-cluster", "05-fan-fern", "06-curled-bellflower", "09-lantern-pods" };
        static readonly float[] Heights = { .68f, .28f, .60f, .72f, 1.4f, 1.25f, 1.6f };

        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        static Bounds BoundsOf(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            Require(renderers.Length > 0, "No renderers: " + go.name);
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = path.Substring(0, path.LastIndexOf('/'));
            Folder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static GameObject CreatePlantPrefab(int index)
        {
            var folder = Models + "/" + Species[index];
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(Directory.GetFiles(folder, "*.fbx").Single().Replace('\\', '/'));
            Require(source != null, "Missing numbered model " + folder);
            var materialPath = Materials + "/M_" + Species[index] + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (!material)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { enableInstancing = true };
                var colorPath = Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
                    .Single(p => Path.GetFileNameWithoutExtension(p).EndsWith("_basecolor", StringComparison.OrdinalIgnoreCase));
                material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(colorPath.Replace('\\', '/')));
                material.SetFloat("_Metallic", 0f);
                material.SetFloat("_Smoothness", .23f);
                AssetDatabase.CreateAsset(material, materialPath);
            }
            Require(material.GetTexture("_BaseMap"), "Missing FBM base color " + folder);
            var root = new GameObject(Species[index]) { layer = GameLayers.Prop };
            var pivot = new GameObject("Visual").transform;
            pivot.SetParent(root.transform, false);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(source);
            model.transform.SetParent(pivot, false);
            foreach (var collider in model.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(collider);
            foreach (var renderer in model.GetComponentsInChildren<Renderer>())
                renderer.sharedMaterials = Enumerable.Repeat(material, renderer.sharedMaterials.Length).ToArray();
            var bounds = BoundsOf(root);
            model.transform.localScale *= Heights[index] / bounds.size.y;
            bounds = BoundsOf(root);
            model.transform.position -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            bounds = BoundsOf(root);
            var trigger = root.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = new Vector3(0f, Mathf.Max(bounds.size.y, .5f) * .5f, 0f);
            trigger.size = new Vector3(bounds.size.x * .78f, Mathf.Max(bounds.size.y, .5f), bounds.size.z * .78f);
            var rustle = root.AddComponent<PlantRustle>();
            var serialized = new SerializedObject(rustle);
            serialized.FindProperty("visual").objectReferenceValue = pivot;
            serialized.FindProperty("swayDegrees").floatValue = 25f;
            serialized.FindProperty("damping").floatValue = 3.5f;
            serialized.FindProperty("playerCooldown").floatValue = .22f;
            serialized.FindProperty("_movementSpeedMultiplier").floatValue = index >= 4 ? .45f : 1f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, Prefabs + "/PF_" + Species[index] + ".prefab");
            Object.DestroyImmediate(root);
            return prefab;
        }

        static GameObject CreateTreePrefab(string name, float height)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Environment/Stage1/PF_Stage1_" + name + ".prefab");
            Require(source, "Missing existing tree prefab " + name);
            var root = new GameObject(name);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(source);
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            model.transform.SetParent(root.transform, false);
            foreach (var collider in model.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(collider);
            foreach (var rustle in model.GetComponentsInChildren<PlantRustle>()) Object.DestroyImmediate(rustle);
            model.transform.localScale *= height / BoundsOf(root).size.y;
            var bounds = BoundsOf(root);
            model.transform.position -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            foreach (var mesh in model.GetComponentsInChildren<MeshFilter>())
            {
                var collider = mesh.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = mesh.sharedMesh;
                collider.convex = false;
            }
            foreach (var t in root.GetComponentsInChildren<Transform>()) t.gameObject.layer = GameLayers.Ground;
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, Prefabs + "/PF_Canopy_" + name + ".prefab");
            Object.DestroyImmediate(root);
            return prefab;
        }

        // Accept both the existing sculpted mesh and future TerrainCollider surfaces.
        static bool Ground(Collider[] surfaces, Vector3 point, out RaycastHit best)
        {
            best = default;
            bool found = false;
            var ray = new Ray(new Vector3(point.x, 60f, point.z), Vector3.down);
            foreach (var surface in surfaces)
                if (surface.Raycast(ray, out var hit, 100f) && (!found || hit.point.y > best.point.y))
                { best = hit; found = true; }
            return found && best.normal.y > .85f;
        }

        static bool Clear(Vector3 p, float radius, List<Bounds> exclusions)
        {
            foreach (var bounds in exclusions)
            {
                if (p.y > bounds.max.y + 2f || p.y + 4f < bounds.min.y) continue;
                var closest = bounds.ClosestPoint(new Vector3(p.x, bounds.center.y, p.z));
                if (new Vector2(closest.x - p.x, closest.z - p.z).sqrMagnitude < radius * radius) return false;
            }
            return true;
        }

        public static void ApplyIsolated()
        {
            Require(Application.isBatchMode && File.Exists("../ISOLATED_PART2_TRAIL"), "Isolated authoring project required.");
            var scene = EditorSceneManager.OpenScene(ScenePath);
            Require(!scene.GetRootGameObjects().Any(g => g.name == RootName), "Vegetation already exists; refusing duplicate placement.");
            var originals = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
            var surfaces = originals.Select(t => t.GetComponent<Collider>())
                .Where(c => c && c.enabled && (c.name.Contains("sculpted ground") || c is TerrainCollider)).ToArray();
            Require(surfaces.Length > 0, "No authored ground.");
            var exclusions = new List<Bounds>();
            foreach (var t in originals)
            {
                var collider = t.GetComponent<Collider>();
                if (collider && collider.enabled && collider.gameObject.activeInHierarchy && !surfaces.Contains(collider) && !collider.isTrigger)
                    exclusions.Add(collider.bounds);
                if (t.name.StartsWith("Astra Floor Guidance") || t.name.Contains("Player Spawn"))
                    exclusions.Add(new Bounds(t.position, new Vector3(3.5f, 5f, 3.5f)));
                if (t.GetComponent<Metagame.Gameplay.Parts.SpaceshipPart>())
                    exclusions.Add(new Bounds(t.position, new Vector3(8f, 8f, 8f)));
            }
            Folder(Prefabs); Folder(Materials);
            var plants = Enumerable.Range(0, Species.Length).Select(CreatePlantPrefab).ToArray();
            var trees = new[] { CreateTreePrefab("MushroomTree", 5.4f) };
            var root = new GameObject(RootName).transform;
            var random = new System.Random(9172026);
            float Range(float a, float b) => a + (float)random.NextDouble() * (b-a);
            var accepted = new List<Vector3>();
            var treePositions = new List<Vector3>();
            int count = 0;
            for (int x = -44; x <= 44; x += 8)
            for (int z = -44; z <= 44; z += 8)
            {
                var center = new Vector3(x + Range(-2.5f,2.5f), 0f, z + Range(-2.5f,2.5f));
                var group = new GameObject($"Grove {x:+00;-00;00} {z:+00;-00;00}").transform;
                group.SetParent(root, false);
                for (int j=0; j<24; j++)
                {
                    float angle=Range(0f,Mathf.PI*2f), distance=Mathf.Sqrt(Range(0f,1f))*4.5f;
                    var p=center+new Vector3(Mathf.Cos(angle)*distance,0f,Mathf.Sin(angle)*distance);
                    if (Mathf.Abs(p.x)>49f || Mathf.Abs(p.z)>46f || !Ground(surfaces,p,out var hit)) continue;
                    p=hit.point;
                    int species = random.Next(7);
                    float spacing = species >= 4 ? 1f : .65f;
                    if (!Clear(p, species >= 4 ? 1.8f : 1.0f, exclusions) || accepted.Any(v=>(v-p).sqrMagnitude<spacing*spacing)) continue;
                    var instance=(GameObject)PrefabUtility.InstantiatePrefab(plants[species],scene);
                    instance.transform.SetParent(group,false);
                    instance.transform.position=p-Vector3.up*.025f;
                    instance.transform.rotation=Quaternion.Euler(0f,Range(0f,360f),0f);
                    instance.transform.localScale=Vector3.one*Range(.86f,1.18f);
                    accepted.Add(p); count++;
                }
                if (group.childCount == 0) Object.DestroyImmediate(group.gameObject);
                if (random.NextDouble()>.8 || !Ground(surfaces,center,out var treeHit)) continue;
                var tp=treeHit.point;
                if (!Clear(tp,2.4f,exclusions) || treePositions.Any(p=>(p-tp).sqrMagnitude<49f)) continue;
                var tree=(GameObject)PrefabUtility.InstantiatePrefab(trees[random.Next(trees.Length)],scene);
                tree.transform.SetParent(root,false); tree.transform.position=tp-Vector3.up*.025f;
                tree.transform.rotation=Quaternion.Euler(0f,Range(0f,360f),0f);
                tree.transform.localScale=Vector3.one*Range(.85f,1.2f);
                treePositions.Add(tp);
            }
            Physics.SyncTransforms();
            Require(count > 500 && treePositions.Count > 10, $"Insufficient flora: {count}, trees: {treePositions.Count}");
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Validate();
        }

        public static void Validate()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath) scene = EditorSceneManager.OpenScene(ScenePath);
            var root = scene.GetRootGameObjects().Single(g => g.name == RootName);
            var plants = root.GetComponentsInChildren<PlantRustle>();
            Require(plants.Length > 500, "Missing plant instances");
            foreach (var plant in plants)
            {
                var colliders = plant.GetComponentsInChildren<Collider>();
                Require(colliders.Length == 1 && colliders[0].isTrigger, "Plant blocks movement: " + plant.name);
                Require(plant.Visual != null, "Missing sway pivot");
                Require(plant.gameObject.layer == GameLayers.Prop, "Incorrect plant layer");
            }
            int missing = root.GetComponentsInChildren<Transform>().Sum(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
            Require(missing==0,"Missing scripts");
            var meshes=root.GetComponentsInChildren<MeshCollider>();
            Require(meshes.Length>10 && meshes.All(c=>!c.isTrigger&&c.sharedMesh),"Missing tree colliders");
            var all=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
            var ground=all.Select(t=>t.GetComponent<Collider>()).Where(c=>c&&c.enabled&&(c.name.Contains("sculpted ground")||c is TerrainCollider)).ToArray();
            int grounded=0, routeSamples=0;
            foreach(var plant in plants)
            {
                Require(Ground(ground,plant.transform.position,out var hit),"Plant outside ground");
                Require(Mathf.Abs(plant.transform.position.y-hit.point.y+.025f)<.03f,"Floating plant");
                grounded++;
            }
            Physics.SyncTransforms();
            foreach(var marker in all.Where(t=>t.name.StartsWith("Astra Floor Guidance")||t.name.Contains("Player Spawn")))
            {
                var p=marker.position;
                if(Ground(ground,p,out var hit)&&p.y<hit.point.y+1f)p=hit.point;
                var overlaps=Physics.OverlapCapsule(p+Vector3.up*.3f,p+Vector3.up*1.3f,.4f,GameLayers.GroundMask,QueryTriggerInteraction.Ignore);
                Require(!overlaps.Any(c=>c.transform.IsChildOf(root.transform)),"Tree blocks route: "+marker.name);
                routeSamples++;
            }
            foreach (var r in root.GetComponentsInChildren<Renderer>())
                Require(r.sharedMaterials.All(m=>m&&m.shader&&m.GetTexture("_BaseMap")),"Missing textured material: "+r.name);
            File.WriteAllText("../vegetation-validation.txt", $"PASS\nplants={plants.Length}\nslowPlants={plants.Count(p=>p.MovementSpeedMultiplier<1f)}\ntreeMeshColliders={meshes.Length}\nmissingScripts={missing}\ngrounded={grounded}\nrouteClearanceSamples={routeSamples}\n");
            Debug.Log("STAGE1_VEGETATION_PASS");
        }

        [MenuItem("Tools/Not This Planet/Vegetation/Resnap vegetation to ground")]
        public static void Resnap()
        {
            var scene=SceneManager.GetActiveScene();
            var root=scene.GetRootGameObjects().SingleOrDefault(g=>g.name==RootName);
            Require(root,"Open the authored Stage1 vegetation scene first.");
            var surfaces=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Collider>())
                .Where(c=>c.enabled&&(c is TerrainCollider||c.name.Contains("sculpted ground"))).ToArray();
            var items=root.GetComponentsInChildren<PlantRustle>().Select(p=>p.transform)
                .Concat(root.transform.Cast<Transform>().Where(t=>t.name.StartsWith("MushroomTree")||t.name.StartsWith("UmbrellaTree")));
            foreach(var item in items)
            {
                if(!Ground(surfaces,item.position,out var hit)) continue;
                Undo.RecordObject(item,"Resnap vegetation");
                item.position=new Vector3(item.position.x,hit.point.y-.025f,item.position.z);
                PrefabUtility.RecordPrefabInstancePropertyModifications(item);
            }
            EditorSceneManager.MarkSceneDirty(scene);
        }
    }
}
