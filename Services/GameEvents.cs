using Il2CppInterop.Runtime;
using ProjectM.Network;
using Stunlock.Core;
using Unity.Entities;

namespace DragUI.Services;

// Sends the same client->server events the game's own V Blood menu sends. A client network event
// is an entity with FromCharacter + NetworkEventType + SendNetworkEventTag + the event component;
// the game's send system serialises and destroys it.
internal static class GameEvents
{
    public static bool RequestVBloodPositions()
    {
        if (!TryCreate<GetVBloodPositionsEvent>(NetworkEvents.EventId_GetVBloodPositionsEvent, out _)) return false;
        return true;
    }

    public static bool StartTracking(PrefabGUID vblood)
    {
        if (!TryCreate<StartTrackVBloodUnitEventV2>(NetworkEvents.EventId_StartTrackVBloodUnitEventV2, out var entity)) return false;
        entity.Write(new StartTrackVBloodUnitEventV2 { HuntTarget = vblood });
        return true;
    }

    public static bool StopTracking()
    {
        return TryCreate<StopTrackVBloodUnitEvent>(NetworkEvents.EventId_StopTrackVBloodUnitEvent, out _);
    }

    /// <summary>Equip the item in this slot of the player's own inventory (what using it from the inventory does).</summary>
    public static bool EquipItem(int inventorySlot)
    {
        if (!TryCreate<EquipItemEvent>(NetworkEvents.EventId_EquipItemEvent, out var entity)) return false;
        entity.Write(new EquipItemEvent { SlotIndex = inventorySlot, IsCosmetic = false });
        return true;
    }

    /// <summary>Put a spell (V Blood ability group) on the bar: what picking it in the spellbook sends.
    /// PrimarySlot picks spell slot 1 over slot 2; an ultimate goes to the ultimate slot either way.</summary>
    public static bool ActivateVBloodAbility(PrefabGUID ability, bool primarySlot)
    {
        if (!TryCreate<ActivateVBloodAbilityEvent>(NetworkEvents.EventId_ActivateVBloodAbilityEvent, out var entity)) return false;
        entity.Write(new ActivateVBloodAbilityEvent { AbilityGUID = ability, PrimarySlot = primarySlot });
        return true;
    }

    public static bool EquipPassive(PrefabGUID passive, int slot)
    {
        if (!TryCreate<EquipSpellSchoolPassive>(NetworkEvents.EventId_EquipSpellSchoolPassive, out var entity)) return false;
        entity.Write(new EquipSpellSchoolPassive { Passive = passive, Slot = slot });
        return true;
    }

    public static bool UnequipPassive(int slot)
    {
        if (!TryCreate<UnEquipSpellSchoolPassive>(NetworkEvents.EventId_UnEquipSpellSchoolPassive, out var entity)) return false;
        entity.Write(new UnEquipSpellSchoolPassive { Slot = slot });
        return true;
    }

    static bool TryCreate<TEvent>(int eventId, out Entity entity)
    {
        entity = Entity.Null;
        var character = Core.LocalCharacter;
        var user = Core.LocalUser;
        if (character == Entity.Null || user == Entity.Null) return false;

        var em = Core.EntityManager;
        entity = em.CreateEntity(new[]
        {
            ComponentType.ReadOnly(Il2CppType.Of<FromCharacter>()),
            ComponentType.ReadOnly(Il2CppType.Of<NetworkEventType>()),
            ComponentType.ReadOnly(Il2CppType.Of<SendNetworkEventTag>()),
            ComponentType.ReadOnly(Il2CppType.Of<TEvent>()),
        });
        entity.Write(new FromCharacter { Character = character, User = user });
        entity.Write(new NetworkEventType { EventId = eventId, IsAdminEvent = false, IsDebugEvent = false });
        return true;
    }
}
