using System.Globalization;
using BepInEx;
using VBloodHunter.Data;

namespace VBloodHunter.Services;

// Respawn durations measured exactly in-game (ghost respawn time minus the game's own time of death),
// used ahead of the catalog defaults. Each line in BepInEx/config/VBloodHunter/respawn-times.txt is
// "<PrefabGUID>=<seconds>" after a version header.
//
// Only exact measurements are stored. Earlier versions also learned from poll observations, which drifted
// badly on servers that keep dead V Bloods on their position list; files without the header are discarded.
internal static class RespawnMemory
{
    const string Header = "# VBloodHunter respawn times v2 (exact measurements only)";
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

    /// <summary>Record an exactly measured respawn duration.</summary>
    public static void RecordExact(VBloodInfo info, double seconds)
    {
        if (!Settings.LearnRespawnTimes) return;
        if (seconds < 30 || seconds > 4 * 3600) return; // implausible; ignore

        int key = info.Guid.GuidHash;
        double value = Math.Round(seconds);
        if (_learned.TryGetValue(key, out double existing) && Math.Abs(existing - value) < 2) return;
        _learned[key] = value;
        Core.Log.LogInfo($"Measured respawn time for {info.Name}: {value:0}s" +
                         (info.DefaultRespawnSeconds > 0 ? $" (default {info.DefaultRespawnSeconds}s)." : "."));
        Save();
    }

    public static void Load()
    {
        _learned.Clear();
        try
        {
            if (!File.Exists(_path)) return;
            var lines = File.ReadAllLines(_path);
            if (lines.Length == 0 || lines[0] != Header)
            {
                File.Delete(_path);
                Core.Log.LogInfo("Discarded respawn times learned by an older version (they could drift); using the built-in times.");
                return;
            }
            foreach (var line in lines.Skip(1))
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
            File.WriteAllLines(_path, _learned
                .Select(kv => $"{kv.Key}={kv.Value.ToString("0", CultureInfo.InvariantCulture)}")
                .Prepend(Header));
        }
        catch (Exception ex)
        {
            Core.Log.LogWarning($"Couldn't save {_path}: {ex.Message}");
        }
    }
}
