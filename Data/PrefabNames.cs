using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace DragUI.Data;

// Prefab GUID -> internal name for abilities, units, buffs and items (generated from the game's prefab
// list into an embedded, gzipped "guid<TAB>name" table: the client has no name lookup of its own), plus
// a readable form for the UI ("AB_Vampire_Sword_Primary_MeleeAttack_Hit" -> "Sword Primary Melee Attack").
internal static class PrefabNames
{
    const string ResourceName = "DragUI.prefab-names.tsv.gz";

    // Name parts that only say how an ability is built, not what it is.
    static readonly HashSet<string> _noise = new(StringComparer.OrdinalIgnoreCase)
    {
        "AB", "CHAR", "EquipBuff", "Item", "Buff", "Vampire", "AbilityGroup", "Group", "Cast", "Hit", "Projectile",
        "Trigger", "Object", "Area", "Spawn", "Effect", "Shared", "Server", "Base", "Standard",
    };
    static readonly Regex _camel = new("(?<=[a-z])(?=[A-Z])", RegexOptions.Compiled);

    static Dictionary<int, string> _names;
    static readonly Dictionary<int, string> _pretty = new();

    static Dictionary<int, string> Names => _names ??= Load();

    public static bool TryGet(int guid, out string name) => Names.TryGetValue(guid, out name);

    /// <summary>Readable name for the UI; "#guid" when unknown. V Bloods use the catalog's proper names.</summary>
    public static string Pretty(int guid)
    {
        if (_pretty.TryGetValue(guid, out var cached)) return cached;
        string result;
        if (VBloodCatalog.TryGet(guid, out var vblood)) result = vblood.Name;
        else if (!TryGet(guid, out var name)) result = $"#{guid}";
        else
        {
            var parts = name.Split('_', StringSplitOptions.RemoveEmptyEntries).Where(p => !_noise.Contains(p)).ToList();
            result = parts.Count == 0 ? name : _camel.Replace(string.Join(" ", parts), " ");
        }
        _pretty[guid] = result;
        return result;
    }

    static Dictionary<int, string> Load()
    {
        var names = new Dictionary<int, string>();
        try
        {
            using var stream = typeof(PrefabNames).Assembly.GetManifestResourceStream(ResourceName);
            if (stream == null)
            {
                Core.Log.LogWarning($"Prefab name table '{ResourceName}' is missing; names show as numbers.");
                return names;
            }
            using var gzip = new GZipStream(stream, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip, Encoding.UTF8);
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                int tab = line.IndexOf('\t');
                if (tab > 0 && int.TryParse(line.AsSpan(0, tab), out int guid)) names[guid] = line[(tab + 1)..];
            }
        }
        catch (Exception ex)
        {
            Core.Log.LogWarning($"Couldn't load the prefab name table: {ex.Message}");
        }
        return names;
    }
}
