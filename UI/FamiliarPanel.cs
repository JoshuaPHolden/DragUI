using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using Object = UnityEngine.Object;
using DragUI.Services;

namespace DragUI.UI;

// Quick summon for Bloodcraft familiars: type a name (partial is fine) and press Enter or Summon, or
// click a saved name. Every name summoned from the box is saved here, newest first. Each saved name can
// have a hotkey: click its key button, then press the key (UIManager polls the binds). The bound
// familiar (as Bloodcraft last reported it) is shown on the Bound line and coloured in the list.
// List All switches the list to every familiar in every box (FamiliarCatalog), each with Save (add to
// the quick summon list) and Delete (permanent, so it asks first).
internal sealed class FamiliarPanel : Window
{
    const float PanelWidth = 420f;
    const float RowHeight = 30f;
    const float SummonWidth = 96f;
    const float ActionsY = ContentTop + 40f;
    const float StatusY = ActionsY + 30f;
    const float SavedY = StatusY + 30f;
    const float ListTop = SavedY + 26f;
    const float FooterHeight = 48f;
    const float KeyWidth = 78f;
    const float TabWidth = 84f;
    const float AllRowHeight = 40f;
    const double ConfirmSeconds = 4;

    const string SavedFooter =
        "Binds with Bloodcraft's .fam sb (partial names work, not in combat); summoning the bound familiar again calls/dismisses it (.fam t). " +
        "Hotkeys: click a key button and press a key (Esc cancels, Backspace clears). Use keys the game doesn't, e.g. F5-F12, numpad or Ctrl+key.";
    const string AllFooter =
        "List All reads every box with Bloodcraft's .fam boxes / cb / l (replies hidden) and then selects your box again. " +
        "Save adds a familiar to the quick summon list. Delete removes it from its box for good (.fam r).";

    readonly TMP_InputField _input;
    readonly TextMeshProUGUI _bound;
    readonly TextMeshProUGUI _status;
    readonly TextMeshProUGUI _empty;
    readonly UIFactory.ScrollRef _scroll;
    readonly UIFactory.ScrollRef _allScroll;
    readonly TextMeshProUGUI _allEmpty;
    readonly TextMeshProUGUI _catalogStatus;
    readonly TextMeshProUGUI _footer;
    readonly UIFactory.ButtonRef _savedTab;
    readonly UIFactory.ButtonRef _allTab;
    readonly List<RectTransform> _allRows = new();
    readonly Dictionary<FamiliarCatalog.Familiar, UIFactory.ButtonRef> _deleteButtons = new();
    FamiliarCatalog.Familiar _confirmDelete;
    double _confirmUntil;
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

        _savedTab = UIFactory.CreateButton("SavedTab", Root, "Saved", 12, () => ShowAll(false), radius: 6f);
        _savedTab.Rect.Place(12, SavedY - 3, TabWidth, 22);
        _allTab = UIFactory.CreateButton("ListAll", Root, "List All", 12, ListAll, radius: 6f);
        _allTab.Rect.Place(12 + TabWidth + 6, SavedY - 3, TabWidth, 22);
        float statusX = 12 + TabWidth * 2 + 18;
        _catalogStatus = UIFactory.CreateText("CatalogStatus", Root, string.Empty, 11, Theme.TextDim, TextAlignmentOptions.MidlineRight);
        _catalogStatus.overflowMode = TextOverflowModes.Ellipsis;
        _catalogStatus.rectTransform.Place(statusX, SavedY - 3, Width - 14 - statusX, 22);
        UIFactory.CreateDivider(Root, 12, SavedY + 22, Width - 24);

        _scroll = UIFactory.CreateScrollView("List", Root);
        _scroll.Scroll.GetComponent<RectTransform>().Fill(Inset, ListTop, 8, FooterHeight);
        _empty = UIFactory.CreateText("Empty", _scroll.Content, "Summon a familiar by name and it's saved here.", 12, Theme.TextDim);
        _empty.rectTransform.Place(10, 4, Width - 40, RowHeight);

        _allScroll = UIFactory.CreateScrollView("AllList", Root);
        _allScroll.Scroll.GetComponent<RectTransform>().Fill(Inset, ListTop, 8, FooterHeight);
        _allEmpty = UIFactory.CreateText("Empty", _allScroll.Content, "Click List All to read every box.", 12, Theme.TextDim);
        _allEmpty.rectTransform.Place(10, 4, Width - 40, RowHeight);

        _footer = UIFactory.CreateText("Footer", Root, SavedFooter, 11, Theme.TextDim);
        _footer.enableWordWrapping = true;
        _footer.rectTransform.Place(14, Height - FooterHeight, Width - 28, FooterHeight - 4);

        ShowAll(false);
        Rebuild();
    }

    void ListAll()
    {
        ShowAll(true);
        FamiliarCatalog.Scan(); // already running: just shows it
    }

    void ShowAll(bool all)
    {
        _scroll.Scroll.gameObject.SetActive(!all);
        _allScroll.Scroll.gameObject.SetActive(all);
        _footer.text = all ? AllFooter : SavedFooter;
        _savedTab.SetColors(all ? Theme.Button : Theme.Accent, all ? Theme.ButtonHover : Theme.AccentHover);
        _allTab.SetColors(all ? Theme.Accent : Theme.Button, all ? Theme.AccentHover : Theme.ButtonHover);
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
        RebuildAll();
    }

    void RebuildAll()
    {
        foreach (var row in _allRows) Object.Destroy(row.gameObject);
        _allRows.Clear();
        _deleteButtons.Clear();

        var all = FamiliarCatalog.All;
        for (int i = 0; i < all.Count; i++) _allRows.Add(CreateAllRow(all[i], i));
        _allEmpty.text = FamiliarCatalog.Busy ? "Reading your boxes..." : FamiliarCatalog.Scanned ? "No familiars found." : "Click List All to read every box.";
        _allEmpty.gameObject.SetActive(all.Count == 0);
        _allScroll.Content.sizeDelta = new Vector2(0f, Math.Max(1, all.Count) * AllRowHeight);
        if (_confirmDelete != null && !_deleteButtons.ContainsKey(_confirmDelete)) _confirmDelete = null;
    }

    RectTransform CreateAllRow(FamiliarCatalog.Familiar familiar, int index)
    {
        const float SaveButtonWidth = 60f, DeleteWidth = 60f;
        float rowWidth = Width - 30;
        var background = UIFactory.CreatePanel(familiar.Name, _allScroll.Content, index % 2 == 1 ? Theme.RowAlt : new Color(0, 0, 0, 0), raycast: false);
        var rect = background.rectTransform.Place(2, index * AllRowHeight + 1, rowWidth, AllRowHeight - 2);

        float textWidth = rowWidth - 12 - SaveButtonWidth - DeleteWidth - 18;
        var name = UIFactory.CreateText("Name", rect, familiar.Shiny ? $"{familiar.Name} <color=#EBBA52>*</color>" : familiar.Name, 13, Theme.Text);
        name.overflowMode = TextOverflowModes.Ellipsis;
        name.rectTransform.Place(12, 2, textWidth, 20);
        string prestige = familiar.Prestige > 0 ? $"  ·  P{familiar.Prestige}" : string.Empty;
        var detail = UIFactory.CreateText("Detail", rect, $"Lv {familiar.Level}{prestige}  ·  {familiar.Box}", 11, Theme.TextDim);
        detail.overflowMode = TextOverflowModes.Ellipsis;
        detail.rectTransform.Place(12, 20, textWidth, 16);

        float x = rowWidth - 6 - DeleteWidth;
        var delete = UIFactory.CreateButton("Delete", rect, "Delete", 12, () => OnDelete(familiar), radius: 6f);
        delete.Rect.Place(x, 8, DeleteWidth, 22);
        delete.SetColors(Theme.Button, Theme.AccentHover);
        _deleteButtons[familiar] = delete;

        x -= 6 + SaveButtonWidth;
        bool saved = FamiliarStore.Entries.Any(e => string.Equals(e.Name, familiar.Name, StringComparison.OrdinalIgnoreCase));
        var save = UIFactory.CreateButton("Save", rect, saved ? "Saved" : "Save", 12, () => FamiliarStore.Remember(familiar.Name), radius: 6f);
        save.Rect.Place(x, 8, SaveButtonWidth, 22);
        if (saved)
        {
            save.Button.interactable = false;
            save.Label.color = Theme.TextDim;
        }
        else
        {
            save.SetColors(Theme.Accent, Theme.AccentHover);
        }
        return rect;
    }

    // Permanent, so the first click only arms it ("Sure?") for a few seconds.
    void OnDelete(FamiliarCatalog.Familiar familiar)
    {
        if (_confirmDelete != familiar || Time.realtimeSinceStartupAsDouble >= _confirmUntil)
        {
            SetConfirmDelete(familiar);
            return;
        }
        SetConfirmDelete(null);
        FamiliarCatalog.Delete(familiar);
    }

    void SetConfirmDelete(FamiliarCatalog.Familiar familiar)
    {
        if (_confirmDelete != null && _deleteButtons.TryGetValue(_confirmDelete, out var previous))
        {
            previous.Label.text = "Delete";
            previous.SetColors(Theme.Button, Theme.AccentHover);
        }
        _confirmDelete = familiar;
        _confirmUntil = Time.realtimeSinceStartupAsDouble + ConfirmSeconds;
        if (familiar != null && _deleteButtons.TryGetValue(familiar, out var button))
        {
            button.Label.text = "Sure?";
            button.SetColors(Theme.Accent, Theme.AccentHover);
        }
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
        _catalogStatus.text = FamiliarCatalog.Status;
        _allTab.Label.text = FamiliarCatalog.Busy ? "Listing..." : "List All";
        if (_confirmDelete != null && Time.realtimeSinceStartupAsDouble >= _confirmUntil) SetConfirmDelete(null);

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
