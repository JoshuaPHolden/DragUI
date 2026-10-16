using BepInEx.Configuration;
using UnityEngine;

namespace VBloodHunter;

internal static class Settings
{
    static ConfigEntry<KeyCode> _toggleKey;
    static ConfigEntry<float> _uiScale;
    static ConfigEntry<float> _pollSeconds;
    static ConfigEntry<int> _fallbackRespawnSeconds;
    static ConfigEntry<bool> _learnRespawnTimes;
    static ConfigEntry<bool> _autoTrackNext;
    static ConfigEntry<bool> _readChatKills;
    static ConfigEntry<string> _chatKillChannels;
    static ConfigEntry<string> _chatKillWords;
    static ConfigEntry<bool> _blockClicksOverUI;
    static ConfigEntry<bool> _diagnostics;
    static ConfigEntry<float> _hudX, _hudY, _panelX, _panelY, _barX, _barY, _famPanelX, _famPanelY;
    static ConfigEntry<bool> _hudCollapsed;
    static ConfigEntry<bool> _hudVisible;
    static ConfigEntry<bool> _familiarsEnabled;
    static ConfigEntry<bool> _lockKeysWhileTyping;

    public static KeyCode ToggleKey => _toggleKey.Value;
    public static float UIScale => Mathf.Clamp(_uiScale.Value, 0.5f, 2.5f);
    public static float PollSeconds => Mathf.Max(2f, _pollSeconds.Value);
    public static int FallbackRespawnSeconds => Math.Max(30, _fallbackRespawnSeconds.Value);
    public static bool LearnRespawnTimes => _learnRespawnTimes.Value;
    public static bool AutoTrackNext => _autoTrackNext.Value;
    public static bool ReadChatKills => _readChatKills.Value;
    public static string ChatKillChannels => _chatKillChannels.Value ?? string.Empty;
    public static string ChatKillWords => _chatKillWords.Value ?? string.Empty;
    public static bool BlockClicksOverUI => _blockClicksOverUI.Value;
    public static bool Diagnostics => _diagnostics.Value;
    public static bool FamiliarsEnabled => _familiarsEnabled.Value;
    public static bool LockKeysWhileTyping => _lockKeysWhileTyping.Value;

    public static Vector2 HudPosition
    {
        get => new(_hudX.Value, _hudY.Value);
        set { _hudX.Value = Mathf.Round(value.x); _hudY.Value = Mathf.Round(value.y); }
    }

    public static Vector2 PanelPosition
    {
        get => new(_panelX.Value, _panelY.Value);
        set { _panelX.Value = Mathf.Round(value.x); _panelY.Value = Mathf.Round(value.y); }
    }
    public static Vector2 BarPosition
    {
        get => new(_barX.Value, _barY.Value);
        set { _barX.Value = Mathf.Round(value.x); _barY.Value = Mathf.Round(value.y); }
    }

    public static Vector2 FamiliarPanelPosition
    {
        get => new(_famPanelX.Value, _famPanelY.Value);
        set { _famPanelX.Value = Mathf.Round(value.x); _famPanelY.Value = Mathf.Round(value.y); }
    }

    public static bool HudCollapsed { get => _hudCollapsed.Value; set => _hudCollapsed.Value = value; }
    public static bool HudVisible { get => _hudVisible.Value; set => _hudVisible.Value = value; }

    public static void Initialize(ConfigFile config)
    {
        _toggleKey = config.Bind("General", "ToggleListKey", KeyCode.None,
            "Optional hotkey that opens/closes the V Blood list (e.g. F8). None = use the on-screen button only. Avoid keys you type in chat.");
        _uiScale = config.Bind("General", "UIScale", 1f,
            "Scale of the hunt tracker and V Blood list (0.5 - 2.5).");
        _pollSeconds = config.Bind("Tracking", "PollSeconds", 5f,
            "How often (seconds, min 2) to ask the server which V Bloods are alive. This is the same request the game's V Blood menu makes.");
        _fallbackRespawnSeconds = config.Bind("Tracking", "FallbackRespawnSeconds", 600,
            "Respawn estimate (seconds) for a V Blood with no known default and no observed respawn yet.");
        _learnRespawnTimes = config.Bind("Tracking", "LearnRespawnTimes", true,
            "Remember respawn durations measured in-game (from V Blood ghosts and observed respawns) and use them for future estimates.");
        _autoTrackNext = config.Bind("Tracking", "AutoTrackNext", false,
            "When nothing is tracked (e.g. your tracked V Blood just died), automatically track the nearest alive V Blood on your hunt list.");
        _readChatKills = config.Bind("Chat", "ReadChatKills", true,
            "Start a respawn countdown when a server chat message announces a V Blood kill (from kill-feed/announcement mods). " +
            "A message counts when it names exactly one V Blood and contains a kill word.");
        _chatKillChannels = config.Bind("Chat", "ChatKillChannels", "System",
            "Chat channels to read kill announcements from, comma-separated: System, Global, Region, Team, Local, Lore. " +
            "Announcement mods use System; adding Global lets player chat like 'killed alpha' start timers too.");
        _chatKillWords = config.Bind("Chat", "ExtraChatKillWords", "",
            "Extra words that mark a chat message as a kill announcement, on top of the built-in ones " +
            "(killed, slain, defeated, beating, ...). Comma-separated, whole words, case-insensitive.");
        _blockClicksOverUI = config.Bind("General", "BlockClicksOverUI", true,
            "Stop clicks on this mod's buttons from also firing your primary attack / casts in the world.");
        _lockKeysWhileTyping = config.Bind("General", "LockKeysWhileTyping", true,
            "While you type in one of this mod's text boxes, stop the keys from also moving your character or firing abilities " +
            "(uses the same input lock as the game's chat box). Turn off only if typing in the mod causes problems.");
        _familiarsEnabled = config.Bind("Familiars", "Enabled", true,
            "Show the Familiars window (quick summon for Bloodcraft familiars). It sends Bloodcraft's chat commands (.fam sb, .fam t) " +
            "for you, so it only works on servers running Bloodcraft with familiars enabled. On other servers the commands " +
            "would show up as local chat, so turn this off there.");
        _diagnostics = config.Bind("Debug", "Diagnostics", false,
            "Log what the mod sees (position responses, ghosts, tracking buffs). Turn on when reporting a problem.");
        _hudX = config.Bind("Layout", "HudX", 16f, "Hunt tracker left edge, in reference pixels from the left of the screen. Drag the tracker header to change.");
        _hudY = config.Bind("Layout", "HudY", -220f, "Hunt tracker top edge, in reference pixels from the top of the screen (negative = down).");
        _panelX = config.Bind("Layout", "PanelX", 0f, "V Blood list window offset from the screen centre (x). Drag its title bar to change.");
        _panelY = config.Bind("Layout", "PanelY", 0f, "V Blood list window offset from the screen centre (y).");
        _hudCollapsed = config.Bind("Layout", "HudCollapsed", false,
            "Whether the hunt tracker rows are collapsed (only the header button shows).");
        _hudVisible = config.Bind("Layout", "HudVisible", true,
            "Whether the hunt tracker is shown. Toggle it with the Tracker button on the control bar.");
        _barX = config.Bind("Layout", "BarX", 16f, "Control bar left edge, in reference pixels from the left of the screen. Drag its grip to change.");
        _barY = config.Bind("Layout", "BarY", -176f, "Control bar top edge, in reference pixels from the top of the screen (negative = down).");
        _famPanelX = config.Bind("Layout", "FamiliarPanelX", 380f, "Familiars window offset from the screen centre (x). Drag its title bar to change.");
        _famPanelY = config.Bind("Layout", "FamiliarPanelY", 0f, "Familiars window offset from the screen centre (y).");
    }
}
