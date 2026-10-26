using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;
using DragUI.Services;
using DragUI.UI;

namespace DragUI;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
internal class Plugin : BasePlugin
{
    Harmony _harmony;

    internal static Plugin Instance { get; private set; }
    internal static ManualLogSource LogInstance => Instance.Log;

    /// <summary>Where the mod keeps its data files (hunts, familiars, schedule, XP history...).</summary>
    internal static string DataDirectory => Path.Combine(Paths.ConfigPath, "DragUI");

    public override void Load()
    {
        Instance = this;

        if (Application.productName == "VRisingServer")
        {
            Log.LogInfo($"{MyPluginInfo.PLUGIN_NAME} is a client mod; not loading on the server.");
            return;
        }

        MigrateFromVBloodHunter();
        Settings.Initialize(Config);
        HuntStore.Load();
        RespawnMemory.Load();
        FamiliarStore.Load();
        BossAlarm.Load();

        _harmony = Harmony.CreateAndPatchAll(typeof(Plugin).Assembly, MyPluginInfo.PLUGIN_GUID);
        Ticker.Start();

        Log.LogInfo($"{MyPluginInfo.PLUGIN_NAME}[{MyPluginInfo.PLUGIN_VERSION}] loaded; waiting for the client world.");
    }

    // The mod was called VBloodHunter before 0.11.0: carry its settings and data over on the first run.
    void MigrateFromVBloodHunter()
    {
        try
        {
            string oldConfig = Path.Combine(Paths.ConfigPath, "nezoic.VBloodHunter.cfg");
            if (File.Exists(oldConfig) && !File.Exists(Config.ConfigFilePath))
            {
                File.Copy(oldConfig, Config.ConfigFilePath);
                Config.Reload();
                Log.LogInfo("Settings carried over from VBloodHunter.");
            }

            string oldData = Path.Combine(Paths.ConfigPath, "VBloodHunter");
            if (Directory.Exists(oldData) && !Directory.Exists(DataDirectory))
            {
                Directory.Move(oldData, DataDirectory);
                Log.LogInfo("Data folder carried over from VBloodHunter.");
            }
        }
        catch (Exception ex)
        {
            Log.LogWarning($"Couldn't carry over VBloodHunter's settings/data: {ex.Message}");
        }
    }

    public override bool Unload()
    {
        _harmony?.UnpatchSelf();
        _harmony = null;
        Ticker.Stop();
        AlarmSound.Stop();
        UIManager.Destroy();
        Core.Reset();
        return true;
    }
}
