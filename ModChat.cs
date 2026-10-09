using System;
using System.Collections.Generic;
using Bolt;
using HarmonyLib;
using RedLoader;
using TheForest.UI.Multiplayer;
using TheForest.Utils;

namespace SotfModChat;

public static class ModChat
{
    public const string Prefix = "[mod] ";

    private static readonly Dictionary<string, List<Action<ulong, string>>> Handlers = new(StringComparer.OrdinalIgnoreCase);

    public static void On(string mod, Action<ulong, string> handler)
    {
        if (!Handlers.TryGetValue(mod, out var list))
            Handlers[mod] = list = new List<Action<ulong, string>>();
        list.Add(handler);
    }

    public static ulong LocalId
    {
        get
        {
            var entity = LocalPlayer.Entity;
            return entity ? entity.networkId.PackedValue : 0UL;
        }
    }

    public static bool Send(string mod, string text)
    {
        try
        {
            var entity = LocalPlayer.Entity;
            if (!BoltNetwork.isRunning || !entity || !entity.isAttached)
                return false;
            var line = $"{Prefix}{mod}: {text}";
            var box = UnityEngine.Object.FindObjectOfType<ChatBox>();
            if (box)
            {
                box.SendLine(line);
                return true;
            }
            var ev = ChatEvent.Create(GlobalTargets.Everyone);
            ev.Sender = entity.networkId;
            ev.Message = line;
            ev.Send();
            return true;
        }
        catch (Exception e)
        {
            RLog.Warning($"ModChat: send failed: {e.Message}");
            return false;
        }
    }

    public static ulong IdOf(UnityEngine.Component component)
    {
        if (!component)
            return 0UL;
        var entity = component.GetComponentInParent<BoltEntity>();
        return entity && entity.isAttached ? entity.networkId.PackedValue : 0UL;
    }

    private static void Dispatch(ulong sender, string message)
    {
        var body = message.Substring(Prefix.Length);
        var colon = body.IndexOf(':');
        if (colon <= 0)
            return;
        var mod = body.Substring(0, colon).Trim();
        var text = body.Substring(colon + 1).Trim();
        if (!Handlers.TryGetValue(mod, out var list))
            return;
        foreach (var handler in list)
        {
            try
            {
                handler(sender, text);
            }
            catch (Exception e)
            {
                RLog.Error($"ModChat: {mod} handler failed: {e}");
            }
        }
    }

    [HarmonyPatch(typeof(ChatBox), nameof(ChatBox.AddLine))]
    private static class AddLinePatch
    {
        private static bool Prefix(Il2CppSystem.Nullable<NetworkId> playerId, string message)
        {
            if (message == null || !message.StartsWith(ModChat.Prefix, StringComparison.Ordinal))
                return true;
            try
            {
                var sender = playerId != null && playerId.HasValue ? playerId.Value.PackedValue : 0UL;
                if (sender != LocalId)
                {
                    RLog.Msg($"ModChat: received from {sender}: {message}");
                    Dispatch(sender, message);
                }
            }
            catch (Exception e)
            {
                RLog.Error($"ModChat: receive failed: {e}");
            }
            return false;
        }
    }
}
