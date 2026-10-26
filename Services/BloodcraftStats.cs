using System.Globalization;
using System.Text.RegularExpressions;
using ProjectM;
using ProjectM.Network;

namespace DragUI.Services;

// Bloodcraft's stat bonuses (weapon expertise + blood legacy), recomputed on the client. The server puts
// them in a buff whose real values never reach the client, but Bloodcraft sends Eclipse users everything
// needed to recompute them, as System chat messages "[subtype]:a,b,c;mac<signature>" (Eclipse protocol,
// by zfolmt; the format is read here, no Eclipse code is used):
//   [2] ConfigsToClient: prestige stat multiplier, class synergy multiplier, max levels (player, legacy,
//       expertise, familiar, profession), extra recipes, primal cost, 12 weapon-stat caps, 12 blood-stat
//       caps, then per class: class id, weapon synergy stats, blood synergy stats (2-digit codes).
//   [1] ProgressToClient: experience (percent, level, prestige, class), legacy (percent, level, prestige,
//       blood type, chosen stats), expertise (percent, level, prestige, weapon type, chosen stats), ...,
//       and last the class shift spell (index in the class's spell list, -1 = none).
// Bloodcraft: value = cap[stat] x (class synergy ? synergy mult : 1) x (1 + prestige mult x prestige)
//             x (level / max level), applied as ModificationType.Add.
// Our chat hook runs before Eclipse's, which destroys these messages after reading them.
internal static class BloodcraftStats
{
    /// <summary>The buff Bloodcraft keeps its stat bonuses in (SetBonus_AllLeech_T09).</summary>
    public const int BonusBuffGuid = 1774716596;

    // Stat codes 1-12 (0 = none), in Bloodcraft's WeaponStatType / BloodStatType order.
    static readonly UnitStatType[] WeaponStats =
    {
        UnitStatType.MaxHealth, UnitStatType.MovementSpeed, UnitStatType.PrimaryAttackSpeed, UnitStatType.PhysicalLifeLeech,
        UnitStatType.SpellLifeLeech, UnitStatType.PrimaryLifeLeech, UnitStatType.PhysicalPower, UnitStatType.SpellPower,
        UnitStatType.PhysicalCriticalStrikeChance, UnitStatType.PhysicalCriticalStrikeDamage,
        UnitStatType.SpellCriticalStrikeChance, UnitStatType.SpellCriticalStrikeDamage,
    };
    static readonly UnitStatType[] BloodStats =
    {
        UnitStatType.HealingReceived, UnitStatType.DamageReduction, UnitStatType.PhysicalResistance, UnitStatType.SpellResistance,
        UnitStatType.ResourceYield, UnitStatType.ReducedBloodDrain, UnitStatType.SpellCooldownRecoveryRate,
        UnitStatType.WeaponCooldownRecoveryRate, UnitStatType.UltimateCooldownRecoveryRate, UnitStatType.MinionDamage,
        UnitStatType.AbilityAttackSpeed, UnitStatType.CorruptionDamageReduction,
    };
    static readonly string[] WeaponTypes =
    {
        "Sword", "Axe", "Mace", "Spear", "Crossbow", "Great Sword", "Slashers", "Pistols", "Reaper", "Longbow", "Whip",
        "Unarmed", "Fishing Pole", "Twin Blades", "Daggers", "Claws",
    };
    /// <summary>Bloodcraft's PlayerClass names, by class id - 1 (what .class s / .class c accept).</summary>
    public static readonly string[] ClassNames =
    {
        "BloodKnight", "DemonHunter", "VampireLord", "ShadowBlade", "ArcaneSorcerer", "DeathMage",
    };
    static readonly string[] BloodTypes =
    {
        "Worker", "Warrior", "Scholar", "Rogue", "Mutant", "V Blood", "Frailed", "Gate Boss", "Draculin", "Immortal",
        "Creature", "Brute", "Corruption",
    };

    static readonly Regex _message = new(@"^\[(?<type>\d+)\]:(?<body>.*?)(?:;mac[^;]*)?$", RegexOptions.Compiled | RegexOptions.Singleline);

    sealed class Track
    {
        public int Level, Prestige, Type;
        public int[] Chosen = Array.Empty<int>();
    }

    static float _prestigeMult, _synergyMult;
    static int _maxLegacy, _maxExpertise;
    static float[] _weaponCaps, _bloodCaps;
    static readonly Dictionary<int, (HashSet<int> Weapon, HashSet<int> Blood)> _synergies = new();
    static int _class;
    static int _shiftSpell = -1;
    static Track _legacy, _expertise;

    public sealed record Bonus(UnitStatType Stat, float Value, string Source);

    /// <summary>True once both the config and a progress message have been seen this session.</summary>
    public static bool Known => _weaponCaps != null && _expertise != null;

    /// <summary>True once a progress message has been seen this session (ClassId and ShiftSpell are set).</summary>
    public static bool ProgressKnown => _expertise != null;

    /// <summary>Bloodcraft class: 1-based index into ClassNames, 0 = none chosen.</summary>
    public static int ClassId => _class;

    /// <summary>Class shift spell: index in the class's spell list (.class csp takes index + 1), -1 = none.</summary>
    public static int ShiftSpell => _shiftSpell;

    public static string ClassLabel(int classId) =>
        classId >= 1 && classId <= ClassNames.Length ? Regex.Replace(ClassNames[classId - 1], "(?<=[a-z])(?=[A-Z])", " ") : "No class";

    public static void OnServerMessage(ServerChatMessageType type, string message)
    {
        if (type != ServerChatMessageType.System || message == null || message.Length < 4 || message[0] != '[') return;
        var match = _message.Match(message);
        if (!match.Success) return;
        try
        {
            var fields = match.Groups["body"].Value.Split(',');
            switch (match.Groups["type"].Value)
            {
                case "2": ParseConfig(fields); break;
                case "1": ParseProgress(fields); break;
            }
        }
        catch (Exception ex)
        {
            Core.Diag($"Bloodcraft data message not understood: {ex.Message}");
        }
    }

    static void ParseConfig(string[] f)
    {
        int i = 0;
        float prestige = F(f[i++]), synergy = F(f[i++]);
        i++; // max player level
        int maxLegacy = I(f[i++]), maxExpertise = I(f[i++]);
        i += 4; // max familiar, max profession, extra recipes, primal cost
        var weaponCaps = f.Skip(i).Take(12).Select(F).ToArray();
        var bloodCaps = f.Skip(i + 12).Take(12).Select(F).ToArray();
        if (weaponCaps.Length != 12 || bloodCaps.Length != 12) return;

        _synergies.Clear();
        var rest = f.Skip(i + 24).ToArray();
        for (int c = 0; c + 2 < rest.Length; c += 3)
            _synergies[I(rest[c])] = (Codes(rest[c + 1]).ToHashSet(), Codes(rest[c + 2]).ToHashSet());

        (_prestigeMult, _synergyMult, _maxLegacy, _maxExpertise, _weaponCaps, _bloodCaps) =
            (prestige, synergy, maxLegacy, maxExpertise, weaponCaps, bloodCaps);
        Core.Diag($"Bloodcraft config: prestige x{prestige}, synergy x{synergy}, max legacy {maxLegacy}, max expertise {maxExpertise}.");
    }

    static void ParseProgress(string[] f)
    {
        // experience: percent, level, prestige, class
        _class = I(f[3]);
        // legacy: percent, level, prestige, blood type, chosen stats
        _legacy = new Track { Level = I(f[5]), Prestige = I(f[6]), Type = I(f[7]), Chosen = Codes(f[8]).ToArray() };
        // expertise: percent, level, prestige, weapon type, chosen stats
        _expertise = new Track { Level = I(f[10]), Prestige = I(f[11]), Type = I(f[12]), Chosen = Codes(f[13]).ToArray() };
        _shiftSpell = int.TryParse(f[^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int shift) ? shift : -1;
    }

    /// <summary>Bloodcraft's current stat bonuses (empty until its data has been seen this session).</summary>
    public static List<Bonus> CurrentBonuses()
    {
        var bonuses = new List<Bonus>();
        if (!Known) return bonuses;
        _synergies.TryGetValue(_class, out var synergy);

        string weapon = Name(WeaponTypes, _expertise.Type);
        foreach (int code in _expertise.Chosen)
        {
            if (code < 1 || code > 12) continue;
            float value = _weaponCaps[code - 1] * (synergy.Weapon?.Contains(code) == true ? _synergyMult : 1f)
                          * (1 + _prestigeMult * _expertise.Prestige) * _expertise.Level / Math.Max(1, _maxExpertise);
            bonuses.Add(new Bonus(WeaponStats[code - 1], value,
                $"Bloodcraft {weapon} expertise {_expertise.Level}/{_maxExpertise}{Prestige(_expertise)}"));
        }

        string blood = Name(BloodTypes, _legacy.Type);
        foreach (int code in _legacy.Chosen)
        {
            if (code < 1 || code > 12) continue;
            float value = _bloodCaps[code - 1] * (synergy.Blood?.Contains(code) == true ? _synergyMult : 1f)
                          * (1 + _prestigeMult * _legacy.Prestige) * _legacy.Level / Math.Max(1, _maxLegacy);
            bonuses.Add(new Bonus(BloodStats[code - 1], value,
                $"Bloodcraft {blood} legacy {_legacy.Level}/{_maxLegacy}{Prestige(_legacy)}"));
        }
        return bonuses;
    }

    static string Prestige(Track t) => t.Prestige > 0 ? $" P{t.Prestige}" : string.Empty;
    static string Name(string[] names, int index) => index >= 0 && index < names.Length ? names[index] : $"type {index}";
    static IEnumerable<int> Codes(string s) => Enumerable.Range(0, s.Length / 2).Select(j => I(s.Substring(j * 2, 2)));
    static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);
    static int I(string s) => int.Parse(s, CultureInfo.InvariantCulture);

    public static void OnWorldTeardown()
    {
        _weaponCaps = _bloodCaps = null;
        _legacy = _expertise = null;
        _class = 0;
        _shiftSpell = -1;
        _synergies.Clear();
    }
}
