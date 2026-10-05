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
    private static bool _beforeRenderHooked;
    private static float _nextPlayerScan;
    private static bool _gameClothes = true;
    private static bool _settingsLoaded;
    private static readonly Dictionary<string, float> HandOffsets = new() { { "alyssa", 0.07f }, { "rachel", 0.10f } };
    private static readonly Dictionary<string, float> HeadOffsets = new() { { "alyssa", 0f }, { "rachel", 0.01f } };
    private static readonly Dictionary<string, float> OutfitHand = new();
    private static readonly Dictionary<string, float> OutfitHead = new();
    private static bool _neckFiller = true;
    private static string _play;
    private static int _prevRace = -1;
    private static bool _spawnApplied = true;
    private static float _spawnApplyAt;
    private static int _lastRaceSystem;
    private static readonly Dictionary<string, Color> SkinTones = new();
    private static bool _armFiller = true;
    private static bool _fillerLoadStarted;
    private static AsyncOperationHandle<GameObject> _whiteHeadHandle;
    private static AsyncOperationHandle<GameObject> _whiteArmsHandle;
    private static GameObject _whiteHead;
    private static GameObject _whiteArms;
    private static readonly HashSet<string> HeadKeep = new() { "Neck", "Neck1", "Spine", "Spine1", "Spine2", "LeftShoulder", "RightShoulder" };

    public FemaleCharacter()
    {
        OnUpdateCallback = OnUpdate;
        OnLateUpdateCallback = OnLateUpdate;
    }

    protected override void OnSdkInitialized()
    {
        try
        {
            _beforeRender = DelegateSupport.ConvertDelegate<UnityAction>(new Action(DriveAll));
            Application.add_onBeforeRender(_beforeRender);
            _beforeRenderHooked = true;
        }
        catch (Exception e)
        {
            RLog.Warning($"FemaleCharacter: could not hook onBeforeRender, using LateUpdate only: {e.Message}");
        }
        LoadSettings();
        _spawnApplied = false;
        RLog.Msg($"FemaleCharacter loaded. Playing as: {_play ?? "off"}. Latin shows as Alyssa, BlackB shows as Rachel. Clothes: {(_gameClothes ? "game" : "own")}. Command: femalecharacter [alyssa|rachel|off|status|clothes own|clothes game|handoffset <model> <meters>|headoffset <model> <meters>|outfit|hand <meters>|head <meters>|fillers on|off|fillers neck on|off|fillers arms on|off|skintone <r> <g> <b>|preview alyssa|preview rachel|preview off]");
    }

    [DebugCommand("femalecharacter")]
    private static void Command(string args)
    {
        try
        {
            var parts = (args ?? string.Empty).Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1 && (parts[0] == "alyssa" || parts[0] == "rachel" || parts[0] == "off"))
            {
                Play(parts[0]);
                return;
            }
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
                    _neckFiller = _armFiller = parts[1] == "on";
                    SaveSettings();
                    RebuildAll();
                }
                else if (parts.Length >= 3 && (parts[1] == "neck" || parts[1] == "arms") && (parts[2] == "on" || parts[2] == "off"))
                {
                    if (parts[1] == "neck")
                        _neckFiller = parts[2] == "on";
                    else
                        _armFiller = parts[2] == "on";
                    SaveSettings();
                    RebuildAll();
                }
                Say($"FemaleCharacter fillers: neck {(_neckFiller ? "on" : "off")}, arms {(_armFiller ? "on" : "off")}, white neck {(_whiteHead ? "loaded" : "not loaded")}, white arms {(_whiteArms ? "loaded" : "not loaded")}");
                return;
            }
            if (parts.Length >= 1 && parts[0] == "skintone")
            {
                if (_preview == null)
                {
                    Say("FemaleCharacter: start a preview first, for example femalecharacter preview alyssa");
                    return;
                }
                if (parts.Length >= 2 && parts[1] == "reset")
                {
                    SkinTones.Remove(_preview.Model);
                    SaveSettings();
                    RebuildAll();
                    Say($"FemaleCharacter: {_preview?.Model} skin tone reset");
                    return;
                }
                if (parts.Length < 4 || !float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var r) || !float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var g) || !float.TryParse(parts[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var b))
                {
                    var cur = SkinTone(_preview.Model);
                    Say($"FemaleCharacter {_preview.Model} filler skin tone {cur.r:F2} {cur.g:F2} {cur.b:F2}. Use femalecharacter skintone 1.1 1.05 1.0 or femalecharacter skintone reset");
                    return;
                }
                SkinTones[_preview.Model] = new Color(Mathf.Clamp(r, 0f, 3f), Mathf.Clamp(g, 0f, 3f), Mathf.Clamp(b, 0f, 3f), 1f);
                SaveSettings();
                ApplySkinTone(_preview);
                Say($"FemaleCharacter: {_preview.Model} filler skin tone {r:F2} {g:F2} {b:F2}");
                return;
            }
            if (parts.Length >= 1 && parts[0] == "outfit")
            {
                var key = OutfitKey(LocalFrame());
                Say($"FemaleCharacter outfit: {key}. alyssa hand {HandFor("alyssa", key):F3} head {HeadFor("alyssa", key):F3}, rachel hand {HandFor("rachel", key):F3} head {HeadFor("rachel", key):F3}");
                return;
            }
            if (parts.Length >= 1 && (parts[0] == "hand" || parts[0] == "head"))
            {
                if (_preview == null)
                {
                    Say("FemaleCharacter: start a preview first, for example femalecharacter preview rachel");
                    return;
                }
                var key = OutfitKey(LocalFrame());
                var table = parts[0] == "hand" ? OutfitHand : OutfitHead;
                var id = $"{_preview.Model}|{key}";
                if (parts.Length < 2 || !float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var meters))
                {
                    Say($"FemaleCharacter {_preview.Model} in {key}: hand {HandFor(_preview.Model, key):F3} head {HeadFor(_preview.Model, key):F3}. Use femalecharacter {parts[0]} 0.05");
                    return;
                }
                table[id] = Mathf.Clamp(meters, -0.2f, 0.2f);
                SaveSettings();
                RefreshOutfits();
                Say($"FemaleCharacter: {_preview.Model} in {key} {parts[0]} {table[id]:F3} m");
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
                    _neckFiller = _armFiller = value != "off";
                else if (key.StartsWith("skintone."))
                {
                    var rgb = value.Split(',');
                    if (rgb.Length == 3 && float.TryParse(rgb[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var tr) && float.TryParse(rgb[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var tg) && float.TryParse(rgb[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var tb))
                        SkinTones[key.Substring("skintone.".Length)] = new Color(tr, tg, tb, 1f);
                }
                else if (key == "play")
                    _play = value == "alyssa" || value == "rachel" ? value : null;
                else if (key == "prevrace" && int.TryParse(value, out var pr))
                    _prevRace = pr;
                else if (key == "fillers.neck")
                    _neckFiller = value != "off";
                else if (key == "fillers.arms")
                    _armFiller = value != "off";
                else if (key.StartsWith("handoffset.") && float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f))
                    HandOffsets[key.Substring("handoffset.".Length)] = f;
                else if (key.StartsWith("headoffset.") && float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var h))
                    HeadOffsets[key.Substring("headoffset.".Length)] = h;
                else if (key.StartsWith("hand.") && float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var oh))
                    OutfitHand[key.Substring("hand.".Length)] = oh;
                else if (key.StartsWith("head.") && float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var od))
                    OutfitHead[key.Substring("head.".Length)] = od;
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
            var lines = new List<string> { $"clothes={(_gameClothes ? "game" : "own")}", $"fillers.neck={(_neckFiller ? "on" : "off")}", $"fillers.arms={(_armFiller ? "on" : "off")}", $"play={_play ?? "off"}", $"prevrace={_prevRace}" };
            foreach (var kv in HandOffsets)
                lines.Add($"handoffset.{kv.Key}={kv.Value.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}");
            foreach (var kv in HeadOffsets)
                lines.Add($"headoffset.{kv.Key}={kv.Value.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}");
            foreach (var kv in SkinTones)
                lines.Add($"skintone.{kv.Key}={string.Join(",", new[] { kv.Value.r, kv.Value.g, kv.Value.b }.Select(v => v.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)))}");
            foreach (var kv in OutfitHand.OrderBy(k => k.Key))
                lines.Add($"hand.{kv.Key}={kv.Value.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}");
            foreach (var kv in OutfitHead.OrderBy(k => k.Key))
                lines.Add($"head.{kv.Key}={kv.Value.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}");
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

    private static Transform LocalFrame()
    {
        var race = LocalPlayer.RaceSystem;
        if (!race)
            return null;
        return race.transform.parent ? race.transform.parent : race.transform;
    }

    private static string OutfitKey(Transform frame)
    {
        var names = OutfitNames(frame);
        names.Sort(StringComparer.Ordinal);
        return names.Count > 0 ? string.Join("+", names) : "none";
    }

    private static float HandFor(string model, string outfit)
    {
        return OutfitHand.TryGetValue($"{model}|{outfit}", out var v) ? v : Get(HandOffsets, model);
    }

    private static float HeadFor(string model, string outfit)
    {
        return OutfitHead.TryGetValue($"{model}|{outfit}", out var v) ? v : Get(HeadOffsets, model);
    }

    private static void UpdateOutfit(Entry entry)
    {
        entry.OutfitKey = OutfitKey(entry.Frame);
        entry.HandAdjust = HandFor(entry.Model, entry.OutfitKey);
        entry.HeadAdjust = HeadFor(entry.Model, entry.OutfitKey);
    }

    private static void RefreshOutfits()
    {
        foreach (var entry in Entries.Values)
            UpdateOutfit(entry);
        if (_preview == null)
            return;
        var before = _preview.OutfitKey;
        UpdateOutfit(_preview);
        if (_preview.GameClothes && before != _preview.OutfitKey)
        {
            var model = _preview.Model;
            RLog.Msg($"FemaleCharacter: outfit changed to {_preview.OutfitKey}, rebuilding preview");
            SetPreview(model);
        }
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
        _nextPlayerScan = 0f;
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

    private static PlayerRace.Race RaceFor(string model)
    {
        return model == "rachel" ? PlayerRace.Race.BlackB : PlayerRace.Race.Latin;
    }

    private static void Play(string choice)
    {
        var race = LocalPlayer.RaceSystem;
        if (!race)
        {
            Say("FemaleCharacter: load into a game first");
            return;
        }

        if (choice == "off")
        {
            var restore = _prevRace >= 0 ? (PlayerRace.Race)_prevRace : PlayerRace.Race.White;
            _play = null;
            _prevRace = -1;
            race.ApplyRace(restore);
            SaveSettings();
            SyncCharacterSelect(restore);
            Say($"FemaleCharacter: off, back to {restore}");
            return;
        }

        var current = race.CurrentRace;
        if (_play == null && current != PlayerRace.Race.Latin && current != PlayerRace.Race.BlackB)
            _prevRace = (int)current;
        _play = choice;
        var target = RaceFor(choice);
        race.ApplyRace(target);
        SaveSettings();
        SyncCharacterSelect(target);
        Say($"FemaleCharacter: playing as {char.ToUpper(choice[0])}{choice.Substring(1)}. Other players with the mod see you as her.");
    }

    private static void SyncCharacterSelect(PlayerRace.Race race)
    {
        try
        {
            var path = Path.Combine(LoaderEnvironment.UserDataDirectory, "CharacterSelect.txt");
            var installed = File.Exists(Path.Combine(LoaderEnvironment.ModsDirectory, "CharacterSelect.dll"));
            if (installed || File.Exists(path))
                File.WriteAllText(path, ((int)race).ToString());
        }
        catch (Exception e)
        {
            RLog.Warning($"FemaleCharacter: could not sync CharacterSelect: {e.Message}");
        }
    }

    private static void TickPlay()
    {
        var race = LocalPlayer.RaceSystem;
        if (!race)
            return;

        var id = race.GetInstanceID();
        if (id != _lastRaceSystem)
        {
            _lastRaceSystem = id;
            _spawnApplied = false;
            _spawnApplyAt = Time.unscaledTime + 3f;
        }

        if (!_spawnApplied)
        {
            if (Time.unscaledTime < _spawnApplyAt)
                return;
            _spawnApplied = true;
            if (_play != null && race.CurrentRace != RaceFor(_play))
            {
                race.ApplyRace(RaceFor(_play));
                RLog.Msg($"FemaleCharacter: applied saved character {_play}");
            }
            return;
        }

        if (_play != null && race.CurrentRace != RaceFor(_play))
        {
            RLog.Msg($"FemaleCharacter: character changed to {race.CurrentRace} by another mod, femalecharacter play cleared");
            _play = null;
            SaveSettings();
        }
    }

    private static void OnUpdate()
    {
        try
        {
            TickPlay();
        }
        catch (Exception e)
        {
            RLog.Error($"FemaleCharacter play failed: {e.Message}");
        }
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
        DriveAll();
    }

    private static void DriveAll()
    {
        if (Entries.Count == 0 && _preview == null)
            return;
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
        if (Time.unscaledTime < _nextPlayerScan)
            return;
        _nextPlayerScan = Time.unscaledTime + 5f;
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
        if ((!_neckFiller && !_armFiller) || !_gameClothes)
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

    private static Color SkinTone(string model)
    {
        return SkinTones.TryGetValue(model, out var c) ? c : Color.white;
    }

    private static void ApplySkinTone(Entry entry)
    {
        var tone = SkinTone(entry.Model);
        foreach (var r in entry.FillerRenderers)
        {
            if (!r)
                continue;
            foreach (var m in r.materials)
            {
                if (!m)
                    continue;
                if (m.HasProperty("_BaseColor"))
                    m.SetColor("_BaseColor", tone);
                if (m.HasProperty("_Color"))
                    m.SetColor("_Color", tone);
            }
        }
    }

    private static void AddFillers(Entry entry, Dictionary<string, Transform> pBones)
    {
        if (_neckFiller && _whiteHead && pBones.TryGetValue("Head", out var head))
            AddFiller(entry, _whiteHead, pBones, name => HeadKeep.Contains(name) ? null : head, true);
        if (_armFiller && _whiteArms && pBones.TryGetValue("LeftHand", out var lh) && pBones.TryGetValue("RightHand", out var rh))
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

        var playerRace = entry.Race ? entry.Race.transform : null;
        var boneNames = new Dictionary<int, string[]>();
        foreach (var smr in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            boneNames[smr.GetInstanceID()] = BoneNames(smr, playerRace);

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
            boneNames.TryGetValue(smr.GetInstanceID(), out var names);
            if (names == null)
            {
                smr.enabled = false;
                continue;
            }
            var mapped = new Transform[names.Length];
            int kept = 0, collapsed = 0, missing = 0;
            for (int i = 0; i < names.Length; i++)
            {
                var n = names[i];
                var collapse = n == null ? null : collapseTo(n);
                if (collapse)
                {
                    mapped[i] = Dummy(collapse);
                    collapsed++;
                }
                else if (n != null && pBones.TryGetValue(n, out var b))
                {
                    mapped[i] = b;
                    kept++;
                }
                else
                {
                    mapped[i] = head && pBones.TryGetValue("Head", out var hb) ? Dummy(hb) : fallback;
                    missing++;
                }
            }
            RLog.Msg($"FemaleCharacter: filler {entry.Model} {smr.name} bones {names.Length} kept {kept} collapsed {collapsed} missing {missing} kept names {string.Join(" ", names.Where((n, i) => n != null && mapped[i] && !mapped[i].name.StartsWith("FemaleCharacter_Collapse")).Distinct())}");
            smr.bones = mapped;
            smr.rootBone = fallback;
            smr.updateWhenOffscreen = true;
            smr.shadowCastingMode = ShadowCastingMode.On;
            smr.enabled = true;
            if (layer >= 0)
                smr.gameObject.layer = layer;
            entry.FillerRenderers.Add(smr);
        }
        inst.SetActive(true);
        ApplySkinTone(entry);
    }

    private static float MatrixDistance(Matrix4x4 a, Matrix4x4 b)
    {
        return Mathf.Abs(a.m00 - b.m00) + Mathf.Abs(a.m01 - b.m01) + Mathf.Abs(a.m02 - b.m02) + Mathf.Abs(a.m03 - b.m03)
            + Mathf.Abs(a.m10 - b.m10) + Mathf.Abs(a.m11 - b.m11) + Mathf.Abs(a.m12 - b.m12) + Mathf.Abs(a.m13 - b.m13)
            + Mathf.Abs(a.m20 - b.m20) + Mathf.Abs(a.m21 - b.m21) + Mathf.Abs(a.m22 - b.m22) + Mathf.Abs(a.m23 - b.m23);
    }

    private static string[] BoneNames(SkinnedMeshRenderer smr, Transform playerRace)
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
            if (!cache)
                cache = smr.GetComponentInParent<SkinnedMeshBoneRemapCache>();
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
        if (names.Any(n => n == null) && playerRace)
        {
            foreach (var other in playerRace.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (other.name != smr.name || other.bones == null || other.bones.Length != count)
                    continue;
                for (int i = 0; i < count; i++)
                    if (names[i] == null && other.bones[i])
                        names[i] = other.bones[i].name;
                RLog.Msg($"FemaleCharacter: filler {smr.name} bone names taken from player {other.transform.parent.name}/{other.name}");
                break;
            }
        }
        if (names.Any(n => n == null) && playerRace)
        {
            var frame = playerRace.parent ? playerRace.parent : playerRace;
            var binds = mesh.bindposes;
            var known = new List<(Matrix4x4 bind, string name)>();
            foreach (var other in frame.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var ob = other.bones;
                var om = other.sharedMesh;
                if (ob == null || !om)
                    continue;
                var obinds = om.bindposes;
                for (int j = 0; j < ob.Length && j < obinds.Length; j++)
                    if (ob[j])
                        known.Add((obinds[j], ob[j].name));
            }
            int matched = 0;
            for (int i = 0; i < count; i++)
            {
                if (names[i] != null)
                    continue;
                var best = float.MaxValue;
                string bestName = null;
                foreach (var (bind, name) in known)
                {
                    var d = MatrixDistance(bind, binds[i]);
                    if (d < best)
                    {
                        best = d;
                        bestName = name;
                    }
                }
                if (bestName != null && best < 0.01f)
                {
                    names[i] = bestName;
                    matched++;
                }
            }
            RLog.Msg($"FemaleCharacter: filler {smr.name} matched {matched} bones by bind pose");
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
        var needed = NeededBones(frame, srcHips, withClothes);
        m.Hips = CloneBone(srcHips, m.Root.transform, m, needed);

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

    private static HashSet<int> NeededBones(Transform frame, Transform srcHips, bool withClothes)
    {
        var names = new HashSet<string>(DriveOrder);
        foreach (var n in new[] { "Neck1", "Spine1", "Spine2", "Head" })
            names.Add(n);
        var sources = new List<Transform>();
        var race = frame.Find("RaceSystem");
        if (race)
            sources.Add(race);
        var clothing = frame.Find("ClothingSystem");
        if (clothing && withClothes)
            sources.Add(clothing);
        foreach (var src in sources)
        {
            foreach (var r in src.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var bones = r.bones;
                if (bones == null)
                    continue;
                foreach (var b in bones)
                    if (b)
                        names.Add(b.name);
            }
        }

        var ids = new HashSet<int>();
        foreach (var t in srcHips.GetComponentsInChildren<Transform>(true))
        {
            if (!names.Contains(t.name))
                continue;
            var p = t;
            while (p && ids.Add(p.GetInstanceID()) && p != srcHips)
                p = p.parent;
        }
        ids.Add(srcHips.GetInstanceID());
        return ids;
    }

    private static Transform CloneBone(Transform src, Transform parent, Mannequin m, HashSet<int> needed)
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
        {
            var child = src.GetChild(i);
            if (needed.Contains(child.GetInstanceID()))
                CloneBone(child, t, m, needed);
        }
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

        var handOffset = entry.HandAdjust;
        var headOffset = entry.HeadAdjust;
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
        public string OutfitKey;
        public readonly List<GameObject> Owned = new();
        public readonly List<SkinnedMeshRenderer> FillerRenderers = new();
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
