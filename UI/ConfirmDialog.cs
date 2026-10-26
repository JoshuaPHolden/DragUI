using TMPro;
using UnityEngine;

namespace DragUI.UI;

// A yes/no prompt over a window: dims the whole window (and blocks clicks to it) and shows a small card
// with a title, a message, Cancel and a confirm button. Nothing happens until the confirm button is
// clicked; Cancel or showing another prompt drops the pending action.
internal sealed class ConfirmDialog
{
    const float CardWidth = 340f;
    const float CardHeight = 156f;
    const float Pad = 16f;
    const float ButtonWidth = 96f;
    const float ButtonHeight = 28f;

    readonly RectTransform _overlay;
    readonly TextMeshProUGUI _title;
    readonly TextMeshProUGUI _message;
    readonly UIFactory.ButtonRef _confirm;
    Action _onConfirm;

    public bool Visible => _overlay.gameObject.activeSelf;

    /// <param name="parent">The window's root; the prompt covers all of it.</param>
    public ConfirmDialog(RectTransform parent)
    {
        _overlay = UIFactory.CreatePanel("Confirm", parent, new Color(0f, 0f, 0f, 0.6f)).rectTransform;
        _overlay.Fill();

        var card = UIFactory.CreateCard("Card", _overlay, Theme.PanelBg, 10f).Root;
        card.anchorMin = card.anchorMax = card.pivot = new Vector2(0.5f, 0.5f);
        card.anchoredPosition = Vector2.zero;
        card.sizeDelta = new Vector2(CardWidth, CardHeight);

        _title = UIFactory.CreateText("Title", card, string.Empty, 15, Theme.Text);
        _title.fontStyle = FontStyles.Bold;
        _title.rectTransform.Place(Pad, 12, CardWidth - Pad * 2, 22);
        _message = UIFactory.CreateText("Message", card, string.Empty, 12, Theme.TextDim, TextAlignmentOptions.TopLeft);
        _message.enableWordWrapping = true;
        _message.rectTransform.Place(Pad, 40, CardWidth - Pad * 2, CardHeight - 40 - ButtonHeight - 22);

        float y = CardHeight - 12 - ButtonHeight;
        _confirm = UIFactory.CreateButton("Confirm", card, "OK", 13, Confirm, radius: 7f);
        _confirm.Rect.Place(CardWidth - Pad - ButtonWidth, y, ButtonWidth, ButtonHeight);
        _confirm.SetColors(Theme.Accent, Theme.AccentHover);
        _confirm.Label.fontStyle = FontStyles.Bold;
        var cancel = UIFactory.CreateButton("Cancel", card, "Cancel", 13, Hide, radius: 7f);
        cancel.Rect.Place(CardWidth - Pad - ButtonWidth * 2 - 8, y, ButtonWidth, ButtonHeight);

        _overlay.gameObject.SetActive(false);
    }

    public void Show(string title, string message, string confirmLabel, Action onConfirm)
    {
        _title.text = title;
        _message.text = message;
        _confirm.Label.text = confirmLabel;
        _onConfirm = onConfirm;
        _overlay.SetAsLastSibling();
        _overlay.gameObject.SetActive(true);
    }

    public void Hide()
    {
        _onConfirm = null;
        _overlay.gameObject.SetActive(false);
    }

    void Confirm()
    {
        var action = _onConfirm;
        Hide();
        action?.Invoke();
    }
}
