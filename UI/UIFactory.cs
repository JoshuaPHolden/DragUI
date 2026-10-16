using Il2CppInterop.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VBloodHunter.UI;

internal static class Theme
{
    public static readonly Color PanelBg = new(0.055f, 0.05f, 0.06f, 0.92f);
    public static readonly Color HudBg = new(0.055f, 0.05f, 0.06f, 0.78f);
    public static readonly Color TitleBg = new(0.30f, 0.06f, 0.08f, 0.95f);
    public static readonly Color RowAlt = new(1f, 1f, 1f, 0.035f);
    public static readonly Color RowHunted = new(0.70f, 0.12f, 0.15f, 0.16f);
    public static readonly Color Button = new(0.20f, 0.18f, 0.20f, 1f);
    public static readonly Color ButtonHover = new(0.32f, 0.28f, 0.30f, 1f);
    public static readonly Color ButtonPressed = new(0.14f, 0.12f, 0.14f, 1f);
    public static readonly Color Accent = new(0.72f, 0.13f, 0.16f, 1f);
    public static readonly Color AccentHover = new(0.85f, 0.20f, 0.22f, 1f);
    public static readonly Color Text = new(0.90f, 0.88f, 0.86f, 1f);
    public static readonly Color TextDim = new(0.62f, 0.60f, 0.60f, 1f);
    public static readonly Color Alive = new(0.47f, 0.83f, 0.47f, 1f);
    public static readonly Color Respawning = new(0.92f, 0.73f, 0.32f, 1f);
    public static readonly Color Due = new(1f, 0.50f, 0.33f, 1f);
    public static readonly Color Unknown = new(0.55f, 0.55f, 0.58f, 1f);
}

// Minimal uGUI builder. Layout is done with explicit anchors/positions rather than layout groups so
// it behaves predictably under IL2CPP.
internal static class UIFactory
{
    static TMP_FontAsset _font;

    static TMP_FontAsset Font
    {
        get
        {
            if (_font != null) return _font;
            _font = TMP_Settings.defaultFontAsset;
            if (_font == null)
            {
                var fonts = Resources.FindObjectsOfTypeAll(Il2CppType.Of<TMP_FontAsset>());
                if (fonts.Length > 0) _font = fonts[0].Cast<TMP_FontAsset>();
            }
            return _font;
        }
    }

    public static RectTransform CreateRect(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.layer = 5; // UI
        var rect = go.AddComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    /// <summary>Anchor to the parent's top-left; position is the offset of the rect's top-left corner.</summary>
    public static RectTransform Place(this RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
        return rect;
    }

    /// <summary>Stretch to fill the parent, inset by the given margins.</summary>
    public static RectTransform Fill(this RectTransform rect, float left = 0, float top = 0, float right = 0, float bottom = 0)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
        return rect;
    }

    public static Image CreatePanel(string name, Transform parent, Color color, bool raycast = true)
    {
        var rect = CreateRect(name, parent);
        var image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = raycast;
        return image;
    }

    public static TextMeshProUGUI CreateText(string name, Transform parent, string text, float size,
        Color color, TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft)
    {
        var rect = CreateRect(name, parent);
        var tmp = rect.gameObject.AddComponent<TextMeshProUGUI>();
        if (Font != null) tmp.font = Font;
        tmp.text = text;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.alignment = alignment;
        tmp.enableWordWrapping = false;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        tmp.raycastTarget = false;
        return tmp;
    }

    public sealed class ButtonRef
    {
        public Button Button;
        public Image Background;
        public TextMeshProUGUI Label;
        /// <summary>Last style applied by a caller, so per-frame refreshes only restyle on change.</summary>
        public string Style;
        public RectTransform Rect => Button.GetComponent<RectTransform>();

        public void SetColors(Color normal, Color hover)
        {
            var colors = Button.colors;
            colors.normalColor = normal;
            colors.highlightedColor = hover;
            colors.selectedColor = normal;
            colors.pressedColor = Theme.ButtonPressed;
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            Button.colors = colors;
        }
    }

    public static ButtonRef CreateButton(string name, Transform parent, string label, float fontSize, Action onClick)
    {
        var rect = CreateRect(name, parent);
        var image = rect.gameObject.AddComponent<Image>();
        image.color = Color.white; // tinted by the button's ColorBlock
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        var navigation = button.navigation;
        navigation.mode = Navigation.Mode.None; // don't let keyboard/gamepad navigation grab our buttons
        button.navigation = navigation;

        var text = CreateText("Label", rect, label, fontSize, Theme.Text, TextAlignmentOptions.Center);
        text.rectTransform.Fill(4, 0, 4, 0);

        var buttonRef = new ButtonRef { Button = button, Background = image, Label = text };
        buttonRef.SetColors(Theme.Button, Theme.ButtonHover);
        button.onClick.AddListener((UnityEngine.Events.UnityAction)(() =>
        {
            try { onClick(); }
            catch (Exception ex) { Core.Log.LogError($"Button '{name}' failed: {ex}"); }
        }));
        return buttonRef;
    }

    public sealed class CheckboxRef
    {
        public ButtonRef Box;
        public Image Mark;
        public void Set(bool on) => Mark.gameObject.SetActive(on);
    }

    public static CheckboxRef CreateCheckbox(string name, Transform parent, Action onClick)
    {
        var box = CreateButton(name, parent, string.Empty, 12, onClick);
        box.SetColors(new Color(0.12f, 0.11f, 0.12f, 1f), new Color(0.24f, 0.21f, 0.23f, 1f));
        var mark = CreatePanel("Mark", box.Rect, Theme.Accent, raycast: false);
        mark.rectTransform.Fill(4, 4, 4, 4);
        return new CheckboxRef { Box = box, Mark = mark };
    }

    public sealed class ScrollRef
    {
        public ScrollRect Scroll;
        public RectTransform Viewport;
        public RectTransform Content;
    }

    /// <summary>Vertical scroll view filling <paramref name="parent"/>, with a scrollbar on the right.</summary>
    public static ScrollRef CreateScrollView(string name, Transform parent, float scrollbarWidth = 8f)
    {
        var root = CreateRect(name, parent);
        var scroll = root.gameObject.AddComponent<ScrollRect>();

        var viewportImage = CreatePanel("Viewport", root, new Color(0, 0, 0, 0.001f));
        var viewport = viewportImage.rectTransform.Fill(0, 0, scrollbarWidth + 4, 0);
        viewport.gameObject.AddComponent<RectMask2D>();

        var content = CreateRect("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = new Vector2(0f, 0f);

        var track = CreatePanel("Scrollbar", root, new Color(1f, 1f, 1f, 0.06f));
        var trackRect = track.rectTransform;
        trackRect.anchorMin = new Vector2(1f, 0f);
        trackRect.anchorMax = new Vector2(1f, 1f);
        trackRect.pivot = new Vector2(1f, 0.5f);
        trackRect.sizeDelta = new Vector2(scrollbarWidth, 0f);
        trackRect.anchoredPosition = Vector2.zero;

        var slidingArea = CreateRect("SlidingArea", trackRect).Fill();
        var handle = CreatePanel("Handle", slidingArea, new Color(0.62f, 0.20f, 0.22f, 0.9f));
        handle.rectTransform.Fill();

        var scrollbar = track.gameObject.AddComponent<Scrollbar>();
        scrollbar.handleRect = handle.rectTransform;
        scrollbar.targetGraphic = handle;
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        var navigation = scrollbar.navigation;
        navigation.mode = Navigation.Mode.None;
        scrollbar.navigation = navigation;

        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 28f;
        scroll.inertia = false;
        scroll.verticalScrollbar = scrollbar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

        return new ScrollRef { Scroll = scroll, Viewport = viewport, Content = content };
    }
}
