using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;
using VBloodHunter.Services;
using VBloodHunter.UI;

namespace VBloodHunter;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
internal class Plugin : BasePlugin
{
    Harmony _harmony;

    internal static Plugin Instance { get; private set; }
    internal static ManualLogSource LogInstance => Instance.Log;

    public override void Load()
    {
        Instance = this;

        if (Application.productName == "VRisingServer")
        {
            Log.LogInfo($"{MyPluginInfo.PLUGIN_NAME} is a client mod; not loading on the server.");
            return;
        }

        Settings.Initialize(Config);
        HuntStore.Load();
        RespawnMemory.Load();

        _harmony = Harmony.CreateAndPatchAll(typeof(Plugin).Assembly, MyPluginInfo.PLUGIN_GUID);
        Ticker.Start();

        Log.LogInfo($"{MyPluginInfo.PLUGIN_NAME}[{MyPluginInfo.PLUGIN_VERSION}] loaded; waiting for the client world.");
    }

    public override bool Unload()
    {
        _harmony?.UnpatchSelf();
        _harmony = null;
        Ticker.Stop();
        UIManager.Destroy();
        Core.Reset();
        return true;
    }
}
