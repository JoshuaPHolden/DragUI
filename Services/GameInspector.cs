using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using ProjectM;
using ProjectM.Network;
using Unity.Collections;
using Unity.Entities;
using DragUI.Data;

namespace DragUI.Services;

// Diagnostics for working out how the game does something (only with Debug.Diagnostics on):
//   * every event the client sends to the server, with its values for the build-related ones, so doing
//     something by hand in a game menu shows which event and values it uses;
//   * the player's spell slots and passive buffers, and which build-related components they have.
// (Generic raw dumps don't work here: TypeManager's sizes read as 0 through the interop, so the known
// types are read directly.)
internal static class GameInspector
{
    static readonly HashSet<string> _quietEvents = new() { "GetVBloodPositionsEvent", "SpellModSetAckEvent" };
    static readonly HashSet<string> _carrierTypes = new() { "FromCharacter", "NetworkEventType", "SendNetworkEventTag", "Simulate" };
    static readonly Regex _buildTypes = new("Passive|Spell|Ability|Jewel|Slot|Equip", RegexOptions.Compiled);
    static Dictionary<int, string> _eventNames;

    /// <summary>Log one outgoing client event (called for each event the game is about to send).</summary>
    public static void LogSentEvent(Entity entity)
    {
        string name = entity.Has<NetworkEventType>() ? EventName(entity.Read<NetworkEventType>().EventId) : "(no event type)";
        if (_quietEvents.Contains(name)) return;

        string values;
        if (entity.Has<ActivateVBloodAbilityEvent>())
        {
            var e = entity.Read<ActivateVBloodAbilityEvent>();
            values = $"AbilityGUID {Guid(e.AbilityGUID.GuidHash)}, PrimarySlot {e.PrimarySlot}";
        }
        else if (entity.Has<EquipSpellSchoolPassive>())
        {
            var e = entity.Read<EquipSpellSchoolPassive>();
            values = $"Passive {Guid(e.Passive.GuidHash)}, Slot {e.Slot}";
        }
        else if (entity.Has<UnEquipSpellSchoolPassive>())
        {
            values = $"Slot {entity.Read<UnEquipSpellSchoolPassive>().Slot}";
        }
        else if (entity.Has<EquipItemEvent>())
        {
            var e = entity.Read<EquipItemEvent>();
            values = $"SlotIndex {e.SlotIndex}, IsCosmetic {e.IsCosmetic}";
        }
        else
        {
            values = string.Join(", ", ComponentNames(entity).Where(n => !_carrierTypes.Contains(n)));
        }
        Core.Log.LogInfo($"[diag] Sent {name}: {values}");
    }

    /// <summary>Log the spell slots, passive buffers and build-related components of the character and user.</summary>
    public static void DumpCharacter(string reason)
    {
        var em = Core.EntityManager;
        var character = Core.LocalCharacter;
        var text = new StringBuilder($"{reason}:");

        if (character.Exists() && character.Has<AbilityGroupSlotBuffer>())
        {
            var slots = em.GetBuffer<AbilityGroupSlotBuffer>(character, true);
            text.Append($"\n    AbilityGroupSlotBuffer [{slots.Length}]");
            for (int i = 0; i < slots.Length; i++)
            {
                text.Append($"\n      #{i} base {Guid(slots[i].BaseAbilityGroupOnSlot.GuidHash)}");
                if (slots[i].GroupSlotEntity.TryGetSyncedEntity(out var slotEntity) && slotEntity.Exists() && slotEntity.Has<AbilityGroupSlot>())
                {
                    var slot = slotEntity.Read<AbilityGroupSlot>();
                    text.Append($" | SlotId {slot.SlotId}, group {Guid(slot.GroupGuid._Value.GuidHash)}");
                }
                else text.Append(" | slot entity not synced");
            }
        }
        else text.Append("\n    no AbilityGroupSlotBuffer on the character");

        foreach (var (label, entity) in new[] { ("character", character), ("user", Core.LocalUser) })
        {
            if (!entity.Exists()) continue;
            if (entity.Has<ActivePassivesBuffer>())
            {
                var passives = em.GetBuffer<ActivePassivesBuffer>(entity, true);
                text.Append($"\n    {label} ActivePassivesBuffer [{passives.Length}]");
                for (int i = 0; i < passives.Length; i++) text.Append($"\n      #{i} {Guid(passives[i].PrefabGuid.GuidHash)}");
            }
            if (entity.Has<UnlockedPassivesBuffer>())
                text.Append($"\n    {label} UnlockedPassivesBuffer [{em.GetBuffer<UnlockedPassivesBuffer>(entity, true).Length}]");
            text.Append($"\n    {label} components: {string.Join(", ", ComponentNames(entity).Where(n => _buildTypes.IsMatch(n)))}");
        }
        Core.Log.LogInfo($"[diag] {text}");
    }

    static string Guid(int guid) => guid == 0 ? "0" : PrefabNames.TryGet(guid, out var name) ? $"{guid}={name}" : guid.ToString();

    static List<string> ComponentNames(Entity entity)
    {
        var names = new List<string>();
        var types = Core.EntityManager.GetComponentTypes(entity, Allocator.Temp);
        try
        {
            foreach (var type in types)
            {
                string name;
                try { name = type.GetManagedType()?.Name ?? type.ToString(); }
                catch { name = type.ToString(); }
                names.Add(name);
            }
        }
        finally
        {
            types.Dispose();
        }
        return names;
    }

    static string EventName(int id)
    {
        if (_eventNames == null)
        {
            _eventNames = new Dictionary<int, string>();
            foreach (var property in typeof(NetworkEvents).GetProperties(BindingFlags.Public | BindingFlags.Static))
            {
                if (!property.Name.StartsWith("EventId_", StringComparison.Ordinal) || property.PropertyType != typeof(int)) continue;
                try { _eventNames.TryAdd((int)property.GetValue(null), property.Name["EventId_".Length..]); }
                catch { }
            }
        }
        return _eventNames.TryGetValue(id, out var name) ? name : $"event #{id}";
    }
}
