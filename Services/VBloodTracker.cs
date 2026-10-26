using Il2CppInterop.Runtime;
using ProjectM;
using ProjectM.UI;
using Stunlock.Core;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using DragUI.Data;

namespace DragUI.Services;

internal enum HuntState
{
    /// <summary>No information yet.</summary>
    Unknown,
    /// <summary>Up, as far as we can tell.</summary>
    Alive,
    /// <summary>Dead, with a respawn countdown (exact from its ghost, or estimated).</summary>
    Respawning,
    /// <summary>The countdown has run out but we haven't seen it back yet.</summary>
    Due,
    /// <summary>The server didn't list it and we never saw it die, so there's no timer.</summary>
    NotDetected,
}

internal sealed class VBloodStatus
{
    public VBloodStatus(VBloodInfo info) => Info = info;

    public VBloodInfo Info { get; }
    public HuntState State { get; set; } = HuntState.Unknown;

    public bool HasPosition { get; set; }
    public float2 Position { get; set; }

    // All times below are on the local clock (VBloodTracker.Now).

    /// <summary>When it respawns (valid in Respawning/Due).</summary>
    public double RespawnAt { get; set; }
    /// <summary>RespawnAt came from the V Blood's ghost rather than an estimate.</summary>
    public bool RespawnExact { get; set; }
    public RespawnMemory.Source EstimateSource { get; set; }

    /// <summary>When it died (0 = unknown).</summary>
    public double DiedAt { get; set; }
    /// <summary>DiedAt is the game's own time of death, not just when we noticed.</summary>
    public bool DiedAtExact { get; set; }

    public bool IsDead => State is HuntState.Respawning or HuntState.Due;

    public void ClearDeath()
    {
        RespawnAt = 0;
        RespawnExact = false;
        DiedAt = 0;
        DiedAtExact = false;
    }
}

// Works out, per V Blood, whether it's alive and when it respawns, from what a normal client sees:
//   1. Nearby V Blood units (VBloodConsumeSource + Health): seeing one die gives the exact time of
//      death, so the countdown starts right when you kill it.
//   2. Ghosts (VbloodGhostBuff_Shared.DestroyTime): the exact respawn time of a dead V Blood near you.
//   3. GetVBloodPositionsEvent - the request the game's V Blood menu sends. A boss dropping off the
//      server's list means it died somewhere else. Whether the server keeps dead bosses on that list
//      is worked out at runtime (see _serverListsDead).
//   4. Script_BloodAltar_TrackVBloodUnit_Shared on your character: which V Blood is tracked.
// Countdowns run on the local clock; server times (ghost, time of death) are converted once.
internal static class VBloodTracker
{
    const double ScanInterval = 0.5;
    const double TrackPendingSeconds = 4;
    const double AutoTrackRetrySeconds = 10;
    const double MinDeadSecondsBeforeRespawn = 15;
    const float GhostMatchRadius = 60f;

    static readonly Dictionary<int, VBloodStatus> _status =
        VBloodCatalog.All.ToDictionary(v => v.Guid.GuidHash, v => new VBloodStatus(v));

    static double _nextPollAt;
    static double _nextScanAt;
    static double _nextAutoTrackAt;
    static int _responsesSeen;
    static bool _warnedNoResponse;
    static double _firstRequestAt;
    static HashSet<int> _lastListed;

    // Does the server keep dead V Bloods on its position list? null = not known yet. Learned the first
    // time we see a boss die in front of us and then check whether the next response still lists it.
    static bool? _serverListsDead;
    static int _calibrationGuid;
    static double _calibrationAfter;

    static World _queryWorld;
    static EntityQuery _ghostQuery;
    static EntityQuery _trackQuery;
    static EntityQuery _unitQuery;

    static readonly HashSet<int> _loggedGhosts = new();
    static readonly HashSet<long> _handledDeaths = new();
    static readonly HashSet<long> _ghostTargets = new();

    public static PrefabGUID TrackedUnit { get; private set; }
    public static PrefabGUID PendingTrack { get; private set; }
    static double _pendingUntil;

    /// <summary>The local clock every countdown runs on (seconds).</summary>
    public static double Now => Time.realtimeSinceStartupAsDouble;

    public static bool HasPositionData => _responsesSeen > 0;

    public static VBloodStatus Get(VBloodInfo info) => _status[info.Guid.GuidHash];

    public static bool IsTracked(VBloodInfo info) => TrackedUnit.GuidHash == info.Guid.GuidHash;

    public static bool IsPendingTrack(VBloodInfo info) =>
        PendingTrack.GuidHash == info.Guid.GuidHash && Now < _pendingUntil;

    // ---------------------------------------------------------------- per-frame

    public static void Tick()
    {
        double now = Now;

        if (now >= _nextPollAt)
        {
            _nextPollAt = now + Settings.PollSeconds;
            if (GameEvents.RequestVBloodPositions() && _firstRequestAt == 0) _firstRequestAt = now;
        }

        if (!_warnedNoResponse && _firstRequestAt > 0 && _responsesSeen == 0 && now - _firstRequestAt > 30)
        {
            _warnedNoResponse = true;
            Core.Log.LogWarning("No V Blood position response from the server after 30s. Bosses you kill or see " +
                                "will still get timers; others stay unknown.");
        }

        if (now >= _nextScanAt)
        {
            _nextScanAt = now + ScanInterval;
            EnsureQueries();
            Guard("tracking scan", ScanTrackingBuff);
            Guard("ghost scan", ScanGhosts);
            Guard("unit scan", ScanUnits);
            RefreshDueStates();
            TryAutoTrack(now);
        }
    }

    static void Guard(string what, Action action)
    {
        try { action(); }
        catch (Exception ex) { Core.Log.LogError($"V Blood {what} failed: {ex}"); }
    }

    // ---------------------------------------------------------------- state changes

    static void MarkDead(VBloodStatus status, double diedAt, bool diedAtExact, string source)
    {
        status.DiedAt = diedAt;
        status.DiedAtExact = diedAtExact;
        if (!status.RespawnExact)
        {
            double estimate = RespawnMemory.EstimateSeconds(status.Info, out var estimateSource);
            status.RespawnAt = diedAt + estimate;
            status.EstimateSource = estimateSource;
        }
        status.State = status.RespawnAt > Now ? HuntState.Respawning : HuntState.Due;
        Core.Log.LogInfo($"{status.Info.Name} died ({source}); respawn {(status.RespawnExact ? "exactly" : "estimated")} " +
                         $"in {Math.Max(0, status.RespawnAt - Now):0}s.");
    }

    static void MarkAlive(VBloodStatus status, string source)
    {
        if (status.IsDead)
            Core.Log.LogInfo($"{status.Info.Name} is up again ({source}).");
        status.State = HuntState.Alive;
        status.ClearDeath();
    }

    // Respawn times are fixed per boss (ghosts confirm the built-in times to the second), so once a countdown
    // has run out (plus a short grace) the boss is up. Servers that keep dead bosses on their position list
    // give no other "it's back" signal unless you're nearby.
    const double DueGraceSeconds = 5;

    static void RefreshDueStates()
    {
        double now = Now;
        foreach (var status in _status.Values)
        {
            if (status.State == HuntState.Respawning && now >= status.RespawnAt)
                status.State = HuntState.Due;
            else if (status.State == HuntState.Due && status.RespawnAt > now)
                status.State = HuntState.Respawning;

            if (status.State == HuntState.Due && _serverListsDead != false && now - status.RespawnAt >= DueGraceSeconds)
                MarkAlive(status, "respawn timer ended");
        }
    }

    // ---------------------------------------------------------------- tracking

    /// <summary>Track this V Blood, or stop tracking it if it's already tracked.</summary>
    public static void ToggleTrack(VBloodInfo info)
    {
        if (IsTracked(info))
        {
            if (GameEvents.StopTracking())
            {
                Core.Diag($"Stop tracking {info.Name}.");
                PendingTrack = default;
            }
            return;
        }

        if (GameEvents.StartTracking(info.Guid))
        {
            Core.Diag($"Track request sent for {info.Name} ({info.Guid.GuidHash}).");
            PendingTrack = info.Guid;
            _pendingUntil = Now + TrackPendingSeconds;
        }
    }

    static void ScanTrackingBuff()
    {
        var character = Core.LocalCharacter;
        PrefabGUID tracked = default;

        var entities = _trackQuery.ToEntityArray(Allocator.Temp);
        try
        {
            foreach (var buffEntity in entities)
            {
                if (buffEntity.Read<Buff>().Target != character) continue;
                tracked = buffEntity.Read<Script_BloodAltar_TrackVBloodUnit_Shared>().TrackedUnit;
                break;
            }
        }
        finally
        {
            entities.Dispose();
        }

        if (tracked.GuidHash != TrackedUnit.GuidHash)
        {
            Core.Diag(VBloodCatalog.TryGet(tracked, out var info)
                ? $"Now tracking {info.Name}."
                : tracked.GuidHash == 0 ? "No longer tracking anything." : $"Tracking unknown unit {tracked.GuidHash}.");
            TrackedUnit = tracked;
        }

        if (tracked.GuidHash != 0 && tracked.GuidHash == PendingTrack.GuidHash) PendingTrack = default;
    }

    static void TryAutoTrack(double now)
    {
        if (!Settings.AutoTrackNext || TrackedUnit.GuidHash != 0 || now < _pendingUntil || now < _nextAutoTrackAt) return;
        if (!Core.TryGetLocalPosition(out float2 me)) return;

        VBloodStatus best = null;
        float bestDistance = float.MaxValue;
        foreach (var info in HuntStore.Hunted)
        {
            var status = Get(info);
            if (status.State != HuntState.Alive || !status.HasPosition) continue;
            float d = math.distance(me, status.Position);
            if (d < bestDistance) { bestDistance = d; best = status; }
        }

        if (best == null) return;
        _nextAutoTrackAt = now + AutoTrackRetrySeconds;
        Core.Diag($"Auto-tracking {best.Info.Name} ({bestDistance:0}m).");
        ToggleTrack(best.Info);
    }

    // ---------------------------------------------------------------- nearby V Blood units

    static void ScanUnits()
    {
        double now = Now;
        var entities = _unitQuery.ToEntityArray(Allocator.Temp);
        try
        {
            foreach (var entity in entities)
            {
                if (!VBloodCatalog.TryGet(entity.Read<PrefabGUID>(), out var info)) continue;
                var status = Get(info);
                var health = entity.Read<Health>();
                long key = EntityKey(entity);

                if (health.IsDead || health.Value <= 0f)
                {
                    if (!_handledDeaths.Add(key)) continue; // already counted this corpse
                    bool exact = Core.TryServerToLocal(health.TimeOfDeath, out double diedAt) && diedAt <= now + 1;
                    if (!exact) diedAt = now;
                    // A corpse from a death we already have a newer record of isn't news.
                    if (status.IsDead && status.DiedAt >= diedAt - 5) continue;
                    MarkDead(status, diedAt, exact, "seen dying nearby");
                    if (now - diedAt < 10) ExperienceTracker.OnVBloodKilledNearby();
                    StartCalibration(status, now);
                    continue;
                }

                if (_ghostTargets.Contains(key)) continue; // a ghost, not the living boss

                if (entity.Has<LocalToWorld>())
                {
                    var p = entity.Read<LocalToWorld>().Position;
                    status.Position = new float2(p.x, p.z);
                    status.HasPosition = true;
                }

                if (status.State == HuntState.Alive) continue;
                if (status.IsDead)
                {
                    // Don't believe "alive" while its ghost still says otherwise, or right after it died.
                    if (status.RespawnExact && status.RespawnAt > now) continue;
                    if (status.DiedAt > 0 && now - status.DiedAt < MinDeadSecondsBeforeRespawn) continue;
                }
                MarkAlive(status, "seen nearby");
            }
        }
        finally
        {
            entities.Dispose();
        }
    }

    static long EntityKey(Entity entity) => ((long)entity.Index << 32) | (uint)entity.Version;

    // ---------------------------------------------------------------- ghosts (exact respawn times)

    static void ScanGhosts()
    {
        _ghostTargets.Clear();
        if (!Core.TryGetServerTime(out double serverNow)) return;

        var entities = _ghostQuery.ToEntityArray(Allocator.Temp);
        try
        {
            foreach (var buffEntity in entities)
            {
                double destroyTime = buffEntity.Read<VbloodGhostBuff_Shared>().DestroyTime;
                var target = buffEntity.Read<Buff>().Target;
                if (target != Entity.Null) _ghostTargets.Add(EntityKey(target));

                // The same buff also turns up on ordinary mobs (e.g. Stavros' worshippers) with no time: skip those.
                int targetPrefab = target.Exists() && target.Has<PrefabGUID>() ? target.Read<PrefabGUID>().GuidHash : 0;
                if (targetPrefab != 0 && !VBloodCatalog.IsKnown(targetPrefab)) continue;

                double remaining = destroyTime - serverNow;
                bool valid = double.IsFinite(remaining) && remaining > -60 && remaining < 4 * 3600;
                var status = valid ? MatchGhost(buffEntity, target, out string how) : null;

                if (_loggedGhosts.Add(buffEntity.Index) && (status != null || _loggedGhosts.Add(-1 - targetPrefab)))
                {
                    Core.Log.LogInfo($"V Blood ghost seen: target prefab {targetPrefab}, respawn in {remaining:0}s " +
                                     $"(DestroyTime {destroyTime:0.0}, server time {serverNow:0.0}) -> " +
                                     $"{(status != null ? status.Info.Name : valid ? "unmatched" : "ignored, bad time")}.");
                }
                if (status == null) continue;

                double respawnAt = Now + remaining;
                if (status.RespawnExact && Math.Abs(status.RespawnAt - respawnAt) < 2) continue; // already known

                if (status.DiedAt > 0 && status.DiedAtExact)
                    RespawnMemory.RecordExact(status.Info, respawnAt - status.DiedAt);

                status.RespawnAt = respawnAt;
                status.RespawnExact = true;
                if (!status.IsDead)
                    MarkDead(status, status.DiedAt > 0 ? status.DiedAt : Now, status.DiedAtExact, "ghost");
                else
                    status.State = respawnAt > Now ? HuntState.Respawning : HuntState.Due;
            }
        }
        finally
        {
            entities.Dispose();
        }
    }

    static VBloodStatus MatchGhost(Entity buffEntity, Entity target, out string how)
    {
        how = "prefab";
        if (target.Exists() && target.Has<PrefabGUID>()
            && VBloodCatalog.TryGet(target.Read<PrefabGUID>(), out var info))
        {
            return Get(info);
        }

        // The ghost may be its own unit: fall back to the nearest V Blood we last saw there.
        how = "position";
        Entity located = target.Exists() && target.Has<LocalToWorld>() ? target : buffEntity;
        if (!located.Has<LocalToWorld>()) return null;
        var p3 = located.Read<LocalToWorld>().Position;
        var p = new float2(p3.x, p3.z);

        VBloodStatus best = null;
        float bestDistance = GhostMatchRadius;
        foreach (var status in _status.Values)
        {
            if (!status.HasPosition) continue;
            float d = math.distance(p, status.Position);
            if (d < bestDistance) { bestDistance = d; best = status; }
        }
        return best;
    }

    // ---------------------------------------------------------------- chat kill announcements

    /// <summary>A chat message announced this V Blood's death <paramref name="ageSeconds"/> ago.</summary>
    public static void OnChatKill(VBloodInfo info, double ageSeconds, string message)
    {
        if (!Core.InGame) return;
        var status = Get(info);
        double diedAt = Now - Math.Max(0, ageSeconds);

        // Already know about this death (seen nearby, or announced twice)? Keep the better record.
        if (status.IsDead && status.DiedAt > 0 && Math.Abs(status.DiedAt - diedAt) < 60)
        {
            Core.Diag($"Chat kill for {info.Name} matches a death we already have: \"{message}\"");
            return;
        }

        status.ClearDeath(); // a fresh death: any old ghost time no longer applies
        MarkDead(status, diedAt, diedAtExact: false, $"chat: \"{message}\"");
    }

    // ---------------------------------------------------------------- server position list

    /// <summary>Called from the GetVBloodsPositionResponseSystem postfix when a response was processed.</summary>
    public static void OnPositionsResponse(Il2CppSystem.Collections.Generic.List<VBloodPosition> positions)
    {
        if (positions == null || !Core.InGame) return;

        double now = Now;
        var listed = new Dictionary<int, float2>(positions.Count);
        for (int i = 0; i < positions.Count; i++)
        {
            var p = positions[i];
            listed[p.UnitPrefabGuid.GuidHash] = p.Position;
        }

        var previouslyListed = _lastListed;
        _responsesSeen++;
        LogListChanges(listed);
        Calibrate(listed, now);

        foreach (var status in _status.Values)
        {
            if (listed.TryGetValue(status.Info.Guid.GuidHash, out float2 position))
            {
                status.HasPosition = true;
                status.Position = position;

                // Being listed never cancels a running countdown: some servers keep dead bosses on the list,
                // and respawn times are fixed, so the countdown itself says when the boss is back.
                switch (status.State)
                {
                    case HuntState.Unknown:
                    case HuntState.NotDetected:
                    case HuntState.Due when _serverListsDead == false:
                        MarkAlive(status, "listed by server");
                        break;
                }
                continue;
            }

            switch (status.State)
            {
                // Only a boss the server HAD listed can drop off it. Some (e.g. Nibbles, Dracula) are
                // never listed, even when we can see them alive nearby.
                case HuntState.Alive when _serverListsDead != true
                                       && previouslyListed != null
                                       && previouslyListed.Contains(status.Info.Guid.GuidHash):
                    MarkDead(status, now, diedAtExact: false, "dropped off the server list");
                    break;
                case HuntState.Unknown:
                    status.State = HuntState.NotDetected;
                    break;
            }
        }
    }

    static void StartCalibration(VBloodStatus status, double now)
    {
        if (_serverListsDead.HasValue || _calibrationGuid != 0) return;
        // Only a boss the server was listing tells us anything: some (Dracula, Nibbles) are never listed.
        if (_lastListed == null || !_lastListed.Contains(status.Info.Guid.GuidHash)) return;
        _calibrationGuid = status.Info.Guid.GuidHash;
        _calibrationAfter = now + 2; // give the server a moment to process the death
    }

    static void Calibrate(Dictionary<int, float2> listed, double now)
    {
        if (_calibrationGuid == 0 || now < _calibrationAfter) return;
        _serverListsDead = listed.ContainsKey(_calibrationGuid);
        string name = VBloodCatalog.TryGet(_calibrationGuid, out var info) ? info.Name : _calibrationGuid.ToString();
        Core.Log.LogInfo(_serverListsDead == true
            ? $"Calibration: the server still lists {name} after it died, so its list includes dead V Bloods. " +
              "Timers will come from deaths seen nearby and from ghosts."
            : $"Calibration: {name} left the server list after it died, so the list only has living V Bloods. " +
              "Bosses killed anywhere will get timers.");
        _calibrationGuid = 0;
    }

    static void LogListChanges(Dictionary<int, float2> listed)
    {
        var now = listed.Keys.ToHashSet();
        if (_lastListed == null)
        {
            var missing = VBloodCatalog.All.Where(v => !now.Contains(v.Guid.GuidHash)).Select(v => v.Name);
            Core.Log.LogInfo($"V Blood position response: {listed.Count} listed. Not listed: {string.Join(", ", missing)}.");
        }
        else
        {
            var added = now.Except(_lastListed).Select(Name).ToList();
            var removed = _lastListed.Except(now).Select(Name).ToList();
            if (added.Count > 0) Core.Log.LogInfo($"Server list: now listed {string.Join(", ", added)}.");
            if (removed.Count > 0) Core.Log.LogInfo($"Server list: no longer listed {string.Join(", ", removed)}.");
        }
        _lastListed = now;

        if (Settings.Diagnostics)
        {
            foreach (var (guid, p) in listed)
                Core.Diag($"  listed {Name(guid)} at ({p.x:0},{p.y:0})");
        }

        static string Name(int guid) => VBloodCatalog.TryGet(guid, out var info) ? info.Name : guid.ToString();
    }

    // ---------------------------------------------------------------- lifecycle

    static void EnsureQueries()
    {
        if (_queryWorld == Core.Client) return;
        var em = Core.EntityManager;
        _ghostQuery = BuildQuery(em, Il2CppType.Of<Buff>(), Il2CppType.Of<VbloodGhostBuff_Shared>());
        _trackQuery = BuildQuery(em, Il2CppType.Of<Buff>(), Il2CppType.Of<Script_BloodAltar_TrackVBloodUnit_Shared>());
        _unitQuery = BuildQuery(em, Il2CppType.Of<VBloodConsumeSource>(), Il2CppType.Of<Health>(), Il2CppType.Of<PrefabGUID>());
        _queryWorld = Core.Client;
    }

    static EntityQuery BuildQuery(EntityManager em, params Il2CppSystem.Type[] all) => Core.BuildQuery(all);

    /// <summary>World is being torn down (logout / server switch). Plain field resets only.</summary>
    public static void OnWorldTeardown()
    {
        _queryWorld = null;
        _ghostQuery = default;
        _trackQuery = default;
        _unitQuery = default;
        foreach (var status in _status.Values)
        {
            status.State = HuntState.Unknown;
            status.HasPosition = false;
            status.ClearDeath();
        }
        _loggedGhosts.Clear();
        _handledDeaths.Clear();
        _ghostTargets.Clear();
        _lastListed = null;
        _calibrationGuid = 0;
        _serverListsDead = null;
        TrackedUnit = default;
        PendingTrack = default;
        _pendingUntil = 0;
        _nextPollAt = 0;
        _nextScanAt = 0;
        _nextAutoTrackAt = 0;
        _responsesSeen = 0;
        _warnedNoResponse = false;
        _firstRequestAt = 0;
    }
}
