using System.Text.Json;
using System.Text.RegularExpressions;
using ProjectM.Network;
using UnityEngine;

namespace DragUI.Services;

// The player's ScarletTeleports teleports: their personal ones (.stp stp to set one where they stand,
// .stp rtp to remove it) and the server's global ones, read from .stp ltp (its replies are hidden while
// reading). Any of them can have a hotkey. Binds are saved per character in
// BepInEx/config/DragUI/teleports-<character>.json, together with the personal-teleport limit: the server
// only tells a player the limit when it's reached ("maximum number of teleports (N)"), so until then the
// window assumes ScarletTeleports' default of 3.
internal static class TeleportStore
{
    public const int DefaultMax = 3;
    const double QuietSeconds = 0.5;   // a listing is complete once nothing more arrives for this long
    const double TimeoutSeconds = 4;
    const double RefreshDelaySeconds = 0.6;
    const double ResetDelaySeconds = 1.0; // between removing and setting again when moving one here

    sealed class SaveData
    {
        public int MaxTeleports { get; set; }
        public Dictionary<string, string> Hotkeys { get; set; } = new();
    }

    enum Section { None, Personal, Global }

    static readonly Regex _richText = new("<[^>]*>", RegexOptions.Compiled);
    static readonly Regex _set = new(@"^Teleport (?<name>.+) set at your position\.$", RegexOptions.Compiled);
    static readonly Regex _removed = new(@"^Teleport (?<name>.+) removed successfully\.$", RegexOptions.Compiled);
    static readonly Regex _max = new(@"maximum number of teleports \((?<max>\d+)\)", RegexOptions.Compiled);
    static readonly Regex _teleported = new(@"^Teleported to (?<name>.+) successfully\.$", RegexOptions.Compiled);

    static readonly List<string> _personal = new();
    static readonly List<string> _global = new();
    static readonly List<string> _readPersonal = new();
    static readonly List<string> _readGlobal = new();
    static Section _section;
    static double _listingSince, _lastReplyAt, _refreshAt, _setAt;
    static bool _gotReply;
    static string _setName;
    static SaveData _data;
    static string _path;

    public static IReadOnlyList<string> Personal => _personal;
    public static IReadOnlyList<string> Global => _global;
    /// <summary>True once the list has been read this session.</summary>
    public static bool Listed { get; private set; }
    public static bool Listing => _listingSince > 0;
    /// <summary>The server's personal-teleport limit, once it has said (null until then).</summary>
    public static int? KnownMax => EnsureLoaded() && _data.MaxTeleports > 0 ? _data.MaxTeleports : null;
    /// <summary>Rows to show for personal teleports: the limit if known, else the default plus room to try one more.</summary>
    public static int SlotCount => KnownMax ?? Math.Max(DefaultMax, _personal.Count + 1);
    public static string Status { get; private set; } = string.Empty;

    /// <summary>Raised when the lists, limit or hotkeys change.</summary>
    public static event Action Changed;

    /// <summary>Read the teleport list from the server (.stp ltp).</summary>
    public static void Refresh()
    {
        if (Listing || !Core.InGame) return;
        if (!ChatCommandSender.TrySend(".stp ltp")) return;
        _listingSince = Time.realtimeSinceStartupAsDouble;
        _section = Section.None;
        _readPersonal.Clear();
        _readGlobal.Clear();
        _gotReply = false;
    }

    public static void Teleport(string name)
    {
        if (ChatCommandSender.TrySend($".stp tp {Quote(name)}")) Status = $"Teleporting to {name}...";
    }

    /// <summary>Set a new personal teleport where the player stands.</summary>
    public static bool Set(string name)
    {
        name = Clean(name);
        if (name.Length == 0) { Status = "Type a name for the teleport first."; return false; }
        if (_personal.Contains(name, StringComparer.OrdinalIgnoreCase)) { Status = $"You already have a teleport called {name}."; return false; }
        if (!ChatCommandSender.TrySend($".stp stp {Quote(name)}")) return false;
        Status = $"Setting {name} here...";
        return true;
    }

    /// <summary>Move a personal teleport to where the player stands: remove it, then set it again (same name, same hotkey).</summary>
    public static void ResetHere(string name)
    {
        if (!ChatCommandSender.TrySend($".stp rtp {Quote(name)}")) return;
        _setName = name;
        _setAt = Time.realtimeSinceStartupAsDouble + ResetDelaySeconds;
        Status = $"Moving {name} here...";
    }

    public static void Remove(string name)
    {
        if (!ChatCommandSender.TrySend($".stp rtp {Quote(name)}")) return;
        if (EnsureLoaded() && _data.Hotkeys.Remove(name)) Save();
        Status = $"Removing {name}...";
    }

    public static Hotkey GetHotkey(string name) =>
        EnsureLoaded() && _data.Hotkeys.TryGetValue(name, out var text) && Hotkey.TryParse(text, out var hotkey) ? hotkey : Hotkey.None;

    /// <summary>Bind <paramref name="hotkey"/> to a teleport (None clears it). A key used by another teleport
    /// moves to this one; one used by a familiar or the V Blood list isn't taken: returns why, else null.</summary>
    public static string SetHotkey(string name, Hotkey hotkey)
    {
        if (!EnsureLoaded()) return "Not in game.";
        if (!hotkey.IsNone)
        {
            string other = HotkeyOwners.UsedByOther(hotkey, HotkeyOwners.Feature.Teleport);
            if (other != null) return $"{hotkey} is already {other}. Pick another key.";
            foreach (var bound in _data.Hotkeys.Where(h => h.Value == hotkey.ToString()).Select(h => h.Key).ToList())
                _data.Hotkeys.Remove(bound);
            _data.Hotkeys[name] = hotkey.ToString();
        }
        else _data.Hotkeys.Remove(name);
        Save();
        Changed?.Invoke();
        return null;
    }

    /// <summary>All teleport binds (for conflict checks).</summary>
    public static IEnumerable<(string Name, Hotkey Hotkey)> Hotkeys =>
        EnsureLoaded() ? _data.Hotkeys.Select(h => (h.Key, Hotkey.TryParse(h.Value, out var k) ? k : Hotkey.None)) : Enumerable.Empty<(string, Hotkey)>();

    /// <summary>The teleport whose hotkey was pressed this frame, if any.</summary>
    public static bool TryGetPressed(out string name)
    {
        foreach (var (teleport, hotkey) in Hotkeys)
        {
            if (!hotkey.IsDown()) continue;
            name = teleport;
            return true;
        }
        name = null;
        return false;
    }

    public static void Tick()
    {
        double now = Time.realtimeSinceStartupAsDouble;
        if (Listing && (_gotReply ? now - _lastReplyAt >= QuietSeconds : now - _listingSince >= TimeoutSeconds))
        {
            _listingSince = 0;
            if (_gotReply)
            {
                _personal.Clear();
                _personal.AddRange(_readPersonal);
                _global.Clear();
                _global.AddRange(_readGlobal);
                Listed = true;
            }
            else Status = "ScarletTeleports didn't answer .stp ltp.";
            Changed?.Invoke();
        }
        if (_setAt > 0 && now >= _setAt)
        {
            _setAt = 0;
            if (ChatCommandSender.TrySend($".stp stp {Quote(_setName)}")) Status = $"Setting {_setName} here...";
        }
        if (_refreshAt > 0 && now >= _refreshAt && !Listing)
        {
            _refreshAt = 0;
            Refresh();
        }
    }

    /// <summary>Read ScarletTeleports' replies. True = part of a listing we asked for: hide it from chat.</summary>
    public static bool OnServerMessage(ServerChatMessageType type, string message)
    {
        if (type != ServerChatMessageType.System || string.IsNullOrEmpty(message)) return false;
        string text = _richText.Replace(message, string.Empty).Trim();
        double now = Time.realtimeSinceStartupAsDouble;

        if (Listing)
        {
            bool ours = true;
            if (text == "Available Teleports:" || text == "No teleports available.") _section = Section.None;
            else if (text == "Personal") _section = Section.Personal;
            else if (text == "Global") _section = Section.Global;
            else if (text.StartsWith("•", StringComparison.Ordinal) && _section != Section.None)
                (_section == Section.Personal ? _readPersonal : _readGlobal).Add(text[1..].Trim());
            else ours = false;
            if (ours)
            {
                _gotReply = true;
                _lastReplyAt = now;
                return true;
            }
        }

        Match match;
        if ((match = _set.Match(text)).Success)
        {
            Status = $"{match.Groups["name"].Value} set here.";
            _refreshAt = now + RefreshDelaySeconds;
        }
        else if ((match = _removed.Match(text)).Success)
        {
            if (_setAt == 0) Status = $"{match.Groups["name"].Value} removed.";
            _refreshAt = now + RefreshDelaySeconds;
        }
        else if ((match = _max.Match(text)).Success && int.TryParse(match.Groups["max"].Value, out int max))
        {
            Status = $"The server allows {max} personal teleports.";
            if (EnsureLoaded() && _data.MaxTeleports != max)
            {
                _data.MaxTeleports = max;
                Save();
                Changed?.Invoke();
            }
        }
        else if ((match = _teleported.Match(text)).Success) Status = $"Teleported to {match.Groups["name"].Value}.";
        else if (text.StartsWith("You must wait", StringComparison.Ordinal) || text.StartsWith("You cannot teleport", StringComparison.Ordinal)
                 || text.StartsWith("You do not have enough", StringComparison.Ordinal) || text.StartsWith("You already have a teleport", StringComparison.Ordinal)
                 || text.StartsWith("You cannot set a teleport", StringComparison.Ordinal))
        {
            Status = text;
        }
        return false;
    }

    // VCF splits arguments on spaces, so a name with spaces goes in quotes (and can't contain quotes).
    static string Quote(string name) => name.Contains(' ') ? $"\"{name}\"" : name;
    static string Clean(string name) => Regex.Replace((name ?? string.Empty).Replace("\"", string.Empty), @"\s+", " ").Trim();

    static bool EnsureLoaded()
    {
        if (_data != null) return true;
        var user = Core.LocalUser;
        if (user == Unity.Entities.Entity.Null || !user.Has<User>()) return false;
        string character = user.Read<User>().CharacterName.Value;
        if (string.IsNullOrWhiteSpace(character)) return false;

        string safe = string.Concat(character.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        _path = Path.Combine(Plugin.DataDirectory, $"teleports-{safe}.json");
        _data = new SaveData();
        try
        {
            if (File.Exists(_path)) _data = JsonSerializer.Deserialize<SaveData>(File.ReadAllText(_path)) ?? new SaveData();
        }
        catch (Exception ex)
        {
            Core.Log.LogWarning($"Couldn't read {_path}: {ex.Message}");
        }
        _data.Hotkeys ??= new();
        return true;
    }

    static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            File.WriteAllText(_path, JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Core.Log.LogWarning($"Couldn't save {_path}: {ex.Message}");
        }
    }

    public static void OnWorldTeardown()
    {
        _personal.Clear();
        _global.Clear();
        _listingSince = _refreshAt = _setAt = 0;
        Listed = false;
        Status = string.Empty;
        _data = null;
        _path = null;
    }
}

// Who else uses a hotkey, so one feature's bind can't silently clash with another's.
internal static class HotkeyOwners
{
    public enum Feature { Familiar, Teleport }

    /// <summary>"familiar Alpha's hotkey" / "the Home teleport's hotkey" / "the V Blood list key", or null if
    /// nothing outside <paramref name="self"/> uses it.</summary>
    public static string UsedByOther(Hotkey hotkey, Feature self)
    {
        if (hotkey.IsNone) return null;
        if (Settings.ToggleKey != KeyCode.None && hotkey == new Hotkey(Settings.ToggleKey)) return "the V Blood list key";
        if (self != Feature.Familiar)
        {
            var familiar = FamiliarStore.Entries.FirstOrDefault(e => e.Hotkey == hotkey);
            if (familiar != null) return $"familiar {familiar.Name}'s hotkey";
        }
        if (self != Feature.Teleport)
        {
            var teleport = TeleportStore.Hotkeys.FirstOrDefault(t => t.Hotkey == hotkey);
            if (teleport.Name != null) return $"the {teleport.Name} teleport's hotkey";
        }
        return null;
    }
}
