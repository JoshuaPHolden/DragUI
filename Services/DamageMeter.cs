using Il2CppInterop.Runtime;
using ProjectM;
using Stunlock.Core;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using DragUI.Data;

namespace DragUI.Services;

// DPS meter built from the damage numbers the server sends the client: each is a short-lived entity
// with a ScrollingCombatTextMessage (value, SCT type, source, target, and the ability/weapon prefab that
// did it). Counts only the local player's hits (plus their minions and familiar), so it measures real
// damage after every modifier - gear, Bloodcraft, procs, other mods.
// A fight starts with the first hit and ends FightTimeoutSeconds after the last one.
internal static class DamageMeter
{
    const int MaxHistory = 10;
    const double SeenForgetSeconds = 15;

    // SCT types for damage dealt (prefab GUIDs from the game's SCT_Type_* prefabs).
    static readonly int TypeDamage = 1185773513;          // SCT_Type_Damage
    static readonly int TypeDamageDone = 781573820;       // SCT_Type_DamageDone
    static readonly int TypeDamageDoneWeak = -403787477;  // SCT_Type_DamageDoneWeak
    static readonly int TypeCrit = 71612833;              // SCT_Type_CritDamage
    static readonly HashSet<int> _damageTypes = new() { TypeDamage, TypeDamageDone, TypeDamageDoneWeak, TypeCrit };

    public const string FamiliarLine = "Familiar / minions";

    public sealed class Line
    {
        public string Name;
        public double Damage;
        public int Hits;
        public int Crits;
    }

    public sealed class Fight
    {
        public double Start;       // local realtime of the first hit
        public double End;         // ... and of the last
        public DateTime StartedAt;
        public double Damage;
        public int Hits;
        public int Crits;
        public readonly Dictionary<string, Line> Lines = new();
        public readonly Dictionary<int, double> DamageByTarget = new();

        public double Seconds => Math.Max(1, End - Start);
        public double Dps => Damage / Seconds;

        /// <summary>The unit that took the most damage.</summary>
        public string MainTarget => DamageByTarget.Count == 0 ? "-" : PrefabNames.Pretty(DamageByTarget.MaxBy(kv => kv.Value).Key);
    }

    static EntityQuery _query;
    static World _queryWorld;
    static readonly Dictionary<long, double> _seen = new();
    static readonly List<Fight> _history = new();
    static readonly HashSet<int> _loggedTypes = new();
    static double _nextForgetAt;

    /// <summary>The fight in progress, if any.</summary>
    public static Fight Current { get; private set; }
    /// <summary>Finished fights, newest first.</summary>
    public static IReadOnlyList<Fight> History => _history;
    /// <summary>Current fight, or the last finished one.</summary>
    public static Fight Latest => Current ?? (_history.Count > 0 ? _history[0] : null);
    public static int Version { get; private set; }

    public static void Reset()
    {
        Current = null;
        _history.Clear();
        Version++;
    }

    public static void Tick()
    {
        double now = Time.realtimeSinceStartupAsDouble;
        if (Current != null && now - Current.End > Settings.DpsFightTimeoutSeconds) EndFight();

        var character = Core.LocalCharacter;
        if (character == Entity.Null) return;
        if (_queryWorld != Core.Client)
        {
            _query = Core.BuildQuery(Il2CppType.Of<ScrollingCombatTextMessage>());
            _queryWorld = Core.Client;
        }

        var entities = _query.ToEntityArray(Allocator.Temp);
        try
        {
            foreach (var entity in entities)
            {
                long key = ((long)entity.Index << 32) | (uint)entity.Version;
                if (_seen.ContainsKey(key)) continue;
                _seen[key] = now;
                try { Count(entity.Read<ScrollingCombatTextMessage>(), character, now); }
                catch (Exception ex) { Core.Diag($"DPS: skipped a damage number: {ex.Message}"); }
            }
        }
        finally
        {
            entities.Dispose();
        }

        if (now >= _nextForgetAt)
        {
            _nextForgetAt = now + 5;
            foreach (var old in _seen.Where(kv => now - kv.Value > SeenForgetSeconds).Select(kv => kv.Key).ToList()) _seen.Remove(old);
        }
    }

    static void Count(ScrollingCombatTextMessage sct, Entity character, double now)
    {
        if (sct.Value <= 0 || !float.IsFinite(sct.Value)) return;
        var source = sct.Source.GetSyncedEntityOrNull();
        var target = sct.Target.GetSyncedEntityOrNull();
        if (target == character) return; // damage taken
        if (!IsCreature(target)) return;  // trees, rocks, castle objects aren't fights

        string line;
        if (source == character) line = PrefabNames.Pretty(sct.SourceObjectType.GuidHash);
        else if (IsMinionOrFamiliar(source, character)) line = FamiliarLine;
        else return;

        int type = sct.Type.GuidHash;
        if (!_damageTypes.Contains(type))
        {
            if (_loggedTypes.Add(type)) Core.Diag($"DPS: ignoring number type {type} from you ({sct.Value:0}, {line}).");
            return;
        }

        if (Current == null)
        {
            Current = new Fight { Start = now, StartedAt = DateTime.Now };
            Version++;
        }
        var fight = Current;
        bool crit = type == TypeCrit;
        fight.End = now;
        fight.Damage += sct.Value;
        fight.Hits++;
        if (crit) fight.Crits++;
        if (!fight.Lines.TryGetValue(line, out var l)) fight.Lines[line] = l = new Line { Name = line };
        l.Damage += sct.Value;
        l.Hits++;
        if (crit) l.Crits++;
        if (target != Entity.Null && target.Has<PrefabGUID>())
        {
            int guid = target.Read<PrefabGUID>().GuidHash;
            fight.DamageByTarget[guid] = fight.DamageByTarget.GetValueOrDefault(guid) + sct.Value;
        }
    }

    // Units are CHAR_* prefabs; resource nodes and buildings (TM_*, etc.) aren't in that set.
    static bool IsCreature(Entity target)
    {
        if (target == Entity.Null || !target.Exists() || !target.Has<PrefabGUID>()) return false;
        int guid = target.Read<PrefabGUID>().GuidHash;
        return VBloodCatalog.TryGet(guid, out _) || (PrefabNames.TryGet(guid, out var name) && name.StartsWith("CHAR_", StringComparison.Ordinal));
    }

    static bool IsMinionOrFamiliar(Entity source, Entity character)
    {
        if (source == Entity.Null || !source.Exists()) return false;
        if (source.Has<EntityOwner>() && source.Read<EntityOwner>().Owner == character) return true;
        return source.Has<Follower>() && source.Read<Follower>().Followed._Value == character;
    }

    static void EndFight()
    {
        var fight = Current;
        Current = null;
        Version++;
        if (fight.Damage <= 0) return;
        _history.Insert(0, fight);
        if (_history.Count > MaxHistory) _history.RemoveAt(_history.Count - 1);
        Core.Log.LogInfo($"DPS: fight vs {fight.MainTarget}: {fight.Damage:0} damage in {fight.Seconds:0.0}s = {fight.Dps:0} DPS " +
                         $"({fight.Hits} hits, {fight.Crits} crits).");
    }

    public static void OnWorldTeardown()
    {
        if (Current != null) EndFight();
        _query = default;
        _queryWorld = null;
        _seen.Clear();
    }
}
