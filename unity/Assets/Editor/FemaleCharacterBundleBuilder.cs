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
    private static readonly Dictionary<string, float> WristFractions = new();

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

            SavePrefab(model, name, false, 0f);
            SavePrefab(model, $"{name}_head", true, WristFractions.TryGetValue(name, out var wf) ? wf : 0f);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        BuildPipeline.BuildAssetBundles(OutDir, BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.StandaloneWindows64);
        var output = Path.GetFullPath(Path.Combine(OutDir, BundleName));
        Debug.Log($"Built {output}");
        if (!Application.isBatchMode)
            EditorUtility.RevealInFinder(output);
    }

    private static void SavePrefab(GameObject model, string prefabName, bool headOnly, float wristFraction)
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
                var mesh = HeadMesh(smr, wristFraction);
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

    private static Mesh HeadMesh(SkinnedMeshRenderer smr, float wristFraction)
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

        var binds = src.bindposes;
        var vertices = src.vertices;
        var wristIndex = new int[bones.Length];
        var wristReach = new float[bones.Length];
        for (int i = 0; i < bones.Length; i++)
        {
            wristIndex[i] = -1;
            if (wristFraction <= 0f || bones[i] == null || !StripPrefix(bones[i].name).Contains("ForeArm") || i >= binds.Length)
                continue;
            var handName = StripPrefix(bones[i].name).Replace("ForeArm", "Hand");
            for (int j = 0; j < bones.Length; j++)
            {
                if (bones[j] != null && j < binds.Length && StripPrefix(bones[j].name) == handName)
                {
                    wristIndex[i] = j;
                    var elbow = binds[i].inverse.GetColumn(3);
                    var wrist = binds[j].inverse.GetColumn(3);
                    wristReach[i] = Vector3.Distance(elbow, wrist) * wristFraction;
                    break;
                }
            }
        }

        var keepVertex = new bool[weights.Length];
        for (int v = 0; v < weights.Length; v++)
        {
            var w = weights[v];
            var best = w.boneIndex0;
            var bestW = w.weight0;
            if (w.weight1 > bestW) { best = w.boneIndex1; bestW = w.weight1; }
            if (w.weight2 > bestW) { best = w.boneIndex2; bestW = w.weight2; }
            if (w.weight3 > bestW) { best = w.boneIndex3; }
            if (best < 0 || best >= keepBone.Length)
                continue;
            if (keepBone[best])
            {
                keepVertex[v] = true;
            }
            else if (wristIndex[best] >= 0)
            {
                var wrist = (Vector3)binds[wristIndex[best]].inverse.GetColumn(3);
                keepVertex[v] = Vector3.Distance(vertices[v], wrist) <= wristReach[best];
            }
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

    private static string StripPrefix(string boneName)
    {
        var i = boneName.LastIndexOf(':');
        return i >= 0 ? boneName.Substring(i + 1) : boneName;
    }

    private static bool KeepBone(string boneName)
    {
        var n = StripPrefix(boneName);
        var lower = n.ToLowerInvariant();
        return n == "Neck" || n == "Head" || n == "HeadTop_End" || lower.EndsWith("eye") || n.StartsWith("Bow") || n.Contains("Hand");
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
