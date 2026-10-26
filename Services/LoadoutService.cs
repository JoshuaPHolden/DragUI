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

    static readonly Queue<(Action Run, double Wait)> _steps = new();
    static readonly List<string> _missing = new();
    static double _nextStepAt;

    /// <summary>What the last apply did (or is doing), for the panel.</summary>
    public static string Status { get; private set; } = string.Empty;
    public static bool Busy => _steps.Count > 0;

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

    /// <summary>True if applying would change an already chosen Bloodcraft class (which costs items).</summary>
    public static bool NeedsClassChange(Loadout loadout) =>
        loadout.ClassId > 0 && BloodcraftStats.ProgressKnown && BloodcraftStats.ClassId > 0 && BloodcraftStats.ClassId != loadout.ClassId;

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

    /// <param name="changeClass">Also change an already chosen class (the player confirmed the cost).</param>
    public static void Apply(Loadout loadout, bool changeClass)
    {
        _steps.Clear();
        _missing.Clear();
        _nextStepAt = 0;
        Status = $"Applying {loadout.Name}...";
        string note = string.Empty;

        if (loadout.ClassId > 0 && BloodcraftStats.ProgressKnown && BloodcraftStats.ClassId != loadout.ClassId)
        {
            string name = BloodcraftStats.ClassNames[loadout.ClassId - 1];
            if (BloodcraftStats.ClassId == 0) Command($".class s {name}");
            else if (changeClass) Command($".class c {name}");
            else note = " Class not changed.";
        }

        foreach (var type in GearSlots)
        {
            if (!loadout.Gear.TryGetValue(type.ToString(), out int guid) || guid == 0) continue;
            var slot = type;
            _steps.Enqueue((() => EquipGear(slot, guid), StepSeconds));
        }

        foreach (var (_, key) in SpellSlots)
        {
            if (!loadout.Spells.TryGetValue(key, out int guid) || guid == 0) continue;
            string slot = key;
            _steps.Enqueue((() => SetSpell(slot, guid), StepSeconds));
        }

        // Only when the set differs: clear every slot, then fill them in order (a passive can't be equipped
        // while it's still in another slot).
        var current = ReadPassives();
        if (!current.OrderBy(g => g).SequenceEqual(loadout.Passives.OrderBy(g => g)))
        {
            for (int i = 0; i < PassiveSlots; i++)
            {
                int slot = i;
                _steps.Enqueue((() => GameEvents.UnequipPassive(slot), StepSeconds));
            }
            for (int i = 0; i < loadout.Passives.Count && i < PassiveSlots; i++)
            {
                int slot = i, guid = loadout.Passives[i];
                _steps.Enqueue((() => GameEvents.EquipPassive(new PrefabGUID(guid), slot), StepSeconds));
            }
        }

        if (loadout.ShiftSpell >= 0)
        {
            // Checked when it runs, after any class change has been reported back.
            int shift = loadout.ShiftSpell;
            _steps.Enqueue((() =>
            {
                if (BloodcraftStats.ShiftSpell != shift) ChatCommandSender.TrySend($".class csp {shift + 1}");
            }, CommandSeconds));
        }

        string loadoutName = loadout.Name;
        _steps.Enqueue((() =>
        {
            Status = _missing.Count == 0
                ? $"Applied {loadoutName}.{note}"
                : $"Applied {loadoutName}.{note} Not in your inventory: {string.Join(", ", _missing)}.";
        }, 0));
    }

    /// <summary>Per frame while in game: run the next step when it's due.</summary>
    public static void Tick()
    {
        if (_steps.Count == 0) return;
        double now = Time.realtimeSinceStartupAsDouble;
        if (now < _nextStepAt) return;
        var (run, wait) = _steps.Dequeue();
        _nextStepAt = now + wait;
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
        Status = string.Empty;
    }

    static void Command(string command) => _steps.Enqueue((() => ChatCommandSender.TrySend(command), CommandSeconds));

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
