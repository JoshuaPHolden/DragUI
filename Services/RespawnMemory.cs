using System.Globalization;
using BepInEx;
using VBloodHunter.Data;

namespace VBloodHunter.Services;

// Respawn durations measured in-game, used to sharpen future estimates. Each line in
// BepInEx/config/VBloodHunter/respawn-times.txt is "<PrefabGUID>=<seconds>".
internal static class RespawnMemory
{
    static readonly string _path = Path.Combine(Paths.ConfigPath, "VBloodHunter", "respawn-times.txt");
    static readonly Dictionary<int, double> _learned = new();

    public enum Source { Learned, Default, Fallback }

    /// <summary>Best respawn-duration estimate for a V Blood, and where it came from.</summary>
    public static double EstimateSeconds(VBloodInfo info, out Source source)
    {
        if (Settings.LearnRespawnTimes && _learned.TryGetValue(info.Guid.GuidHash, out double learned))
        {
            source = Source.Learned;
            return learned;
        }
        if (info.DefaultRespawnSeconds > 0)
        {
            source = Source.Default;
            return info.DefaultRespawnSeconds;
        }
        source = Source.Fallback;
        return Settings.FallbackRespawnSeconds;
    }

    /// <summary>Record a measured respawn duration. Exact measurements (from a ghost) replace the
    /// stored value; poll-based measurements are blended in, since they're only accurate to a poll.</summary>
    public static void Record(VBloodInfo info, double seconds, bool exact)
    {
        if (!Settings.LearnRespawnTimes) return;
        if (seconds < 30 || seconds > 4 * 3600) return; // implausible; ignore

        int key = info.Guid.GuidHash;
        double value = !exact && _learned.TryGetValue(key, out double previous)
            ? (previous + seconds) / 2
            : seconds;
        value = Math.Round(value);

        if (_learned.TryGetValue(key, out double existing) && Math.Abs(existing - value) < 1) return;
        _learned[key] = value;
        Core.Diag($"Learned respawn for {info.Name}: {value:0}s ({(exact ? "exact" : "observed")}).");
        Save();
    }

    public static void Load()
    {
        _learned.Clear();
        try
        {
            if (!File.Exists(_path)) return;
            foreach (var line in File.ReadAllLines(_path))
            {
                var parts = line.Split('=');
                if (parts.Length == 2
                    && int.TryParse(parts[0].Trim(), out int guid)
                    && double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds)
                    && VBloodCatalog.TryGet(guid, out _))
                {
                    _learned[guid] = seconds;
                }
            }
        }
        catch (Exception ex)
        {
            Core.Log.LogWarning($"Couldn't read {_path}: {ex.Message}");
        }
    }

    static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            File.WriteAllLines(_path, _learned.Select(kv => $"{kv.Key}={kv.Value.ToString("0", CultureInfo.InvariantCulture)}"));
        }
        catch (Exception ex)
        {
            Core.Log.LogWarning($"Couldn't save {_path}: {ex.Message}");
        }
    }
}
