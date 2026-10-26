using TMPro;
using UnityEngine;
using DragUI.Data;
using DragUI.Services;

namespace DragUI.UI;

// The full list: every V Blood with a hunt checkbox, where it lives, its live status and a Track button.
internal sealed class VBloodListPanel : Window
{
    const float PanelWidth = 700f;
    const float RowHeight = 30f;
    const float ToolbarY = ContentTop;
    const float HeaderY = ToolbarY + 36f;
    const float ListTop = HeaderY + 26f;
    const float FooterHeight = 28f;

    // Column x positions inside a row.
    const float ColCheck = 10f, ColName = 40f, ColRegion = 290f, ColStatus = 400f, ColTrack = PanelWidth - 110f;

    sealed class Row
    {
        public VBloodInfo Info;
        public RectTransform Rect;
        public UnityEngine.UI.Image Background;
        public UIFactory.CheckboxRef Check;
        public TextMeshProUGUI Status;
        public UIFactory.ButtonRef Track;
    }

    readonly List<Row> _rows = new();
    readonly UIFactory.ScrollRef _scroll;
    readonly UIFactory.ButtonRef _filterAll;
    readonly UIFactory.ButtonRef _filterHunted;
    readonly TextMeshProUGUI _summary;
    bool _showHuntedOnly;

    public override Vector2 SavedPosition
    {
        get => Settings.PanelPosition;
        set => Settings.PanelPosition = value;
    }

    public VBloodListPanel(Transform parent)
        : base("VBloodList", parent, "V Blood Hunt", "tick the V Bloods you want on your tracker", PanelWidth, 580f)
    {
        // Toolbar.
        _filterAll = UIFactory.CreateButton("ShowAll", Root, "All", 13, () => SetFilter(false), radius: 13f);
        _filterAll.Rect.Place(12, ToolbarY, 70, 26);
        _filterHunted = UIFactory.CreateButton("ShowHunted", Root, "Hunting", 13, () => SetFilter(true), radius: 13f);
        _filterHunted.Rect.Place(88, ToolbarY, 86, 26);
        var clear = UIFactory.CreateButton("Clear", Root, "Clear hunts", 13, HuntStore.Clear, radius: 13f);
        clear.Rect.Place(Width - 118, ToolbarY, 106, 26);
        _summary = UIFactory.CreateText("Summary", Root, string.Empty, 13, Theme.TextDim);
        _summary.rectTransform.Place(186, ToolbarY, Width - 316, 26);

        // Column headers.
        Header("Hunt", ColCheck, 40, HeaderY);
        Header("V Blood", ColName + 4, 200, HeaderY);
        Header("Region", ColRegion + 4, 120, HeaderY);
        Header("Status", ColStatus + 4, 160, HeaderY);
        UIFactory.CreateDivider(Root, 12, HeaderY + 22, Width - 24);

        // List.
        _scroll = UIFactory.CreateScrollView("List", Root);
        _scroll.Scroll.GetComponent<RectTransform>().Fill(Inset, ListTop, 8, FooterHeight);
        foreach (var info in VBloodCatalog.All) _rows.Add(CreateRow(info));

        var footer = UIFactory.CreateText("Footer", Root,
            "~ = estimated respawn. Exact timers appear once you're near a dead V Blood's ghost. Statuses come from the same request as the game's V Blood menu.",
            11, Theme.TextDim);
        footer.rectTransform.Place(14, Height - FooterHeight, Width - 28, FooterHeight - 4);

        SetFilter(false);
    }

    void Header(string text, float x, float width, float y)
    {
        var label = UIFactory.CreateText(text, Root, text.ToUpperInvariant(), 11, Theme.TextDim);
        label.characterSpacing = 4f;
        label.rectTransform.Place(x, y, width, 20);
    }

    Row CreateRow(VBloodInfo info)
    {
        // The row itself is a button: clicking anywhere on it toggles the hunt checkbox.
        var rowButton = UIFactory.CreateButton(info.PrefabName, _scroll.Content, string.Empty, 12, () => HuntStore.Toggle(info),
            radius: 6f, outline: false);
        var rect = rowButton.Rect;
        var row = new Row { Info = info, Rect = rect, Background = rowButton.Background };

        row.Check = UIFactory.CreateCheckbox("Hunt", rect, () => HuntStore.Toggle(info));
        row.Check.Box.Rect.Place(ColCheck, 5, 20, 20);

        var name = UIFactory.CreateText("Name", rect, info.Name, 14, Theme.Text);
        name.rectTransform.Place(ColName, 0, ColRegion - ColName - 8, RowHeight);

        var region = UIFactory.CreateText("Region", rect, $"{info.Region} · {info.ActLabel}", 12, Theme.TextDim);
        region.rectTransform.Place(ColRegion, 0, ColStatus - ColRegion - 8, RowHeight);

        row.Status = UIFactory.CreateText("Status", rect, "...", 13, Theme.Unknown);
        row.Status.rectTransform.Place(ColStatus, 0, ColTrack - ColStatus - 8, RowHeight);

        row.Track = UIFactory.CreateButton("Track", rect, "Track", 12, () => VBloodTracker.ToggleTrack(info), radius: 11f);
        row.Track.Rect.Place(ColTrack, 4, 76, RowHeight - 8);
        return row;
    }

    void SetFilter(bool huntedOnly)
    {
        _showHuntedOnly = huntedOnly;
        _filterAll.SetColors(huntedOnly ? Theme.Button : Theme.Accent, huntedOnly ? Theme.ButtonHover : Theme.AccentHover);
        _filterHunted.SetColors(huntedOnly ? Theme.Accent : Theme.Button, huntedOnly ? Theme.AccentHover : Theme.ButtonHover);
        LayoutRows();
    }

    /// <summary>Re-apply visibility, order and hunted highlighting (after the hunt set changes).</summary>
    public void LayoutRows()
    {
        int visible = 0;
        foreach (var row in _rows)
        {
            bool hunted = HuntStore.IsHunted(row.Info);
            bool show = !_showHuntedOnly || hunted;
            row.Rect.gameObject.SetActive(show);
            row.Check.Set(hunted);
            if (!show) continue;

            row.Rect.Place(2, visible * RowHeight + 1, Width - 30, RowHeight - 2);
            var normal = hunted ? Theme.RowHunted : visible % 2 == 1 ? Theme.RowAlt : new Color(0, 0, 0, 0);
            var hover = new Color(normal.r, normal.g, normal.b, normal.a + 0.08f);
            var colors = row.Background.GetComponent<UnityEngine.UI.Button>().colors;
            colors.normalColor = normal;
            colors.highlightedColor = hover;
            colors.selectedColor = normal;
            colors.pressedColor = hover;
            row.Background.GetComponent<UnityEngine.UI.Button>().colors = colors;
            visible++;
        }
        _scroll.Content.sizeDelta = new Vector2(0f, visible * RowHeight);
        Refresh();
    }

    public override void Refresh()
    {
        if (!Visible) return;

        string tracked = VBloodCatalog.TryGet(VBloodTracker.TrackedUnit, out var trackedInfo) ? trackedInfo.Name : "nothing";
        string data = VBloodTracker.HasPositionData ? string.Empty : " · waiting for server data";
        _summary.text = $"{HuntStore.Count} hunting · tracking {tracked}{data}";

        foreach (var row in _rows)
        {
            if (!row.Rect.gameObject.activeSelf) continue;
            try
            {
                var (text, color) = StatusFormat.Describe(VBloodTracker.Get(row.Info), compact: false);
                row.Status.text = text;
                row.Status.color = color;
                TrackButtonStyle.Apply(row.Track, row.Info);
            }
            catch (Exception ex)
            {
                RowErrors.Report(row.Info, ex);
            }
        }
    }
}
