using System.Text.RegularExpressions;
using ProjectM.Network;
using UnityEngine;

namespace VBloodHunter.Services;

// Quick summon for Bloodcraft familiars, by sending its chat commands for the player:
//   .fam sb "<name>"  smartbind: finds the name (case-insensitive, partial names work) in every box,
//                     unbinds the current familiar and binds the match. Fails if the name matches in
//                     more than one box, and can't bind in combat. Quoted because VCF splits arguments
//                     on spaces.
//   .fam t            calls / dismisses the bound familiar (quick, works in combat).
// Which familiar is bound, and whether it's out, is followed from Bloodcraft's replies (System
// messages only this player gets), so summoning the familiar that's already bound just toggles it.
// The state is unknown after a login until the first reply; then a summon falls back to smartbind.
internal static class FamiliarService
{
    const double BindCooldownSeconds = 1.5;   // smartbind unbinds then rebinds on a delay; don't stack them
    const double ToggleCooldownSeconds = 0.5;
    const double PendingBindSeconds = 5;      // how long a bind waits for the dismissed familiar to come back
    const int MaxNameLength = 48;

    public enum BindState { Unknown, None, Bound }

    static readonly Regex _richText = new("<[^>]*>", RegexOptions.Compiled);
    static readonly Regex _boundReply = new(@"^(?<name>.+?)\*? bound!$", RegexOptions.Compiled);
    static readonly Regex _unboundReply = new(@"^(?<name>.+?)\*? unbound!$", RegexOptions.Compiled);

    static double _lastSentAt = double.NegativeInfinity;
    static string _lastBindRequest;  // name in the last smartbind sent, until Bloodcraft answers it
    static string _lastToggleFor;    // name a .fam t was sent for (rebinds it if the familiar turns out to be gone)
    static string _pendingBind;      // smartbind to send once the dismissed familiar is called back
    static double _pendingUntil;

    /// <summary>What the panel shows under the buttons: the last command sent, or Bloodcraft's answer.</summary>
    public static string LastStatus { get; private set; } = string.Empty;

    public static BindState State { get; private set; } = BindState.Unknown;
    /// <summary>The bound familiar's full name; null if none is bound or it isn't known which.</summary>
    public static string BoundName { get; private set; }
    /// <summary>True when the bound familiar is dismissed rather than out.</summary>
    public static bool Dismissed { get; private set; }

    /// <summary>Trim, collapse spaces and drop quotes (they'd break the command's quoting).</summary>
    public static string CleanName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        string clean = Regex.Replace(name.Replace("\"", string.Empty), @"\s+", " ").Trim();
        return clean.Length > MaxNameLength ? clean[..MaxNameLength].TrimEnd() : clean;
    }

    /// <summary>Is the bound familiar the one <paramref name="name"/> picks? Same partial match as smartbind.</summary>
    public static bool IsBound(string name) =>
        State == BindState.Bound && BoundName != null && !string.IsNullOrEmpty(name)
        && BoundName.Contains(name, StringComparison.OrdinalIgnoreCase);

    /// <summary>Summon the familiar matching <paramref name="name"/>: toggle it if it's already bound,
    /// otherwise bind it. True if a command was sent.</summary>
    public static bool Summon(string name, bool remember)
    {
        name = CleanName(name);
        if (name.Length == 0) return false;

        bool sent;
        if (IsBound(name))
        {
            sent = Send(".fam t", Dismissed ? $"Calling {BoundName}..." : $"Dismissing {BoundName}...", ToggleCooldownSeconds);
            if (sent) _lastToggleFor = name;
        }
        else if (State == BindState.Bound && Dismissed)
        {
            // Bloodcraft won't unbind a dismissed familiar: call it back, then bind on its "enabled" reply.
            sent = Send(".fam t", $"Calling back your familiar to swap to {name}...", BindCooldownSeconds);
            if (sent) SetPending(name);
        }
        else
        {
            sent = SendBind(name);
        }

        if (sent && remember) FamiliarStore.Remember(name);
        return sent;
    }

    /// <summary>Call or dismiss the currently bound familiar.</summary>
    public static bool ToggleActive()
    {
        if (!Send(".fam t", "Calling / dismissing familiar...", ToggleCooldownSeconds)) return false;
        _lastToggleFor = null;
        return true;
    }

    static bool SendBind(string name, bool ignoreCooldown = false)
    {
        if (!Send($".fam sb \"{name}\"", $"Binding {name}...", ignoreCooldown ? 0 : BindCooldownSeconds)) return false;
        _lastBindRequest = name;
        return true;
    }

    static void SetPending(string name)
    {
        _pendingBind = name;
        _pendingUntil = Time.realtimeSinceStartupAsDouble + PendingBindSeconds;
    }

    static bool Send(string command, string status, double cooldown)
    {
        double now = Time.realtimeSinceStartupAsDouble;
        if (now - _lastSentAt < cooldown)
        {
            LastStatus = "Too fast - wait a moment.";
            return false;
        }
        if (!ChatCommandSender.TrySend(command))
        {
            LastStatus = "Couldn't send (not in game?).";
            return false;
        }
        _lastSentAt = now;
        LastStatus = status;
        return true;
    }

    /// <summary>Follow Bloodcraft's familiar replies (called for every incoming server chat message).</summary>
    public static void OnServerMessage(ServerChatMessageType type, string message)
    {
        if (type != ServerChatMessageType.System || string.IsNullOrEmpty(message)) return;
        string text = _richText.Replace(message, string.Empty).Trim();

        Match match;
        if ((match = _boundReply.Match(text)).Success)
        {
            SetState(BindState.Bound, match.Groups["name"].Value.Trim(), dismissed: false);
            LastStatus = $"{BoundName} bound.";
            _lastBindRequest = null;
            _pendingBind = null;
        }
        else if ((match = _unboundReply.Match(text)).Success)
        {
            SetState(BindState.None, null, dismissed: false);
            LastStatus = $"{match.Groups["name"].Value.Trim()} unbound.";
        }
        else if (text == "Familiar enabled!")
        {
            _lastToggleFor = null;
            SetState(BindState.Bound, BoundName, dismissed: false);
            string pending = _pendingBind;
            _pendingBind = null;
            if (pending != null && Time.realtimeSinceStartupAsDouble < _pendingUntil && !IsBound(pending))
                SendBind(pending, ignoreCooldown: true);
        }
        else if (text == "Familiar disabled!")
        {
            _lastToggleFor = null;
            SetState(BindState.Bound, BoundName, dismissed: true);
        }
        else if (text.StartsWith("Can't unbind familiar right now! (dismissed)", StringComparison.Ordinal))
        {
            // We didn't know a familiar was out of sight (e.g. right after login): call it back, then bind.
            SetState(BindState.Bound, BoundName, dismissed: true);
            string retry = _lastBindRequest;
            _lastBindRequest = null;
            if (retry != null && Send(".fam t", $"Calling back your familiar to swap to {retry}...", 0)) SetPending(retry);
        }
        else if (text.StartsWith("Couldn't find active familiar", StringComparison.Ordinal)
                 || text.StartsWith("Active familiar doesn't exist", StringComparison.Ordinal))
        {
            // Our state was stale (the familiar died or was reset): bind the one that was asked for.
            SetState(BindState.None, null, dismissed: false);
            string retry = _lastToggleFor;
            _lastToggleFor = null;
            if (retry != null) SendBind(retry, ignoreCooldown: true);
        }
        else if (text.StartsWith("Couldn't find any matches", StringComparison.Ordinal))
        {
            LastStatus = $"No familiar matches \"{_lastBindRequest}\".";
            _lastBindRequest = null;
        }
        else if (text.StartsWith("Multiple matches found", StringComparison.Ordinal))
        {
            LastStatus = $"\"{_lastBindRequest}\" matches familiars in more than one box - use a longer name.";
            _lastBindRequest = null;
        }
        else if (text.StartsWith("You can't bind in combat", StringComparison.Ordinal))
        {
            LastStatus = "Can't bind in combat.";
            _lastBindRequest = null;
        }
    }

    static void SetState(BindState state, string boundName, bool dismissed)
    {
        bool changed = state != State || boundName != BoundName || dismissed != Dismissed;
        State = state;
        BoundName = boundName;
        Dismissed = dismissed;
        if (changed) Core.Diag($"Familiar state: {state} {boundName ?? "(unknown)"}{(dismissed ? " dismissed" : "")}");
    }

    /// <summary>Leaving the server: what's bound there means nothing for the next one.</summary>
    public static void OnWorldTeardown()
    {
        State = BindState.Unknown;
        BoundName = null;
        Dismissed = false;
        _lastBindRequest = null;
        _lastToggleFor = null;
        _pendingBind = null;
    }
}
