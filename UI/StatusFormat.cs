using Unity.Mathematics;
using UnityEngine;
using VBloodHunter.Services;

namespace VBloodHunter.UI;

internal static class StatusFormat
{
    static readonly string[] _directions = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
    const string LocationColor = "#A09A9A"; // dimmed, so the timer stands out

    /// <summary>Status text (TMP rich text) and colour. "~" marks an estimated (not exact) respawn time.
    /// Distance and direction are shown whenever a position is known, dead or alive.</summary>
    public static (string Text, Color Color) Describe(VBloodStatus status, bool compact)
    {
        double now = VBloodTracker.Now;
        string where = Where(status);

        switch (status.State)
        {
            case HuntState.Alive:
                if (where.Length == 0) return ("Alive", Theme.Alive);
                return (compact ? where : $"Alive · {where}", Theme.Alive);

            case HuntState.Respawning:
            {
                string prefix = status.RespawnExact ? "" : "~";
                string time = prefix + Clock(status.RespawnAt - now);
                return (WithLocation(compact ? time : $"Respawn {time}", where), Theme.Respawning);
            }

            case HuntState.Due:
            {
                double overdue = now - status.RespawnAt;
                string due = status.RespawnExact || overdue < 15
                    ? (compact ? "Due" : "Respawning now")
                    : (compact ? $"Due +{Clock(overdue)}" : $"Due, {Clock(overdue)} over");
                return (WithLocation(due, where), Theme.Due);
            }

            case HuntState.NotDetected:
                // The server left it out of its answer: dead (and we didn't see it die), or not trackable yet.
                return (WithLocation(compact ? "Not listed" : "Not listed (dead, or not trackable yet)", where), Theme.Unknown);

            default:
                return (VBloodTracker.HasPositionData ? "-" : "...", Theme.Unknown);
        }
    }

    static string WithLocation(string text, string where) =>
        where.Length == 0 ? text : $"{text} <color={LocationColor}>· {where}</color>";

    static string Where(VBloodStatus status)
    {
        if (!status.HasPosition || !Core.TryGetLocalPosition(out float2 me)) return string.Empty;
        float2 delta = status.Position - me;
        float distance = math.length(delta);
        if (distance < 25f) return "here";
        // 8-way compass, assuming +Z is north on the world map.
        float angle = math.degrees(math.atan2(delta.x, delta.y));
        int index = (int)math.round(((angle % 360f) + 360f) % 360f / 45f) % 8;
        string range = distance >= 1000f ? $"{distance / 1000f:0.0}km" : $"{distance:0}m";
        return $"{range} {_directions[index]}";
    }

    public static string Clock(double seconds)
    {
        if (!double.IsFinite(seconds)) return "?";
        if (seconds < 0) seconds = 0;
        if (seconds > 99 * 3600) seconds = 99 * 3600;
        var t = TimeSpan.FromSeconds(Math.Ceiling(seconds));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    }
}
