using System.IO;
using UnityEditor;
using UnityEngine;

public static class FemaleCharacterBundleBuilder
{
    private const string ModelsDir = "Assets/Models";
    private const string PrefabDir = "Assets/Prefabs";
    private const string TexDir = "Assets/Textures";
    private const string OutDir = "Bundles";
    private const string BundleName = "femalecharacter";

    [MenuItem("FemaleCharacter/Build Bundle")]
    public static void Build()
    {
        Directory.CreateDirectory(PrefabDir);
        Directory.CreateDirectory(TexDir);
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
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.SaveAndReimport();

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            instance.name = name;
            foreach (var animator in instance.GetComponentsInChildren<Animator>(true))
                Object.DestroyImmediate(animator);

            var prefabPath = $"{PrefabDir}/{name}.prefab";
            PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
            Object.DestroyImmediate(instance);

            AssetImporter.GetAtPath(prefabPath).assetBundleName = BundleName;
            Debug.Log($"Prepared {prefabPath}");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        BuildPipeline.BuildAssetBundles(OutDir, BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.StandaloneWindows64);
        var output = Path.GetFullPath(Path.Combine(OutDir, BundleName));
        Debug.Log($"Built {output}");
        if (!Application.isBatchMode)
            EditorUtility.RevealInFinder(output);
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
