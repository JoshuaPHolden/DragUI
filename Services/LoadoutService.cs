using ProjectM;
using Stunlock.Core;
using Unity.Entities;
using UnityEngine;
using DragUI.Data;
using Loadout = DragUI.Services.LoadoutStore.Loadout;

namespace DragUI.Services;

// Records and restores a build: the equipped gear, the spells, the spell school passives, and the Bloodcraft
// class and class shift spell. Applying sends what the game's own menus send (EquipItemEvent from the
// inventory, ActivateVBloodAbilityEvent from the spellbook, Equip/UnEquipSpellSchoolPassive) plus
// Bloodcraft's chat commands, one step at a time so each step sees what the last one left. The server checks every step as if done by hand: gear has to be in the
// inventory. Bloodcraft's weapon and blood stats need nothing here: Bloodcraft keeps them per weapon type
// and blood type, so they follow the gear.
internal static class LoadoutService
{
    const int PassiveSlots = 5;
    const double StepSeconds = 0.2;
    const double CommandSeconds = 1.5; // give Bloodcraft time to act (and send its update) before the next step

    // The bag first (it changes the inventory's size), the weapon last.
    static readonly EquipmentType[] GearSlots =
    {
        EquipmentType.Bag, EquipmentType.Headgear, EquipmentType.Chest, EquipmentType.Gloves, EquipmentType.Legs,
        EquipmentType.Footgear, EquipmentType.Cloak, EquipmentType.MagicSource, EquipmentType.Weapon,
    };

    // Ability bar slots holding spells (AbilityTypeEnum: SpellSlot1 = 5, SpellSlot2 = 6, Ultimate = 7).
    static readonly (int SlotId, string Key)[] SpellSlots = { (5, "SpellSlot1"), (6, "SpellSlot2"), (7, "Ultimate") };

    static readonly Queue<(Action Run, double Wait, string Label)> _steps = new();
    static readonly List<string> _missing = new();
    static double _nextStepAt;
    static int _total;          // steps in the running apply
    static int _ran = -1;       // index of the last step run (-1 = none yet)
    static double _ranAt, _ranWait;

    public static bool Busy => _steps.Count > 0 || (_total > 0 && Time.realtimeSinceStartupAsDouble < _nextStepAt);
    /// <summary>The loadout being (or last) applied.</summary>
    public static string ApplyingName { get; private set; } = string.Empty;
    /// <summary>What the running apply is doing now ("Equipping Chest", ...).</summary>
    public static string StepLabel { get; private set; } = string.Empty;
    /// <summary>What went wrong in the last apply (items not in the inventory), empty if nothing.</summary>
    public static string Problem { get; private set; } = string.Empty;
    /// <summary>realtimeSinceStartup when the last apply finished (0 = none yet).</summary>
    public static double FinishedAt { get; private set; }

    /// <summary>0..1 through the running apply, moving smoothly through each step's wait.</summary>
    public static float Progress
    {
        get
        {
            if (_total == 0 || _ran < 0) return 0f;
            double into = _ranWait > 0 ? Math.Min(1.0, (Time.realtimeSinceStartupAsDouble - _ranAt) / _ranWait) : 1.0;
            return Mathf.Clamp01((float)((_ran + into) / _total));
        }
    }

    public static Loadout Capture(string name)
    {
        var loadout = new Loadout { Name = name, Spells = ReadSpells(), Passives = ReadPassives() };
        foreach (var (type, guid) in ReadGear())
            if (guid != 0) loadout.Gear[type.ToString()] = guid;
        if (BloodcraftStats.ProgressKnown)
        {
            loadout.ClassId = BloodcraftStats.ClassId;
            loadout.ShiftSpell = BloodcraftStats.ShiftSpell;
        }
        if (Settings.Diagnostics) GameInspector.DumpCharacter($"Loadout '{name}'");
        Core.Diag($"Loadout '{name}' recorded: gear [{string.Join(", ", loadout.Gear.Select(g => $"{g.Key} {PrefabNames.Pretty(g.Value)}"))}], " +
                  $"spells [{string.Join(", ", loadout.Spells.Select(s => $"{s.Key} {PrefabNames.Pretty(s.Value)}"))}], " +
                  $"passives [{string.Join(", ", loadout.Passives.Select(PrefabNames.Pretty))}], class {loadout.ClassId}, shift {loadout.ShiftSpell}.");
        return loadout;
    }

    /// <summary>One line for the list: class, weapon and counts.</summary>
    public static string Describe(Loadout loadout)
    {
        var parts = new List<string>();
        if (loadout.ClassId > 0)
            parts.Add(BloodcraftStats.ClassLabel(loadout.ClassId) + (loadout.ShiftSpell >= 0 ? $" (shift {loadout.ShiftSpell + 1})" : string.Empty));
        if (loadout.Gear.TryGetValue(nameof(EquipmentType.Weapon), out int weapon)) parts.Add(PrefabNames.Pretty(weapon));
        parts.Add($"{loadout.Gear.Count} gear");
        parts.Add($"{loadout.Spells.Count} spells");
        parts.Add($"{loadout.Passives.Count} passives");
        return string.Join("  ·  ", parts);
    }

    /// <summary>Put <paramref name="loadout"/> on, step by step (Tick runs the steps). Changes the Bloodcraft
    /// class too if it differs, which costs whatever the server charges for .class c.</summary>
    public static void Apply(Loadout loadout)
    {
        _steps.Clear();
        _missing.Clear();
        _nextStepAt = 0;
        _ran = -1;
        ApplyingName = loadout.Name;
        Problem = string.Empty;

        if (loadout.ClassId > 0 && BloodcraftStats.ProgressKnown && BloodcraftStats.ClassId != loadout.ClassId)
        {
            string name = BloodcraftStats.ClassNames[loadout.ClassId - 1];
            Command(BloodcraftStats.ClassId == 0 ? $".class s {name}" : $".class c {name}", $"Changing class to {BloodcraftStats.ClassLabel(loadout.ClassId)}");
        }

        foreach (var type in GearSlots)
        {
            if (!loadout.Gear.TryGetValue(type.ToString(), out int guid) || guid == 0) continue;
            var slot = type;
            _steps.Enqueue((() => EquipGear(slot, guid), StepSeconds, $"Equipping {GearLabel(type)}"));
        }

        foreach (var (_, key) in SpellSlots)
        {
            if (!loadout.Spells.TryGetValue(key, out int guid) || guid == 0) continue;
            string slot = key;
            _steps.Enqueue((() => SetSpell(slot, guid), StepSeconds, "Setting spells"));
        }

        // Only when the set differs: clear every slot, then fill them in order (a passive can't be equipped
        // while it's still in another slot).
        var current = ReadPassives();
        if (!current.OrderBy(g => g).SequenceEqual(loadout.Passives.OrderBy(g => g)))
        {
            for (int i = 0; i < PassiveSlots; i++)
            {
                int slot = i;
                _steps.Enqueue((() => GameEvents.UnequipPassive(slot), StepSeconds, "Clearing passives"));
            }
            for (int i = 0; i < loadout.Passives.Count && i < PassiveSlots; i++)
            {
                int slot = i, guid = loadout.Passives[i];
                _steps.Enqueue((() => GameEvents.EquipPassive(new PrefabGUID(guid), slot), StepSeconds, "Equipping passives"));
            }
        }

        if (loadout.ShiftSpell >= 0)
        {
            // Checked when it runs, after any class change has been reported back.
            int shift = loadout.ShiftSpell;
            _steps.Enqueue((() =>
            {
                if (BloodcraftStats.ShiftSpell != shift) ChatCommandSender.TrySend($".class csp {shift + 1}");
            }, CommandSeconds, "Setting shift spell"));
        }

        _steps.Enqueue((() =>
        {
            Problem = _missing.Count == 0 ? string.Empty : $"Not in your inventory: {string.Join(", ", _missing)}.";
            FinishedAt = Time.realtimeSinceStartupAsDouble;
        }, 0, "Done"));
        _total = _steps.Count;
        StepLabel = _steps.Peek().Label;
    }

    static string GearLabel(EquipmentType type) => type switch
    {
        EquipmentType.MagicSource => "amulet",
        EquipmentType.Headgear => "head",
        EquipmentType.Footgear => "boots",
        _ => type.ToString().ToLowerInvariant(),
    };

    /// <summary>Per frame while in game: run the next step when it's due.</summary>
    public static void Tick()
    {
        if (_steps.Count == 0) return;
        double now = Time.realtimeSinceStartupAsDouble;
        if (now < _nextStepAt) return;
        var (run, wait, label) = _steps.Dequeue();
        _nextStepAt = now + wait;
        _ran++;
        _ranAt = now;
        _ranWait = wait;
        StepLabel = label;
        try
        {
            run();
        }
        catch (Exception ex)
        {
            Core.Log.LogWarning($"Loadout step failed: {ex.Message}");
        }
    }

    public static void OnWorldTeardown()
    {
        _steps.Clear();
        _missing.Clear();
        _total = 0;
        _ran = -1;
        Problem = string.Empty;
        FinishedAt = 0;
    }

    static void Command(string command, string label) => _steps.Enqueue((() => ChatCommandSender.TrySend(command), CommandSeconds, label));

    static void EquipGear(EquipmentType type, int guid)
    {
        if (ReadGear().TryGetValue(type, out int current) && current == guid) return;
        if (!TryFindInInventory(guid, out int slot))
        {
            _missing.Add(PrefabNames.Pretty(guid));
            return;
        }
        GameEvents.EquipItem(slot);
        Core.Diag($"Loadout: equip {PrefabNames.Pretty(guid)} ({type}) from inventory slot {slot}.");
    }

    static Dictionary<EquipmentType, int> ReadGear()
    {
        var gear = new Dictionary<EquipmentType, int>();
        var character = Core.LocalCharacter;
        if (!character.Exists() || !character.Has<Equipment>()) return gear;

        var e = character.Read<Equipment>();
        gear[EquipmentType.Headgear] = ItemGuid(e.ArmorHeadgearSlot);
        gear[EquipmentType.Chest] = ItemGuid(e.ArmorChestSlot);
        gear[EquipmentType.Weapon] = ItemGuid(e.WeaponSlot);
        gear[EquipmentType.MagicSource] = ItemGuid(e.GrimoireSlot);
        gear[EquipmentType.Footgear] = ItemGuid(e.ArmorFootgearSlot);
        gear[EquipmentType.Legs] = ItemGuid(e.ArmorLegsSlot);
        gear[EquipmentType.Cloak] = ItemGuid(e.CloakSlot);
        gear[EquipmentType.Gloves] = ItemGuid(e.ArmorGlovesSlot);
        gear[EquipmentType.Bag] = ItemGuid(e.BagSlot);
        return gear;
    }

    // Re-read when it runs: putting a spell into one slot can move the other (picking slot 2's spell for
    // slot 1 swaps them), so a later step may find it already done.
    static void SetSpell(string key, int guid)
    {
        if (ReadSpells().TryGetValue(key, out int current) && current == guid) return;
        GameEvents.ActivateVBloodAbility(new PrefabGUID(guid), primarySlot: key != "SpellSlot2");
        Core.Diag($"Loadout: spell {PrefabNames.Pretty(guid)} -> {key}.");
    }

    static Dictionary<string, int> ReadSpells()
    {
        var spells = new Dictionary<string, int>();
        var character = Core.LocalCharacter;
        if (!character.Exists() || !character.Has<AbilityGroupSlotBuffer>()) return spells;

        var slots = Core.EntityManager.GetBuffer<AbilityGroupSlotBuffer>(character, true);
        for (int i = 0; i < slots.Length; i++)
        {
            if (!slots[i].GroupSlotEntity.TryGetSyncedEntity(out var slotEntity) || !slotEntity.Exists() || !slotEntity.Has<AbilityGroupSlot>()) continue;
            var slot = slotEntity.Read<AbilityGroupSlot>();
            string key = SpellSlots.FirstOrDefault(s => s.SlotId == slot.SlotId).Key;
            int guid = slot.GroupGuid._Value.GuidHash;
            if (key != null && guid != 0) spells[key] = guid;
        }
        return spells;
    }

    static int ItemGuid(EquipmentSlot slot) =>
        slot.SlotEntity.TryGetSyncedEntity(out var item) && item.Exists() && item.Has<PrefabGUID>() ? item.Read<PrefabGUID>().GuidHash : 0;

    // Equipped passives: ActivePassivesBuffer, on the character or else the user. Entries named like
    // something other than a spell school passive (or blank) are skipped; unnamed ones are kept.
    static List<int> ReadPassives()
    {
        var passives = new List<int>();
        var owner = new[] { Core.LocalCharacter, Core.LocalUser }.FirstOrDefault(e => e.Exists() && e.Has<ActivePassivesBuffer>());
        if (owner == Entity.Null) return passives;

        var buffer = Core.EntityManager.GetBuffer<ActivePassivesBuffer>(owner, true);
        for (int i = 0; i < buffer.Length; i++)
        {
            int guid = buffer[i].PrefabGuid.GuidHash;
            if (guid == 0) continue;
            if (!PrefabNames.TryGet(guid, out var name) || name.Contains("SpellPassive", StringComparison.OrdinalIgnoreCase))
                passives.Add(guid);
        }
        return passives;
    }

    static bool TryFindInInventory(int guid, out int slot)
    {
        slot = -1;
        if (!TryGetInventory(out var inventory)) return false;
        var items = Core.EntityManager.GetBuffer<InventoryBuffer>(inventory, true);
        for (int i = 0; i < items.Length; i++)
        {
            if (items[i].ItemType.GuidHash != guid) continue;
            slot = i;
            return true;
        }
        return false;
    }

    // The player's main inventory: the first inventory the character owns.
    static bool TryGetInventory(out Entity inventory)
    {
        inventory = Entity.Null;
        var character = Core.LocalCharacter;
        if (!character.Exists() || !character.Has<InventoryInstanceElement>()) return false;
        var owned = Core.EntityManager.GetBuffer<InventoryInstanceElement>(character, true);
        for (int i = 0; i < owned.Length; i++)
        {
            if (!owned[i].ExternalInventoryEntity.TryGetSyncedEntity(out var entity) || !entity.Exists() || !entity.Has<InventoryBuffer>()) continue;
            inventory = entity;
            return true;
        }
        return false;
    }
}
