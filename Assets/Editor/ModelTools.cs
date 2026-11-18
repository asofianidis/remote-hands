using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RH.EditorTools
{
    /// <summary>
    /// Import settings for the Blender-generated models in Assets/Resources/Models: real-world metres,
    /// no cameras, lights, animation or colliders. Materials are swapped for the game's own at runtime.
    /// </summary>
    public class ModelImport : AssetPostprocessor
    {
        void OnPreprocessModel()
        {
            if (!assetPath.Replace('\\', '/').Contains("Resources/Models/")) return;
            var mi = (ModelImporter)assetImporter;
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.bakeAxisConversion = true;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importAnimation = false;
            mi.animationType = ModelImporterAnimationType.None;
            mi.addCollider = false;
            mi.isReadable = false;
            mi.importNormals = ModelImporterNormals.Import;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
        }
    }

    public static class ModelTools
    {
        /// <summary>Batch-mode report: every model's bounds and its children's centres, to check axes and sizes.</summary>
        public static void Report()
        {
            AssetDatabase.Refresh();
            foreach (var path in AssetDatabase.FindAssets("t:Model", new[] { "Assets/Resources/Models" }).Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p))
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var inst = Object.Instantiate(go);
                var rs = inst.GetComponentsInChildren<Renderer>();
                var b = rs.Length > 0 ? rs[0].bounds : new Bounds();
                foreach (var r in rs) b.Encapsulate(r.bounds);
                Debug.Log($"[model] {System.IO.Path.GetFileNameWithoutExtension(path)} min {b.min:F3} max {b.max:F3} children {inst.transform.childCount}");
                foreach (var r in rs)
                    Debug.Log($"[model]   {r.name} centre {r.bounds.center:F3} size {r.bounds.size:F3} mats {string.Join(",", r.sharedMaterials.Select(m => m != null ? m.name : "null"))}");
                Object.DestroyImmediate(inst);
            }
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
