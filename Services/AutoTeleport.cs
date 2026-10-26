using System.Text.RegularExpressions;
using ProjectM.Network;
using UnityEngine;

namespace DragUI.Services;

// Teleport requests from other players (ScarletTeleports .stp tpr). Every request is tracked until it's
// answered or expires, for the Teleport window's pending list (Accept / Deny) and the control bar's
// notification slot. With auto-accept on, ".stp tpa <Name>" is sent as soon as the request arrives (no
// queue: BloodCraftEZLife's version shared a rate-limited queue with its other commands and re-queued
// each accept five times, so the backlog grew until accepts arrived after the request had expired).
// ScarletTeleports keeps a request pending until it expires, even when an accept fails, so an automatic
// accept that can clear up (the requester in combat, us in Dracula's room / a no-teleport zone) is retried
// every RetrySeconds until it works or expires. Anything else isn't retried, so it can't spam.
internal static class AutoTeleport
{
    const double RetrySeconds = 3;
    const double DefaultExpirySeconds = 30;
    const double AnswerCooldownSeconds = 1;

    static readonly Regex _richText = new("<[^>]*>", RegexOptions.Compiled);
    static readonly Regex _request = new(@"^(?<name>[^\s.""]\S*) has requested to teleport to you(?:, you have (?<secs>\d+) seconds)?", RegexOptions.Compiled);
    static readonly Regex _requesterInCombat = new(@"^Player (?<name>\S+) is currently in combat and cannot teleport", RegexOptions.Compiled);
    static readonly Regex _teleported = new(@"^Player (?<name>\S+) has teleported to you", RegexOptions.Compiled);
    static readonly Regex _noPending = new(@"^You do not have a pending request from (?<name>\S+?)\.?$", RegexOptions.Compiled);
    static readonly Regex _denied = new(@"^Player (?<name>\S+) has been denied your teleport request", RegexOptions.Compiled);

    sealed class Pending
    {
        public double ExpiresAt;
        public bool Auto;                                // auto-accept handles it (retries included)
        public double RetryAt = double.PositiveInfinity; // set when a retryable failure comes back
    }

    public readonly record struct Request(string Name, double SecondsLeft);

    static readonly Dictionary<string, Pending> _pending = new(StringComparer.OrdinalIgnoreCase);
    static string _lastAccepted; // whose accept we sent last (for replies that don't name the requester)
    static double _lastAnswerAt = double.NegativeInfinity;

    public static bool Enabled => Settings.AutoTeleportEnabled;

    /// <summary>Requests waiting for an answer, soonest to expire first.</summary>
    public static IReadOnlyList<Request> Requests
    {
        get
        {
            double now = Time.realtimeSinceStartupAsDouble;
            return _pending.Select(p => new Request(p.Key, Math.Max(0, p.Value.ExpiresAt - now))).OrderBy(r => r.SecondsLeft).ToList();
        }
    }

    public static int PendingCount => _pending.Count;

    public static void SetEnabled(bool enabled)
    {
        Settings.AutoTeleportEnabled = enabled;
        foreach (var (name, pending) in _pending.ToList())
        {
            pending.Auto = enabled && Allowed(name);
            pending.RetryAt = double.PositiveInfinity;
            if (pending.Auto) Accept(name); // waiting requests are taken now
        }
        Core.Log.LogInfo($"Auto-accept teleport requests {(enabled ? "on" : "off")}.");
    }

    /// <summary>Accept a request by hand (from the window or the notification).</summary>
    public static void AcceptManually(string name)
    {
        double now = Time.realtimeSinceStartupAsDouble;
        if (now - _lastAnswerAt < AnswerCooldownSeconds) return;
        _lastAnswerAt = now;
        Accept(name);
    }

    public static void Deny(string name)
    {
        double now = Time.realtimeSinceStartupAsDouble;
        if (now - _lastAnswerAt < AnswerCooldownSeconds) return;
        _lastAnswerAt = now;
        if (_pending.TryGetValue(name, out var pending)) pending.RetryAt = double.PositiveInfinity;
        ChatCommandSender.TrySend($".stp tpd {name}");
    }

    public static void OnServerMessage(ServerChatMessageType type, string message)
    {
        if (type != ServerChatMessageType.System || string.IsNullOrEmpty(message)) return;
        string text = _richText.Replace(message, string.Empty).Trim();
        double now = Time.realtimeSinceStartupAsDouble;
        Match match;

        if ((match = _request.Match(text)).Success)
        {
            string name = match.Groups["name"].Value;
            double seconds = int.TryParse(match.Groups["secs"].Value, out int s) ? s : DefaultExpirySeconds;
            bool auto = Enabled && Allowed(name);
            _pending[name] = new Pending { ExpiresAt = now + seconds, Auto = auto };
            if (auto) Accept(name);
            else
            {
                if (Enabled) Core.Log.LogInfo($"Teleport request from {name} not auto-accepted (not in AutoTeleport.OnlyFrom).");
                PostNotice(name);
            }
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
            Core.Log.LogInfo($"Teleport accepted: {match.Groups["name"].Value} teleported to you.");
        }
        else if ((match = _noPending.Match(text)).Success || (match = _denied.Match(text)).Success)
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

    // A request nobody's auto-accepting goes into the notification slot: click to accept.
    static void PostNotice(string name)
    {
        Notifications.Post(new Notifications.Notice
        {
            Source = "Teleport",
            GetText = () => _pending.TryGetValue(name, out var p)
                ? $"Accept TP: {name} {Math.Max(0, (int)(p.ExpiresAt - Time.realtimeSinceStartupAsDouble))}s"
                : $"Accept TP: {name}",
            OnClick = () => AcceptManually(name),
            IsFlashing = () => true,
            IsOver = () => !_pending.ContainsKey(name),
        });
    }

    static void ScheduleRetry(string name, double now)
    {
        if (_pending.TryGetValue(name, out var pending) && pending.Auto && now + RetrySeconds < pending.ExpiresAt)
            pending.RetryAt = now + RetrySeconds;
    }

    static void Accept(string name)
    {
        if (_pending.TryGetValue(name, out var pending)) pending.RetryAt = double.PositiveInfinity;
        _lastAccepted = name;
        if (ChatCommandSender.TrySend($".stp tpa {name}")) Core.Diag($"Accepting teleport request from {name}.");
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
