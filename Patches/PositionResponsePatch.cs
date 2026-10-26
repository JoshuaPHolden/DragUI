using HarmonyLib;
using ProjectM.UI;
using DragUI.Services;

namespace DragUI.Patches;

// The game's GetVBloodsPositionResponseSystem turns the server's GetVBloodPositionsResponseEvent
// into its public Positions list. We hand that list to the tracker on frames where a response was
// actually processed (prefix: the response query had entities), so "missing from the list" always
// means "missing from a fresh answer", never a stale one.
[HarmonyPatch(typeof(GetVBloodsPositionResponseSystem), nameof(GetVBloodsPositionResponseSystem.OnUpdate))]
internal static class PositionResponsePatch
{
    static void Prefix(GetVBloodsPositionResponseSystem __instance, out bool __state)
    {
        __state = false;
        try
        {
            if (Core.InGame) __state = !__instance._ResponseEntityQuery.IsEmptyIgnoreFilter;
        }
        catch (Exception ex)
        {
            Core.Diag($"Position response prefix skipped: {ex.Message}");
        }
    }

    static void Postfix(GetVBloodsPositionResponseSystem __instance, bool __state)
    {
        if (!__state) return;
        try
        {
            VBloodTracker.OnPositionsResponse(__instance.Positions);
        }
        catch (Exception ex)
        {
            Core.Log.LogError($"Handling V Blood positions failed: {ex}");
        }
    }
}
