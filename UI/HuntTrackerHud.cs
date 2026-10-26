using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;
using DragUI.Data;
using DragUI.Services;

namespace DragUI.UI;

// Always-on widget: a header that collapses/expands it, a gear that opens the V Blood list (to pick the
// hunt), and one row per hunted V Blood with its live status and a Track button.
internal sealed class HuntTrackerHud
{
    const float Width = 400f;
    const float Pad = 6f;
    const float HeaderHeight = 28f;
    const float RowHeight = 26f;
    const float Radius = 10f;
    const float TrackWidth = 60f;

    sealed class Row
    {
        public VBloodInfo Info;
        public RectTransform Rect;
        public UnityEngine.UI.Image Dot;
        public UnityEngine.UI.Image Divider;
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
    public bool Visible => Root.gameObject.activeSelf;

    public HuntTrackerHud(Transform parent)
    {
        Root = UIFactory.CreateCard("HuntTracker", parent, Theme.HudBg, Radius).Root;

        _headerButton = UIFactory.CreateButton("ListButton", Root, "V Blood Hunt", 15, OnHeaderClicked, radius: 7f);
        _headerButton.Rect.Place(Pad, Pad, Width - Pad * 4 - HeaderHeight * 2, HeaderHeight);
        _headerButton.SetColors(Theme.Accent, Theme.AccentHover);
        _headerButton.Label.fontStyle = FontStyles.Bold;
        _headerButton.Label.characterSpacing = 1.5f;

        var gear = UIFactory.CreateButton("Choose", Root, string.Empty, 16, UIManager.ToggleList, radius: 7f);
        gear.Rect.Place(Width - Pad * 2 - HeaderHeight * 2, Pad, HeaderHeight, HeaderHeight);
        gear.SetColors(Theme.TitleBg, Theme.AccentHover);
        DrawGear(gear.Rect, HeaderHeight / 2f);

        _collapseButton = UIFactory.CreateButton("Collapse", Root, "-", 16, ToggleCollapsed, radius: 7f);
        _collapseButton.Rect.Place(Width - Pad - HeaderHeight, Pad, HeaderHeight, HeaderHeight);
        _collapseButton.SetColors(Theme.TitleBg, Theme.AccentHover);

        _body = UIFactory.CreateRect("Rows", Root);
        _emptyText = UIFactory.CreateText("Empty", _body, "No V Bloods selected. Click the gear to pick some.",
            12, Theme.TextDim);
        _emptyText.rectTransform.Place(12, 0, Width - 24, RowHeight);

        Rebuild();
        Root.anchoredPosition = Settings.HudPosition; // only here: rebuilds keep wherever it was dragged
        Root.gameObject.SetActive(Settings.HudVisible);
    }

    public void SetVisible(bool visible)
    {
        Settings.HudVisible = visible;
        Root.gameObject.SetActive(visible);
        if (visible) Refresh();
    }

    // No gear glyph in the game's font, so: a ring of teeth around a hub with a hole.
    static void DrawGear(Transform parent, float centre)
    {
        for (int i = 0; i < 8; i++)
        {
            float angle = i * Mathf.PI / 4f;
            UIFactory.CreateDot(parent, centre + Mathf.Cos(angle) * 6f - 2f, centre + Mathf.Sin(angle) * 6f - 2f, 4f, Theme.Text);
        }
        UIFactory.CreateDot(parent, centre - 5.5f, centre - 5.5f, 11f, Theme.Text);
        UIFactory.CreateDot(parent, centre - 2f, centre - 2f, 4f, Theme.TitleBg);
    }

    void OnHeaderClicked()
    {
        if (UIManager.ConsumeDragClick()) return;
        ToggleCollapsed();
    }

    void ToggleCollapsed()
    {
        Settings.HudCollapsed = !Settings.HudCollapsed;
        Layout();
        Refresh();
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

        row.Divider = UIFactory.CreateDivider(rect, 10, 0, Width - 20);
        row.Dot = UIFactory.CreateDot(rect, 12, (RowHeight - 8) / 2, 8, Theme.Unknown);

        row.Name = UIFactory.CreateText("Name", rect, info.Name, 13, Theme.Text);
        row.Name.rectTransform.Place(26, 0, 146, RowHeight);

        float statusX = 176;
        float statusWidth = Width - Pad - TrackWidth - 8 - statusX;
        row.Status = UIFactory.CreateText("Status", rect, "...", 13, Theme.Unknown, TextAlignmentOptions.MidlineRight);
        row.Status.rectTransform.Place(statusX, 0, statusWidth, RowHeight);

        row.Track = UIFactory.CreateButton("Track", rect, "Track", 12, () => VBloodTracker.ToggleTrack(info), radius: 10f);
        row.Track.Rect.Place(Width - Pad - TrackWidth, 3, TrackWidth, RowHeight - 6);
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
        {
            _rows[i].Rect.Place(0, i * RowHeight, Width, RowHeight);
            _rows[i].Divider.gameObject.SetActive(i > 0);
        }

        float bodyTop = Pad + HeaderHeight + Pad;
        float bodyHeight = count * RowHeight;
        _body.Place(0, bodyTop, Width, bodyHeight);
        float height = collapsed ? Pad + HeaderHeight + Pad : bodyTop + bodyHeight + Pad;

        Root.anchorMin = Root.anchorMax = new Vector2(0f, 1f);
        Root.pivot = new Vector2(0f, 1f);
        Root.sizeDelta = new Vector2(Width, height);
    }

    public void Refresh()
    {
        if (!Visible) return;
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
                row.Dot.color = color;
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
