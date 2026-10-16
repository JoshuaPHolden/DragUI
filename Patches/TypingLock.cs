using HarmonyLib;
using ProjectM;
using ProjectM.UI;
using Unity.Entities;
using UnityEngine;
using VBloodHunter.UI;

namespace VBloodHunter.Patches;

// Keeps keystrokes typed into our text boxes from also moving the character, casting or opening
// menus. Approach adapted from Raphael (MIT, KDavidP1987), which found that:
//   * the game's input runs through an ordered stack of IInputContext consumers on InputActionSystem;
//     while the native chat is focused, ClientChatSystem adds its ChatFocusedInputContext (order 99),
//     which consumes every game action for the contexts below it;
//   * registering our OWN instance of that game class (order 100) while we type gives the same lock
//     with no injected types and no managed callbacks - an injected IInputContext crashed natively;
//   * the right InputActionSystem is the one GameplayInputSystem holds (a world lookup found none);
//   * movement isn't a button action, so GameplayInputSystem is also skipped (with held movement
//     cleared) while typing.
// Add/remove happen on our Ticker, never inside a game system. Teardown only drops references: the
// dying world's InputActionSystem takes the registration with it.
[HarmonyPatch]
internal static class TypingLock
{
    const double ReleaseGraceSeconds = 0.2; // the Enter that submits a field mustn't reach the game

    static InputActionSystem _inputActions;
    static World _inputWorld;
    static ClientChatSystem.ChatFocusedInputContext _contextObject;
    static IInputContext _context;
    static bool _registered;
    static double _releaseAt;
    static double _retryAt;
    static bool _loggedOnce;

    /// <summary>True while one of our text boxes has the keyboard (plus a short grace after).</summary>
    public static bool Active { get; private set; }

    [HarmonyPatch(typeof(GameplayInputSystem), nameof(GameplayInputSystem.OnUpdate))]
    [HarmonyPrefix]
    static bool GameplayInputPrefix(GameplayInputSystem __instance)
    {
        try
        {
            if (_inputActions == null && __instance._InputActionSystem != null)
            {
                _inputActions = __instance._InputActionSystem;
                _inputWorld = _inputActions.World;
            }
            if (!Active) return true;

            var character = Core.LocalCharacter;
            if (character != Entity.Null && character.Has<EntityInput>())
            {
                var input = character.Read<EntityInput>();
                input.Movement = default;
                input.State = default;
                character.Write(input);
            }
            return false;
        }
        catch (Exception ex)
        {
            Core.Diag($"Typing lock (movement) skipped: {ex.Message}");
            return true; // never freeze the game's input
        }
    }

    public static void Tick()
    {
        double now = Time.realtimeSinceStartupAsDouble;
        bool typing = Settings.LockKeysWhileTyping && Core.InGame && UIManager.TypingInUI;
        if (typing) _releaseAt = now + ReleaseGraceSeconds;
        Active = typing || now < _releaseAt;

        if (Active == _registered || _inputActions == null || now < _retryAt) return;
        try
        {
            if (_inputWorld == null || !_inputWorld.IsCreated)
            {
                OnWorldTeardown();
                return;
            }

            if (Active)
            {
                _contextObject ??= new ClientChatSystem.ChatFocusedInputContext();
                _context ??= _contextObject.Cast<IInputContext>();
                if (!_inputActions.IsContextRegistered(_context))
                    _inputActions.AddInputContext(_context, _inputWorld, (int)InputContextOrder.ChatInput + 1);
                _registered = true;
                if (!_loggedOnce) Core.Log.LogInfo("Typing lock engaged (game keys are blocked while typing in the mod).");
                _loggedOnce = true;
            }
            else
            {
                if (_context != null && _inputActions.IsContextRegistered(_context))
                    _inputActions.RemoveInputContext(_context);
                _registered = false;
            }
        }
        catch (Exception ex)
        {
            // A failed remove keeps retrying, so the keyboard can't stay locked.
            Core.Diag($"Typing lock {(Active ? "add" : "remove")} failed, retrying: {ex.Message}");
            _retryAt = now + 0.5;
        }
    }

    public static void OnWorldTeardown()
    {
        _inputActions = null;
        _inputWorld = null;
        _contextObject = null;
        _context = null;
        _registered = false;
        _releaseAt = 0;
        _retryAt = 0;
        Active = false;
    }
}
