using Il2CppInterop.Runtime;
using ProjectM.Network;
using Unity.Collections;
using Unity.Entities;

namespace DragUI.Services;

// Sends a chat command to the server as if the player had typed it (e.g. ".fam sb Alpha"), so a
// button can run a server mod's command. Same network-event injection Eclipse and Raphael use: one
// entity with the chat event and SendNetworkEventTag, which the game's network system sends and
// destroys. Main thread only (button clicks and the Ticker are).
internal static class ChatCommandSender
{
    // Built on first use: ComponentType lookups fail before the game's ECS type manager is up.
    static ComponentType[] _components;

    static ComponentType[] Components => _components ??=
    [
        ComponentType.ReadOnly(Il2CppType.Of<FromCharacter>()),
        ComponentType.ReadOnly(Il2CppType.Of<NetworkEventType>()),
        ComponentType.ReadOnly(Il2CppType.Of<SendNetworkEventTag>()),
        ComponentType.ReadOnly(Il2CppType.Of<ChatMessageEvent>()),
    ];

    static readonly NetworkEventType _eventType = new()
    {
        IsAdminEvent = false,
        EventId = NetworkEvents.EventId_ChatMessageEvent,
        IsDebugEvent = false,
    };

    /// <summary>Send <paramref name="command"/> as a Local chat message. False if not in game.</summary>
    public static bool TrySend(string command)
    {
        if (string.IsNullOrWhiteSpace(command) || !Core.InGame) return false;

        var character = Core.LocalCharacter;
        var user = Core.LocalUser;
        if (character == Entity.Null || user == Entity.Null || !user.Has<NetworkId>()) return false;

        try
        {
            var entity = Core.EntityManager.CreateEntity(Components);
            entity.Write(new FromCharacter { Character = character, User = user });
            entity.Write(_eventType);
            entity.Write(new ChatMessageEvent
            {
                MessageText = new FixedString512Bytes(command),
                MessageType = ChatMessageType.Local,
                ReceiverEntity = user.Read<NetworkId>(),
            });
            Core.Diag($"Sent command: {command}");
            return true;
        }
        catch (Exception ex)
        {
            Core.Log.LogWarning($"Couldn't send '{command}': {ex.Message}");
            return false;
        }
    }
}
