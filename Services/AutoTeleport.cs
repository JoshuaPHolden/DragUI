using System.Text.RegularExpressions;
using ProjectM.Network;
using UnityEngine;

namespace DragUI.Services;

// Auto-accepts ScarletTeleports teleport requests: when "<Name> has requested to teleport to you, you have
// N seconds to respond." arrives, ".stp tpa <Name>" is sent straight away (no queue: BloodCraftEZLife's
// version shared a rate-limited queue with its other commands and re-queued each accept five times, so
// the backlog grew until accepts arrived after the request had expired).
// ScarletTeleports keeps a request pending until it expires, even when an accept fails, so a failure that
// can clear up (the requester in combat, us in Dracula's room / a no-teleport zone) is retried every
// RetrySeconds until it works or expires. Anything else isn't retried, so it can't spam.
internal static class AutoTeleport
{
    const double RetrySeconds = 3;
    const double DefaultExpirySeconds = 30;

    static readonly Regex _richText = new("<[^>]*>", RegexOptions.Compiled);
    static readonly Regex _request = new(@"^(?<name>[^\s.""]\S*) has requested to teleport to you(?:, you have (?<secs>\d+) seconds)?", RegexOptions.Compiled);
    static readonly Regex _requesterInCombat = new(@"^Player (?<name>\S+) is currently in combat and cannot teleport", RegexOptions.Compiled);
    static readonly Regex _teleported = new(@"^Player (?<name>\S+) has teleported to you", RegexOptions.Compiled);
    static readonly Regex _noPending = new(@"^You do not have a pending request from (?<name>\S+?)\.?$", RegexOptions.Compiled);

    sealed class Pending
    {
        public double ExpiresAt;
        public double RetryAt = double.PositiveInfinity; // set when a retryable failure comes back
    }

    static readonly Dictionary<string, Pending> _pending = new(StringComparer.OrdinalIgnoreCase);
    static string _lastAccepted; // whose accept we sent last (for replies that don't name the requester)

    public static bool Enabled => Settings.AutoTeleportEnabled;

    public static void SetEnabled(bool enabled)
    {
        Settings.AutoTeleportEnabled = enabled;
        if (!enabled) _pending.Clear();
        Core.Log.LogInfo($"Auto-accept teleport requests {(enabled ? "on" : "off")}.");
    }

    public static void OnServerMessage(ServerChatMessageType type, string message)
    {
        if (!Enabled || type != ServerChatMessageType.System || string.IsNullOrEmpty(message)) return;
        string text = _richText.Replace(message, string.Empty).Trim();
        double now = Time.realtimeSinceStartupAsDouble;
        Match match;

        if ((match = _request.Match(text)).Success)
        {
            string name = match.Groups["name"].Value;
            if (!Allowed(name))
            {
                Core.Log.LogInfo($"Teleport request from {name} not auto-accepted (not in AutoTeleport.OnlyFrom).");
                return;
            }
            double seconds = int.TryParse(match.Groups["secs"].Value, out int s) ? s : DefaultExpirySeconds;
            _pending[name] = new Pending { ExpiresAt = now + seconds };
            Accept(name);
        }
        else if ((match = _requesterInCombat.Match(text)).Success)
        {
            ScheduleRetry(match.Groups["name"].Value, now);
        }
        else if (text.StartsWith("Teleport failed: you are in", StringComparison.Ordinal) && _lastAccepted != null)
        {
            ScheduleRetry(_lastAccepted, now); // we're in Dracula's room / a restricted zone: maybe we move
        }
        else if ((match = _teleported.Match(text)).Success)
        {
            _pending.Remove(match.Groups["name"].Value);
            Core.Log.LogInfo($"Auto-accepted teleport: {match.Groups["name"].Value} teleported to you.");
        }
        else if ((match = _noPending.Match(text)).Success)
        {
            _pending.Remove(match.Groups["name"].Value);
        }
    }

    public static void Tick()
    {
        if (_pending.Count == 0) return;
        double now = Time.realtimeSinceStartupAsDouble;
        foreach (var (name, pending) in _pending.ToList())
        {
            if (now >= pending.ExpiresAt) _pending.Remove(name);
            else if (now >= pending.RetryAt) Accept(name);
        }
    }

    static void ScheduleRetry(string name, double now)
    {
        if (_pending.TryGetValue(name, out var pending) && now + RetrySeconds < pending.ExpiresAt)
            pending.RetryAt = now + RetrySeconds;
    }

    static void Accept(string name)
    {
        if (_pending.TryGetValue(name, out var pending)) pending.RetryAt = double.PositiveInfinity;
        _lastAccepted = name;
        if (ChatCommandSender.TrySend($".stp tpa {name}")) Core.Diag($"Auto-accepting teleport request from {name}.");
    }

    static bool Allowed(string name)
    {
        string list = Settings.AutoTeleportOnlyFrom;
        if (string.IsNullOrWhiteSpace(list)) return true;
        return list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
    }

    public static void OnWorldTeardown()
    {
        _pending.Clear();
        _lastAccepted = null;
    }
}
