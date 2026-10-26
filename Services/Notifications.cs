using UnityEngine;

namespace DragUI.Services;

// The control bar's notification slot. Any feature can post a notice; the newest one replaces whatever
// was showing. A notice shows live text, can flash, and either does something when clicked (e.g. teleport
// to a boss) or is dismissed by the click. It goes away when its owner says it's over, when it times out,
// or when the player dismisses it.
internal static class Notifications
{
    public sealed class Notice
    {
        /// <summary>Who posted it (e.g. "BossAlarm"), so the poster can clear its own notice later.</summary>
        public string Source;
        public Func<string> GetText;
        /// <summary>Null = clicking just dismisses the notice.</summary>
        public Action OnClick;
        public Func<bool> IsFlashing;
        /// <summary>Null = only ExpiresAt (or a dismiss) ends it.</summary>
        public Func<bool> IsOver;
        /// <summary>realtimeSinceStartup at which it goes away on its own.</summary>
        public double ExpiresAt = double.PositiveInfinity;
    }

    static Notice _current;

    /// <summary>Show <paramref name="notice"/>, replacing the current one.</summary>
    public static void Post(Notice notice) => _current = notice;

    /// <summary>Remove the showing notice if <paramref name="source"/> posted it.</summary>
    public static void Clear(string source)
    {
        if (_current?.Source == source) _current = null;
    }

    /// <summary>The notice to show, or null (drops one that has ended).</summary>
    public static Notice Current
    {
        get
        {
            if (_current == null) return null;
            bool over;
            try { over = Time.realtimeSinceStartupAsDouble >= _current.ExpiresAt || (_current.IsOver?.Invoke() ?? false); }
            catch { over = true; }
            if (over) _current = null;
            return _current;
        }
    }

    public static string Text => Current?.GetText?.Invoke() ?? string.Empty;
    public static bool Flashing => Current?.IsFlashing?.Invoke() ?? false;

    /// <summary>The slot was clicked: run the notice's action, or dismiss it if it has none.</summary>
    public static void Click()
    {
        var notice = Current;
        if (notice == null) return;
        if (notice.OnClick != null) notice.OnClick();
        else _current = null;
    }

    public static void OnWorldTeardown() => _current = null;
}
