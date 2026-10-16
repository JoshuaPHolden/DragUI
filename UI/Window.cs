using TMPro;
using UnityEngine;

namespace VBloodHunter.UI;

// A feature window: rounded card centred on screen, with a title bar (drag handle) and a close
// button. Hidden until opened from the control bar. UIManager drags it and saves where it's put.
internal abstract class Window
{
    protected const float Radius = 12f;
    protected const float Inset = 5f;
    protected const float TitleHeight = 36f;
    /// <summary>Top of the content area below the title bar.</summary>
    protected const float ContentTop = Inset + TitleHeight + 10f;

    protected readonly float Width;
    protected readonly float Height;

    public RectTransform Root { get; }
    public RectTransform TitleBar { get; }
    public RectTransform CloseButton { get; }
    public bool Visible => Root.gameObject.activeSelf;

    /// <summary>Saved position (offset from the screen centre).</summary>
    public abstract Vector2 SavedPosition { get; set; }

    protected Window(string name, Transform parent, string title, string hint, float width, float height)
    {
        Width = width;
        Height = height;

        Root = UIFactory.CreateCard(name, parent, Theme.PanelBg, Radius).Root;
        Root.anchorMin = Root.anchorMax = new Vector2(0.5f, 0.5f);
        Root.pivot = new Vector2(0.5f, 0.5f);
        Root.sizeDelta = new Vector2(width, height);
        Root.anchoredPosition = SavedPosition;

        var bar = UIFactory.CreatePanel("TitleBar", Root, Theme.TitleBg).Rounded(Radius - Inset + 2);
        TitleBar = bar.rectTransform.Place(Inset, Inset, width - Inset * 2, TitleHeight);
        var titleText = UIFactory.CreateText("Title", TitleBar, title, 18, Theme.Text);
        titleText.fontStyle = FontStyles.Bold;
        titleText.characterSpacing = 2f;
        titleText.rectTransform.Place(14, 0, width - 60, TitleHeight);
        if (!string.IsNullOrEmpty(hint))
        {
            titleText.ForceMeshUpdate();
            float hintX = 14 + titleText.preferredWidth + 14;
            var hintText = UIFactory.CreateText("Hint", TitleBar, hint, 13, Theme.TextDim);
            hintText.rectTransform.Place(hintX, 0, Mathf.Max(0, width - Inset * 2 - 46 - hintX), TitleHeight);
        }

        var close = UIFactory.CreateButton("Close", TitleBar, "X", 14, () => SetVisible(false), radius: 7f);
        close.Rect.Place(width - Inset * 2 - 34, 5, 28, TitleHeight - 10);
        close.SetColors(Theme.TitleBg, Theme.AccentHover);
        CloseButton = close.Rect;

        Root.gameObject.SetActive(false);
    }

    public void SetVisible(bool visible)
    {
        if (visible == Visible) return;
        Root.gameObject.SetActive(visible);
        if (visible)
        {
            Root.SetAsLastSibling();
            OnShown();
        }
    }

    public void Toggle() => SetVisible(!Visible);

    protected virtual void OnShown() => Refresh();

    /// <summary>Update live values (called a few times a second while visible).</summary>
    public virtual void Refresh() { }
}
