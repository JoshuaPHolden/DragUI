using Unity.Mathematics;
using UnityEngine;
using VBloodHunter.Services;

namespace VBloodHunter.UI;

internal static class StatusFormat
{
    static readonly string[] _directions = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

    /// <summary>Status text and colour. "~" marks an estimated (not exact) respawn time.</summary>
    public static (string Text, Color Color) Describe(VBloodStatus status, bool compact)
    {
        double now = VBloodTracker.Now;
        switch (status.State)
        {
            case HuntState.Alive:
                string where = Where(status);
                return (compact ? (where.Length > 0 ? where : "Alive") : (where.Length > 0 ? $"Alive · {where}" : "Alive"), Theme.Alive);

            case HuntState.Respawning:
            {
                string time = Clock(status.RespawnAt - now);
                string prefix = status.RespawnExact ? "" : "~";
                return (compact ? $"{prefix}{time}" : $"Respawns in {prefix}{time}", Theme.Respawning);
            }

            case HuntState.Due:
            {
                double overdue = now - status.RespawnAt;
                if (status.RespawnExact || overdue < 15)
                    return (compact ? "Due" : "Respawning now", Theme.Due);
                return (compact ? $"Due +{Clock(overdue)}" : $"Due (estimate passed {Clock(overdue)} ago)", Theme.Due);
            }

            case HuntState.NotDetected:
                return (compact ? "Not up" : "Not up · timer unknown", Theme.Unknown);

            default:
                return (VBloodTracker.HasPositionData ? "-" : "...", Theme.Unknown);
        }
    }

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
