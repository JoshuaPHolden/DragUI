using System.Text.RegularExpressions;
using ProjectM.Network;
using DragUI.Data;

namespace DragUI.Services;

// Starts a respawn countdown when a server chat message announces a V Blood kill (kill-feed /
// announcement mods, e.g. "Player has slain Alpha the White Wolf!"). Formats differ between mods, so
// rather than one fixed pattern a message counts as a kill when it names exactly one V Blood AND
// contains a kill word somewhere outside that name.
internal static class ChatKillFeed
{
    static readonly Regex _richText = new("<[^>]*>", RegexOptions.Compiled);
    static readonly Regex _whitespace = new(@"\s+", RegexOptions.Compiled);

    // Nicknames people and mods use that aren't derivable from the catalog name.
    static readonly Dictionary<string, string[]> _extraAliases = new()
    {
        ["CHAR_Forest_Wolf_VBlood"] = new[] { "Alpha Wolf", "White Wolf" },
        ["CHAR_Vermin_DireRat_VBlood"] = new[] { "Putrid Rat" },
        ["CHAR_Bandit_Tourok_VBlood"] = new[] { "Quincy" },
        ["CHAR_WerewolfChieftain_Human"] = new[] { "Wilfred" },
        ["CHAR_Spider_Queen_VBlood"] = new[] { "Spider Queen" },
        ["CHAR_BatVampire_VBlood"] = new[] { "Styx" },
        ["CHAR_Cursed_ToadKing_VBlood"] = new[] { "Duke of Balaton" },
        ["CHAR_Vampire_Dracula_VBlood"] = new[] { "Dracula" },
        ["CHAR_VHunter_CastleMan"] = new[] { "Simon Belmont" },
        ["CHAR_Manticore_VBlood"] = new[] { "Winged Horror" },
        ["CHAR_Gloomrot_Monster_VBlood"] = new[] { "Adam" },
    };

    // Titles that announcements often drop ("for beating Valencia!").
    static readonly string[] _titles = { "General ", "Sir ", "Lord " };

    // Built in, so new words reach players whose config already exists. ExtraChatKillWords adds to these.
    const string BuiltInKillWords =
        "killed,kills,slain,slew,slays,defeated,defeats,vanquished,felled,fallen,destroyed,conquered,died," +
        "downed,took down,taken down,beat,beats,beaten,beating";

    sealed record Alias(VBloodInfo Info, string Text, bool IsFullName, Regex Pattern, Regex PrimalPattern);

    static readonly List<Alias> _aliases = BuildAliases();
    static Regex _killWords;
    static string _killWordsSource;
    static HashSet<ServerChatMessageType> _channels;
    static string _channelsSource;

    static readonly Dictionary<string, double> _recentMessages = new();
    static readonly HashSet<ServerChatMessageType> _loggedChannels = new();

    static List<Alias> BuildAliases()
    {
        var aliases = new List<Alias>();
        foreach (var info in VBloodCatalog.All)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { info.Name };
            int the = info.Name.IndexOf(" the ", StringComparison.OrdinalIgnoreCase);
            if (the > 0) names.Add(info.Name[..the]); // "Alpha", "Sir Magnus", "Henry Blackbrew"
            foreach (var name in names.ToList()) // "Valencia", "Magnus", "Styx"
                foreach (var title in _titles)
                    if (name.StartsWith(title, StringComparison.OrdinalIgnoreCase)) names.Add(name[title.Length..]);
            if (_extraAliases.TryGetValue(info.PrefabName, out var extra)) names.UnionWith(extra);

            foreach (var name in names.Where(n => n.Length >= 3))
            {
                string escaped = Regex.Escape(name.ToLowerInvariant());
                aliases.Add(new Alias(info, name, string.Equals(name, info.Name, StringComparison.OrdinalIgnoreCase),
                    new Regex($@"\b{escaped}\b", RegexOptions.Compiled),
                    new Regex($@"\bprimal\s+{escaped}\b", RegexOptions.Compiled)));
            }
        }
        // Longest first, so "Alpha the White Wolf" is consumed before "Alpha".
        return aliases.OrderByDescending(a => a.Text.Length).ToList();
    }

    public static void OnServerMessage(ServerChatMessageType type, string message, long timeUtc)
    {
        if (!Settings.ReadChatKills || string.IsNullOrWhiteSpace(message)) return;

        string text = _whitespace.Replace(_richText.Replace(message, " "), " ").Trim();
        string lower = text.ToLowerInvariant();

        // The same announcement can be relayed more than once; only count it once.
        double now = VBloodTracker.Now;
        if (_recentMessages.TryGetValue(lower, out double seenAt) && now - seenAt < 30) return;
        _recentMessages[lower] = now;
        if (_recentMessages.Count > 200) PruneRecent(now);

        // Full boss names first: a message with exactly one full V Blood name is about that boss, even if a
        // player's name happens to be a short alias ("Ben", "Adam"). Short names/nicknames are a fallback.
        VBloodInfo match = null;
        string remainder = lower;
        foreach (bool fullNames in new[] { true, false })
        {
            foreach (var alias in _aliases)
            {
                if (alias.IsFullName != fullNames || !alias.Pattern.IsMatch(remainder)) continue;
                if (alias.PrimalPattern.IsMatch(remainder)) // "Primal Rufus" (Bloodcraft) isn't the V Blood itself
                {
                    remainder = alias.PrimalPattern.Replace(remainder, " ");
                    continue;
                }
                if (match != null && match != alias.Info)
                {
                    Core.Diag($"Chat names more than one V Blood, ignored: \"{text}\"");
                    return;
                }
                match = alias.Info;
                remainder = alias.Pattern.Replace(remainder, " "); // so "Undead General" can't supply "dead"
            }
            if (match != null) break;
        }
        if (match == null) return;

        if (!KillWords().IsMatch(remainder))
        {
            Core.Diag($"Chat mentions {match.Name} but no kill word, ignored: \"{text}\"");
            return;
        }

        if (!Channels().Contains(type))
        {
            if (_loggedChannels.Add(type))
                Core.Log.LogInfo($"Kill announcement on the {type} channel ignored (not in ChatKillChannels): \"{text}\". " +
                                 $"Add {type} to ChatKillChannels if this is how your server announces kills.");
            return;
        }

        VBloodTracker.OnChatKill(match, MessageAgeSeconds(timeUtc), text);
    }

    /// <summary>How long ago the message was sent, if its timestamp is plausible (else 0).</summary>
    static double MessageAgeSeconds(long timeUtc)
    {
        if (timeUtc <= 0) return 0;
        DateTimeOffset sent;
        try
        {
            // Accept Unix seconds, Unix milliseconds or .NET ticks; anything else is ignored.
            if (timeUtc < 10_000_000_000L) sent = DateTimeOffset.FromUnixTimeSeconds(timeUtc);
            else if (timeUtc < 10_000_000_000_000L) sent = DateTimeOffset.FromUnixTimeMilliseconds(timeUtc);
            else sent = new DateTimeOffset(timeUtc, TimeSpan.Zero);
        }
        catch (ArgumentOutOfRangeException)
        {
            return 0;
        }
        double age = (DateTimeOffset.UtcNow - sent).TotalSeconds;
        return age is > 0 and < 3600 ? age : 0;
    }

    static Regex KillWords()
    {
        string source = Settings.ChatKillWords;
        if (_killWords != null && source == _killWordsSource) return _killWords;
        var words = (BuiltInKillWords + "," + source)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(w => Regex.Escape(w.ToLowerInvariant()))
            .Distinct();
        _killWords = new Regex($@"\b({string.Join("|", words)})\b", RegexOptions.Compiled);
        _killWordsSource = source;
        return _killWords;
    }

    static HashSet<ServerChatMessageType> Channels()
    {
        string source = Settings.ChatKillChannels;
        if (_channels != null && source == _channelsSource) return _channels;
        _channels = new HashSet<ServerChatMessageType>();
        foreach (var name in source.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Enum.TryParse(name, ignoreCase: true, out ServerChatMessageType type)) _channels.Add(type);
            else Core.Log.LogWarning($"ChatKillChannels: unknown channel '{name}' (use System, Global, Region, Team, Local, Lore).");
        }
        _channelsSource = source;
        return _channels;
    }

    static void PruneRecent(double now)
    {
        foreach (var key in _recentMessages.Where(kv => now - kv.Value > 30).Select(kv => kv.Key).ToList())
            _recentMessages.Remove(key);
    }

    public static void OnWorldTeardown() => _recentMessages.Clear();
}
