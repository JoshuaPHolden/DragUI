using UnityEngine;

namespace DragUI.UI;

// The mod's main control: a small always-on bar with one toggle button per feature (lit while that
// feature's window is open or the feature is on), plus buttons that only show when relevant (e.g. a
// boss teleport) and can flash. Drag it by the grip on the left. New features add an Entry in UIManager.
// The buttons flow into rows of the player's chosen width (drag the strip on the right edge), each row
// stretched to fill it, so the bar can be a row, a column or anything between. An Entry with Children
// is a group: its button drops down a menu of them.
internal sealed class ControlBar
{
    const float Pad = 5f;
    const float ButtonHeight = 26f;
    const float GripWidth = 18f;
    const float Gap = 4f;
    const float Radius = 10f;
    const float FlashSpeed = 2.5f; // pulses per second
    const float ResizeWidth = 18f;

    public sealed class Entry
    {
        public string Label;
        public float Width = 88f;
        public Func<bool> IsOpen;
        public Action Toggle;
        /// <summary>Null = always shown.</summary>
        public Func<bool> IsVisible;
        /// <summary>Live label (e.g. a countdown); null = Label.</summary>
        public Func<string> GetLabel;
        /// <summary>Null = never flashes.</summary>
        public Func<bool> IsFlashing;
        /// <summary>Non-null = a group: the button opens a menu of these (IsOpen and Toggle are set for it).</summary>
        public List<Entry> Children;
    }

    sealed class Menu
    {
        public Entry Group;
        public UIFactory.ButtonRef GroupButton;
        public RectTransform Card;
        public readonly List<(Entry Entry, UIFactory.ButtonRef Button)> Items = new();
    }

    readonly List<(Entry Entry, UIFactory.ButtonRef Button)> _buttons = new();
    readonly bool[] _shown;
    readonly RectTransform[] _gripDots = new RectTransform[6];
    readonly List<Menu> _menus = new();
    Menu _openMenu;
    float _width;   // requested button-area width; 0 = one row
    float _natural; // one-row width of the shown buttons
    float _widest;

    public RectTransform Root { get; }
    public RectTransform Grip { get; }
    public RectTransform ResizeHandle { get; }

    /// <summary>Current width of the button area.</summary>
    public float Width { get; private set; }

    /// <summary>Root width beyond the button area (padding, grip and resize strip).</summary>
    public static float Chrome => Pad * 2 + GripWidth + ResizeWidth + Gap * 2;

    public ControlBar(Transform parent, IEnumerable<Entry> entries)
    {
        Root = UIFactory.CreateCard("ControlBar", parent, Theme.HudBg, Radius).Root;
        Root.anchorMin = Root.anchorMax = new Vector2(0f, 1f);
        Root.pivot = new Vector2(0f, 1f);

        Grip = UIFactory.CreatePanel("Grip", Root, new Color(0, 0, 0, 0)).rectTransform;
        for (int i = 0; i < _gripDots.Length; i++)
            _gripDots[i] = UIFactory.CreateDot(Grip, 0, 0, 3, Theme.TextDim).rectTransform;

        foreach (var entry in entries)
        {
            Menu menu = null;
            if (entry.Children != null)
            {
                var m = menu = new Menu { Group = entry };
                entry.Toggle = () => SetMenu(_openMenu == m ? null : m);
                entry.IsOpen = () => _openMenu == m || m.Group.Children.Any(c => c.IsOpen());
            }
            var button = UIFactory.CreateButton(entry.Label, Root, entry.Label, 13, entry.Toggle, radius: 7f);
            _buttons.Add((entry, button));
            if (menu != null)
            {
                menu.GroupButton = button;
                _menus.Add(menu);
            }
        }
        _shown = new bool[_buttons.Count];

        // A full-height strip on the right edge, faintly lit so it reads as something to grab, with a
        // column-resize icon (drag sideways) so it isn't mistaken for the move grip's dots.
        ResizeHandle = UIFactory.CreatePanel("Resize", Root, new Color(1f, 1f, 1f, 0.06f)).Rounded(5f).rectTransform;
        var icon = UIFactory.CreatePanel("Icon", ResizeHandle, Theme.TextDim, raycast: false);
        icon.sprite = Icons.ResizeHorizontal;
        icon.preserveAspect = true;
        var iconRect = icon.rectTransform;
        iconRect.anchorMin = iconRect.anchorMax = iconRect.pivot = new Vector2(0.5f, 0.5f);
        iconRect.anchoredPosition = Vector2.zero;
        iconRect.sizeDelta = new Vector2(ResizeWidth - 2f, ResizeWidth - 2f);

        // After the buttons so they draw over the bar.
        foreach (var menu in _menus) BuildMenu(menu);

        _width = Settings.BarWidth;
        Layout();
        Root.anchoredPosition = Settings.BarPosition;
        Refresh();
    }

    /// <summary>Live resize (drag); SaveWidth() keeps it.</summary>
    public void Resize(float width)
    {
        // At least one row's worth = one row, which also keeps a row when buttons are added later.
        _width = width >= _natural - 0.5f ? 0f : Mathf.Max(width, _widest);
        Layout();
    }

    public void SaveWidth() => Settings.BarWidth = _width;

    /// <summary>Snaps to one column, or from any other shape to one row.</summary>
    public void ToggleRowColumn()
    {
        _width = Width >= _natural - 0.5f ? _widest : 0f;
        Layout();
        SaveWidth();
    }

    void Layout()
    {
        var shown = new List<int>();
        _natural = 0f;
        _widest = 0f;
        for (int i = 0; i < _buttons.Count; i++)
        {
            var (entry, button) = _buttons[i];
            _shown[i] = entry.IsVisible?.Invoke() ?? true;
            button.Rect.gameObject.SetActive(_shown[i]);
            if (!_shown[i]) continue;
            shown.Add(i);
            _natural += (shown.Count > 1 ? Gap : 0f) + entry.Width;
            _widest = Mathf.Max(_widest, entry.Width);
        }
        float width = Width = _width <= 0f ? _natural : Mathf.Clamp(_width, _widest, _natural);

        // Fill each row left to right, then stretch its buttons (by their widths) to the full width.
        float left = Pad + GripWidth + Gap;
        float y = Pad;
        for (int start = 0; start < shown.Count;)
        {
            int end = start;
            float used = 0f;
            for (; end < shown.Count; end++)
            {
                float add = (end > start ? Gap : 0f) + _buttons[shown[end]].Entry.Width;
                if (end > start && used + add > width + 0.5f) break;
                used += add;
            }

            float gaps = Gap * (end - start - 1);
            float stretch = (width - gaps) / (used - gaps);
            float x = left;
            for (int k = start; k < end; k++)
            {
                var (entry, button) = _buttons[shown[k]];
                float w = k == end - 1 ? left + width - x : Mathf.Round(entry.Width * stretch);
                button.Rect.Place(x, y, w, ButtonHeight);
                x += w + Gap;
            }
            y += ButtonHeight + Gap;
            start = end;
        }

        float height = Mathf.Max(y - Gap, Pad + ButtonHeight) + Pad;
        Root.sizeDelta = new Vector2(left + width + Gap + ResizeWidth + Pad, height);

        // Grip: the full-height strip on the left, its 2x3 dots centred.
        float gripHeight = height - Pad * 2;
        Grip.Place(Pad, Pad, GripWidth, gripHeight);
        float dotsTop = Mathf.Ceil((gripHeight - 15f) / 2f);
        for (int i = 0; i < _gripDots.Length; i++)
            _gripDots[i].Place(5 + i % 2 * 6, dotsTop + i / 2 * 6, 3, 3);

        // Resize strip: the same height (its "<>" label fills it, centred).
        ResizeHandle.Place(left + width + Gap, Pad, ResizeWidth, gripHeight);
        if (_openMenu != null) SetMenu(_openMenu); // follow its button
    }

    void BuildMenu(Menu menu)
    {
        float width = Mathf.Max(menu.GroupButton.Rect.sizeDelta.x, menu.Group.Children.Max(c => c.Width));
        menu.Card = UIFactory.CreateCard(menu.Group.Label + "Menu", Root, Theme.HudBg, Radius).Root;
        menu.Card.sizeDelta = new Vector2(width + Pad * 2, Pad * 2 + menu.Group.Children.Count * (ButtonHeight + Gap) - Gap);
        float y = Pad;
        foreach (var child in menu.Group.Children)
        {
            var c = child;
            var button = UIFactory.CreateButton(c.Label, menu.Card, c.Label, 13, () =>
            {
                SetMenu(null);
                c.Toggle();
            }, radius: 7f);
            button.Rect.Place(Pad, y, width, ButtonHeight);
            menu.Items.Add((c, button));
            y += ButtonHeight + Gap;
        }
        menu.Card.gameObject.SetActive(false);
    }

    void SetMenu(Menu menu)
    {
        if (_openMenu != null) _openMenu.Card.gameObject.SetActive(false);
        _openMenu = menu;
        if (menu == null) return;

        // Dropped down from its button, as wide as the button (or its widest item).
        var at = menu.GroupButton.Rect;
        float width = Mathf.Max(at.sizeDelta.x, menu.Group.Children.Max(c => c.Width)) + Pad * 2;
        menu.Card.Place(at.anchoredPosition.x - Pad, -at.anchoredPosition.y + ButtonHeight + Gap, width, menu.Card.sizeDelta.y);
        foreach (var (_, button) in menu.Items)
            button.Rect.sizeDelta = new Vector2(width - Pad * 2, ButtonHeight);
        menu.Card.SetAsLastSibling();
        menu.Card.gameObject.SetActive(true);
    }

    /// <summary>The open drop-down menu, if any (counts as our UI for click blocking).</summary>
    public RectTransform OpenMenu => _openMenu?.Card;

    /// <summary>Per frame: show/hide conditional buttons, update labels, styles and flashing.</summary>
    public void Refresh()
    {
        // A click anywhere but the menu or its button closes it (the button toggles it itself).
        if (_openMenu != null && Input.GetMouseButtonDown(0)
            && !Contains(_openMenu.Card, Input.mousePosition) && !Contains(_openMenu.GroupButton.Rect, Input.mousePosition))
        {
            SetMenu(null);
        }

        for (int i = 0; i < _buttons.Count; i++)
        {
            if ((_buttons[i].Entry.IsVisible?.Invoke() ?? true) == _shown[i]) continue;
            Layout();
            break;
        }

        float pulse = Mathf.PingPong(Time.realtimeSinceStartup * FlashSpeed * 2f, 1f);
        for (int i = 0; i < _buttons.Count; i++)
            if (_shown[i]) Restyle(_buttons[i].Entry, _buttons[i].Button, pulse);
        if (_openMenu != null)
            foreach (var (entry, button) in _openMenu.Items) Restyle(entry, button, pulse);
    }

    static void Restyle(Entry entry, UIFactory.ButtonRef button, float pulse)
    {
        if (entry.GetLabel != null)
        {
            string label = entry.GetLabel();
            if (button.Label.text != label) button.Label.text = label;
        }

        if (entry.IsFlashing?.Invoke() ?? false)
        {
            button.Style = "flash";
            button.SetColors(Color.Lerp(Theme.Accent, Theme.Due, pulse), Theme.AccentHover);
            return;
        }

        string style = entry.IsOpen() ? "open" : "closed";
        if (button.Style == style) return;
        button.Style = style;
        if (style == "open") button.SetColors(Theme.Accent, Theme.AccentHover);
        else button.SetColors(Theme.Button, Theme.ButtonHover);
    }

    static bool Contains(RectTransform rect, Vector2 screenPoint) =>
        rect.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(rect, screenPoint, null);
}
