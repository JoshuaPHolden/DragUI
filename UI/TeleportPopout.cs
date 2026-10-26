using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;
using DragUI.Services;

namespace DragUI.UI;

// The player's teleports as a small window that can stay open while playing: one button per teleport
// (personal first, then the server's), showing its hotkey; click to teleport. Opened with Pop out in the
// Teleport window; dragged by its header; it sizes itself to the list. Position and whether it's open
// are saved.
internal sealed class TeleportPopout
{
    const float Width = 210f;
    const float Pad = 6f;
    const float HeaderHeight = 24f;
    const float RowHeight = 26f;
    const float Gap = 3f;
    const float Radius = 10f;

    readonly RectTransform _header;
    readonly RectTransform _close;
    readonly List<RectTransform> _rows = new();

    public RectTransform Root { get; }
    public RectTransform Header => _header;
    public RectTransform CloseButton => _close;
    public bool Visible => Root.gameObject.activeSelf;

    public TeleportPopout(Transform parent)
    {
        Root = UIFactory.CreateCard("TeleportPopout", parent, Theme.HudBg, Radius).Root;
        Root.anchorMin = Root.anchorMax = new Vector2(0f, 1f);
        Root.pivot = new Vector2(0f, 1f);

        var header = UIFactory.CreatePanel("Header", Root, Theme.TitleBg).Rounded(7f);
        _header = header.rectTransform.Place(Pad, Pad, Width - Pad * 2, HeaderHeight);
        var title = UIFactory.CreateText("Title", _header, "TELEPORTS", 11, Theme.Text);
        title.fontStyle = FontStyles.Bold;
        title.characterSpacing = 2f;
        title.rectTransform.Place(10, 0, Width - Pad * 2 - 40, HeaderHeight);
        var close = UIFactory.CreateButton("Close", _header, "x", 12, () => SetVisible(false), radius: 6f);
        close.Rect.Place(Width - Pad * 2 - 24, 3, 20, HeaderHeight - 6);
        close.SetColors(Theme.TitleBg, Theme.AccentHover);
        _close = close.Rect;

        Rebuild();
        Root.anchoredPosition = Settings.TeleportPopoutPosition;
        Root.gameObject.SetActive(Settings.TeleportPopoutVisible);
    }

    public void SetVisible(bool visible)
    {
        Settings.TeleportPopoutVisible = visible;
        Root.gameObject.SetActive(visible);
        if (!visible) return;
        Root.SetAsLastSibling();
        if (!TeleportStore.Listed) TeleportStore.Refresh();
    }

    public void Toggle() => SetVisible(!Visible);

    /// <summary>Recreate the buttons (after the list or hotkeys change).</summary>
    public void Rebuild()
    {
        foreach (var row in _rows) Object.Destroy(row.gameObject);
        _rows.Clear();

        float y = Pad + HeaderHeight + Pad;
        foreach (var name in TeleportStore.Personal) y = AddRow(name, y, global: false);
        if (TeleportStore.Personal.Count > 0 && TeleportStore.Global.Count > 0)
        {
            var divider = UIFactory.CreateDivider(Root, 10, y + 1, Width - 20);
            _rows.Add(divider.rectTransform);
            y += Gap * 2;
        }
        foreach (var name in TeleportStore.Global) y = AddRow(name, y, global: true);

        if (_rows.Count == 0)
        {
            string text = TeleportStore.Listed ? "No teleports yet: set some in the Teleport window." : "Reading your teleports...";
            var empty = UIFactory.CreateText("Empty", Root, text, 11, Theme.TextDim);
            empty.enableWordWrapping = true;
            empty.rectTransform.Place(10, y, Width - 20, 34);
            _rows.Add(empty.rectTransform);
            y += 34 + Gap;
        }
        Root.sizeDelta = new Vector2(Width, y - Gap + Pad);
    }

    float AddRow(string name, float y, bool global)
    {
        var button = UIFactory.CreateButton(name, Root, name, 13, () => TeleportStore.Teleport(name), radius: 6f);
        button.Rect.Place(Pad, y, Width - Pad * 2, RowHeight);
        button.Label.alignment = TextAlignmentOptions.MidlineLeft;
        button.Label.overflowMode = TextOverflowModes.Ellipsis;
        button.Label.color = global ? Theme.TextDim : Theme.Text;

        var hotkey = TeleportStore.GetHotkey(name);
        float keyWidth = hotkey.IsNone ? 0f : 64f;
        button.Label.rectTransform.Fill(10, 0, 10 + keyWidth, 0);
        if (!hotkey.IsNone)
        {
            var key = UIFactory.CreateText("Key", button.Rect, hotkey.ToString(), 11, Theme.TextDim, TextAlignmentOptions.MidlineRight);
            key.rectTransform.Place(Width - Pad * 2 - 8 - keyWidth, 0, keyWidth, RowHeight);
        }
        _rows.Add(button.Rect);
        return y + RowHeight + Gap;
    }
}
