using UnityEngine;

namespace DragUI.Services;

// A key plus Ctrl/Alt/Shift. Modifiers must match exactly, so "Ctrl+1" and "1" are different binds.
// Saved as text like "Ctrl+Alt+F5".
internal readonly record struct Hotkey(KeyCode Key, bool Ctrl = false, bool Alt = false, bool Shift = false)
{
    public static readonly Hotkey None = new(KeyCode.None);

    // Keys a bind can use: the keyboard, minus modifiers (they're the Ctrl/Alt/Shift flags), the mouse,
    // and keys the capture itself uses (Escape cancels, Backspace/Delete clear) or the game needs (Enter
    // opens chat).
    static readonly KeyCode[] _bindable = Enum.GetValues(typeof(KeyCode)).Cast<KeyCode>()
        .Where(k => k > KeyCode.None && k < KeyCode.Mouse0)
        .Where(k => k is not (>= KeyCode.RightShift and <= KeyCode.AltGr))
        .Where(k => k is not (KeyCode.Escape or KeyCode.Backspace or KeyCode.Delete or KeyCode.Return or KeyCode.KeypadEnter))
        .Distinct()
        .ToArray();

    public bool IsNone => Key == KeyCode.None;

    static bool CtrlHeld => Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
    static bool AltHeld => Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
    static bool ShiftHeld => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

    /// <summary>True on the frame this bind is pressed.</summary>
    public bool IsDown() =>
        !IsNone && Input.GetKeyDown(Key) && Ctrl == CtrlHeld && Alt == AltHeld && Shift == ShiftHeld;

    /// <summary>The bind being pressed this frame, if any (used while waiting for the player to pick one).</summary>
    public static bool TryCapture(out Hotkey hotkey)
    {
        hotkey = None;
        if (!Input.anyKeyDown) return false;
        foreach (var key in _bindable)
        {
            if (!Input.GetKeyDown(key)) continue;
            hotkey = new Hotkey(key, CtrlHeld, AltHeld, ShiftHeld);
            return true;
        }
        return false;
    }

    public override string ToString()
    {
        if (IsNone) return string.Empty;
        string key = Key switch
        {
            >= KeyCode.Alpha0 and <= KeyCode.Alpha9 => ((int)(Key - KeyCode.Alpha0)).ToString(),
            >= KeyCode.Keypad0 and <= KeyCode.Keypad9 => "Num" + (int)(Key - KeyCode.Keypad0),
            _ when Key.ToString().StartsWith("Keypad") => "Num" + Key.ToString()["Keypad".Length..],
            _ => Key.ToString(),
        };
        return (Ctrl ? "Ctrl+" : "") + (Alt ? "Alt+" : "") + (Shift ? "Shift+" : "") + key;
    }

    public static bool TryParse(string text, out Hotkey hotkey)
    {
        hotkey = None;
        if (string.IsNullOrWhiteSpace(text)) return false;
        bool ctrl = false, alt = false, shift = false;
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToLowerInvariant())
            {
                case "ctrl": ctrl = true; break;
                case "alt": alt = true; break;
                case "shift": shift = true; break;
                default: return false;
            }
        }
        string last = parts.Length > 0 ? parts[^1] : string.Empty;
        KeyCode key;
        if (last.Length == 1 && char.IsDigit(last[0])) key = KeyCode.Alpha0 + (last[0] - '0');
        else if (!Enum.TryParse(last, ignoreCase: true, out key)
            && !(last.StartsWith("Num", StringComparison.OrdinalIgnoreCase)
                 && Enum.TryParse("Keypad" + last[3..], ignoreCase: true, out key))) return false;
        if (!_bindable.Contains(key)) return false;
        hotkey = new Hotkey(key, ctrl, alt, shift);
        return true;
    }
}
