using TMPro;
using UnityEngine;
using DragUI.Services;

namespace DragUI.UI;

// Full character sheet: every stat with its value and where it comes from (StatSheet). Text rows are
// pooled and refilled on each refresh, so nothing is created or destroyed while it's open.
internal sealed class CharacterPanel : Window
{
    const float PanelWidth = 560f;
    const float LineHeight = 20f;
    const int PoolSize = 220;
    const float SummaryHeight = 58f;
    const float ListTop = ContentTop + SummaryHeight;
    const float FooterHeight = 34f;

    readonly TextMeshProUGUI _summary;
    readonly UIFactory.ScrollRef _scroll;
    readonly (TextMeshProUGUI Left, TextMeshProUGUI Right, TextMeshProUGUI Note)[] _pool = new (TextMeshProUGUI, TextMeshProUGUI, TextMeshProUGUI)[PoolSize];
    double _nextBuildAt;

    public override Vector2 SavedPosition
    {
        get => Settings.CharacterPanelPosition;
        set => Settings.CharacterPanelPosition = value;
    }

    public CharacterPanel(Transform parent)
        : base("Character", parent, "Character", "every stat and where it comes from", PanelWidth, 640f)
    {
        _summary = UIFactory.CreateText("Summary", Root, string.Empty, 12, Theme.TextDim);
        _summary.enableWordWrapping = true;
        _summary.rectTransform.Place(14, ContentTop - 4, Width - 150, SummaryHeight - 6);
        var refresh = UIFactory.CreateButton("RefreshPerks", Root, "Refresh perks", 12, () =>
        {
            PerkshopStats.Sync();
            _nextBuildAt = 0;
        }, radius: 12f);
        refresh.Rect.Place(Width - 130, ContentTop, 118, 24);
        UIFactory.CreateDivider(Root, 12, ListTop - 4, Width - 24);

        _scroll = UIFactory.CreateScrollView("Stats", Root);
        _scroll.Scroll.GetComponent<RectTransform>().Fill(Inset, ListTop, 8, FooterHeight);
        for (int i = 0; i < PoolSize; i++)
        {
            var left = UIFactory.CreateText($"L{i}", _scroll.Content, string.Empty, 13, Theme.Text);
            var right = UIFactory.CreateText($"R{i}", _scroll.Content, string.Empty, 13, Theme.Text, TextAlignmentOptions.MidlineRight);
            var note = UIFactory.CreateText($"N{i}", _scroll.Content, string.Empty, 11, Theme.TextDim, TextAlignmentOptions.MidlineRight);
            left.rectTransform.Place(10, i * LineHeight, 330, LineHeight);
            right.rectTransform.Place(340, i * LineHeight, 90, LineHeight);
            note.rectTransform.Place(432, i * LineHeight, 92, LineHeight);
            _pool[i] = (left, right, note);
        }

        var footer = UIFactory.CreateText("Footer", Root,
            "Values are what your client knows, with Bloodcraft's expertise/legacy bonuses (from the data it sends Eclipse) and PerkShop stat perks (.perk status) added. " +
            "Changes a server makes without telling the client don't show here - the DPS meter measures the real result.",
            11, Theme.TextDim);
        footer.enableWordWrapping = true;
        footer.rectTransform.Place(14, Height - FooterHeight, Width - 28, FooterHeight - 4);
    }

    protected override void OnShown()
    {
        // Check PerkShop once per session, and again if the last read is a few minutes old.
        if (PerkshopStats.Status == PerkshopStats.State.Unknown
            || (PerkshopStats.Status == PerkshopStats.State.Present && Time.realtimeSinceStartupAsDouble - PerkshopStats.LastSyncAt > 180))
            PerkshopStats.Sync();
        _nextBuildAt = 0;
        Refresh();
    }

    public override void Refresh()
    {
        if (!Visible) return;
        double now = Time.realtimeSinceStartupAsDouble;
        if (now < _nextBuildAt) return;
        _nextBuildAt = now + 0.5;

        List<StatSheet.Row> rows;
        try { rows = StatSheet.Build(); }
        catch (Exception ex)
        {
            _summary.text = "Couldn't read your stats (see the log).";
            Core.Log.LogError($"Character sheet failed: {ex}");
            return;
        }
        if (rows == null) return;

        float physical = rows.FirstOrDefault(r => r.Stat == ProjectM.UnitStatType.PhysicalPower)?.Total ?? 0;
        float spell = rows.FirstOrDefault(r => r.Stat == ProjectM.UnitStatType.SpellPower)?.Total ?? 0;
        float physCrit = StatSheet.CritFactor(rows, ProjectM.UnitStatType.PhysicalCriticalStrikeChance, ProjectM.UnitStatType.PhysicalCriticalStrikeDamage);
        float spellCrit = StatSheet.CritFactor(rows, ProjectM.UnitStatType.SpellCriticalStrikeChance, ProjectM.UnitStatType.SpellCriticalStrikeDamage);
        string bloodcraft = BloodcraftStats.Known
            ? "<color=#78D478>Bloodcraft bonuses: included</color>"
            : "<color=#EBBA52>Bloodcraft bonuses: waiting for its data (needs Eclipse; arrives every few seconds)</color>";
        string perkshop = PerkshopStats.Status switch
        {
            PerkshopStats.State.Present => $"<color=#78D478>PerkShop: {PerkshopStats.Perks.Count} stat perk(s) included</color>",
            PerkshopStats.State.Absent => "PerkShop: not on this server",
            _ => "<color=#EBBA52>PerkShop: checking...</color>",
        };
        _summary.text = $"{bloodcraft} · {perkshop}\n" +
                        $"Average hit power incl. crits ≈ physical {physical * physCrit:0.#} (x{physCrit:0.##}) · spell {spell * spellCrit:0.#} (x{spellCrit:0.##})";

        int line = 0;
        foreach (var row in rows)
        {
            if (line >= PoolSize) break;
            float? shown = row.Total ?? (row.Parts.Count > 0 ? row.Parts.Where(p => p.Type is ProjectM.ModificationType.Add or ProjectM.ModificationType.AddToBase).Sum(p => p.Value) : null);
            string note = row.ClientValue is { } client && row.Total is { } total && Math.Abs(client - total) > 0.0005f
                ? $"game: {StatSheet.FormatValue(row.Format, client)}"
                : row.ClientValue == null ? "sum of parts" : string.Empty;
            Set(line++, $"<b>{row.Name}</b>", shown is { } v ? StatSheet.FormatValue(row.Format, v) : "-", note, Theme.Text);

            foreach (var part in row.Parts)
            {
                if (line >= PoolSize) break;
                var color = part.Source.StartsWith("Bloodcraft") ? Theme.AccentHover : part.Source == "Base / unlisted" ? Theme.Unknown : Theme.TextDim;
                Set(line++, $"     {part.Source}", StatSheet.FormatPart(row.Format, part), string.Empty, color);
            }
        }
        for (int i = line; i < PoolSize; i++) Set(i, string.Empty, string.Empty, string.Empty, Theme.Text);
        _scroll.Content.sizeDelta = new Vector2(0f, line * LineHeight + 4);
    }

    void Set(int i, string left, string right, string note, Color color)
    {
        var (l, r, n) = _pool[i];
        if (l.text != left) l.text = left;
        if (r.text != right) r.text = right;
        if (n.text != note) n.text = note;
        l.color = color;
        r.color = color;
    }
}
