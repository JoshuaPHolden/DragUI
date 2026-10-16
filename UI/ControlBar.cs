using UnityEngine;

namespace VBloodHunter.UI;

// The mod's main control: a small always-on bar with one toggle button per feature window (lit while
// that window is open). Drag it by the grip on the left. New features add an Entry in UIManager.
internal sealed class ControlBar
{
    const float Pad = 5f;
    const float ButtonHeight = 26f;
    const float GripWidth = 18f;
    const float Gap = 4f;
    const float Radius = 10f;

    public sealed class Entry
    {
        public string Label;
        public float Width = 88f;
        public Func<bool> IsOpen;
        public Action Toggle;
    }

    readonly List<(Entry Entry, UIFactory.ButtonRef Button)> _buttons = new();

    public RectTransform Root { get; }
    public RectTransform Grip { get; }

    public ControlBar(Transform parent, IEnumerable<Entry> entries)
    {
        Root = UIFactory.CreateCard("ControlBar", parent, Theme.HudBg, Radius).Root;

        Grip = UIFactory.CreatePanel("Grip", Root, new Color(0, 0, 0, 0)).rectTransform;
        Grip.Place(Pad, Pad, GripWidth, ButtonHeight);
        for (int col = 0; col < 2; col++)
            for (int row = 0; row < 3; row++)
                UIFactory.CreateDot(Grip, 5 + col * 6, 6 + row * 6, 3, Theme.TextDim);

        float x = Pad + GripWidth + Gap;
        foreach (var entry in entries)
        {
            var button = UIFactory.CreateButton(entry.Label, Root, entry.Label, 13, entry.Toggle, radius: 7f);
            button.Rect.Place(x, Pad, entry.Width, ButtonHeight);
            _buttons.Add((entry, button));
            x += entry.Width + Gap;
        }

        Root.anchorMin = Root.anchorMax = new Vector2(0f, 1f);
        Root.pivot = new Vector2(0f, 1f);
        Root.sizeDelta = new Vector2(x - Gap + Pad, ButtonHeight + Pad * 2);
        Root.anchoredPosition = Settings.BarPosition;
        Refresh();
    }

    public void Refresh()
    {
        foreach (var (entry, button) in _buttons)
        {
            string style = entry.IsOpen() ? "open" : "closed";
            if (button.Style == style) continue;
            button.Style = style;
            if (style == "open") button.SetColors(Theme.Accent, Theme.AccentHover);
            else button.SetColors(Theme.Button, Theme.ButtonHover);
        }
    }
}
