using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;
using DragUI.Services;

namespace DragUI.UI;

// Experience per hour, split by source, one row per prestige group (newest first). Opened from the XP
// button on the control bar, which shows the current group's total rate.
internal sealed class ExperiencePanel : Window
{
    const float PanelWidth = 660f;
    const float RowHeight = 28f;
    const float LogY = ContentTop + 32f;
    const float HeaderY = LogY + 38f;
    const float ListTop = HeaderY + 26f;
    const float FooterHeight = 34f;

    // Column x positions and widths: group, active time, quest/h, mobs/h, V Blood/h, total/h, total XP.
    static readonly float[] ColX = { 12f, 184f, 260f, 334f, 408f, 488f, 562f };
    static readonly float[] ColW = { 168f, 72f, 70f, 70f, 76f, 70f, 60f };

    sealed class Row
    {
        public ExperienceTracker.Group Group;
        public RectTransform Rect;
        public TextMeshProUGUI[] Cells;
    }

    readonly TextMeshProUGUI _current;
    readonly TextMeshProUGUI _log;
    readonly UIFactory.ScrollRef _scroll;
    readonly TextMeshProUGUI _empty;
    readonly List<Row> _rows = new();
    ExperienceTracker.Group _builtNewest;
    int _builtCount = -1;

    public override Vector2 SavedPosition
    {
        get => Settings.XpPanelPosition;
        set => Settings.XpPanelPosition = value;
    }

    public ExperiencePanel(Transform parent)
        : base("Experience", parent, "Experience", "per hour, by prestige", PanelWidth, 420f)
    {
        _current = UIFactory.CreateText("Current", Root, string.Empty, 14, Theme.Text);
        _current.rectTransform.Place(14, ContentTop, Width - 220, 26);
        var sync = UIFactory.CreateButton("Sync", Root, "Sync levels", 12, ExperienceTracker.Sync, radius: 12f);
        sync.Rect.Place(Width - 204, ContentTop, 96, 24);
        var reset = UIFactory.CreateButton("Reset", Root, "Reset row", 12, ExperienceTracker.ResetCurrent, radius: 12f);
        reset.Rect.Place(Width - 102, ContentTop, 90, 24);

        _log = UIFactory.CreateText("Log", Root, string.Empty, 12, Theme.TextDim);
        _log.rectTransform.Place(14, LogY, Width - 200, 26);
        var toggleLog = UIFactory.CreateButton("ToggleLog", Root, "Toggle .lvl log", 12, ExperienceTracker.ToggleBloodcraftLog, radius: 12f);
        toggleLog.Rect.Place(Width - 162, LogY, 150, 24);

        string[] headers = { "Prestige", "Active", "Quests/h", "Mobs/h", "V Blood/h", "Total/h", "XP" };
        for (int i = 0; i < headers.Length; i++)
        {
            var label = UIFactory.CreateText(headers[i], Root, headers[i].ToUpperInvariant(), 11, Theme.TextDim,
                i == 0 ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.MidlineRight);
            label.characterSpacing = 2f;
            label.rectTransform.Place(ColX[i] + Inset, HeaderY, ColW[i], 20);
        }
        UIFactory.CreateDivider(Root, 12, HeaderY + 22, Width - 24);

        _scroll = UIFactory.CreateScrollView("Groups", Root);
        _scroll.Scroll.GetComponent<RectTransform>().Fill(Inset, ListTop, 8, FooterHeight);
        _empty = UIFactory.CreateText("Empty", _scroll.Content,
            "No experience counted yet. Turn on Bloodcraft's XP log with the button above, then go kill something.", 12, Theme.TextDim);
        _empty.rectTransform.Place(10, 4, Width - 40, RowHeight);

        var footer = UIFactory.CreateText("Footer", Root,
            "Counts Bloodcraft's +XP lines: quest rewards, the biggest gain when a V Blood dies near you, and everything else as mobs. " +
            "Time only counts while you're gaining XP. A prestige or exo starts a new row.", 11, Theme.TextDim);
        footer.enableWordWrapping = true;
        footer.rectTransform.Place(14, Height - FooterHeight, Width - 28, FooterHeight - 4);
    }

    void RebuildRows()
    {
        foreach (var row in _rows) Object.Destroy(row.Rect.gameObject);
        _rows.Clear();

        int index = 0;
        foreach (var group in ExperienceTracker.Groups)
        {
            var rect = UIFactory.CreateRect("Group", _scroll.Content);
            rect.Place(2, index * RowHeight + 1, Width - 30, RowHeight - 2);
            if (index % 2 == 1) UIFactory.CreatePanel("Alt", rect, Theme.RowAlt, raycast: false).Rounded(6f).rectTransform.Fill();

            var cells = new TextMeshProUGUI[ColX.Length];
            for (int i = 0; i < cells.Length; i++)
            {
                cells[i] = UIFactory.CreateText($"Cell{i}", rect, string.Empty, 13, Theme.Text,
                    i == 0 ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.MidlineRight);
                cells[i].rectTransform.Place(ColX[i] - 2 + Inset, 0, ColW[i], RowHeight - 2);
            }
            _rows.Add(new Row { Group = group, Rect = rect, Cells = cells });
            index++;
        }

        _empty.gameObject.SetActive(_rows.Count == 0);
        _scroll.Content.sizeDelta = new Vector2(0f, Math.Max(1, _rows.Count) * RowHeight);
        _builtCount = _rows.Count;
        _builtNewest = ExperienceTracker.Current;
    }

    public override void Refresh()
    {
        if (!Visible) return;
        if (_builtCount != ExperienceTracker.Groups.Count() || _builtNewest != ExperienceTracker.Current) RebuildRows();

        var current = ExperienceTracker.Current;
        _current.text = current == null
            ? "Current: waiting for experience"
            : $"Current: {current.Label} · {ExperienceTracker.Format(ExperienceTracker.CurrentRate)} XP/h";

        (_log.text, _log.color) = ExperienceTracker.LogEnabled switch
        {
            true => ("Bloodcraft XP log: on", Theme.Alive),
            false => ("Bloodcraft XP log: off - turn it on to count XP", Theme.Due),
            null => ("Bloodcraft XP log: not seen yet this session", Theme.TextDim),
        };

        foreach (var row in _rows)
        {
            var g = row.Group;
            bool isCurrent = g == current;
            row.Cells[0].text = g.Label;
            row.Cells[1].text = FormatTime(g.ActiveSeconds);
            row.Cells[2].text = Rate(g, g.Quest);
            row.Cells[3].text = Rate(g, g.Mobs);
            row.Cells[4].text = Rate(g, g.VBlood);
            row.Cells[5].text = Rate(g, g.Total);
            row.Cells[6].text = ExperienceTracker.Format(g.Total);
            foreach (var cell in row.Cells) cell.color = isCurrent ? Theme.Text : Theme.TextDim;
            row.Cells[5].color = isCurrent ? Theme.AccentHover : Theme.TextDim;
        }
    }

    static string Rate(ExperienceTracker.Group g, double xp) =>
        g.ActiveSeconds < 60 ? "-" : ExperienceTracker.Format(g.PerHour(xp));

    static string FormatTime(double seconds)
    {
        var t = TimeSpan.FromSeconds(seconds);
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes:00}m" : $"{t.Minutes}m {t.Seconds:00}s";
    }
}
