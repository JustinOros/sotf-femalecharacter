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
using Endnight.Animation;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Events;
using UnityEngine.ResourceManagement.AsyncOperations;
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
    private static bool _gameClothes;
    private static bool _settingsLoaded;
    private static readonly Dictionary<string, float> HandOffsets = new() { { "alyssa", 0.02f }, { "rachel", 0.05f } };
    private static readonly Dictionary<string, float> HeadOffsets = new() { { "alyssa", 0f }, { "rachel", 0f } };
    private static readonly Dictionary<string, float> OutfitHand = new();
    private static bool _fillers = true;
    private static bool _fillerLoadStarted;
    private static AsyncOperationHandle<GameObject> _whiteHeadHandle;
    private static AsyncOperationHandle<GameObject> _whiteArmsHandle;
    private static GameObject _whiteHead;
    private static GameObject _whiteArms;
    private static readonly HashSet<string> HeadKeep = new() { "Neck", "Neck1", "Spine", "Spine1", "Spine2", "LeftShoulder", "RightShoulder" };
    private static readonly Dictionary<string, float> OutfitHead = new();

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
        LoadSettings();
        RLog.Msg($"FemaleCharacter loaded. Latin shows as Alyssa, BlackB shows as Rachel. Clothes: {(_gameClothes ? "game" : "own")}. Command: femalecharacter [status|clothes own|clothes game|handoffset <model> <meters>|headoffset <model> <meters>|outfit|outfithand <piece> <meters>|outfithead <piece> <meters>|fillers on|fillers off|preview alyssa|preview rachel|preview off]");
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
            if (parts.Length >= 1 && parts[0] == "clothes")
            {
                if (parts.Length < 2 || (parts[1] != "own" && parts[1] != "game"))
                {
                    Say($"FemaleCharacter clothes: {(_gameClothes ? "game" : "own")}. Use femalecharacter clothes own or femalecharacter clothes game");
                    return;
                }
                _gameClothes = parts[1] == "game";
                SaveSettings();
                RebuildAll();
                Say($"FemaleCharacter clothes: {(_gameClothes ? "game clothing" : "her own outfit")}");
                return;
            }
            if (parts.Length >= 1 && parts[0] == "fillers")
            {
                if (parts.Length >= 2 && (parts[1] == "on" || parts[1] == "off"))
                {
                    _fillers = parts[1] == "on";
                    SaveSettings();
                    RebuildAll();
                }
                Say($"FemaleCharacter fillers: {(_fillers ? "on" : "off")}, white neck {(_whiteHead ? "loaded" : "not loaded")}, white arms {(_whiteArms ? "loaded" : "not loaded")}");
                return;
            }
            if (parts.Length >= 1 && parts[0] == "outfit")
            {
                var names = LocalOutfit();
                Say(names.Count > 0 ? $"FemaleCharacter outfit pieces: {string.Join(", ", names.Select(n => $"{n} (hand {Get(OutfitHand, n):F3}, head {Get(OutfitHead, n):F3})"))}" : "FemaleCharacter: no clothing pieces found");
                return;
            }
            if (parts.Length >= 1 && (parts[0] == "outfithand" || parts[0] == "outfithead"))
            {
                var table = parts[0] == "outfithand" ? OutfitHand : OutfitHead;
                if (parts.Length < 3 || !float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var meters))
                {
                    Say($"Use femalecharacter {parts[0]} <piece> <meters>, piece names come from femalecharacter outfit");
                    return;
                }
                table[parts[1]] = Mathf.Clamp(meters, -0.2f, 0.2f);
                SaveSettings();
                RefreshOutfits();
                Say($"FemaleCharacter: {parts[1]} {(parts[0] == "outfithand" ? "hand" : "head")} adjust {table[parts[1]]:F3} m");
                return;
            }
            if (parts.Length >= 1 && (parts[0] == "handoffset" || parts[0] == "headoffset"))
            {
                var table = parts[0] == "handoffset" ? HandOffsets : HeadOffsets;
                var label = parts[0] == "handoffset" ? "hand" : "head";
                if (parts.Length < 3 || !table.ContainsKey(parts[1]) || !float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var meters))
                {
                    Say($"FemaleCharacter {label} offsets: {string.Join(", ", table.Select(kv => $"{kv.Key} {kv.Value:F3}"))}. Use femalecharacter {parts[0]} rachel 0.03");
                    return;
                }
                table[parts[1]] = Mathf.Clamp(meters, -0.2f, 0.2f);
                SaveSettings();
                Say($"FemaleCharacter: {parts[1]} {label} offset {table[parts[1]]:F3} m");
                return;
            }
            Say($"FemaleCharacter: bundle={(_bundle ? "loaded" : "missing")} clothes={(_gameClothes ? "game" : "own")} rest={(_playerRest != null ? _playerRest.Count : 0)} bones, {Entries.Count} remote players shown as female, preview={(_preview != null ? _preview.Model : "off")}");
        }
        catch (Exception e)
        {
            RLog.Error($"femalecharacter command failed: {e}");
        }
    }

    private static string SettingsPath => Path.Combine(LoaderEnvironment.UserDataDirectory, "FemaleCharacter.cfg");

    private static void LoadSettings()
    {
        if (_settingsLoaded)
            return;
        _settingsLoaded = true;
        try
        {
            if (!File.Exists(SettingsPath))
                return;
            foreach (var raw in File.ReadAllLines(SettingsPath))
            {
                var line = raw.Trim();
                var eq = line.IndexOf('=');
                if (eq <= 0)
                    continue;
                var key = line.Substring(0, eq).Trim().ToLowerInvariant();
                var value = line.Substring(eq + 1).Trim().ToLowerInvariant();
                if (key == "clothes")
                    _gameClothes = value == "game";
                else if (key == "fillers")
                    _fillers = value != "off";
                else if (key.StartsWith("handoffset.") && float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f))
                    HandOffsets[key.Substring("handoffset.".Length)] = f;
                else if (key.StartsWith("headoffset.") && float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var h))
                    HeadOffsets[key.Substring("headoffset.".Length)] = h;
                else if (key.StartsWith("outfithand.") && float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var oh))
                    OutfitHand[key.Substring("outfithand.".Length)] = oh;
                else if (key.StartsWith("outfithead.") && float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var od))
                    OutfitHead[key.Substring("outfithead.".Length)] = od;
            }
        }
        catch (Exception e)
        {
            RLog.Warning($"FemaleCharacter: could not read settings: {e.Message}");
        }
    }

    private static void SaveSettings()
    {
        try
        {
            var lines = new List<string> { $"clothes={(_gameClothes ? "game" : "own")}", $"fillers={(_fillers ? "on" : "off")}" };
            foreach (var kv in HandOffsets)
                lines.Add($"handoffset.{kv.Key}={kv.Value.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}");
            foreach (var kv in HeadOffsets)
                lines.Add($"headoffset.{kv.Key}={kv.Value.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}");
            foreach (var kv in OutfitHand)
                lines.Add($"outfithand.{kv.Key}={kv.Value.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}");
            foreach (var kv in OutfitHead)
                lines.Add($"outfithead.{kv.Key}={kv.Value.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}");
            File.WriteAllLines(SettingsPath, lines);
        }
        catch (Exception e)
        {
            RLog.Warning($"FemaleCharacter: could not save settings: {e.Message}");
        }
    }

    private static float Get(Dictionary<string, float> table, string key)
    {
        return table.TryGetValue(key, out var v) ? v : 0f;
    }

    private static List<string> OutfitNames(Transform frame)
    {
        var names = new List<string>();
        var clothing = frame ? frame.Find("ClothingSystem") : null;
        if (!clothing)
            return names;
        foreach (var r in clothing.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (!r || !r.gameObject.activeInHierarchy)
                continue;
            var n = r.gameObject.name.ToLowerInvariant();
            if (!names.Contains(n))
                names.Add(n);
        }
        return names;
    }

    private static List<string> LocalOutfit()
    {
        var race = LocalPlayer.RaceSystem;
        if (!race)
            return new List<string>();
        return OutfitNames(race.transform.parent ? race.transform.parent : race.transform);
    }

    private static void UpdateOutfit(Entry entry)
    {
        var names = OutfitNames(entry.Frame);
        entry.HandAdjust = names.Sum(n => Get(OutfitHand, n));
        entry.HeadAdjust = names.Sum(n => Get(OutfitHead, n));
    }

    private static void RefreshOutfits()
    {
        foreach (var entry in Entries.Values)
            UpdateOutfit(entry);
        if (_preview != null)
            UpdateOutfit(_preview);
    }

    private static void RebuildAll()
    {
        foreach (var entry in Entries.Values)
            Remove(entry);
        Entries.Clear();
        string previewModel = null;
        if (_preview != null)
        {
            previewModel = _preview.Model;
            Remove(_preview);
            _preview = null;
        }
        _nextScan = 0f;
        if (previewModel != null)
            SetPreview(previewModel);
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
                Drive(entry);
            if (_preview != null)
                Drive(_preview);
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

        RefreshOutfits();
        PollFillers();
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
                if (existing.Model == model && existing.Female && existing.GameClothes == _gameClothes)
                {
                    seen.Add(id);
                    continue;
                }
                Remove(existing);
                Entries.Remove(id);
            }

            if (model == null)
                continue;

            var entry = Create(race, model, false);
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
        _preview = Create(LocalPlayer.RaceSystem, model, true);
        Say(_preview != null ? $"FemaleCharacter preview: {model}" : $"FemaleCharacter: could not create {model}");
    }

    private static Entry Create(PlayerRaceSystem race, string model, bool preview)
    {
        var gameClothes = _gameClothes;
        var prefabName = gameClothes ? $"{model}_head" : model;
        var prefab = GetPrefab(prefabName);
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

        Mannequin mannequin = null;
        Dictionary<string, Transform> pBones;
        if (preview)
        {
            mannequin = BuildMannequin(frame, root, hips, gameClothes);
            pBones = mannequin.Bones;
        }
        else
        {
            pBones = new Dictionary<string, Transform>();
            foreach (var t in hips.GetComponentsInChildren<Transform>(true))
                pBones.TryAdd(t.name, t);
        }

        var female = UnityEngine.Object.Instantiate(prefab);
        female.name = $"FemaleCharacter_{prefabName}";
        var rig = GetRig(prefabName, female);
        if (rig == null)
        {
            UnityEngine.Object.Destroy(female);
            DestroyMannequin(mannequin);
            return null;
        }
        female.transform.localScale = Vector3.one;
        var fBones = BoneMap(female.transform);
        var fLeg = LiveLegLength(fBones);
        var pLeg = LiveLegLength(pBones);
        var scale = fLeg > 0.01f && pLeg > 0.01f ? pLeg / fLeg : 1f;
        female.transform.localScale = Vector3.one * scale;
        RLog.Msg($"FemaleCharacter: {prefabName} leg {fLeg:F3} player leg {pLeg:F3} scale {scale:F3}");

        var entry = new Entry { Model = model, GameClothes = gameClothes, Race = race, Frame = frame, Female = female, Mannequin = mannequin };
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
            DestroyMannequin(mannequin);
            return null;
        }

        if (gameClothes)
        {
            foreach (var name in new[] { "Neck", "LeftHand", "RightHand" })
            {
                if (!fBones.TryGetValue(name, out var f) || !pBones.TryGetValue(name, out var p))
                    continue;
                var link = new Link { Female = f, Player = p };
                if (name.EndsWith("Hand") && pBones.TryGetValue(name.Replace("Hand", "ForeArm"), out var up))
                    link.Up = up;
                if (name == "Neck" && pBones.TryGetValue("Spine2", out var down))
                    link.Up = down;
                entry.Snap.Add(link);
            }
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

        if (!preview)
        {
            var targets = new List<Transform> { race.transform };
            var clothing = frame.Find("ClothingSystem");
            if (clothing && !gameClothes)
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

        UpdateOutfit(entry);
        if (gameClothes)
            AddFillers(entry, pBones);
        return entry;
    }

    private static void StartFillerLoad()
    {
        if (_fillerLoadStarted)
            return;
        var race = LocalPlayer.RaceSystem;
        if (!race || race._races == null)
            return;
        _fillerLoadStarted = true;
        try
        {
            for (int i = 0; i < race._races.Count; i++)
            {
                var r = race._races[i];
                if (!r || r.GetRace != PlayerRace.Race.White)
                    continue;
                if (r.HeadAsset != null)
                    _whiteHeadHandle = Addressables.LoadAssetAsync<GameObject>(r.HeadAsset.RuntimeKey);
                if (r.ArmsAsset != null)
                    _whiteArmsHandle = Addressables.LoadAssetAsync<GameObject>(r.ArmsAsset.RuntimeKey);
                RLog.Msg("FemaleCharacter: loading white neck and arms");
                return;
            }
            RLog.Warning("FemaleCharacter: White race entry not found");
        }
        catch (Exception e)
        {
            RLog.Warning($"FemaleCharacter: could not start filler load: {e.Message}");
        }
    }

    private static void PollFillers()
    {
        if (!_fillers || !_gameClothes)
            return;
        StartFillerLoad();
        var changed = false;
        try
        {
            if (!_whiteHead && _whiteHeadHandle.IsValid() && _whiteHeadHandle.IsDone)
            {
                _whiteHead = _whiteHeadHandle.Result;
                if (_whiteHead)
                    changed = true;
                RLog.Msg($"FemaleCharacter: white neck {(_whiteHead ? "loaded" : "failed")}");
            }
            if (!_whiteArms && _whiteArmsHandle.IsValid() && _whiteArmsHandle.IsDone)
            {
                _whiteArms = _whiteArmsHandle.Result;
                if (_whiteArms)
                    changed = true;
                RLog.Msg($"FemaleCharacter: white arms {(_whiteArms ? "loaded" : "failed")}");
            }
        }
        catch (Exception e)
        {
            RLog.Warning($"FemaleCharacter: filler load failed: {e.Message}");
        }
        if (changed)
            RebuildAll();
    }

    private static void AddFillers(Entry entry, Dictionary<string, Transform> pBones)
    {
        if (!_fillers)
            return;
        if (_whiteHead && pBones.TryGetValue("Head", out var head))
            AddFiller(entry, _whiteHead, pBones, name => HeadKeep.Contains(name) ? null : head, true);
        if (_whiteArms && pBones.TryGetValue("LeftHand", out var lh) && pBones.TryGetValue("RightHand", out var rh))
            AddFiller(entry, _whiteArms, pBones, name => name.Contains("Hand") ? (name.StartsWith("Left") ? lh : rh) : null, false);
    }

    private static void AddFiller(Entry entry, GameObject prefab, Dictionary<string, Transform> pBones, Func<string, Transform> collapseTo, bool head)
    {
        var dummies = new Dictionary<int, Transform>();
        Transform Dummy(Transform parent)
        {
            if (dummies.TryGetValue(parent.GetInstanceID(), out var d))
                return d;
            var go = new GameObject("FemaleCharacter_Collapse");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one * 0.0001f;
            entry.Owned.Add(go);
            dummies[parent.GetInstanceID()] = go.transform;
            return go.transform;
        }

        var inst = UnityEngine.Object.Instantiate(prefab);
        inst.name = $"FemaleCharacter_Filler_{prefab.name}";
        entry.Owned.Add(inst);
        foreach (var mb in inst.GetComponentsInChildren<MonoBehaviour>(true))
        {
            try
            {
                UnityEngine.Object.DestroyImmediate(mb);
            }
            catch
            {
                mb.enabled = false;
            }
        }

        var layer = LayerMask.NameToLayer("Player");
        var fallback = pBones.TryGetValue("Spine2", out var s2) ? s2 : entry.PlayerHips;
        foreach (var smr in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var lower = smr.name.ToLowerInvariant();
            if (head && (lower.Contains("hair") || lower.Contains("eye")))
            {
                smr.enabled = false;
                continue;
            }
            var names = BoneNames(smr);
            if (names == null)
            {
                smr.enabled = false;
                continue;
            }
            var mapped = new Transform[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                var n = names[i];
                var collapse = n == null ? null : collapseTo(n);
                if (collapse)
                    mapped[i] = Dummy(collapse);
                else if (n != null && pBones.TryGetValue(n, out var b))
                    mapped[i] = b;
                else
                    mapped[i] = head && pBones.TryGetValue("Head", out var hb) ? Dummy(hb) : fallback;
            }
            smr.bones = mapped;
            smr.rootBone = fallback;
            smr.updateWhenOffscreen = true;
            smr.shadowCastingMode = ShadowCastingMode.On;
            smr.enabled = true;
            if (layer >= 0)
                smr.gameObject.layer = layer;
        }
        inst.SetActive(true);
    }

    private static string[] BoneNames(SkinnedMeshRenderer smr)
    {
        var bones = smr.bones;
        var mesh = smr.sharedMesh;
        if (!mesh)
            return null;
        var count = mesh.bindposes.Length;
        var names = new string[count];
        var anyNull = false;
        for (int i = 0; i < count; i++)
        {
            if (bones != null && i < bones.Length && bones[i])
                names[i] = bones[i].name;
            else
                anyNull = true;
        }
        if (anyNull)
        {
            var cache = smr.GetComponent<SkinnedMeshBoneRemapCache>();
            var paths = cache ? cache._bonePaths : null;
            if (paths != null)
            {
                for (int i = 0; i < count && i < paths.Count; i++)
                {
                    if (names[i] != null)
                        continue;
                    var path = paths[i];
                    if (string.IsNullOrEmpty(path))
                        continue;
                    var slash = path.LastIndexOf('/');
                    names[i] = slash >= 0 ? path.Substring(slash + 1) : path;
                }
            }
        }
        return names;
    }

    private static Mannequin BuildMannequin(Transform frame, Transform srcRoot, Transform srcHips, bool withClothes)
    {
        var m = new Mannequin
        {
            Root = new GameObject("FemaleCharacter_Mannequin"),
            SrcFrame = frame,
            SrcRoot = srcRoot,
            SrcHips = srcHips
        };
        m.Hips = CloneBone(srcHips, m.Root.transform, m);

        if (withClothes)
        {
            var layer = LayerMask.NameToLayer("Player");
            var clothing = frame.Find("ClothingSystem");
            if (clothing)
            {
                foreach (var src in clothing.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (!src || !src.enabled || !src.gameObject.activeInHierarchy || !src.sharedMesh)
                        continue;
                    var bones = src.bones;
                    if (bones == null)
                        continue;
                    var mapped = new Transform[bones.Length];
                    for (int i = 0; i < bones.Length; i++)
                        mapped[i] = bones[i] && m.Bones.TryGetValue(bones[i].name, out var b) ? b : m.Hips;
                    var go = new GameObject($"FemaleCharacter_{src.name}");
                    go.transform.SetParent(m.Root.transform, false);
                    if (layer >= 0)
                        go.layer = layer;
                    var smr = go.AddComponent<SkinnedMeshRenderer>();
                    smr.sharedMesh = src.sharedMesh;
                    smr.sharedMaterials = src.sharedMaterials;
                    smr.bones = mapped;
                    smr.rootBone = src.rootBone && m.Bones.TryGetValue(src.rootBone.name, out var rb) ? rb : m.Hips;
                    smr.updateWhenOffscreen = true;
                }
            }
        }
        return m;
    }

    private static Transform CloneBone(Transform src, Transform parent, Mannequin m)
    {
        var go = new GameObject(src.name);
        var t = go.transform;
        t.SetParent(parent, false);
        t.localPosition = src.localPosition;
        t.localRotation = src.localRotation;
        t.localScale = src.localScale;
        m.Pairs.Add(new Link { Player = src, Female = t });
        m.Bones.TryAdd(src.name, t);
        for (int i = 0; i < src.childCount; i++)
            CloneBone(src.GetChild(i), t, m);
        return t;
    }

    private static void UpdateMannequin(Mannequin m)
    {
        if (!m.SrcRoot || !m.SrcHips)
            return;
        foreach (var pair in m.Pairs)
        {
            if (!pair.Player || !pair.Female)
                continue;
            pair.Female.localPosition = pair.Player.localPosition;
            pair.Female.localRotation = pair.Player.localRotation;
            pair.Female.localScale = pair.Player.localScale;
        }

        var root = m.Root.transform;
        var yaw = Quaternion.AngleAxis(180f, Vector3.up);
        root.rotation = yaw * m.SrcRoot.rotation;
        root.localScale = m.SrcRoot.lossyScale;
        root.position = m.SrcRoot.position;

        var forward = Vector3.ProjectOnPlane(m.SrcFrame.forward, Vector3.up);
        if (forward.sqrMagnitude < 1e-6f)
            forward = Vector3.forward;
        var desired = m.SrcHips.position + forward.normalized * 2.5f;
        root.position += desired - m.Hips.position;
    }

    private static void DestroyMannequin(Mannequin m)
    {
        if (m != null && m.Root)
            UnityEngine.Object.Destroy(m.Root);
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
        foreach (var go in entry.Owned)
            if (go)
                UnityEngine.Object.Destroy(go);
        entry.Owned.Clear();
        DestroyMannequin(entry.Mannequin);
        entry.Mannequin = null;
    }

    private static void Drive(Entry entry)
    {
        if (!entry.Female || !entry.Race || !entry.PlayerHips)
            return;

        var active = entry.Race.gameObject.activeInHierarchy;
        if (entry.Female.activeSelf != active)
            entry.Female.SetActive(active);
        if (entry.Mannequin != null && entry.Mannequin.Root && entry.Mannequin.Root.activeSelf != active)
            entry.Mannequin.Root.SetActive(active);
        if (!active)
            return;

        foreach (var r in entry.Hidden)
            if (r && r.enabled)
                r.enabled = false;

        var frameRotation = entry.Frame.rotation;
        if (entry.Mannequin != null)
        {
            UpdateMannequin(entry.Mannequin);
            frameRotation = entry.Mannequin.Root.transform.rotation * Quaternion.Inverse(entry.Mannequin.SrcRoot.rotation) * entry.Frame.rotation;
        }

        entry.Female.transform.rotation = frameRotation;
        entry.FemaleHips.position = entry.PlayerHips.position;

        foreach (var link in entry.Drive)
        {
            if (link.Female && link.Player)
                link.Female.rotation = link.Player.rotation * link.Offset;
        }

        var handOffset = Get(HandOffsets, entry.Model) + entry.HandAdjust;
        var headOffset = Get(HeadOffsets, entry.Model) + entry.HeadAdjust;
        foreach (var link in entry.Snap)
        {
            if (!link.Female || !link.Player)
                continue;
            var target = link.Player.position;
            var offset = link.Female.name.EndsWith("Neck") ? headOffset : handOffset;
            if (link.Up && offset != 0f)
            {
                var dir = link.Up.position - link.Player.position;
                if (dir.sqrMagnitude > 1e-6f)
                    target += dir.normalized * offset;
            }
            link.Female.position = target;
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
        public Transform Up;
        public Transform Female;
        public Transform Player;
        public Quaternion Offset;
    }

    private sealed class Mannequin
    {
        public GameObject Root;
        public Transform SrcFrame;
        public Transform SrcRoot;
        public Transform SrcHips;
        public Transform Hips;
        public readonly List<Link> Pairs = new();
        public readonly Dictionary<string, Transform> Bones = new();
    }

    private sealed class Entry
    {
        public string Model;
        public bool GameClothes;
        public float HandAdjust;
        public float HeadAdjust;
        public readonly List<GameObject> Owned = new();
        public Mannequin Mannequin;
        public readonly List<Link> Snap = new();
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
