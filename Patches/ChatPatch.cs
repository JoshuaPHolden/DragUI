using HarmonyLib;
using ProjectM.Network;
using ProjectM.UI;
using Unity.Collections;
using DragUI.Services;

namespace DragUI.Patches;

// Reads incoming server chat messages before the game's chat system consumes them (same hook Eclipse
// uses): V Blood kill announcements, Bloodcraft's familiar replies and its leveling log. Messages are
// left alone, except Bloodcraft XP lines (and replies to the mod's own sync commands), which are
// destroyed here when the player asked to hide them (as Eclipse does with its protocol messages).
[HarmonyPatch(typeof(ClientChatSystem), nameof(ClientChatSystem.OnUpdate))]
internal static class ChatPatch
{
    // Read before other chat mods (Eclipse, BetterMissions, ...) so one destroying a message can't hide it from us.
    [HarmonyPriority(Priority.First)]
    static void Prefix(ClientChatSystem __instance)
    {
        if (!Core.InGame) return; // teleport requests and .stp replies are always read now
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
                    if (TeleportStore.OnServerMessage(chat.MessageType, text))
                    {
                        Core.EntityManager.DestroyEntity(entity); // .stp ltp listing the Teleport window asked for
                        continue;
                    }
                    if (Settings.FamiliarsEnabled && FamiliarCatalog.OnServerMessage(chat.MessageType, text))
                    {
                        Core.EntityManager.DestroyEntity(entity); // List All's own command replies
                        continue;
                    }
                    ChatKillFeed.OnServerMessage(chat.MessageType, text, chat.TimeUTC);
                    if (Settings.FamiliarsEnabled) FamiliarService.OnServerMessage(chat.MessageType, text);
                    if (Settings.SheetEnabled || Settings.LoadoutsEnabled) BloodcraftStats.OnServerMessage(chat.MessageType, text);
                    AutoTeleport.OnServerMessage(chat.MessageType, text);
                    if (Settings.SheetEnabled && PerkshopStats.OnServerMessage(chat.MessageType, text))
                    {
                        Core.EntityManager.DestroyEntity(entity);
                        continue;
                    }
                    if (Settings.XpEnabled && ExperienceTracker.OnServerMessage(chat.MessageType, text))
                        Core.EntityManager.DestroyEntity(entity);
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
