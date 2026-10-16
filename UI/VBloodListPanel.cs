using TMPro;
using UnityEngine;
using VBloodHunter.Data;
using VBloodHunter.Services;

namespace VBloodHunter.UI;

// The full list: every V Blood with a hunt checkbox, where it lives, its live status and a Track button.
internal sealed class VBloodListPanel
{
    const float Width = 700f;
    const float Height = 580f;
    const float TitleHeight = 36f;
    const float RowHeight = 30f;
    const float ListTop = TitleHeight + 64f;
    const float FooterHeight = 26f;

    // Column x positions inside a row.
    const float ColCheck = 10f, ColName = 40f, ColRegion = 290f, ColStatus = 420f, ColTrack = Width - 96f;

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

    public RectTransform Root { get; }
    public RectTransform TitleBar { get; }
    public RectTransform CloseButton { get; }
    public bool Visible => Root.gameObject.activeSelf;

    public VBloodListPanel(Transform parent)
    {
        var background = UIFactory.CreatePanel("VBloodList", parent, Theme.PanelBg);
        Root = background.rectTransform;
        Root.anchorMin = Root.anchorMax = new Vector2(0.5f, 0.5f);
        Root.pivot = new Vector2(0.5f, 0.5f);
        Root.sizeDelta = new Vector2(Width, Height);
        Root.anchoredPosition = Settings.PanelPosition;

        // Title bar (drag handle) + close.
        var title = UIFactory.CreatePanel("TitleBar", Root, Theme.TitleBg);
        TitleBar = title.rectTransform.Place(0, 0, Width, TitleHeight);
        var titleText = UIFactory.CreateText("Title", TitleBar, "V Blood Hunt", 18, Theme.Text);
        titleText.fontStyle = FontStyles.Bold;
        titleText.rectTransform.Place(12, 0, 200, TitleHeight);
        var hint = UIFactory.CreateText("Hint", TitleBar, "tick the V Bloods you want on your tracker", 13, Theme.TextDim);
        hint.rectTransform.Place(150, 0, 380, TitleHeight);
        var close = UIFactory.CreateButton("Close", TitleBar, "X", 15, () => SetVisible(false));
        close.Rect.Place(Width - 38, 5, 30, TitleHeight - 10);
        close.SetColors(Theme.TitleBg, Theme.AccentHover);
        CloseButton = close.Rect;

        // Toolbar.
        float toolbarY = TitleHeight + 8;
        _filterAll = UIFactory.CreateButton("ShowAll", Root, "All", 13, () => SetFilter(false));
        _filterAll.Rect.Place(10, toolbarY, 70, 24);
        _filterHunted = UIFactory.CreateButton("ShowHunted", Root, "Hunting", 13, () => SetFilter(true));
        _filterHunted.Rect.Place(84, toolbarY, 80, 24);
        var clear = UIFactory.CreateButton("Clear", Root, "Clear hunts", 13, HuntStore.Clear);
        clear.Rect.Place(Width - 106, toolbarY, 96, 24);
        _summary = UIFactory.CreateText("Summary", Root, string.Empty, 13, Theme.TextDim);
        _summary.rectTransform.Place(176, toolbarY, Width - 300, 24);

        // Column headers.
        float headerY = TitleHeight + 40;
        Header("Hunt", ColCheck - 2, 40, headerY);
        Header("V Blood", ColName, 200, headerY);
        Header("Region", ColRegion, 120, headerY);
        Header("Status", ColStatus, 160, headerY);

        // List.
        _scroll = UIFactory.CreateScrollView("List", Root);
        _scroll.Scroll.GetComponent<RectTransform>().Fill(0, ListTop, 6, FooterHeight);
        foreach (var info in VBloodCatalog.All) _rows.Add(CreateRow(info));

        var footer = UIFactory.CreateText("Footer", Root,
            "~ = estimated respawn. Exact timers appear once you're near a dead V Blood's ghost. Statuses come from the same request as the game's V Blood menu.",
            11, Theme.TextDim);
        footer.rectTransform.Place(10, Height - FooterHeight, Width - 20, FooterHeight);

        SetFilter(false);
        Root.gameObject.SetActive(false);
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
        var rowButton = UIFactory.CreateButton(info.PrefabName, _scroll.Content, string.Empty, 12, () => HuntStore.Toggle(info));
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

        row.Track = UIFactory.CreateButton("Track", rect, "Track", 12, () => VBloodTracker.ToggleTrack(info));
        row.Track.Rect.Place(ColTrack, 4, 74, RowHeight - 8);
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

            row.Rect.Place(0, visible * RowHeight, Width - 18, RowHeight);
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

    public void SetVisible(bool visible)
    {
        if (visible == Visible) return;
        Root.gameObject.SetActive(visible);
        if (visible)
        {
            Root.SetAsLastSibling();
            Refresh();
        }
    }

    public void SetPosition(Vector2 position) => Root.anchoredPosition = position;

    public void Refresh()
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
