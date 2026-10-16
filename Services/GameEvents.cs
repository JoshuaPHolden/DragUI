using Il2CppInterop.Runtime;
using ProjectM.Network;
using Stunlock.Core;
using Unity.Entities;

namespace VBloodHunter.Services;

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
