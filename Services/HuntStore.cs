using BepInEx;
using DragUI.Data;

namespace DragUI.Services;

// The V Bloods the player chose to hunt. Saved as one PrefabGUID per line in
// BepInEx/config/DragUI/hunts.txt so it survives restarts and server switches.
internal static class HuntStore
{
    static readonly string _directory = Plugin.DataDirectory;
    static readonly string _path = Path.Combine(_directory, "hunts.txt");
    static readonly HashSet<int> _selected = new();

    public static event Action Changed;

    public static int Count => _selected.Count;

    public static bool IsHunted(VBloodInfo info) => _selected.Contains(info.Guid.GuidHash);

    /// <summary>Hunted V Bloods in catalog order.</summary>
    public static IEnumerable<VBloodInfo> Hunted => VBloodCatalog.All.Where(IsHunted);

    public static void SetHunted(VBloodInfo info, bool hunted)
    {
        bool changed = hunted ? _selected.Add(info.Guid.GuidHash) : _selected.Remove(info.Guid.GuidHash);
        if (!changed) return;
        Save();
        Changed?.Invoke();
    }

    public static void Toggle(VBloodInfo info) => SetHunted(info, !IsHunted(info));

    public static void Clear()
    {
        if (_selected.Count == 0) return;
        _selected.Clear();
        Save();
        Changed?.Invoke();
    }

    public static void Load()
    {
        _selected.Clear();
        try
        {
            if (!File.Exists(_path)) return;
            foreach (var line in File.ReadAllLines(_path))
            {
                if (int.TryParse(line.Trim(), out int guid) && VBloodCatalog.TryGet(guid, out _))
                    _selected.Add(guid);
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
            Directory.CreateDirectory(_directory);
            File.WriteAllLines(_path, _selected.Select(g => g.ToString()));
        }
        catch (Exception ex)
        {
            Core.Log.LogWarning($"Couldn't save {_path}: {ex.Message}");
        }
    }
}
