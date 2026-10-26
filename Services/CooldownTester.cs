using DragUI.Data;
using Il2CppInterop.Runtime;
using ProjectM;
using Stunlock.Core;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace DragUI.Services;

// Times the local player's ability cooldowns. Each ability's state entity carries AbilityCooldownState
// (CooldownEndTime in server time, CurrentCooldown = the cooldown the game applied); the ability prefab's
// AbilityCooldownData holds the unmodified base. A cooldown starts when CooldownEndTime jumps into the
// future and ends when the server clock passes it (following any mid-cooldown change), so "measured" is
// the real duration, including server-side bonuses the client's own stats don't know about.
// From base and measured, the bonus is worked out both ways the stat could work:
//   recovery rate:  cooldown = base / (1 + bonus)   ->  bonus = base / measured - 1
//   reduction:      cooldown = base x (1 - bonus)   ->  bonus = 1 - measured / base
internal static class CooldownTester
{
    public enum Kind { Weapon, Spell, Other }

    const int MaxSamples = 10;

    public sealed class Ability
    {
        public int GroupGuid;
        public string Name;
        public Kind Kind;
        public float BaseCooldown;     // unmodified (prefab), 0 = unknown
        public double LastSeenEnd;
        public bool Running;
        public double StartServer;
        public double EndServer;
        public float GameCooldown;     // what the game applied when it started
        public bool Adjusted;          // the end moved during the cooldown
        public double LastCastReal;
        public int Casts;
        public readonly List<float> Samples = new();

        public float? LastMeasured => Samples.Count > 0 ? Samples[^1] : null;
        public float? AverageMeasured => Samples.Count > 0 ? Samples.Average() : null;
    }

    static readonly string[] WeaponTokens =
    {
        "Sword", "Axe", "Mace", "Spear", "Crossbow", "GreatSword", "Slashers", "Pistols", "Reaper", "Longbow", "Whip",
        "TwinBlades", "Daggers", "Claws", "Unarmed",
    };
    static readonly string[] SpellSchools = { "AB_Blood_", "AB_Chaos_", "AB_Frost_", "AB_Illusion_", "AB_Storm_", "AB_Unholy_" };

    static readonly Dictionary<int, Ability> _abilities = new();
    static readonly Dictionary<int, float> _baseCooldowns = new();
    static readonly Dictionary<int, (double End, float Current, int Prefab)> _frame = new();
    static EntityQuery _query;
    static World _queryWorld;

    public static IEnumerable<Ability> Abilities => _abilities.Values.OrderByDescending(a => a.LastCastReal);

    /// <summary>The client's cooldown stats for the local character (bonus values, e.g. 0.33).</summary>
    public static (float Weapon, float Spell, float Ultimate, float All) ClientRates()
    {
        var character = Core.LocalCharacter;
        if (character == Entity.Null || !character.Has<AbilityBar_Shared>()) return default;
        var bar = character.Read<AbilityBar_Shared>();
        return (bar.WeaponCooldownRecoveryRate._Value, bar.SpellCooldownRecoveryRate._Value,
                bar.UltimateCooldownRecoveryRate._Value, bar.CooldownRecoveryRate._Value);
    }

    public static void Reset() => _abilities.Clear();

    /// <summary>Per frame while the tester window is open.</summary>
    public static void Tick()
    {
        var character = Core.LocalCharacter;
        if (character == Entity.Null || !Core.TryGetServerTime(out double serverNow)) return;
        if (_queryWorld != Core.Client)
        {
            _query = Core.BuildQuery(Il2CppType.Of<AbilityCooldownState>());
            _queryWorld = Core.Client;
        }

        // An ability can have more than one entity with a cooldown (group and ability state): per group,
        // keep the latest end time.
        _frame.Clear();
        var entities = _query.ToEntityArray(Allocator.Temp);
        try
        {
            foreach (var entity in entities)
            {
                if (!TryGetGroup(entity, character, out int group)) continue;
                var cd = entity.Read<AbilityCooldownState>();
                int prefab = entity.Has<PrefabGUID>() ? entity.Read<PrefabGUID>().GuidHash : 0;
                if (!_frame.TryGetValue(group, out var seen) || cd.CooldownEndTime > seen.End)
                    _frame[group] = (cd.CooldownEndTime, cd.CurrentCooldown, prefab);
            }
        }
        finally
        {
            entities.Dispose();
        }

        double realNow = Time.realtimeSinceStartupAsDouble;
        foreach (var (group, state) in _frame)
        {
            if (!_abilities.TryGetValue(group, out var a))
            {
                _abilities[group] = a = NewAbility(group, state.Prefab);
                a.LastSeenEnd = state.End; // don't count a cooldown that was already running when we started
                continue;
            }

            if (state.End > a.LastSeenEnd + 0.01 && state.End > serverNow + 0.05)
            {
                if (!a.Running)
                {
                    a.Running = true;
                    a.StartServer = serverNow;
                    a.EndServer = state.End;
                    a.GameCooldown = state.Current > 0 ? state.Current : (float)(state.End - serverNow);
                    a.Adjusted = false;
                    a.Casts++;
                    a.LastCastReal = realNow;
                    if (a.BaseCooldown <= 0) a.BaseCooldown = BaseCooldown(state.Prefab, group);
                }
                else
                {
                    a.EndServer = state.End;
                    a.Adjusted = true;
                }
            }
            else if (a.Running && state.End < a.EndServer - 0.05)
            {
                a.EndServer = state.End; // shortened or reset mid-way
                a.Adjusted = true;
            }
            a.LastSeenEnd = state.End;

            if (a.Running && serverNow >= a.EndServer)
            {
                a.Running = false;
                float measured = (float)(a.EndServer - a.StartServer);
                if (measured > 0.05f)
                {
                    a.Samples.Add(measured);
                    if (a.Samples.Count > MaxSamples) a.Samples.RemoveAt(0);
                    Core.Diag($"Cooldown {a.Name}: {measured:0.00}s (game said {a.GameCooldown:0.00}s, base {a.BaseCooldown:0.00}s).");
                }
            }
        }
    }

    public static double Remaining(Ability a) =>
        a.Running && Core.TryGetServerTime(out double now) ? Math.Max(0, a.EndServer - now) : 0;

    static bool TryGetGroup(Entity entity, Entity character, out int group)
    {
        group = 0;
        var holder = entity;
        if (!holder.Has<AbilityGroupState>())
        {
            if (!entity.Has<AbilityState>()) return false;
            holder = entity.Read<AbilityState>().GroupEntity.GetSyncedEntityOrNull();
            if (holder == Entity.Null || !holder.Exists() || !holder.Has<AbilityGroupState>()) return false;
        }
        var state = holder.Read<AbilityGroupState>();
        if (state.Character.GetSyncedEntityOrNull() != character) return false;
        group = state.GroupId.GuidHash;
        return group != 0;
    }

    static Ability NewAbility(int group, int prefab)
    {
        PrefabNames.TryGet(group, out var raw);
        raw ??= string.Empty;
        // Primary attacks follow attack speed, not the weapon cooldown stat.
        var kind = raw.Contains("_Primary", StringComparison.Ordinal) ? Kind.Other
            : SpellSchools.Any(s => raw.StartsWith(s, StringComparison.Ordinal)) ? Kind.Spell
            : raw.StartsWith("AB_Vampire_", StringComparison.Ordinal) && WeaponTokens.Any(w => raw.Contains($"_{w}_", StringComparison.Ordinal)) ? Kind.Weapon
            : Kind.Other;
        return new Ability { GroupGuid = group, Name = PrefabNames.Pretty(group), Kind = kind, BaseCooldown = BaseCooldown(prefab, group) };
    }

    // The unmodified cooldown, from the ability's prefab (or its group's).
    static float BaseCooldown(int prefab, int group)
    {
        foreach (int guid in new[] { prefab, group })
        {
            if (guid == 0) continue;
            if (_baseCooldowns.TryGetValue(guid, out float cached) && cached > 0) return cached;
            try
            {
                var map = Core.Client.GetExistingSystemManaged<PrefabCollectionSystem>()._PrefabLookupMap;
                if (map.TryGetValue(new PrefabGUID(guid), out Entity prefabEntity) && prefabEntity.Has<AbilityCooldownData>())
                {
                    float value = prefabEntity.Read<AbilityCooldownData>().Cooldown._Value;
                    _baseCooldowns[guid] = value;
                    if (value > 0) return value;
                }
            }
            catch (Exception ex)
            {
                Core.Diag($"Cooldown tester: no base cooldown for {guid}: {ex.Message}");
            }
        }
        return 0;
    }

    public static void OnWorldTeardown()
    {
        _abilities.Clear();
        _frame.Clear();
        _query = default;
        _queryWorld = null;
    }
}
