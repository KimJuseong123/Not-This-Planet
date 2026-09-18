using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Explicit isolated batch entry point. No callbacks or live Editor automation.
public static class Stage1Part2TrailIntegration
{
    const string ScenePath = "Assets/Scenes/Stage1_Astra.unity";
    const string Models = "Assets/_Project/Models/LongWalkingTrail/";
    static readonly string[] Folders = { "01-long-walking-trail-0", "02-long-walking-trail-1", "03-long-walking-trail-2", "04-long-walking-trail-3", "05-long-walking-trail-4", "06-hill-deck-transition" };
    static readonly string[] ModelsNames = { "SM_Astra_Trail0", "SM_Astra_Trail1", "SM_Astra_Trail2", "SM_Astra_Trail3", "SM_Astra_Trail4", "SM_Astra_HillDeckTransition" };
    [Serializable] class Entry { public string name, model; public Vector3 position, size, visualSize; public Quaternion rotation; public int floorSamples; }
    [Serializable] class Report { public string unityVersion; public int missingScriptsBefore, missingScriptsAfter; public List<Entry> objects = new List<Entry>(); public string status; }
    static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    public static void Verify()
    {
        Require(Application.isBatchMode && File.Exists("../ISOLATED_PART2_TRAIL"), "Isolated batch only.");
        var scene = EditorSceneManager.OpenScene(ScenePath);
        var all = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
        var roots = all.Where(t => t.name.StartsWith("Astra Part2 - SM_Astra_")).ToArray();
        Require(roots.Length == 6, "Expected six replacement roots.");
        Require(!all.Any(t => t.name.StartsWith("Long walking trail ") || t.name == "Wayfinding hill deck transition"), "Stale ramps.");
        int samples = 0;
        Physics.SyncTransforms();
        foreach (var t in roots)
        {
            var box = t.GetComponent<BoxCollider>();
            Require(box && box.enabled && !box.isTrigger && t.gameObject.layer == 8, "Invalid collision setup.");
            Require(t.GetComponentsInChildren<Collider>().Length == 1, "Duplicate collision.");
            foreach (var mf in t.GetComponentsInChildren<MeshFilter>()) Require(mf.sharedMesh, "Missing mesh.");
            foreach (var mr in t.GetComponentsInChildren<MeshRenderer>())
                foreach (var mat in mr.sharedMaterials) Require(mat && mat.shader && mat.GetTexture("_BaseMap"), "Missing material/texture.");
            for (int n = 0; n <= 40; n++) for (int w = -1; w <= 1; w++)
            {
                var p = t.TransformPoint(new Vector3(w * (box.size.x / 2 - .4f), box.size.y / 2, Mathf.Lerp(-box.size.z / 2 + .03f, box.size.z / 2 - .03f, n / 40f)));
                Require(box.Raycast(new Ray(p + Vector3.up * 2, Vector3.down), out var hit, 3) && Vector3.Distance(hit.point, p) < .002f, "Reloaded floor mismatch.");
                samples++;
            }
        }
        int missing = all.Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
        File.WriteAllText("../reload-validation.txt", "PASS: saved scene loaded; 6 models, 6 solid Ground-layer colliders; " + samples + " floor rays; missing scripts=" + missing);
    }
    public static void Apply()
    {
        Require(Application.isBatchMode, "Run only in an isolated batch project.");
        Require(File.Exists("../ISOLATED_PART2_TRAIL"), "Missing isolation marker.");
        var scene = EditorSceneManager.OpenScene(ScenePath);
        var all = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
        var report = new Report { unityVersion = Application.unityVersion, missingScriptsBefore = all.Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)) };
        var part = all.Single(t => t.name == "Part_2");
        Vector3 partPos = part.position;
        var created = new List<GameObject>();
        for (int i = 0; i < 6; i++)
        {
            string oldName = i < 5 ? "Long walking trail " + i : "Wayfinding hill deck transition";
            var old = all.Single(t => t && t.name == oldName);
            Require(old.childCount == 0, "Unexpected children on " + oldName);
            Require(old.GetComponents<MonoBehaviour>().Length == 0, "Unexpected gameplay components on " + oldName);
            var oldCollider = old.GetComponent<BoxCollider>();
            Require(oldCollider && oldCollider.enabled && !oldCollider.isTrigger, "Expected solid box " + oldName);
            var size = Vector3.Scale(oldCollider.size, old.lossyScale);
            string path = Models + Folders[i] + "/" + ModelsNames[i] + ".fbx";
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Require(asset, "Missing FBX " + path);
            var root = new GameObject("Astra Part2 - " + ModelsNames[i]);
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
            visual.transform.SetParent(root.transform, false);
            var renderers = visual.GetComponentsInChildren<MeshRenderer>();
            Require(renderers.Length > 0, "No renderers " + path);
            var bounds = renderers[0].bounds;
            foreach (var r in renderers.Skip(1)) bounds.Encapsulate(r.bounds);
            Require(Mathf.Abs(bounds.size.x - size.x) < .02f && Mathf.Abs(bounds.size.y - size.y) < .02f && Mathf.Abs(bounds.size.z - size.z) < .02f,
                "FBX axis/scale mismatch " + path + " actual=" + bounds.size + " expected=" + size);
            Require(bounds.center.magnitude < .015f, "Unexpected model pivot " + path);
            var materialPath = AssetDatabase.FindAssets("t:Material", new[] { Models + Folders[i] }).Select(AssetDatabase.GUIDToAssetPath).Single();
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            Require(material && material.shader && material.shader.name == "Universal Render Pipeline/Lit", "Missing URP material " + path);
            Require(material.GetTexture("_BaseMap"), "Missing base texture " + path);
            foreach (var r in renderers) r.sharedMaterials = Enumerable.Repeat(material, r.sharedMaterials.Length).ToArray();
            root.transform.SetParent(old.parent, false);
            root.transform.position = old.position;
            root.transform.rotation = old.rotation;
            root.transform.SetSiblingIndex(old.GetSiblingIndex());
            var collider = root.AddComponent<BoxCollider>();
            collider.size = size;
            collider.center = Vector3.Scale(oldCollider.center, old.lossyScale);
            collider.sharedMaterial = oldCollider.sharedMaterial;
            collider.isTrigger = false;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                t.gameObject.layer = LayerMask.NameToLayer("Ground");
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, GameObjectUtility.GetStaticEditorFlags(old.gameObject));
            }
            Object.DestroyImmediate(old.gameObject);
            Physics.SyncTransforms();
            int samples = 0;
            for (int n = 0; n <= 40; n++) for (int w = -1; w <= 1; w++)
            {
                var local = new Vector3(w * (size.x / 2 - .4f), size.y / 2, Mathf.Lerp(-size.z / 2 + .03f, size.z / 2 - .03f, n / 40f));
                var point = root.transform.TransformPoint(local);
                Require(collider.Raycast(new Ray(point + Vector3.up * 2, Vector3.down), out var hit, 3), "Floor gap " + root.name);
                Require(Vector3.Distance(hit.point, point) < .002f, "Floor alignment " + root.name);
                samples++;
            }
            report.objects.Add(new Entry { name = root.name, model = path, position = root.transform.position, rotation = root.transform.rotation, size = size, visualSize = bounds.size, floorSamples = samples });
            created.Add(root);
        }
        Require(part.position == partPos, "Part2 position changed.");
        var after = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
        Require(!after.Any(t => t.name.StartsWith("Long walking trail ") || t.name == "Wayfinding hill deck transition"), "Old ramp remains.");
        report.missingScriptsAfter = after.Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
        Require(report.missingScriptsAfter == report.missingScriptsBefore, "Missing script regression.");
        Require(created.Sum(g => g.GetComponentsInChildren<Collider>().Length) == 6, "Duplicate colliders.");
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        report.status = "PASS: 6 FBX replacements; 738 floor rays; identical original collision extents; Part2 preserved";
        File.WriteAllText("../validation.json", JsonUtility.ToJson(report, true));
        Debug.Log(report.status);
    }
}
