using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;
using VBloodHunter.Data;
using VBloodHunter.Services;

namespace VBloodHunter.UI;

// Always-on widget: a header button that opens the V Blood list, and one row per hunted V Blood with
// its live status and a Track button.
internal sealed class HuntTrackerHud
{
    const float Width = 330f;
    const float HeaderHeight = 28f;
    const float RowHeight = 24f;
    const float Pad = 4f;

    sealed class Row
    {
        public VBloodInfo Info;
        public RectTransform Rect;
        public TextMeshProUGUI Name;
        public TextMeshProUGUI Status;
        public UIFactory.ButtonRef Track;
    }

    readonly List<Row> _rows = new();
    readonly UIFactory.ButtonRef _headerButton;
    readonly UIFactory.ButtonRef _collapseButton;
    readonly RectTransform _body;
    readonly TextMeshProUGUI _emptyText;

    public RectTransform Root { get; }
    public RectTransform Header => _headerButton.Rect;

    public HuntTrackerHud(Transform parent)
    {
        var background = UIFactory.CreatePanel("HuntTracker", parent, Theme.HudBg);
        Root = background.rectTransform;

        _headerButton = UIFactory.CreateButton("ListButton", Root, "V Blood Hunt", 15, OnHeaderClicked);
        _headerButton.Rect.Place(0, 0, Width - HeaderHeight, HeaderHeight);
        _headerButton.SetColors(Theme.Accent, Theme.AccentHover);
        _headerButton.Label.fontStyle = FontStyles.Bold;

        _collapseButton = UIFactory.CreateButton("Collapse", Root, "-", 16, () =>
        {
            Settings.HudCollapsed = !Settings.HudCollapsed;
            Layout();
        });
        _collapseButton.Rect.Place(Width - HeaderHeight, 0, HeaderHeight, HeaderHeight);
        _collapseButton.SetColors(Theme.TitleBg, Theme.AccentHover);

        _body = UIFactory.CreateRect("Rows", Root);
        _emptyText = UIFactory.CreateText("Empty", _body, "No V Bloods selected. Click the header to pick some.",
            12, Theme.TextDim);
        _emptyText.rectTransform.Place(8, 0, Width - 16, RowHeight);

        Rebuild();
    }

    void OnHeaderClicked()
    {
        if (UIManager.ConsumeDragClick()) return;
        UIManager.ToggleList();
    }

    public void Rebuild()
    {
        foreach (var row in _rows) Object.Destroy(row.Rect.gameObject);
        _rows.Clear();

        foreach (var info in HuntStore.Hunted) _rows.Add(CreateRow(info));
        Layout();
        Refresh();
    }

    Row CreateRow(VBloodInfo info)
    {
        var rect = UIFactory.CreateRect(info.PrefabName, _body);
        var row = new Row { Info = info, Rect = rect };

        row.Name = UIFactory.CreateText("Name", rect, info.Name, 13, Theme.Text);
        row.Name.rectTransform.Place(8, 0, 160, RowHeight);

        row.Status = UIFactory.CreateText("Status", rect, "...", 13, Theme.Unknown, TextAlignmentOptions.MidlineRight);
        row.Status.rectTransform.Place(170, 0, 92, RowHeight);

        row.Track = UIFactory.CreateButton("Track", rect, "Track", 12, () => VBloodTracker.ToggleTrack(info));
        row.Track.Rect.Place(Width - 62, 2, 56, RowHeight - 4);
        return row;
    }

    void Layout()
    {
        bool collapsed = Settings.HudCollapsed;
        _collapseButton.Label.text = collapsed ? "+" : "-";
        _body.gameObject.SetActive(!collapsed);

        int count = Math.Max(1, _rows.Count);
        _emptyText.gameObject.SetActive(_rows.Count == 0);
        for (int i = 0; i < _rows.Count; i++)
            _rows[i].Rect.Place(0, i * RowHeight, Width, RowHeight);

        float bodyHeight = count * RowHeight;
        _body.Place(0, HeaderHeight + Pad, Width, bodyHeight);
        float height = collapsed ? HeaderHeight : HeaderHeight + Pad + bodyHeight + Pad;

        Root.anchorMin = Root.anchorMax = new Vector2(0f, 1f);
        Root.pivot = new Vector2(0f, 1f);
        Root.sizeDelta = new Vector2(Width, height);
        Root.anchoredPosition = Settings.HudPosition;
    }

    public void SetPosition(Vector2 position) => Root.anchoredPosition = position;

    public void Refresh()
    {
        _headerButton.Label.text = HuntStore.Count > 0 ? $"V Blood Hunt ({HuntStore.Count})" : "V Blood Hunt";
        if (Settings.HudCollapsed) return;

        foreach (var row in _rows)
        {
            try
            {
                var status = VBloodTracker.Get(row.Info);
                var (text, color) = StatusFormat.Describe(status, compact: true);
                row.Status.text = text;
                row.Status.color = color;
                row.Name.color = VBloodTracker.IsTracked(row.Info) ? Theme.AccentHover : Theme.Text;
                TrackButtonStyle.Apply(row.Track, row.Info);
            }
            catch (Exception ex)
            {
                RowErrors.Report(row.Info, ex);
            }
        }
    }
}

// Logs a failing row once instead of every refresh, and keeps the other rows updating.
internal static class RowErrors
{
    static readonly HashSet<int> _reported = new();

    public static void Report(VBloodInfo info, Exception ex)
    {
        if (_reported.Add(info.Guid.GuidHash))
            Core.Log.LogError($"Couldn't show status for {info.Name}: {ex}");
    }
}

internal static class TrackButtonStyle
{
    public static void Apply(UIFactory.ButtonRef button, VBloodInfo info)
    {
        string style = VBloodTracker.IsTracked(info) ? "Tracked" : VBloodTracker.IsPendingTrack(info) ? "..." : "Track";
        if (button.Style == style) return;
        button.Style = style;
        button.Label.text = style;
        if (style == "Tracked") button.SetColors(Theme.Accent, Theme.AccentHover);
        else button.SetColors(Theme.Button, Theme.ButtonHover);
    }
}
