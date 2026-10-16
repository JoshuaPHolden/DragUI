using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using Object = UnityEngine.Object;
using VBloodHunter.Services;

namespace VBloodHunter.UI;

// Quick summon for Bloodcraft familiars: type a name (partial is fine) and press Enter or Summon, or
// click a saved name. Every name summoned from the box is saved here, newest first. Each saved name can
// have a hotkey: click its key button, then press the key (UIManager polls the binds). The bound
// familiar (as Bloodcraft last reported it) is shown on the Bound line and coloured in the list.
internal sealed class FamiliarPanel : Window
{
    const float PanelWidth = 380f;
    const float RowHeight = 30f;
    const float SummonWidth = 96f;
    const float ActionsY = ContentTop + 40f;
    const float StatusY = ActionsY + 30f;
    const float SavedY = StatusY + 30f;
    const float ListTop = SavedY + 26f;
    const float FooterHeight = 48f;
    const float KeyWidth = 78f;

    readonly TMP_InputField _input;
    readonly TextMeshProUGUI _bound;
    readonly TextMeshProUGUI _status;
    readonly TextMeshProUGUI _empty;
    readonly UIFactory.ScrollRef _scroll;
    readonly List<RectTransform> _rows = new();
    readonly Dictionary<string, UIFactory.ButtonRef> _keyButtons = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, TextMeshProUGUI> _nameLabels = new(StringComparer.OrdinalIgnoreCase);
    string _capturing; // saved name waiting for a key press

    /// <summary>True while waiting for the player to press a key to bind (game keys are locked meanwhile).</summary>
    public bool Capturing => _capturing != null;

    public override Vector2 SavedPosition
    {
        get => Settings.FamiliarPanelPosition;
        set => Settings.FamiliarPanelPosition = value;
    }

    public FamiliarPanel(Transform parent)
        : base("Familiars", parent, "Familiars", "quick summon", PanelWidth, 490f)
    {
        float inputWidth = Width - 24 - SummonWidth - 8;
        _input = UIFactory.CreateInputField("Name", Root, "Familiar name...", 14, SummonTyped, characterLimit: 48);
        _input.GetComponent<RectTransform>().Place(12, ContentTop, inputWidth, 32);
        var summon = UIFactory.CreateButton("Summon", Root, "Summon", 14, () => SummonTyped(_input.text), radius: 8f);
        summon.Rect.Place(Width - 12 - SummonWidth, ContentTop, SummonWidth, 32);
        summon.SetColors(Theme.Accent, Theme.AccentHover);
        summon.Label.fontStyle = FontStyles.Bold;

        var toggle = UIFactory.CreateButton("CallDismiss", Root, "Call / Dismiss", 13, () => FamiliarService.ToggleActive(), radius: 13f);
        toggle.Rect.Place(12, ActionsY, 124, 26);
        _bound = UIFactory.CreateText("Bound", Root, string.Empty, 13, Theme.TextDim);
        _bound.rectTransform.Place(146, ActionsY, Width - 158, 26);
        _status = UIFactory.CreateText("Status", Root, string.Empty, 12, Theme.TextDim);
        _status.rectTransform.Place(14, StatusY, Width - 28, 22);

        var label = UIFactory.CreateText("Saved", Root, "SAVED", 11, Theme.TextDim);
        label.characterSpacing = 4f;
        label.rectTransform.Place(14, SavedY, 200, 20);
        UIFactory.CreateDivider(Root, 12, SavedY + 22, Width - 24);

        _scroll = UIFactory.CreateScrollView("List", Root);
        _scroll.Scroll.GetComponent<RectTransform>().Fill(Inset, ListTop, 8, FooterHeight);
        _empty = UIFactory.CreateText("Empty", _scroll.Content, "Summon a familiar by name and it's saved here.", 12, Theme.TextDim);
        _empty.rectTransform.Place(10, 4, Width - 40, RowHeight);

        var footer = UIFactory.CreateText("Footer", Root,
            "Binds with Bloodcraft's .fam sb (partial names work, not in combat); summoning the bound familiar again calls/dismisses it (.fam t). " +
            "Hotkeys: click a key button and press a key (Esc cancels, Backspace clears). Use keys the game doesn't, e.g. F5-F12, numpad or Ctrl+key.",
            11, Theme.TextDim);
        footer.enableWordWrapping = true;
        footer.rectTransform.Place(14, Height - FooterHeight, Width - 28, FooterHeight - 4);

        Rebuild();
    }

    void SummonTyped(string text)
    {
        if (!FamiliarService.Summon(text, remember: true)) return;
        _input.text = string.Empty;
        _input.DeactivateInputField();
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
    }

    /// <summary>Recreate the saved-name rows (after the saved list changes).</summary>
    public void Rebuild()
    {
        foreach (var row in _rows) Object.Destroy(row.gameObject);
        _rows.Clear();
        _keyButtons.Clear();
        _nameLabels.Clear();

        var entries = FamiliarStore.Entries;
        for (int i = 0; i < entries.Count; i++) _rows.Add(CreateRow(entries[i], i));
        _empty.gameObject.SetActive(entries.Count == 0);
        _scroll.Content.sizeDelta = new Vector2(0f, Math.Max(1, entries.Count) * RowHeight);
        if (_capturing != null && !_keyButtons.ContainsKey(_capturing)) _capturing = null;
        foreach (var name in _keyButtons.Keys) StyleKey(name);
    }

    RectTransform CreateRow(FamiliarStore.Entry entry, int index)
    {
        string name = entry.Name;
        var button = UIFactory.CreateButton(name, _scroll.Content, name, 14, () => FamiliarService.Summon(name, remember: false),
            radius: 6f, outline: false);
        var normal = index % 2 == 1 ? Theme.RowAlt : new Color(0, 0, 0, 0);
        button.SetColors(normal, Theme.ButtonHover);
        button.Label.alignment = TextAlignmentOptions.MidlineLeft;
        button.Label.overflowMode = TextOverflowModes.Ellipsis;
        button.Label.rectTransform.Fill(12, 0, 44 + KeyWidth + 6, 0);
        var rect = button.Rect.Place(2, index * RowHeight + 1, Width - 30, RowHeight - 2);
        _nameLabels[name] = button.Label;

        var key = UIFactory.CreateButton("Hotkey", rect, string.Empty, 11, () => ToggleCapture(name), radius: 6f);
        key.Rect.Place(Width - 30 - 32 - 6 - KeyWidth, 3, KeyWidth, RowHeight - 8);
        _keyButtons[name] = key;

        var remove = UIFactory.CreateButton("Forget", rect, "x", 13, () => FamiliarStore.Forget(name), radius: 6f);
        remove.Rect.Place(Width - 30 - 32, 3, 26, RowHeight - 8);
        remove.SetColors(Theme.Button, Theme.AccentHover);
        return rect;
    }

    void ToggleCapture(string name)
    {
        string previous = _capturing;
        _capturing = string.Equals(previous, name, StringComparison.OrdinalIgnoreCase) ? null : name;
        if (previous != null) StyleKey(previous);
        if (_capturing != null) StyleKey(_capturing);
    }

    void StopCapture()
    {
        string name = _capturing;
        _capturing = null;
        if (name != null) StyleKey(name);
    }

    void StyleKey(string name)
    {
        if (!_keyButtons.TryGetValue(name, out var button)) return;
        var hotkey = FamiliarStore.Entries.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase))?.Hotkey
            ?? Hotkey.None;
        if (string.Equals(_capturing, name, StringComparison.OrdinalIgnoreCase))
        {
            button.Label.text = "Press a key";
            button.Label.color = Theme.Text;
            button.SetColors(Theme.Accent, Theme.AccentHover);
        }
        else
        {
            button.Label.text = hotkey.IsNone ? "+ key" : hotkey.ToString();
            button.Label.color = hotkey.IsNone ? Theme.TextDim : Theme.Text;
            button.SetColors(Theme.Button, Theme.ButtonHover);
        }
    }

    /// <summary>Per frame: finish a pending key capture. Runs after the hotkeys are polled, so the key
    /// that's being bound doesn't also summon.</summary>
    public void Update()
    {
        if (_capturing == null) return;
        if (!Visible || Input.GetKeyDown(KeyCode.Escape))
        {
            StopCapture();
        }
        else if (Input.GetKeyDown(KeyCode.Backspace) || Input.GetKeyDown(KeyCode.Delete))
        {
            string name = _capturing;
            StopCapture();
            FamiliarStore.SetHotkey(name, Hotkey.None);
        }
        else if (Hotkey.TryCapture(out var hotkey))
        {
            string name = _capturing;
            StopCapture();
            FamiliarStore.SetHotkey(name, hotkey);
        }
    }

    public override void Refresh()
    {
        if (!Visible) return;
        _status.text = FamiliarService.LastStatus;

        string bound = FamiliarService.BoundName ?? "a familiar";
        (_bound.text, _bound.color) = FamiliarService.State switch
        {
            FamiliarService.BindState.Bound when FamiliarService.Dismissed => ($"Bound: {bound} (dismissed)", Theme.Respawning),
            FamiliarService.BindState.Bound => ($"Bound: {bound} (out)", Theme.Alive),
            FamiliarService.BindState.None => ("No familiar bound", Theme.TextDim),
            _ => ("Bound: unknown until the next summon", Theme.TextDim),
        };

        foreach (var (name, label) in _nameLabels)
            label.color = !FamiliarService.IsBound(name) ? Theme.Text : FamiliarService.Dismissed ? Theme.Respawning : Theme.Alive;
    }
}
