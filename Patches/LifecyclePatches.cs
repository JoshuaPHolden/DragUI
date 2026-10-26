using HarmonyLib;
using ProjectM;
using DragUI.Services;
using DragUI.UI;

namespace DragUI.Patches;

[HarmonyPatch]
internal static class LifecyclePatches
{
    // Binds the client world once game data is ready (same hook Eclipse uses). Runs every frame,
    // so the common path is a couple of cheap checks. If some other world with game data got bound
    // first (and has no local character), the real game world replaces it.
    [HarmonyPatch(typeof(GameDataManager), nameof(GameDataManager.OnUpdate))]
    [HarmonyPostfix]
    static void GameDataManagerOnUpdatePostfix(GameDataManager __instance)
    {
        try
        {
            if (Core.InGame || !__instance.GameDataInitialized) return;
            var world = __instance.World;
            if (world == null || !world.IsCreated || Core.IsBoundTo(world)) return;
            VBloodTracker.OnWorldTeardown();
            Core.Initialize(world);
        }
        catch (Exception ex)
        {
            Core.Log.LogError($"Client world bind failed: {ex}");
        }
    }

    // Leaving a game or switching servers disposes the client world. Only plain field resets here:
    // touching ECS or GameObjects during this teardown can crash the client natively. The UI hides
    // itself on the next Ticker frame once it sees Core.IsReady == false.
    [HarmonyPatch(typeof(ClientBootstrapSystem), nameof(ClientBootstrapSystem.OnDestroy))]
    [HarmonyPrefix]
    static void ClientBootstrapSystemOnDestroyPrefix()
    {
        try
        {
            VBloodTracker.OnWorldTeardown();
            ChatKillFeed.OnWorldTeardown();
            ClickGuard.OnWorldTeardown();
            TypingLock.OnWorldTeardown();
            FamiliarService.OnWorldTeardown();
            FamiliarCatalog.OnWorldTeardown();
            BossAlarm.OnWorldTeardown();
            AutoTeleport.OnWorldTeardown();
            ExperienceTracker.OnWorldTeardown();
            DamageMeter.OnWorldTeardown();
            BloodcraftStats.OnWorldTeardown();
            PerkshopStats.OnWorldTeardown();
            CooldownTester.OnWorldTeardown();
            LoadoutService.OnWorldTeardown();
            LoadoutStore.OnWorldTeardown();
            Core.Reset();
        }
        catch (Exception ex)
        {
            Core.Log.LogError($"Teardown reset failed: {ex}");
        }
    }
}
