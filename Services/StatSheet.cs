using ProjectM;
using ProjectM.Shared;
using Stunlock.Core;
using Unity.Entities;
using DragUI.Data;

namespace DragUI.Services;

// The local character's stats with where they come from. For each stat:
//   * the game's own value, read from the character's components (what the client computed);
//   * every buff on the character that modifies it (gear "EquipBuff"s, set bonuses, blood, potions,
//     spells...), from each buff's ModifyUnitStatBuff_DOTS buffer;
//   * Bloodcraft's expertise/legacy bonuses, recomputed (BloodcraftStats), and PerkShop's stat perks,
//     read from ".perk status" (PerkshopStats) - the client's copy of the buffs they live in doesn't hold
//     the real values, so its entries are replaced by the real ones and the total adjusted;
//   * "base / unlisted": what's left (base stats, gear level, anything applied without a buff).
// Stats the server changes without telling the client (other server mods editing values in place)
// can't be seen; the DPS meter measures their effect.
internal static class StatSheet
{
    public enum Format { Flat, Percent, Speed }

    public sealed record Part(string Source, float Value, ModificationType Type);

    public sealed class Row
    {
        public UnitStatType Stat;
        public string Name;
        public Format Format;
        public float? ClientValue;   // the game's value on the client, when it's held in a component
        public float? Total;         // ClientValue with Bloodcraft's real bonus in place of the client's copy
        public readonly List<Part> Parts = new();
    }

    // The stats shown, in order, with how to show them and where the client keeps the live value.
    static readonly (UnitStatType Stat, string Name, Format Format, Func<Entity, float?> Live)[] Stats =
    {
        (UnitStatType.PhysicalPower, "Physical Power", Format.Flat, c => Unit(c, u => u.PhysicalPower._Value)),
        (UnitStatType.SpellPower, "Spell Power", Format.Flat, c => Unit(c, u => u.SpellPower._Value)),
        (UnitStatType.WeaponSkillPower, "Weapon Skill Power", Format.Percent, c => Vsa(c, v => v.WeaponSkillPower._Value)),
        (UnitStatType.PhysicalCriticalStrikeChance, "Physical Crit Chance", Format.Percent, c => Vsa(c, v => v.PhysicalCriticalStrikeChance._Value)),
        (UnitStatType.PhysicalCriticalStrikeDamage, "Physical Crit Damage", Format.Percent, c => Vsa(c, v => v.PhysicalCriticalStrikeDamage._Value)),
        (UnitStatType.SpellCriticalStrikeChance, "Spell Crit Chance", Format.Percent, c => Vsa(c, v => v.SpellCriticalStrikeChance._Value)),
        (UnitStatType.SpellCriticalStrikeDamage, "Spell Crit Damage", Format.Percent, c => Vsa(c, v => v.SpellCriticalStrikeDamage._Value)),
        (UnitStatType.PrimaryAttackSpeed, "Primary Attack Speed", Format.Percent, c => Bar(c, b => b.PrimaryAttackSpeed._Value)),
        (UnitStatType.AbilityAttackSpeed, "Ability Attack Speed", Format.Percent, c => Bar(c, b => b.AbilityAttackSpeed._Value)),
        (UnitStatType.WeaponCooldownRecoveryRate, "Weapon Cooldown Rate", Format.Percent, c => Bar(c, b => b.WeaponCooldownRecoveryRate._Value)),
        (UnitStatType.SpellCooldownRecoveryRate, "Spell Cooldown Rate", Format.Percent, c => Bar(c, b => b.SpellCooldownRecoveryRate._Value)),
        (UnitStatType.UltimateCooldownRecoveryRate, "Ultimate Cooldown Rate", Format.Percent, c => Bar(c, b => b.UltimateCooldownRecoveryRate._Value)),
        (UnitStatType.CooldownRecoveryRate, "Cooldown Rate (all)", Format.Percent, c => Bar(c, b => b.CooldownRecoveryRate._Value)),
        (UnitStatType.PhysicalLifeLeech, "Physical Life Leech", Format.Percent, c => Leech(c, l => l.PhysicalLifeLeechFactor._Value)),
        (UnitStatType.SpellLifeLeech, "Spell Life Leech", Format.Percent, c => Leech(c, l => l.SpellLifeLeechFactor._Value)),
        (UnitStatType.PrimaryLifeLeech, "Primary Life Leech", Format.Percent, c => Leech(c, l => l.PrimaryLeechFactor._Value)),
        (UnitStatType.MinionDamage, "Minion Damage", Format.Percent, null),
        (UnitStatType.DamageVsVBloods, "Damage vs V Bloods", Format.Percent, null),
        (UnitStatType.DamageVsUndeads, "Damage vs Undead", Format.Percent, null),
        (UnitStatType.DamageVsHumans, "Damage vs Humans", Format.Percent, null),
        (UnitStatType.DamageVsBeasts, "Damage vs Beasts", Format.Percent, null),
        (UnitStatType.DamageVsDemons, "Damage vs Demons", Format.Percent, null),
        (UnitStatType.DamageVsMechanical, "Damage vs Mechanical", Format.Percent, null),
        (UnitStatType.DamageVsVampires, "Damage vs Vampires", Format.Percent, null),
        (UnitStatType.MaxHealth, "Max Health", Format.Flat, c => c.Has<Health>() ? c.Read<Health>().MaxHealth._Value : null),
        (UnitStatType.PhysicalResistance, "Physical Resistance", Format.Percent, c => Unit(c, u => u.PhysicalResistance._Value)),
        (UnitStatType.SpellResistance, "Spell Resistance", Format.Percent, c => Unit(c, u => u.SpellResistance._Value)),
        (UnitStatType.DamageReduction, "Damage Reduction", Format.Percent, c => Unit(c, u => u.DamageReduction._Value)),
        (UnitStatType.HealingReceived, "Healing Received", Format.Percent, c => Unit(c, u => u.HealingReceived._Value)),
        (UnitStatType.CorruptionDamageReduction, "Corruption Dmg Reduction", Format.Percent, c => Unit(c, u => u.CorruptionDamageReduction._Value)),
        (UnitStatType.MovementSpeed, "Movement Speed", Format.Speed, c => c.Has<Movement>() ? c.Read<Movement>().Speed._Value : null),
        (UnitStatType.ReducedBloodDrain, "Reduced Blood Drain", Format.Percent, c => Unit(c, u => u.ReducedBloodDrain._Value)),
        (UnitStatType.ResourceYield, "Resource Yield", Format.Percent, c => Vsa(c, v => v.ResourceYieldModifier._Value)),
    };

    static float? Unit(Entity c, Func<UnitStats, float> get) => c.Has<UnitStats>() ? get(c.Read<UnitStats>()) : null;
    static float? Vsa(Entity c, Func<VampireSpecificAttributes, float> get) => c.Has<VampireSpecificAttributes>() ? get(c.Read<VampireSpecificAttributes>()) : null;
    static float? Bar(Entity c, Func<AbilityBar_Shared, float> get) => c.Has<AbilityBar_Shared>() ? get(c.Read<AbilityBar_Shared>()) : null;
    static float? Leech(Entity c, Func<LifeLeech, float> get) => c.Has<LifeLeech>() ? get(c.Read<LifeLeech>()) : null;

    /// <summary>Build the sheet for the local character (null if not in game).</summary>
    public static List<Row> Build()
    {
        var character = Core.LocalCharacter;
        if (character == Entity.Null) return null;
        var em = Core.EntityManager;

        var rows = new Dictionary<UnitStatType, Row>();
        foreach (var (stat, name, format, live) in Stats)
        {
            float? value = null;
            try { value = live?.Invoke(character); } catch { }
            rows[stat] = new Row { Stat = stat, Name = name, Format = format, ClientValue = value };
        }

        // Server mods that keep stats in a buff the server fills in: the client's copy of that buff holds
        // the prefab's own values, so it's swapped for the real ones (computed / read from the mod).
        var hidden = new Dictionary<int, Dictionary<UnitStatType, float>>();
        var real = new List<(int Carrier, UnitStatType Stat, float Value, string Source)>();
        if (BloodcraftStats.Known)
        {
            hidden[BloodcraftStats.BonusBuffGuid] = new();
            real.AddRange(BloodcraftStats.CurrentBonuses().Select(b => (BloodcraftStats.BonusBuffGuid, b.Stat, b.Value, b.Source)));
        }
        if (PerkshopStats.Status == PerkshopStats.State.Present)
        {
            hidden[PerkshopStats.CarrierBuffGuid] = new();
            real.AddRange(PerkshopStats.Perks.Select(p => (PerkshopStats.CarrierBuffGuid, p.Stat, p.Value, p.Source)));
        }

        // Buffs on the character and what each changes.
        if (em.HasBuffer<BuffBuffer>(character))
        {
            var buffs = em.GetBuffer<BuffBuffer>(character, true);
            for (int i = 0; i < buffs.Length; i++)
            {
                var buff = buffs[i].Entity;
                if (buff == Entity.Null || !em.Exists(buff) || !em.HasBuffer<ModifyUnitStatBuff_DOTS>(buff)) continue;
                int guid = buffs[i].PrefabGuid.GuidHash;
                int stacks = em.HasComponent<Buff>(buff) ? Math.Max(1, (int)em.GetComponentData<Buff>(buff).Stacks) : 1;
                var mods = em.GetBuffer<ModifyUnitStatBuff_DOTS>(buff, true);
                for (int m = 0; m < mods.Length; m++)
                {
                    var mod = mods[m];
                    if (!rows.TryGetValue(mod.StatType, out var row) || !float.IsFinite(mod.Value)) continue;
                    float value = mod.IncreaseByStacks ? mod.Value + mod.ValueByStacks * (stacks - 1) : mod.Value;
                    if (hidden.TryGetValue(guid, out var clientCopy))
                    {
                        if (mod.ModificationType is ModificationType.Add or ModificationType.AddToBase)
                            clientCopy[mod.StatType] = clientCopy.GetValueOrDefault(mod.StatType) + value;
                        continue;
                    }
                    if (value == 0) continue;
                    row.Parts.Add(new Part(PrefabNames.Pretty(guid) + (stacks > 1 ? $" x{stacks}" : ""), value, mod.ModificationType));
                }
            }
        }

        foreach (var row in rows.Values) row.Total = row.ClientValue;
        foreach (var (_, stat, value, source) in real)
        {
            if (rows.TryGetValue(stat, out var row)) row.Parts.Add(new Part(source, value, ModificationType.Add));
        }
        foreach (var row in rows.Values)
        {
            if (row.ClientValue is not { } client) continue;
            float clientCopies = hidden.Values.Sum(h => h.GetValueOrDefault(row.Stat));
            float realValues = real.Where(r => r.Stat == row.Stat).Sum(r => r.Value);
            row.Total = client - clientCopies + realValues;
        }

        // What the listed additive parts don't explain: base stats, gear level, unlisted sources.
        foreach (var row in rows.Values)
        {
            if (row.Total is not { } total) continue;
            float listed = row.Parts.Where(p => p.Type is ModificationType.Add or ModificationType.AddToBase).Sum(p => p.Value);
            float rest = total - listed;
            if (Math.Abs(rest) > 0.0005f && row.Parts.All(p => p.Type is ModificationType.Add or ModificationType.AddToBase))
                row.Parts.Insert(0, new Part("Base / unlisted", rest, ModificationType.Add));
        }

        return Stats.Select(s => rows[s.Stat]).Where(r => r.Total is { } t && t != 0 || r.Parts.Count > 0).ToList();
    }

    public static string FormatValue(Format format, float value, bool signed = false)
    {
        string sign = signed && value > 0 ? "+" : "";
        return format switch
        {
            Format.Percent => $"{sign}{value * 100:0.#}%",
            Format.Speed => $"{sign}{value:0.##}",
            _ => $"{sign}{value:0.#}",
        };
    }

    public static string FormatPart(Format format, Part part) => part.Type switch
    {
        ModificationType.Multiply => $"x{part.Value:0.###}",
        ModificationType.MultiplyBaseAdd => $"x base {FormatValue(format, part.Value, signed: true)}",
        ModificationType.Set => $"= {FormatValue(format, part.Value)}",
        ModificationType.SetMin => $"min {FormatValue(format, part.Value)}",
        ModificationType.SetMax => $"max {FormatValue(format, part.Value)}",
        _ => FormatValue(format, part.Value, signed: true),
    };

    /// <summary>Expected damage factor from crits: 1 + chance x crit damage (chance capped at 100%).</summary>
    public static float CritFactor(List<Row> rows, UnitStatType chance, UnitStatType damage)
    {
        float c = rows.FirstOrDefault(r => r.Stat == chance)?.Total ?? 0;
        float d = rows.FirstOrDefault(r => r.Stat == damage)?.Total ?? 0;
        return 1 + Math.Clamp(c, 0, 1) * Math.Max(0, d);
    }
}
