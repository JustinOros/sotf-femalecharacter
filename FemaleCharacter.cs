using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Il2CppInterop.Runtime;
using RedLoader;
using RedLoader.Utils;
using Sons.Wearable.Race;
using SonsSdk;
using SonsSdk.Attributes;
using TheForest.Utils;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;

namespace FemaleCharacter;

public class FemaleCharacter : SonsMod
{
    private const string BundleFile = "femalecharacter";

    private static readonly Dictionary<PlayerRace.Race, string> RaceModels = new()
    {
        { PlayerRace.Race.Latin, "alyssa" },
        { PlayerRace.Race.BlackB, "rachel" }
    };

    private static readonly List<string[]> Chains = BuildChains();
    private static readonly Dictionary<string, string> AimChild = BuildAimChildren();
    private static readonly Dictionary<string, string> ChainParent = BuildChainParents();
    private static readonly string[] DriveOrder = Chains.SelectMany(c => c).ToArray();

    private static AssetBundle _bundle;
    private static bool _bundleTried;
    private static readonly Dictionary<string, GameObject> Prefabs = new();
    private static readonly Dictionary<string, ModelRig> Rigs = new();
    private static Dictionary<string, Rest> _playerRest;

    private static Material _litBase;
    private static Material _hairBase;
    private static Texture2D _neutralMask;
    private static readonly Dictionary<int, Material> Converted = new();

    private static readonly Dictionary<int, Entry> Entries = new();
    private static Entry _preview;
    private static float _nextScan;
    private static UnityAction _beforeRender;

    public FemaleCharacter()
    {
        OnUpdateCallback = OnUpdate;
        OnLateUpdateCallback = OnLateUpdate;
    }

    protected override void OnSdkInitialized()
    {
        try
        {
            _beforeRender = DelegateSupport.ConvertDelegate<UnityAction>(new Action(OnLateUpdate));
            Application.add_onBeforeRender(_beforeRender);
        }
        catch (Exception e)
        {
            RLog.Warning($"FemaleCharacter: could not hook onBeforeRender, using LateUpdate only: {e.Message}");
        }
        RLog.Msg("FemaleCharacter loaded. Latin shows as Alyssa, BlackB shows as Rachel. Command: femalecharacter [status|preview alyssa|preview rachel|preview off]");
    }

    [DebugCommand("femalecharacter")]
    private static void Command(string args)
    {
        try
        {
            var parts = (args ?? string.Empty).Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 1 && parts[0] == "preview")
            {
                var model = parts.Length > 1 ? parts[1] : "alyssa";
                SetPreview(model);
                return;
            }
            Say($"FemaleCharacter: bundle={(_bundle ? "loaded" : "missing")} rest={(_playerRest != null ? _playerRest.Count : 0)} bones, {Entries.Count} remote players shown as female, preview={(_preview != null ? _preview.Model : "off")}");
        }
        catch (Exception e)
        {
            RLog.Error($"femalecharacter command failed: {e}");
        }
    }

    private static List<string[]> BuildChains()
    {
        var chains = new List<string[]> { new[] { "Hips", "Spine", "Spine1", "Spine2", "Neck", "Head" } };
        foreach (var s in new[] { "Left", "Right" })
        {
            chains.Add(new[] { $"{s}Shoulder", $"{s}Arm", $"{s}ForeArm", $"{s}Hand" });
            chains.Add(new[] { $"{s}UpLeg", $"{s}Leg", $"{s}Foot", $"{s}ToeBase" });
            foreach (var f in new[] { "Thumb", "Index", "Middle", "Ring", "Pinky" })
                chains.Add(new[] { $"{s}Hand{f}1", $"{s}Hand{f}2", $"{s}Hand{f}3" });
        }
        return chains;
    }

    private static Dictionary<string, string> BuildAimChildren()
    {
        var map = new Dictionary<string, string>();
        foreach (var chain in BuildChains())
        {
            for (int i = 0; i + 1 < chain.Length; i++)
                map[chain[i]] = chain[i + 1];
        }
        map["LeftHand"] = "LeftHandMiddle1";
        map["RightHand"] = "RightHandMiddle1";
        return map;
    }

    private static Dictionary<string, string> BuildChainParents()
    {
        var map = new Dictionary<string, string>();
        foreach (var chain in BuildChains())
        {
            for (int i = 1; i < chain.Length; i++)
                map[chain[i]] = chain[i - 1];
        }
        return map;
    }

    private static void OnUpdate()
    {
        if (Time.unscaledTime < _nextScan)
            return;
        _nextScan = Time.unscaledTime + 1f;
        try
        {
            Scan();
        }
        catch (Exception e)
        {
            RLog.Error($"FemaleCharacter scan failed: {e.Message}");
        }
    }

    private static void OnLateUpdate()
    {
        try
        {
            foreach (var entry in Entries.Values)
                Drive(entry, false);
            if (_preview != null)
                Drive(_preview, true);
        }
        catch (Exception e)
        {
            RLog.Error($"FemaleCharacter drive failed: {e.Message}");
        }
    }

    private static bool EnsureReady()
    {
        if (!LocalPlayer.GameObject || !LocalPlayer.RaceSystem)
            return false;

        if (!_bundle && !_bundleTried)
        {
            _bundleTried = true;
            var path = Path.Combine(LoaderEnvironment.ModsDirectory, "FemaleCharacter", BundleFile);
            if (!File.Exists(path))
            {
                RLog.Error($"FemaleCharacter: bundle not found at {path}");
                return false;
            }
            _bundle = AssetBundle.LoadFromFile(path);
            if (!_bundle)
            {
                RLog.Error($"FemaleCharacter: failed to load bundle {path}");
                return false;
            }
            RLog.Msg($"FemaleCharacter: loaded bundle, assets: {string.Join(", ", _bundle.GetAllAssetNames())}");
        }
        if (!_bundle)
            return false;

        if (_playerRest == null)
            _playerRest = BuildPlayerRest(LocalPlayer.RaceSystem);
        return _playerRest != null;
    }

    private static void Scan()
    {
        if (!EnsureReady())
            return;

        var seen = new HashSet<int>();
        foreach (var race in UnityEngine.Object.FindObjectsOfType<PlayerRaceSystem>())
        {
            if (!race || race._isRobbyPlayer || race == LocalPlayer.RaceSystem)
                continue;
            bool isLocal;
            try
            {
                isLocal = race.IsLocalPlayer();
            }
            catch
            {
                isLocal = false;
            }
            if (isLocal)
                continue;

            var id = race.GetInstanceID();
            RaceModels.TryGetValue(race.CurrentRace, out var model);

            if (Entries.TryGetValue(id, out var existing))
            {
                if (existing.Model == model && existing.Female)
                {
                    seen.Add(id);
                    continue;
                }
                Remove(existing);
                Entries.Remove(id);
            }

            if (model == null)
                continue;

            var entry = Create(race, model);
            if (entry == null)
                continue;
            Entries[id] = entry;
            seen.Add(id);
            RLog.Msg($"FemaleCharacter: showing {race.transform.root.name} as {model}");
        }

        foreach (var id in Entries.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            Remove(Entries[id]);
            Entries.Remove(id);
        }
    }

    private static void SetPreview(string model)
    {
        if (_preview != null)
        {
            Remove(_preview);
            _preview = null;
        }
        if (model == "off")
        {
            Say("FemaleCharacter preview off");
            return;
        }
        if (!EnsureReady())
        {
            Say("FemaleCharacter: not ready, check the log");
            return;
        }
        _preview = Create(LocalPlayer.RaceSystem, model, false);
        Say(_preview != null ? $"FemaleCharacter preview: {model}" : $"FemaleCharacter: could not create {model}");
    }

    private static Entry Create(PlayerRaceSystem race, string model, bool hideMale = true)
    {
        var prefab = GetPrefab(model);
        if (!prefab)
            return null;

        var frame = race.transform.parent ? race.transform.parent : race.transform;
        var root = race._animationRoot ? race._animationRoot : frame.Find("PlayerAnimator/Root");
        var hips = root ? FindDeep(root, "Hips") : null;
        if (!hips)
        {
            RLog.Warning($"FemaleCharacter: no Hips on {frame.name}");
            return null;
        }

        var pBones = new Dictionary<string, Transform>();
        foreach (var t in hips.GetComponentsInChildren<Transform>(true))
            if (!pBones.ContainsKey(t.name))
                pBones[t.name] = t;

        var female = UnityEngine.Object.Instantiate(prefab);
        female.name = $"FemaleCharacter_{model}";
        var rig = GetRig(model, female);
        if (rig == null)
        {
            UnityEngine.Object.Destroy(female);
            return null;
        }
        female.transform.localScale = Vector3.one;
        var fBones = BoneMap(female.transform);
        var fLeg = LiveLegLength(fBones);
        var pLeg = LiveLegLength(pBones);
        var scale = fLeg > 0.01f && pLeg > 0.01f ? pLeg / fLeg : 1f;
        female.transform.localScale = Vector3.one * scale;
        RLog.Msg($"FemaleCharacter: {model} leg {fLeg:F3} player leg {pLeg:F3} scale {scale:F3}");
        var entry = new Entry { Model = model, Race = race, Frame = frame, Female = female };
        foreach (var name in DriveOrder)
        {
            if (name.Contains("Hand"))
                continue;
            if (!rig.Offsets.TryGetValue(name, out var offset))
                continue;
            if (!fBones.TryGetValue(name, out var f) || !pBones.TryGetValue(name, out var p))
                continue;
            entry.Drive.Add(new Link { Female = f, Player = p, Offset = offset });
            if (name == "Hips")
            {
                entry.FemaleHips = f;
                entry.PlayerHips = p;
            }
        }
        if (!entry.FemaleHips)
        {
            UnityEngine.Object.Destroy(female);
            return null;
        }

        var layer = LayerMask.NameToLayer("Player");
        foreach (var r in female.GetComponentsInChildren<Renderer>(true))
        {
            if (layer >= 0)
                r.gameObject.layer = layer;
            r.shadowCastingMode = ShadowCastingMode.On;
            var smr = r.TryCast<SkinnedMeshRenderer>();
            if (smr)
                smr.updateWhenOffscreen = true;
        }

        if (hideMale)
        {
            var targets = new List<Transform> { race.transform };
            var clothing = frame.Find("ClothingSystem");
            if (clothing)
                targets.Add(clothing);
            foreach (var t in targets)
            {
                foreach (var r in t.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    entry.Hidden.Add(r);
                    entry.HiddenWasEnabled.Add(r.enabled);
                }
            }
        }

        return entry;
    }

    private static void Remove(Entry entry)
    {
        for (int i = 0; i < entry.Hidden.Count; i++)
        {
            var r = entry.Hidden[i];
            if (r)
                r.enabled = entry.HiddenWasEnabled[i];
        }
        if (entry.Female)
            UnityEngine.Object.Destroy(entry.Female);
        entry.Female = null;
    }

    private static void Drive(Entry entry, bool preview)
    {
        if (!entry.Female || !entry.Race || !entry.PlayerHips)
            return;

        var active = entry.Race.gameObject.activeInHierarchy;
        if (entry.Female.activeSelf != active)
            entry.Female.SetActive(active);
        if (!active)
            return;

        foreach (var r in entry.Hidden)
            if (r && r.enabled)
                r.enabled = false;

        var frame = entry.Frame;
        var yaw = preview ? Quaternion.AngleAxis(180f, frame.up) : Quaternion.identity;
        var fRoot = entry.Female.transform;
        fRoot.rotation = yaw * frame.rotation;

        if (preview)
        {
            var forward = Vector3.ProjectOnPlane(frame.forward, Vector3.up);
            if (forward.sqrMagnitude < 1e-6f)
                forward = Vector3.forward;
            entry.FemaleHips.position = entry.PlayerHips.position + forward.normalized * 2.5f;
        }
        else
        {
            entry.FemaleHips.position = entry.PlayerHips.position;
        }

        foreach (var link in entry.Drive)
        {
            if (link.Female && link.Player)
                link.Female.rotation = yaw * link.Player.rotation * link.Offset;
        }
    }

    private static GameObject GetPrefab(string model)
    {
        if (Prefabs.TryGetValue(model, out var cached) && cached)
            return cached;
        var obj = _bundle.LoadAsset(model, Il2CppType.Of<GameObject>());
        var prefab = obj ? obj.TryCast<GameObject>() : null;
        if (!prefab)
        {
            RLog.Error($"FemaleCharacter: {model} not found in bundle");
            return null;
        }
        ConvertMaterials(prefab);
        Prefabs[model] = prefab;
        return prefab;
    }

    private static ModelRig GetRig(string model, GameObject instance)
    {
        if (Rigs.TryGetValue(model, out var cached))
            return cached;

        var root = instance.transform;
        root.position = Vector3.zero;
        root.rotation = Quaternion.identity;
        root.localScale = Vector3.one;

        var fBones = BoneMap(root);
        var fRest = new Dictionary<string, Rest>();
        foreach (var kv in fBones)
            fRest[kv.Key] = new Rest { Rotation = Quaternion.Inverse(root.rotation) * kv.Value.rotation, Position = root.InverseTransformPoint(kv.Value.position) };

        var rig = new ModelRig();
        foreach (var name in DriveOrder)
        {
            if (!fRest.TryGetValue(name, out var rf) || !_playerRest.TryGetValue(name, out var rp))
            {
                RLog.Warning($"FemaleCharacter: {model} missing bone {name}");
                continue;
            }
            var align = Quaternion.identity;
            Vector3 df = Vector3.zero, dp = Vector3.zero;
            if (AimChild.TryGetValue(name, out var child) && fRest.TryGetValue(child, out var rfc) && _playerRest.TryGetValue(child, out var rpc))
            {
                df = rfc.Position - rf.Position;
                dp = rpc.Position - rp.Position;
            }
            else if (ChainParent.TryGetValue(name, out var parent) && fRest.TryGetValue(parent, out var rfp) && _playerRest.TryGetValue(parent, out var rpp))
            {
                df = rf.Position - rfp.Position;
                dp = rp.Position - rpp.Position;
            }
            if (df.sqrMagnitude > 1e-8f && dp.sqrMagnitude > 1e-8f)
                align = Align(name, df, dp, fRest, _playerRest);
            rig.Offsets[name] = Quaternion.Inverse(rp.Rotation) * (align * rf.Rotation);
        }

        RLog.Msg($"FemaleCharacter: {model} rig built, {rig.Offsets.Count} bones");

        if (rig.Offsets.Count == 0 || !rig.Offsets.ContainsKey("Hips"))
            return null;
        Rigs[model] = rig;
        return rig;
    }

    private static Quaternion Align(string name, Vector3 df, Vector3 dp, Dictionary<string, Rest> fRest, Dictionary<string, Rest> pRest)
    {
        var fallback = Quaternion.FromToRotation(df, dp);
        Vector3 sf, sp;
        var side = name.StartsWith("Left") ? "Left" : name.StartsWith("Right") ? "Right" : null;
        if (side != null && name.Contains("Hand")
            && fRest.TryGetValue($"{side}HandIndex1", out var fi) && fRest.TryGetValue($"{side}HandPinky1", out var fp)
            && pRest.TryGetValue($"{side}HandIndex1", out var pi) && pRest.TryGetValue($"{side}HandPinky1", out var pp))
        {
            sf = fi.Position - fp.Position;
            sp = pi.Position - pp.Position;
        }
        else
        {
            return fallback;
        }

        var nf = df.normalized;
        var np = dp.normalized;
        if (sf.sqrMagnitude < 1e-8f || sp.sqrMagnitude < 1e-8f
            || Mathf.Abs(Vector3.Dot(nf, sf.normalized)) > 0.9f || Mathf.Abs(Vector3.Dot(np, sp.normalized)) > 0.9f)
            return fallback;

        var bf = Quaternion.LookRotation(nf, sf);
        var bp = Quaternion.LookRotation(np, sp);
        return bp * Quaternion.Inverse(bf);
    }

    private static Dictionary<string, Rest> BuildPlayerRest(PlayerRaceSystem race)
    {
        var frame = race.transform.parent ? race.transform.parent : race.transform;
        var root = race._animationRoot ? race._animationRoot : frame.Find("PlayerAnimator/Root");
        var hips = root ? FindDeep(root, "Hips") : null;
        if (!hips)
        {
            RLog.Warning("FemaleCharacter: local player Hips not found");
            return null;
        }

        var ids = new Dictionary<int, string>();
        foreach (var t in hips.GetComponentsInChildren<Transform>(true))
            ids.TryAdd(t.GetInstanceID(), t.name);

        var sources = new List<SkinnedMeshRenderer>();
        var clothing = frame.Find("ClothingSystem");
        if (clothing)
            sources.AddRange(clothing.GetComponentsInChildren<SkinnedMeshRenderer>(true));
        sources.AddRange(race.transform.GetComponentsInChildren<SkinnedMeshRenderer>(true));

        var rest = new Dictionary<string, Rest>();
        foreach (var smr in sources)
        {
            if (!smr || HierPathContains(smr.transform, frame, "OldSkin"))
                continue;
            var mesh = smr.sharedMesh;
            var bones = smr.bones;
            if (!mesh || bones == null)
                continue;
            Matrix4x4[] binds;
            try
            {
                binds = mesh.bindposes.ToArray();
            }
            catch
            {
                continue;
            }
            var toWorld = smr.transform.localToWorldMatrix;
            for (int i = 0; i < bones.Length && i < binds.Length; i++)
            {
                var b = bones[i];
                if (!b || !ids.TryGetValue(b.GetInstanceID(), out var name) || rest.ContainsKey(name))
                    continue;
                var world = toWorld * binds[i].inverse;
                rest[name] = new Rest
                {
                    Rotation = Quaternion.Inverse(frame.rotation) * world.rotation,
                    Position = frame.InverseTransformPoint(new Vector3(world.m03, world.m13, world.m23))
                };
            }
        }

        var missing = DriveOrder.Where(n => !rest.ContainsKey(n)).ToList();
        RLog.Msg($"FemaleCharacter: player rest pose has {rest.Count} bones{(missing.Count > 0 ? $", missing {string.Join(" ", missing)}" : string.Empty)}");
        return rest.ContainsKey("Hips") ? rest : null;
    }

    private static void ConvertMaterials(GameObject prefab)
    {
        FindBaseMaterials();
        foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.sharedMaterials;
            var result = new Material[mats.Length];
            for (int i = 0; i < mats.Length; i++)
                result[i] = Convert(mats[i]);
            r.sharedMaterials = result;
        }
    }

    private static void FindBaseMaterials()
    {
        if (_litBase && _hairBase)
            return;
        foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
        {
            if (!m || !m.shader)
                continue;
            var shader = m.shader.name;
            var lower = m.name.ToLowerInvariant();
            if (!_litBase && shader == "Sons/HDRPLit" && m.renderQueue < 2450 && !lower.Contains("eye") && !lower.Contains("lens") && !lower.Contains("tear"))
                _litBase = m;
            if (!_hairBase && shader.StartsWith("Sons/Hair/"))
                _hairBase = m;
            if (_litBase && _hairBase)
                break;
        }
        if (!_litBase)
        {
            var lit = Shader.Find("HDRP/Lit");
            if (lit)
                _litBase = new Material(lit);
        }
        RLog.Msg($"FemaleCharacter: lit base {(_litBase ? _litBase.name : "none")}, hair base {(_hairBase ? _hairBase.name : "none")}");
    }

    private static Material Convert(Material src)
    {
        if (!src)
            return _litBase;
        if (Converted.TryGetValue(src.GetInstanceID(), out var done))
            return done;

        var main = src.HasProperty("_MainTex") ? src.GetTexture("_MainTex") : null;
        var normal = src.HasProperty("_BumpMap") ? src.GetTexture("_BumpMap") : null;
        var name = src.name.ToLowerInvariant();
        var texName = main ? main.name.ToLowerInvariant() : string.Empty;
        var isHair = name.Contains("hair") || texName.Contains("hair") || src.renderQueue >= 2450 || (src.HasProperty("_Mode") && src.GetFloat("_Mode") > 0.5f);

        var baseMat = isHair && _hairBase ? _hairBase : _litBase;
        var m = new Material(baseMat) { name = $"FC_{src.name}" };

        if (m.HasProperty("_BaseColorMap"))
            m.SetTexture("_BaseColorMap", main);
        if (m.HasProperty("_MainTex"))
            m.SetTexture("_MainTex", main);
        if (m.HasProperty("_BaseColor"))
            m.SetColor("_BaseColor", Color.white);

        if (m.HasProperty("_NormalMap"))
        {
            m.SetTexture("_NormalMap", normal);
            if (normal)
            {
                m.EnableKeyword("_NORMALMAP");
                if (m.HasProperty("_NormalScale"))
                    m.SetFloat("_NormalScale", 1f);
            }
            else
            {
                m.DisableKeyword("_NORMALMAP");
            }
        }

        if (!(isHair && _hairBase))
        {
            if (m.HasProperty("_MaskMap"))
            {
                m.SetTexture("_MaskMap", NeutralMask());
                m.EnableKeyword("_MASKMAP");
            }
            SetFloat(m, "_Metallic", 0f);
            SetFloat(m, "_Smoothness", 0.3f);
            SetFloat(m, "_SmoothnessRemapMin", 0f);
            SetFloat(m, "_SmoothnessRemapMax", 0.35f);
            SetFloat(m, "_AORemapMin", 0f);
            SetFloat(m, "_AORemapMax", 1f);
            if (m.HasProperty("_DetailMap"))
            {
                m.SetTexture("_DetailMap", null);
                m.DisableKeyword("_DETAIL_MAP");
            }
        }

        Converted[src.GetInstanceID()] = m;
        return m;
    }

    private static void SetFloat(Material m, string prop, float value)
    {
        if (m.HasProperty(prop))
            m.SetFloat(prop, value);
    }

    private static Texture2D NeutralMask()
    {
        if (_neutralMask)
            return _neutralMask;
        _neutralMask = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        var c = new Color(0f, 1f, 0f, 1f);
        for (int x = 0; x < 2; x++)
            for (int y = 0; y < 2; y++)
                _neutralMask.SetPixel(x, y, c);
        _neutralMask.Apply();
        _neutralMask.hideFlags = HideFlags.DontUnloadUnusedAsset;
        return _neutralMask;
    }

    private static bool HierPathContains(Transform t, Transform stop, string name)
    {
        while (t && t != stop)
        {
            if (t.name == name)
                return true;
            t = t.parent;
        }
        return false;
    }

    private static float LiveLegLength(Dictionary<string, Transform> bones)
    {
        if (!bones.TryGetValue("LeftUpLeg", out var a) || !bones.TryGetValue("LeftLeg", out var b) || !bones.TryGetValue("LeftFoot", out var c))
            return 0f;
        return Vector3.Distance(a.position, b.position) + Vector3.Distance(b.position, c.position);
    }

    private static Dictionary<string, Transform> BoneMap(Transform root)
    {
        var map = new Dictionary<string, Transform>();
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            var name = t.name;
            var i = name.LastIndexOf(':');
            if (i >= 0)
                name = name.Substring(i + 1);
            map.TryAdd(name, t);
        }
        return map;
    }

    private static Transform FindDeep(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name)
                return t;
        return null;
    }

    private static void Say(string text)
    {
        SonsTools.ShowMessage(text, 6f);
        RLog.Msg(text);
    }

    private sealed class Rest
    {
        public Quaternion Rotation;
        public Vector3 Position;
    }

    private sealed class ModelRig
    {
        public readonly Dictionary<string, Quaternion> Offsets = new();
    }

    private sealed class Link
    {
        public Transform Female;
        public Transform Player;
        public Quaternion Offset;
    }

    private sealed class Entry
    {
        public string Model;
        public PlayerRaceSystem Race;
        public Transform Frame;
        public GameObject Female;
        public Transform FemaleHips;
        public Transform PlayerHips;
        public readonly List<Link> Drive = new();
        public readonly List<SkinnedMeshRenderer> Hidden = new();
        public readonly List<bool> HiddenWasEnabled = new();
    }
}
