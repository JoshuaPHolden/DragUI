using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using BepInEx;
using ProjectM.Network;
using UnityEngine;

namespace DragUI.Services;

// Experience per hour from Bloodcraft's leveling log, split into quests, trash mobs and V Bloods and
// grouped by Exo/Experience prestige (a prestige or exo starts a new group; old ones are kept).
//
// Bloodcraft only reports gains with its log on (".lvl log"): "+123 experience (45%)" (or "+123 rested
// experience ..."). The line doesn't say where it came from, so:
//   * quest:   the gain arrives together with "You've been awarded N% of your total experience!";
//   * V Blood: the biggest gain within a few seconds of a V Blood dying near the player (VBloodTracker);
//   * mobs:    everything else.
// Gains are held for HoldSeconds so the quest line / V Blood death can arrive in either order.
// Time only counts while gaining: gaps longer than IdleMinutes without XP aren't counted as play time.
// The prestige levels come from Bloodcraft's messages; when unknown, ".lvl get" / ".prestige get exo"
// are sent once and their replies hidden. Saved per character in BepInEx/config/DragUI/xp-<name>.json.
internal static class ExperienceTracker
{
    public enum Source { Quest, Mobs, VBlood }

    public sealed class Group
    {
        public int? Exo { get; set; }
        public int? Prestige { get; set; }
        public double ActiveSeconds { get; set; }
        public double Quest { get; set; }
        public double Mobs { get; set; }
        public double VBlood { get; set; }
        public DateTime Started { get; set; }

        [JsonIgnore] public double Total => Quest + Mobs + VBlood;
        public double PerHour(double xp) => ActiveSeconds >= MinRateSeconds ? xp * 3600 / ActiveSeconds : 0;

        // Exo only comes after max prestige, so an exo row doesn't need the prestige.
        [JsonIgnore] public string Label => Exo is > 0
            ? $"Exo {Exo}"
            : $"Prestige {Prestige?.ToString() ?? "?"}{(Exo == null ? " (exo ?)" : "")}";
    }

    sealed class SaveData
    {
        public int? Exo { get; set; }
        public int? Prestige { get; set; }
        public List<Group> Groups { get; set; } = new();
    }

    sealed class Gain
    {
        public double Amount;
        public double At;
        public bool Quest;
    }

    const double HoldSeconds = 3;           // wait for a quest line / V Blood death before classifying
    const double VBloodWindowSeconds = 6;   // a gain this close to a nearby V Blood death may be its XP
    const double QuestWindowSeconds = 1;    // the quest line follows its gain in the same server tick
    const double SyncReplySeconds = 5;      // replies to our own sync commands within this are hidden
    const double MinRateSeconds = 60;       // no rate until a minute of active time
    const double SaveIntervalSeconds = 30;

    static readonly Regex _richText = new("<[^>]*>", RegexOptions.Compiled);
    static readonly Regex _gain = new(@"^\+\s*(?<xp>[\d,]+)\s+(?:rested\s+|bonus\s+)*experience\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex _questAward = new(@"^You've been awarded [\d.,]+% of your total experience", RegexOptions.Compiled);
    static readonly Regex _prestiged = new(@"(?:^You have|has) prestiged in Experience\[(?<n>\d+)\]!", RegexOptions.Compiled);
    static readonly Regex _exoDone = new(@"^Exo\[(?<n>\d+)\] prestige complete!", RegexOptions.Compiled);
    static readonly Regex _levelReply = new(@"^You're level \[\d+\]\[(?<p>\d+)\] with ", RegexOptions.Compiled);
    static readonly Regex _exoReply = new(@"^Current Exo Prestige Level: (?<n>\d+)/", RegexOptions.Compiled);

    static readonly List<Gain> _pending = new();
    static readonly List<(double At, bool Used)> _vbloodDeaths = new();
    static SaveData _data;
    static string _character;
    static string _path;
    static bool _dirty;
    static double _nextSaveAt;
    static double _lastGainAt = double.NegativeInfinity;
    static double _lastTickAt;
    static double _syncSentAt = double.NegativeInfinity;
    static bool _syncedThisSession;
    static bool _loggedFirstGain;
    static int _unmatchedLogged;

    /// <summary>Bloodcraft's XP log state as last reported (null = not seen this session).</summary>
    public static bool? LogEnabled { get; private set; }

    public static bool Ready => _data != null;

    /// <summary>The group XP is going to now (newest).</summary>
    public static Group Current => _data?.Groups.Count > 0 ? _data.Groups[^1] : null;

    /// <summary>All groups, newest first.</summary>
    public static IEnumerable<Group> Groups => _data == null ? Enumerable.Empty<Group>() : Enumerable.Reverse(_data.Groups);

    public static double CurrentRate => Current is { } g ? g.PerHour(g.Total) : 0;

    // ---------------------------------------------------------------- chat

    /// <summary>Follow Bloodcraft's leveling messages. True = hide this message from chat.</summary>
    public static bool OnServerMessage(ServerChatMessageType type, string message)
    {
        if (type != ServerChatMessageType.System || string.IsNullOrEmpty(message) || !EnsureLoaded()) return false;
        string text = _richText.Replace(message, string.Empty).Trim();
        double now = Time.realtimeSinceStartupAsDouble;
        bool ownSync = now - _syncSentAt < SyncReplySeconds;

        Match match;
        // "+X rested experience earned from being logged out..." is banked bonus, not a gain.
        if ((match = _gain.Match(text)).Success && !text.Contains("logged out", StringComparison.OrdinalIgnoreCase))
        {
            if (double.TryParse(match.Groups["xp"].Value.Replace(",", ""), out double xp) && xp > 0)
            {
                _pending.Add(new Gain { Amount = xp, At = now });
                LogEnabled = true;
                if (!_loggedFirstGain) Core.Log.LogInfo($"XP tracking: first gain this session (\"{text}\").");
                _loggedFirstGain = true;
                if (!_syncedThisSession) Sync();
            }
            return Settings.HideXpMessages;
        }
        if (_questAward.IsMatch(text))
        {
            // The quest's own gain line came just before this one.
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                if (now - _pending[i].At > QuestWindowSeconds) break;
                if (_pending[i].Quest) continue;
                _pending[i].Quest = true;
                break;
            }
            return false;
        }
        if ((match = _prestiged.Match(text)).Success)
        {
            StartGroup(Current?.Exo, int.Parse(match.Groups["n"].Value));
            return false;
        }
        if ((match = _exoDone.Match(text)).Success)
        {
            StartGroup(int.Parse(match.Groups["n"].Value), Current?.Prestige);
            return false;
        }
        if ((match = _levelReply.Match(text)).Success)
        {
            Learn(Current?.Exo, int.Parse(match.Groups["p"].Value));
            return ownSync;
        }
        if ((match = _exoReply.Match(text)).Success)
        {
            Learn(int.Parse(match.Groups["n"].Value), Current?.Prestige);
            return ownSync;
        }
        if (text.StartsWith("You have not prestiged in Exo yet", StringComparison.Ordinal)
            || text.StartsWith("Exo prestiging is not enabled", StringComparison.Ordinal))
        {
            Learn(0, Current?.Prestige);
            return ownSync;
        }
        if (ownSync && (text.StartsWith("Enough charge to maintain form", StringComparison.Ordinal)
                        || text.StartsWith("Not enough charge", StringComparison.Ordinal)))
            return true;
        if (text.StartsWith("Level logging enabled", StringComparison.OrdinalIgnoreCase)) { LogEnabled = true; return false; }
        if (text.StartsWith("Level logging disabled", StringComparison.OrdinalIgnoreCase)) { LogEnabled = false; return false; }

        // Evidence for formats we don't know (another Bloodcraft version): log a few per session.
        if (_unmatchedLogged < 5 && text.Contains("experience", StringComparison.OrdinalIgnoreCase))
        {
            _unmatchedLogged++;
            Core.Log.LogInfo($"XP tracking: unrecognised message mentioning experience: \"{text}\"");
        }
        return false;
    }

    /// <summary>A V Blood was seen dying near the player (its XP may be one of the gains around now).</summary>
    public static void OnVBloodKilledNearby()
    {
        if (_data == null) return;
        _vbloodDeaths.Add((Time.realtimeSinceStartupAsDouble, false));
    }

    /// <summary>Turn Bloodcraft's XP log on/off (its reply tells us which it now is).</summary>
    public static void ToggleBloodcraftLog() => ChatCommandSender.TrySend(".lvl log");

    /// <summary>Ask Bloodcraft for the current prestige levels (replies are hidden).</summary>
    public static void Sync()
    {
        _syncedThisSession = true;
        _syncSentAt = Time.realtimeSinceStartupAsDouble;
        ChatCommandSender.TrySend(".lvl get");
        if (Current?.Exo == null) ChatCommandSender.TrySend(".prestige get exo");
    }

    // ---------------------------------------------------------------- per frame

    public static void Tick()
    {
        double now = Time.realtimeSinceStartupAsDouble;
        double dt = _lastTickAt > 0 ? Math.Min(now - _lastTickAt, 1) : 0;
        _lastTickAt = now;
        if (!EnsureLoaded()) return;

        // Play time only counts while XP is coming in.
        if (now - _lastGainAt < Settings.XpIdleMinutes * 60 && Current is { } current)
        {
            current.ActiveSeconds += dt;
            _dirty = true;
        }

        while (_pending.Count > 0 && now - _pending[0].At >= HoldSeconds)
        {
            var gain = _pending[0];
            _pending.RemoveAt(0);
            Record(gain, Classify(gain));
        }
        _vbloodDeaths.RemoveAll(d => now - d.At > VBloodWindowSeconds + HoldSeconds + 5);

        if (_dirty && now >= _nextSaveAt) Save();
    }

    static Source Classify(Gain gain)
    {
        if (gain.Quest) return Source.Quest;
        for (int i = 0; i < _vbloodDeaths.Count; i++)
        {
            var death = _vbloodDeaths[i];
            if (death.Used || Math.Abs(gain.At - death.At) > VBloodWindowSeconds) continue;
            // Trash dying alongside the boss gives XP too: only the biggest gain near the death is the boss's.
            bool biggest = _pending.All(p => p.Quest || Math.Abs(p.At - death.At) > VBloodWindowSeconds || p.Amount <= gain.Amount);
            if (!biggest) continue;
            _vbloodDeaths[i] = (death.At, true);
            return Source.VBlood;
        }
        return Source.Mobs;
    }

    static void Record(Gain gain, Source source)
    {
        var group = Current ?? StartGroup(_data.Exo, _data.Prestige);
        switch (source)
        {
            case Source.Quest: group.Quest += gain.Amount; break;
            case Source.VBlood: group.VBlood += gain.Amount; break;
            default: group.Mobs += gain.Amount; break;
        }
        // The gap since the previous gain counts as play time only while it's short (see Tick).
        _lastGainAt = Math.Max(_lastGainAt, gain.At);
        _dirty = true;
        Core.Diag($"XP +{gain.Amount} ({source}) -> {group.Label}");
    }

    // ---------------------------------------------------------------- groups

    /// <summary>A prestige or exo: close the current group and start a fresh one.</summary>
    static Group StartGroup(int? exo, int? prestige)
    {
        FlushPending();
        var group = new Group { Exo = exo, Prestige = prestige, Started = DateTime.Now };
        _data.Groups.Add(group);
        _data.Exo = exo;
        _data.Prestige = prestige;
        _dirty = true;
        Save();
        Core.Log.LogInfo($"XP tracking: new group {group.Label}.");
        return group;
    }

    /// <summary>Found out the current levels (no prestige happened): relabel or move to the right group.</summary>
    static void Learn(int? exo, int? prestige)
    {
        if (exo == _data.Exo && prestige == _data.Prestige && Current != null) return;
        var current = Current;
        if (current == null)
        {
            StartGroup(exo, prestige);
            return;
        }

        bool currentUnknown = current.Exo == null || current.Prestige == null;
        var existing = _data.Groups.LastOrDefault(g => g != current && g.Exo == exo && g.Prestige == prestige);
        if (currentUnknown && existing != null)
        {
            // Gains logged before we knew belong to the group we already had for these levels.
            existing.ActiveSeconds += current.ActiveSeconds;
            existing.Quest += current.Quest;
            existing.Mobs += current.Mobs;
            existing.VBlood += current.VBlood;
            _data.Groups.Remove(current);
            _data.Groups.Remove(existing);
            _data.Groups.Add(existing);
        }
        else if (currentUnknown || current.Total == 0)
        {
            current.Exo = exo;
            current.Prestige = prestige;
        }
        else
        {
            // Saved levels were out of date (e.g. prestiged while the mod wasn't watching): new group.
            StartGroup(exo, prestige);
            return;
        }
        _data.Exo = exo;
        _data.Prestige = prestige;
        _dirty = true;
        Save();
    }

    /// <summary>Clear the current group's numbers (same levels).</summary>
    public static void ResetCurrent()
    {
        if (Current is not { } g) return;
        _pending.Clear();
        g.ActiveSeconds = g.Quest = g.Mobs = g.VBlood = 0;
        g.Started = DateTime.Now;
        _dirty = true;
        Save();
    }

    static void FlushPending()
    {
        if (_data == null || Current == null) { _pending.Clear(); return; }
        while (_pending.Count > 0)
        {
            var gain = _pending[0];
            _pending.RemoveAt(0);
            Record(gain, Classify(gain));
        }
    }

    // ---------------------------------------------------------------- persistence

    static bool EnsureLoaded()
    {
        if (_data != null) return true;
        var user = Core.LocalUser;
        if (user == Unity.Entities.Entity.Null || !user.Has<User>()) return false;
        string name = user.Read<User>().CharacterName.Value;
        if (string.IsNullOrWhiteSpace(name)) return false;

        _character = name;
        string safe = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        _path = Path.Combine(Plugin.DataDirectory, $"xp-{safe}.json");
        _data = new SaveData();
        try
        {
            if (File.Exists(_path)) _data = JsonSerializer.Deserialize<SaveData>(File.ReadAllText(_path)) ?? new SaveData();
        }
        catch (Exception ex)
        {
            Core.Log.LogWarning($"Couldn't read {_path}: {ex.Message}");
        }
        Core.Diag($"XP tracking loaded for {_character}: {_data.Groups.Count} groups.");
        return true;
    }

    static void Save()
    {
        _nextSaveAt = Time.realtimeSinceStartupAsDouble + SaveIntervalSeconds;
        if (_data == null || _path == null) return;
        _dirty = false;
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

    /// <summary>Leaving the server/character: save and forget (the next login may be someone else).</summary>
    public static void OnWorldTeardown()
    {
        if (_dirty) Save();
        _data = null;
        _path = null;
        _character = null;
        _pending.Clear();
        _vbloodDeaths.Clear();
        _lastGainAt = double.NegativeInfinity;
        _lastTickAt = 0;
        _syncSentAt = double.NegativeInfinity;
        _syncedThisSession = false;
        _loggedFirstGain = false;
        _unmatchedLogged = 0;
        LogEnabled = null;
    }

    /// <summary>Compact XP number: 950, 12.3k, 1.25M.</summary>
    public static string Format(double xp) =>
        xp >= 1_000_000 ? $"{xp / 1_000_000:0.##}M" : xp >= 10_000 ? $"{xp / 1000:0}k" : xp >= 1000 ? $"{xp / 1000:0.#}k" : $"{xp:0}";
}
