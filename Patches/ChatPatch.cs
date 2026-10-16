using HarmonyLib;
using ProjectM.Network;
using ProjectM.UI;
using Unity.Collections;
using VBloodHunter.Services;

namespace VBloodHunter.Patches;

// Reads incoming server chat messages before the game's chat system consumes them (same hook Eclipse
// uses): V Blood kill announcements and Bloodcraft's familiar replies. Read-only: we never destroy or
// change the messages, so the chat shows them as normal.
[HarmonyPatch(typeof(ClientChatSystem), nameof(ClientChatSystem.OnUpdate))]
internal static class ChatPatch
{
    static void Prefix(ClientChatSystem __instance)
    {
        if (!(Settings.ReadChatKills || Settings.FamiliarsEnabled) || !Core.InGame) return;
        try
        {
            var entities = __instance._ReceiveChatMessagesQuery.ToEntityArray(Allocator.Temp);
            try
            {
                foreach (var entity in entities)
                {
                    if (!entity.Has<ChatMessageServerEvent>()) continue;
                    var chat = entity.Read<ChatMessageServerEvent>();
                    string text = chat.MessageText.Value;
                    ChatKillFeed.OnServerMessage(chat.MessageType, text, chat.TimeUTC);
                    if (Settings.FamiliarsEnabled) FamiliarService.OnServerMessage(chat.MessageType, text);
                }
            }
            finally
            {
                entities.Dispose();
            }
        }
        catch (Exception ex)
        {
            Core.Diag($"Chat read skipped: {ex.Message}");
        }
    }
}
