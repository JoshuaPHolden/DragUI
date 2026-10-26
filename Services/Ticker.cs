using System.Collections;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using ProjectM.Physics;
using UnityEngine;
using Object = UnityEngine.Object;
using DragUI.UI;

namespace DragUI.Services;

// Per-frame driver. Hosts a coroutine on a DontDestroyOnLoad GameObject (an existing game
// MonoBehaviour, so no IL2CPP type injection is needed). It keeps running in the main menu,
// which is what lets the UI hide itself on the first frame after a logout.
internal static class Ticker
{
    static GameObject _host;
    static MonoBehaviour _behaviour;

    public static void Start()
    {
        if (_host != null) return;
        _host = new GameObject("DragUI.Ticker");
        Object.DontDestroyOnLoad(_host);
        _host.hideFlags = HideFlags.HideAndDontSave;
        _behaviour = _host.AddComponent<IgnorePhysicsDebugSystem>();
        _behaviour.StartCoroutine(Loop().WrapToIl2Cpp());
    }

    public static void Stop()
    {
        if (_host != null) Object.Destroy(_host);
        _host = null;
        _behaviour = null;
    }

    static IEnumerator Loop()
    {
        while (true)
        {
            Tick();
            yield return null;
        }
    }

    static void Tick()
    {
        try
        {
            if (Core.InGame)
            {
                VBloodTracker.Tick();
                BossAlarm.Tick();
                AutoTeleport.Tick();
                if (Settings.XpEnabled) ExperienceTracker.Tick();
                if (Settings.DpsEnabled) DamageMeter.Tick();
                if (Settings.SheetEnabled) PerkshopStats.Tick();
                if (Settings.LoadoutsEnabled) LoadoutService.Tick();
                if (Settings.FamiliarsEnabled) FamiliarCatalog.Tick();
            }
        }
        catch (Exception ex)
        {
            Core.Log.LogError($"Tracker tick failed: {ex}");
        }

        try
        {
            UIManager.Tick();
        }
        catch (Exception ex)
        {
            Core.Log.LogError($"UI tick failed: {ex}");
        }

        try
        {
            Patches.TypingLock.Tick(); // also runs out of game, so a lock can never outlive the UI
        }
        catch (Exception ex)
        {
            Core.Log.LogError($"Typing lock tick failed: {ex}");
        }
    }
}
