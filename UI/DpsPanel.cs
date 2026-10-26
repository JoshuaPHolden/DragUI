using TMPro;
using UnityEngine;
using DragUI.Services;

namespace DragUI.UI;

// The DPS meter: the current (or a picked recent) fight's damage per ability, and the last fights to
// compare builds. Rows are created once and refreshed in place.
internal sealed class DpsPanel : Window
{
    const float PanelWidth = 600f;
    const float RowHeight = 24f;
    const int AbilityRows = 12;
    const int FightRows = 6;
    const float HeaderY = ContentTop + 40f;
    const float AbilityTop = HeaderY + 24f;
    const float FightsY = AbilityTop + AbilityRows * RowHeight + 12f;
    const float FightsTop = FightsY + 24f;

    // Ability table columns: name, damage, DPS, hits, crit %, share.
    static readonly float[] ColX = { 14f, 250f, 334f, 410f, 466f, 522f };
    static readonly float[] ColW = { 230f, 78f, 70f, 50f, 50f, 64f };

    readonly TextMeshProUGUI _summary;
    readonly UIFactory.ButtonRef _live;
    readonly TextMeshProUGUI[][] _abilityCells = new TextMeshProUGUI[AbilityRows][];
    readonly UIFactory.ButtonRef[] _fightButtons = new UIFactory.ButtonRef[FightRows];
    readonly TextMeshProUGUI[][] _fightCells = new TextMeshProUGUI[FightRows][];
    readonly TextMeshProUGUI _noFights;
    DamageMeter.Fight _selected; // null = live (current or latest fight)

    public override Vector2 SavedPosition
    {
        get => Settings.DpsPanelPosition;
        set => Settings.DpsPanelPosition = value;
    }

    public DpsPanel(Transform parent)
        : base("Dps", parent, "DPS meter", "damage you deal, per fight", PanelWidth, FightsTop + FightRows * RowHeight + 40f)
    {
        _summary = UIFactory.CreateText("Summary", Root, string.Empty, 14, Theme.Text);
        _summary.rectTransform.Place(14, ContentTop, Width - 190, 28);
        _live = UIFactory.CreateButton("Live", Root, "Live", 12, () => _selected = null, radius: 12f);
        _live.Rect.Place(Width - 172, ContentTop + 2, 70, 24);
        var reset = UIFactory.CreateButton("Reset", Root, "Reset", 12, () => { _selected = null; DamageMeter.Reset(); }, radius: 12f);
        reset.Rect.Place(Width - 96, ContentTop + 2, 84, 24);

        string[] headers = { "Ability", "Damage", "DPS", "Hits", "Crit", "Share" };
        for (int c = 0; c < headers.Length; c++) Header(headers[c], c, HeaderY);
        UIFactory.CreateDivider(Root, 12, HeaderY + 20, Width - 24);

        for (int r = 0; r < AbilityRows; r++)
        {
            _abilityCells[r] = new TextMeshProUGUI[ColX.Length];
            for (int c = 0; c < ColX.Length; c++)
            {
                var cell = UIFactory.CreateText($"A{r}_{c}", Root, string.Empty, 13, Theme.Text,
                    c == 0 ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.MidlineRight);
                cell.rectTransform.Place(ColX[c], AbilityTop + r * RowHeight, ColW[c], RowHeight);
                _abilityCells[r][c] = cell;
            }
        }

        var fightsLabel = UIFactory.CreateText("Recent", Root, "RECENT FIGHTS  (click one to show it)", 11, Theme.TextDim);
        fightsLabel.characterSpacing = 3f;
        fightsLabel.rectTransform.Place(14, FightsY, Width - 28, 20);
        UIFactory.CreateDivider(Root, 12, FightsY + 20, Width - 24);
        _noFights = UIFactory.CreateText("NoFights", Root, "Fights show up here once they end (a few seconds after your last hit).", 12, Theme.TextDim);
        _noFights.rectTransform.Place(14, FightsTop, Width - 28, RowHeight);

        for (int r = 0; r < FightRows; r++)
        {
            int index = r;
            var button = UIFactory.CreateButton($"Fight{r}", Root, string.Empty, 12, () => Select(index), radius: 6f, outline: false);
            button.Rect.Place(8, FightsTop + r * RowHeight, Width - 16, RowHeight - 2);
            _fightButtons[r] = button;
            // time, target, duration, DPS, damage
            float[] x = { 8f, 70f, 330f, 400f, 490f };
            float[] w = { 56f, 250f, 64f, 84f, 80f };
            _fightCells[r] = new TextMeshProUGUI[x.Length];
            for (int c = 0; c < x.Length; c++)
            {
                var cell = UIFactory.CreateText($"F{c}", button.Rect, string.Empty, 12, Theme.Text,
                    c is 0 or 1 ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.MidlineRight);
                cell.rectTransform.Place(x[c], 0, w[c], RowHeight - 2);
                _fightCells[r][c] = cell;
            }
        }
    }

    void Header(string text, int column, float y)
    {
        var label = UIFactory.CreateText(text, Root, text.ToUpperInvariant(), 11, Theme.TextDim,
            column == 0 ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.MidlineRight);
        label.characterSpacing = 2f;
        label.rectTransform.Place(ColX[column], y, ColW[column], 20);
    }

    void Select(int index)
    {
        if (index >= DamageMeter.History.Count) return;
        var fight = DamageMeter.History[index];
        _selected = _selected == fight ? null : fight;
    }

    public override void Refresh()
    {
        if (!Visible) return;
        if (_selected != null && !DamageMeter.History.Contains(_selected)) _selected = null;

        var fight = _selected ?? DamageMeter.Latest;
        bool live = _selected == null && DamageMeter.Current != null;
        _summary.text = fight == null
            ? "No damage yet - hit something."
            : $"{(live ? "Fighting" : _selected != null ? $"Fight at {fight.StartedAt:HH:mm}" : "Last fight")} · " +
              $"{FormatTime(fight.Seconds)} · <b>{fight.Dps:N0} DPS</b> · {ExperienceTracker.Format(fight.Damage)} vs {fight.MainTarget}";
        _summary.color = live ? Theme.AccentHover : Theme.Text;
        _live.SetColors(_selected == null ? Theme.Accent : Theme.Button, _selected == null ? Theme.AccentHover : Theme.ButtonHover);

        var lines = fight?.Lines.Values.OrderByDescending(l => l.Damage).ToList() ?? new List<DamageMeter.Line>();
        for (int r = 0; r < AbilityRows; r++)
        {
            var cells = _abilityCells[r];
            if (r >= lines.Count)
            {
                foreach (var cell in cells) cell.text = string.Empty;
                continue;
            }
            var line = lines[r];
            cells[0].text = line.Name;
            cells[1].text = ExperienceTracker.Format(line.Damage);
            cells[2].text = (line.Damage / fight.Seconds).ToString("N0");
            cells[3].text = line.Hits.ToString();
            cells[4].text = line.Hits == 0 ? "-" : $"{100.0 * line.Crits / line.Hits:0}%";
            cells[5].text = $"{100 * line.Damage / Math.Max(1, fight.Damage):0}%";
            cells[0].color = line.Name == DamageMeter.FamiliarLine ? Theme.Respawning : Theme.Text;
        }

        var history = DamageMeter.History;
        _noFights.gameObject.SetActive(history.Count == 0);
        for (int r = 0; r < FightRows; r++)
        {
            bool show = r < history.Count;
            _fightButtons[r].Rect.gameObject.SetActive(show);
            if (!show) continue;
            var f = history[r];
            var cells = _fightCells[r];
            cells[0].text = f.StartedAt.ToString("HH:mm");
            cells[1].text = f.MainTarget;
            cells[2].text = FormatTime(f.Seconds);
            cells[3].text = $"{f.Dps:N0} DPS";
            cells[4].text = ExperienceTracker.Format(f.Damage);
            bool picked = f == _selected;
            var normal = picked ? Theme.RowHunted : r % 2 == 1 ? Theme.RowAlt : new Color(0, 0, 0, 0);
            _fightButtons[r].SetColors(normal, Theme.ButtonHover);
        }
    }

    static string FormatTime(double seconds)
    {
        var t = TimeSpan.FromSeconds(seconds);
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{(int)t.TotalMinutes}:{t.Seconds:00}";
    }
}
