using System.Text.Json;
using ProjectM.Network;

namespace DragUI.Services;

// Saved loadouts for the current character, in the order they were made. Saved in
// BepInEx/config/DragUI/loadouts-<character>.json.
internal static class LoadoutStore
{
    public sealed class Loadout
    {
        public string Name { get; set; }
        /// <summary>EquipmentType name (Headgear, Weapon, ...) -> item prefab GUID.</summary>
        public Dictionary<string, int> Gear { get; set; } = new();
        /// <summary>Spells by bar slot (SpellSlot1, SpellSlot2, Ultimate) -> ability group prefab GUID.</summary>
        public Dictionary<string, int> Spells { get; set; } = new();
        /// <summary>Spell school passive prefab GUIDs, in slot order.</summary>
        public List<int> Passives { get; set; } = new();
        /// <summary>Bloodcraft class id (1-based), 0 = none / not on a Bloodcraft server.</summary>
        public int ClassId { get; set; }
        /// <summary>Bloodcraft class shift spell index, -1 = none.</summary>
        public int ShiftSpell { get; set; } = -1;
    }

    static List<Loadout> _loadouts;
    static string _path;

    public static event Action Changed;

    /// <summary>The current character's loadouts (empty until their name is known).</summary>
    public static IReadOnlyList<Loadout> Loadouts => EnsureLoaded() ? _loadouts : Array.Empty<Loadout>();

    public static Loadout Find(string name) =>
        EnsureLoaded() ? _loadouts.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase)) : null;

    /// <summary>Add <paramref name="loadout"/>, replacing a saved one with the same name (in its place).</summary>
    public static void Put(Loadout loadout)
    {
        if (!EnsureLoaded()) return;
        int index = _loadouts.FindIndex(l => string.Equals(l.Name, loadout.Name, StringComparison.OrdinalIgnoreCase));
        if (index >= 0) _loadouts[index] = loadout;
        else _loadouts.Add(loadout);
        Save();
        Changed?.Invoke();
    }

    public static void Delete(string name)
    {
        if (!EnsureLoaded() || _loadouts.RemoveAll(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase)) == 0) return;
        Save();
        Changed?.Invoke();
    }

    static bool EnsureLoaded()
    {
        if (_loadouts != null) return true;
        var user = Core.LocalUser;
        if (user == Unity.Entities.Entity.Null || !user.Has<User>()) return false;
        string name = user.Read<User>().CharacterName.Value;
        if (string.IsNullOrWhiteSpace(name)) return false;

        string safe = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        _path = Path.Combine(Plugin.DataDirectory, $"loadouts-{safe}.json");
        _loadouts = new List<Loadout>();
        try
        {
            if (File.Exists(_path)) _loadouts = JsonSerializer.Deserialize<List<Loadout>>(File.ReadAllText(_path)) ?? new List<Loadout>();
        }
        catch (Exception ex)
        {
            Core.Log.LogWarning($"Couldn't read {_path}: {ex.Message}");
        }
        Changed?.Invoke(); // the panel may have drawn an empty list before the name was known
        return true;
    }

    static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            File.WriteAllText(_path, JsonSerializer.Serialize(_loadouts, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Core.Log.LogWarning($"Couldn't save {_path}: {ex.Message}");
        }
    }

    /// <summary>Leaving the server/character: forget (the next login may be someone else).</summary>
    public static void OnWorldTeardown()
    {
        _loadouts = null;
        _path = null;
    }
}
