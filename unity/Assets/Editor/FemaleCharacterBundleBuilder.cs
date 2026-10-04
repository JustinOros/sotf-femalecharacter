using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class FemaleCharacterBundleBuilder
{
    private const string ModelsDir = "Assets/Models";
    private const string PrefabDir = "Assets/Prefabs";
    private const string TexDir = "Assets/Textures";
    private const string MeshDir = "Assets/Meshes";
    private const string OutDir = "Bundles";
    private const string BundleName = "femalecharacter";

    [MenuItem("FemaleCharacter/Build Bundle")]
    public static void Build()
    {
        Directory.CreateDirectory(PrefabDir);
        Directory.CreateDirectory(TexDir);
        Directory.CreateDirectory(MeshDir);
        Directory.CreateDirectory(OutDir);

        foreach (var file in Directory.GetFiles(ModelsDir, "*.fbx"))
        {
            var path = file.Replace('\\', '/');
            var name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            if (importer == null)
            {
                Debug.LogError($"No ModelImporter for {path}");
                continue;
            }

            var texOut = $"{TexDir}/{name}";
            Directory.CreateDirectory(texOut);
            importer.ExtractTextures(texOut);
            AssetDatabase.Refresh();
            ConfigureTextures(texOut);

            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
            importer.importAnimation = false;
            importer.isReadable = true;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.SaveAndReimport();

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            SavePrefab(model, name, false);
            SavePrefab(model, $"{name}_head", true);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        BuildPipeline.BuildAssetBundles(OutDir, BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.StandaloneWindows64);
        var output = Path.GetFullPath(Path.Combine(OutDir, BundleName));
        Debug.Log($"Built {output}");
        if (!Application.isBatchMode)
            EditorUtility.RevealInFinder(output);
    }

    private static void SavePrefab(GameObject model, string prefabName, bool headOnly)
    {
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
        PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        instance.name = prefabName;
        foreach (var animator in instance.GetComponentsInChildren<Animator>(true))
            Object.DestroyImmediate(animator);

        if (headOnly)
        {
            foreach (var smr in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = HeadMesh(smr);
                if (mesh == null)
                {
                    Object.DestroyImmediate(smr.gameObject);
                    continue;
                }
                var meshPath = $"{MeshDir}/{prefabName}_{Sanitize(smr.name)}.asset";
                AssetDatabase.DeleteAsset(meshPath);
                AssetDatabase.CreateAsset(mesh, meshPath);
                smr.sharedMesh = mesh;
            }
        }

        var prefabPath = $"{PrefabDir}/{prefabName}.prefab";
        PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
        Object.DestroyImmediate(instance);
        AssetImporter.GetAtPath(prefabPath).assetBundleName = BundleName;
        Debug.Log($"Prepared {prefabPath}");
    }

    private static Mesh HeadMesh(SkinnedMeshRenderer smr)
    {
        var src = smr.sharedMesh;
        var bones = smr.bones;
        if (src == null || bones == null || bones.Length == 0)
            return null;

        var keepBone = new bool[bones.Length];
        for (int i = 0; i < bones.Length; i++)
            keepBone[i] = bones[i] != null && KeepBone(bones[i].name);

        var weights = src.boneWeights;
        if (weights == null || weights.Length != src.vertexCount)
            return null;

        var keepVertex = new bool[weights.Length];
        for (int v = 0; v < weights.Length; v++)
        {
            var w = weights[v];
            var best = w.boneIndex0;
            var bestW = w.weight0;
            if (w.weight1 > bestW) { best = w.boneIndex1; bestW = w.weight1; }
            if (w.weight2 > bestW) { best = w.boneIndex2; bestW = w.weight2; }
            if (w.weight3 > bestW) { best = w.boneIndex3; }
            keepVertex[v] = best >= 0 && best < keepBone.Length && keepBone[best];
        }

        var mesh = Object.Instantiate(src);
        mesh.name = $"{src.name}_head";
        int total = 0;
        for (int sub = 0; sub < src.subMeshCount; sub++)
        {
            var tris = src.GetTriangles(sub);
            var kept = new List<int>(tris.Length);
            for (int t = 0; t + 2 < tris.Length; t += 3)
            {
                if (keepVertex[tris[t]] && keepVertex[tris[t + 1]] && keepVertex[tris[t + 2]])
                {
                    kept.Add(tris[t]);
                    kept.Add(tris[t + 1]);
                    kept.Add(tris[t + 2]);
                }
            }
            mesh.SetTriangles(kept, sub);
            total += kept.Count;
        }

        if (total == 0)
        {
            Object.DestroyImmediate(mesh);
            return null;
        }
        mesh.RecalculateBounds();
        return mesh;
    }

    private static bool KeepBone(string boneName)
    {
        var i = boneName.LastIndexOf(':');
        var n = i >= 0 ? boneName.Substring(i + 1) : boneName;
        var lower = n.ToLowerInvariant();
        return n == "Neck" || n == "Head" || n == "HeadTop_End" || lower.EndsWith("eye") || n.StartsWith("Bow") || n.Contains("Hand") || n.Contains("ForeArm");
    }

    private static string Sanitize(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            s = s.Replace(c, '_');
        return s.Replace(':', '_').Replace(' ', '_');
    }

    private static void ConfigureTextures(string folder)
    {
        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { folder }))
        {
            var texPath = AssetDatabase.GUIDToAssetPath(guid);
            var ti = AssetImporter.GetAtPath(texPath) as TextureImporter;
            if (ti == null)
                continue;
            var lower = Path.GetFileNameWithoutExtension(texPath).ToLowerInvariant();
            ti.maxTextureSize = 2048;
            if (lower.Contains("normal"))
            {
                ti.textureType = TextureImporterType.NormalMap;
            }
            else
            {
                ti.textureType = TextureImporterType.Default;
                var data = lower.Contains("gloss") || lower.Contains("specular") || lower.Contains("rough") || lower.Contains("metal");
                ti.sRGBTexture = !data;
                ti.alphaIsTransparency = !data;
            }
            ti.SaveAndReimport();
        }
    }
}
