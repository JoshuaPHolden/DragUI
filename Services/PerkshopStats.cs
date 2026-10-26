using System.Globalization;
using System.Text.RegularExpressions;
using ProjectM;
using ProjectM.Network;
using UnityEngine;

namespace DragUI.Services;

// Stat perks bought from the PerkShop server mod. PerkShop puts them in a "carrier" buff the server fills
// in (default SetBonus_ShieldPowerAndHealingReceived_T08), so like Bloodcraft's they never reach the
// client. The player command ".perk status" lists them, one System message per page:
//   === PerkShop Status Page 1/2 ===
//   Purchased Stats:
//     critchance x3 = +0.15 PhysicalCriticalStrikeChance
//   Admin Flat Stats:
//     PhysicalPower +10
//   Next page: .perk status 2
// We send it ourselves (replies hidden), follow the pages, and hand the totals to the character sheet.
// No reply within ReplySeconds = the server doesn't run PerkShop. Perk *buffs* are ordinary game buffs
// the sheet already reads.
internal static class PerkshopStats
{
    /// <summary>PerkShop's default stat carrier buff (configurable on the server).</summary>
    public const int CarrierBuffGuid = -809648681;

    const double ReplySeconds = 6;
    const int MaxPages = 6;

    public enum State { Unknown, Checking, Present, Absent }

    public sealed record Perk(UnitStatType Stat, float Value, string Source);

    static readonly Regex _header = new(@"^=== PerkShop Status Page (?<page>\d+)/(?<pages>\d+) ===", RegexOptions.Compiled);
    static readonly Regex _purchased = new(@"^\s*(?<key>\S+) x(?<n>\d+) = \+?(?<v>-?[\d.,]+) (?<stat>\w+)\s*$", RegexOptions.Compiled);
    static readonly Regex _adminFlat = new(@"^\s*(?<stat>\w+) \+?(?<v>-?[\d.,]+)\s*$", RegexOptions.Compiled);
    static readonly Regex _richText = new("<[^>]*>", RegexOptions.Compiled);

    static readonly List<Perk> _collecting = new();
    static List<Perk> _perks = new();
    static string _section;
    static double _sentAt = double.NegativeInfinity;

    public static State Status { get; private set; } = State.Unknown;
    public static IReadOnlyList<Perk> Perks => _perks;
    public static double LastSyncAt { get; private set; } = double.NegativeInfinity;

    /// <summary>Ask the server for the player's perks (the reply is hidden).</summary>
    public static void Sync() => RequestPage(1);

    static void RequestPage(int page)
    {
        // A section can carry on to the next page without its heading, so it's only reset on page 1.
        if (page == 1)
        {
            _collecting.Clear();
            _section = null;
        }
        _sentAt = Time.realtimeSinceStartupAsDouble;
        if (Status != State.Present) Status = State.Checking;
        ChatCommandSender.TrySend(page == 1 ? ".perk status" : $".perk status {page}");
    }

    public static void Tick()
    {
        if (Status == State.Checking && Time.realtimeSinceStartupAsDouble - _sentAt > ReplySeconds)
        {
            Status = State.Absent;
            Core.Log.LogInfo("PerkShop: no reply to '.perk status' - not on this server.");
        }
    }

    /// <summary>True = hide this message (a reply to our own request).</summary>
    public static bool OnServerMessage(ServerChatMessageType type, string message)
    {
        if (type != ServerChatMessageType.System || string.IsNullOrEmpty(message)) return false;
        if (Time.realtimeSinceStartupAsDouble - _sentAt > ReplySeconds) return false;
        string text = _richText.Replace(message, string.Empty);
        var header = _header.Match(text);
        if (!header.Success) return false;

        int page = int.Parse(header.Groups["page"].Value), pages = int.Parse(header.Groups["pages"].Value);
        foreach (var raw in text.Split('\n'))
        {
            string line = raw.TrimEnd();
            if (line.EndsWith(':') && !line.StartsWith(' ')) { _section = line.TrimEnd(':'); continue; }
            if (_section == "Purchased Stats" && _purchased.Match(line) is { Success: true } p)
                Add(p.Groups["stat"].Value, p.Groups["v"].Value, $"PerkShop {p.Groups["key"].Value} x{p.Groups["n"].Value}");
            else if (_section == "Admin Flat Stats" && _adminFlat.Match(line) is { Success: true } a)
                Add(a.Groups["stat"].Value, a.Groups["v"].Value, "PerkShop admin grant");
        }

        if (page < pages && page < MaxPages)
        {
            RequestPage(page + 1);
        }
        else
        {
            _perks = new List<Perk>(_collecting);
            Status = State.Present;
            LastSyncAt = Time.realtimeSinceStartupAsDouble;
            _sentAt = double.NegativeInfinity;
            Core.Log.LogInfo($"PerkShop: {_perks.Count} stat perk(s) read.");
        }
        return true;
    }

    static void Add(string statName, string value, string source)
    {
        if (!Enum.TryParse(statName, ignoreCase: true, out UnitStatType stat)) return;
        if (!float.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out float v)) return;
        _collecting.Add(new Perk(stat, v, source));
    }

    public static void OnWorldTeardown()
    {
        Status = State.Unknown;
        _perks = new List<Perk>();
        _collecting.Clear();
        _sentAt = double.NegativeInfinity;
        LastSyncAt = double.NegativeInfinity;
    }
}
