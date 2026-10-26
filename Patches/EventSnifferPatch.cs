using HarmonyLib;
using ProjectM.Network;
using Unity.Collections;
using DragUI.Services;

namespace DragUI.Patches;

// With Debug.Diagnostics on, logs each event the client is about to send to the server, just before
// the game serialises them (see GameInspector). Does nothing otherwise.
[HarmonyPatch(typeof(SerializeAndSendClientEventsSystem), nameof(SerializeAndSendClientEventsSystem.OnUpdate))]
internal static class EventSnifferPatch
{
    static void Prefix(SerializeAndSendClientEventsSystem __instance)
    {
        if (!Settings.Diagnostics || !Core.InGame) return;
        try
        {
            var entities = __instance._Query.ToEntityArray(Allocator.Temp);
            try
            {
                foreach (var entity in entities) GameInspector.LogSentEvent(entity);
            }
            finally
            {
                entities.Dispose();
            }
        }
        catch (Exception ex)
        {
            Core.Diag($"Event sniffer skipped: {ex.Message}");
        }
    }
}
