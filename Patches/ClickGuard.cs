using HarmonyLib;
using ProjectM;
using UnityEngine;
using DragUI.UI;

namespace DragUI.Patches;

// Stops clicks on our buttons from also firing an attack in the world. Approach adapted from
// Raphael (MIT, KDavidP1987), which found in testing that:
//   * the game doesn't check whether the cursor is over a mod's UI before casting, so a click on a
//     button is also a primary attack;
//   * skipping AbilityInputSystem while the cursor is over the UI (and interrupting the cast)
//     stops that safely - skipping InputActionSystem instead freezes all UI input;
//   * the click's button-up can be swallowed, leaving a "phantom" primary attack latched after the
//     cursor leaves the UI; cancelling only a Primary cast until the player physically clicks
//     again clears it without blocking spells.
[HarmonyPatch(typeof(AbilityInputSystem), nameof(AbilityInputSystem.OnUpdate))]
internal static class ClickGuard
{
    const double PhantomGuardSeconds = 3;

    static bool _phantomArmed;
    static double _phantomArmedAt;

    /// <summary>Called by the UI when the left mouse button goes down over one of our panels.</summary>
    public static void ArmPhantomGuard()
    {
        _phantomArmed = true;
        _phantomArmedAt = Time.realtimeSinceStartupAsDouble;
    }

    public static void OnWorldTeardown() => _phantomArmed = false;

    static bool Prefix()
    {
        try
        {
            // Backstop for the typing lock: no casts at all while typing in one of our text boxes.
            if (TypingLock.Active && Core.InGame)
            {
                CancelCast(onlyPrimary: false);
                return false;
            }

            if (!Settings.BlockClicksOverUI || !Core.InGame) return true;

            // Only while a mouse button is involved: keyboard casts with the cursor resting over the
            // tracker still go through.
            if (UIManager.PointerOverUI && MouseButtonActive())
            {
                CancelCast(onlyPrimary: false);
                return false;
            }

            if (_phantomArmed)
            {
                if ((Input.GetMouseButton(0) && !UIManager.PointerOverUI)
                    || Time.realtimeSinceStartupAsDouble - _phantomArmedAt > PhantomGuardSeconds)
                {
                    _phantomArmed = false; // a real click outside the UI (or timeout) ends the guard
                }
                else if (!Input.GetMouseButton(0))
                {
                    CancelCast(onlyPrimary: true);
                }
            }
        }
        catch (Exception ex)
        {
            Core.Diag($"Click guard skipped: {ex.Message}");
        }
        return true;
    }

    static bool MouseButtonActive() =>
        Input.GetMouseButton(0) || Input.GetMouseButtonUp(0) || Input.GetMouseButton(1) || Input.GetMouseButtonUp(1);

    static void CancelCast(bool onlyPrimary)
    {
        var character = Core.LocalCharacter;
        if (character == Unity.Entities.Entity.Null || !character.Has<EntityAbilityInput>()) return;

        var input = character.Read<EntityAbilityInput>();
        if (onlyPrimary && input.CastInput != ButtonInputAction.Primary) return;
        if (!onlyPrimary && input.CastInput == ButtonInputAction.None && input.QueuedCastGroup == Unity.Entities.Entity.Null) return;

        input.Interrupt = true;
        input.CastInput = ButtonInputAction.None;
        input.QueuedCastGroup = Unity.Entities.Entity.Null;
        input.PrepareCastGroup = Unity.Entities.Entity.Null;
        character.Write(input);
    }
}
