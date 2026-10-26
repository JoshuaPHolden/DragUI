using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using Object = UnityEngine.Object;
using DragUI.Services;

namespace DragUI.UI;

// Saved builds: type a name and Save to record what's on now; Apply puts a saved one back on, Update
// re-records it, x deletes it. Overwriting a saved loadout (Update, or Save with a name that's taken)
// and deleting one ask first in a prompt. Applying a loadout that changes an already chosen Bloodcraft class (which costs
// items) asks too: its Apply button turns into Confirm for a few seconds.
internal sealed class LoadoutPanel : Window
{
    const float PanelWidth = 460f;
    const float RowHeight = 50f;
    const float SaveWidth = 120f;
    const float StatusY = ContentTop + 40f;
    const float SavedY = StatusY + 30f;
    const float ListTop = SavedY + 26f;
    const float FooterHeight = 48f;
    const float ApplyWidth = 74f;
    const float UpdateWidth = 66f;
    const double ConfirmSeconds = 6;

    readonly TMP_InputField _input;
    readonly TextMeshProUGUI _status;
    readonly TextMeshProUGUI _empty;
    readonly UIFactory.ScrollRef _scroll;
    readonly ConfirmDialog _prompt;
    readonly List<RectTransform> _rows = new();
    readonly Dictionary<string, UIFactory.ButtonRef> _applyButtons = new(StringComparer.OrdinalIgnoreCase);
    string _message;    // shown instead of the last apply's status until the next apply
    string _confirming; // loadout waiting for a second click to change class
    double _confirmUntil;

    public override Vector2 SavedPosition
    {
        get => Settings.LoadoutPanelPosition;
        set => Settings.LoadoutPanelPosition = value;
    }

    public LoadoutPanel(Transform parent)
        : base("Loadouts", parent, "Loadouts", "builds in one click", PanelWidth, 470f)
    {
        float inputWidth = Width - 24 - SaveWidth - 8;
        _input = UIFactory.CreateInputField("Name", Root, "Loadout name...", 14, SaveTyped, characterLimit: 32);
        _input.GetComponent<RectTransform>().Place(12, ContentTop, inputWidth, 32);
        var save = UIFactory.CreateButton("Save", Root, "Save current", 14, () => SaveTyped(_input.text), radius: 8f);
        save.Rect.Place(Width - 12 - SaveWidth, ContentTop, SaveWidth, 32);
        save.SetColors(Theme.Accent, Theme.AccentHover);
        save.Label.fontStyle = FontStyles.Bold;

        _status = UIFactory.CreateText("Status", Root, string.Empty, 12, Theme.TextDim);
        _status.enableWordWrapping = true;
        _status.rectTransform.Place(14, StatusY - 2, Width - 28, 30);

        var label = UIFactory.CreateText("Saved", Root, "SAVED", 11, Theme.TextDim);
        label.characterSpacing = 4f;
        label.rectTransform.Place(14, SavedY, 200, 20);
        UIFactory.CreateDivider(Root, 12, SavedY + 22, Width - 24);

        _scroll = UIFactory.CreateScrollView("List", Root);
        _scroll.Scroll.GetComponent<RectTransform>().Fill(Inset, ListTop, 8, FooterHeight);
        _empty = UIFactory.CreateText("Empty", _scroll.Content, "Type a name and click Save current to record what you have on now.", 12, Theme.TextDim);
        _empty.rectTransform.Place(10, 4, Width - 40, 30);

        var footer = UIFactory.CreateText("Footer", Root,
            "Records equipped gear, spells, spell passives, and your Bloodcraft class and shift spell (these need Eclipse, like the Stats sheet). " +
            "Applying equips gear from your inventory (anything missing is listed), then spells and passives. Weapon and blood stats follow automatically.",
            11, Theme.TextDim);
        footer.enableWordWrapping = true;
        footer.rectTransform.Place(14, Height - FooterHeight, Width - 28, FooterHeight - 4);

        _prompt = new ConfirmDialog(Root); // last, so it covers everything above
        Rebuild();
    }

    protected override void OnShown()
    {
        _prompt.Hide(); // don't come back to a question asked before the window was closed
        base.OnShown();
    }

    void SaveTyped(string text)
    {
        string name = text?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            _message = "Type a name for the loadout first.";
            return;
        }
        var existing = LoadoutStore.Find(name);
        if (existing == null)
        {
            Save(name);
            return;
        }
        _input.DeactivateInputField();
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        _prompt.Show($"Overwrite {existing.Name}?",
            $"This replaces the saved {existing.Name} ({LoadoutService.Describe(existing)}) with what you have on now. " +
            "The old version can't be recovered.",
            "Overwrite", () => Save(existing.Name));
    }

    void Save(string name)
    {
        bool replacing = LoadoutStore.Find(name) != null;
        LoadoutStore.Put(LoadoutService.Capture(name));
        _message = replacing ? $"Updated {name}." : $"Saved {name}.";
        _input.text = string.Empty;
        _input.DeactivateInputField();
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
    }

    /// <summary>Recreate the rows (after the saved list changes).</summary>
    public void Rebuild()
    {
        foreach (var row in _rows) Object.Destroy(row.gameObject);
        _rows.Clear();
        _applyButtons.Clear();

        var loadouts = LoadoutStore.Loadouts;
        for (int i = 0; i < loadouts.Count; i++) _rows.Add(CreateRow(loadouts[i], i));
        _empty.gameObject.SetActive(loadouts.Count == 0);
        _scroll.Content.sizeDelta = new Vector2(0f, Math.Max(1, loadouts.Count) * RowHeight);
    }

    RectTransform CreateRow(LoadoutStore.Loadout loadout, int index)
    {
        string name = loadout.Name;
        float rowWidth = Width - 30;
        var background = UIFactory.CreatePanel(name, _scroll.Content, index % 2 == 1 ? Theme.RowAlt : new Color(0, 0, 0, 0), raycast: false);
        var rect = background.rectTransform.Place(2, index * RowHeight + 1, rowWidth, RowHeight - 2);

        float textWidth = rowWidth - 12 - ApplyWidth - UpdateWidth - 26 - 24;
        var title = UIFactory.CreateText("Name", rect, name, 14, Theme.Text);
        title.fontStyle = FontStyles.Bold;
        title.overflowMode = TextOverflowModes.Ellipsis;
        title.rectTransform.Place(12, 4, textWidth, 22);
        var summary = UIFactory.CreateText("Summary", rect, LoadoutService.Describe(loadout), 11, Theme.TextDim);
        summary.overflowMode = TextOverflowModes.Ellipsis;
        summary.rectTransform.Place(12, 25, textWidth, 18);

        float x = rowWidth - 6 - 26;
        var delete = UIFactory.CreateButton("Delete", rect, "x", 13, () => _prompt.Show($"Delete {name}?",
            $"This removes the saved {name} ({LoadoutService.Describe(loadout)}). It can't be recovered.",
            "Delete", () => LoadoutStore.Delete(name)), radius: 6f);
        delete.Rect.Place(x, 12, 26, 24);
        delete.SetColors(Theme.Button, Theme.AccentHover);
        x -= 6 + UpdateWidth;
        var update = UIFactory.CreateButton("Update", rect, "Update", 12, () => SaveTyped(name), radius: 6f);
        update.Rect.Place(x, 12, UpdateWidth, 24);
        x -= 6 + ApplyWidth;
        var apply = UIFactory.CreateButton("Apply", rect, "Apply", 13, () => OnApply(name), radius: 6f);
        apply.Rect.Place(x, 12, ApplyWidth, 24);
        apply.SetColors(Theme.Accent, Theme.AccentHover);
        _applyButtons[name] = apply;
        return rect;
    }

    void OnApply(string name)
    {
        var loadout = LoadoutStore.Find(name);
        if (loadout == null) return;

        double now = Time.realtimeSinceStartupAsDouble;
        bool confirmed = string.Equals(_confirming, name, StringComparison.OrdinalIgnoreCase) && now < _confirmUntil;
        if (LoadoutService.NeedsClassChange(loadout) && !confirmed)
        {
            SetConfirming(name, now + ConfirmSeconds);
            _message = $"{name} changes your class to {BloodcraftStats.ClassLabel(loadout.ClassId)}, which costs items " +
                       "(a server setting, 750 by default). Click Confirm to go ahead.";
            return;
        }

        SetConfirming(null, 0);
        _message = null;
        LoadoutService.Apply(loadout, changeClass: confirmed);
    }

    void SetConfirming(string name, double until)
    {
        if (_confirming != null && _applyButtons.TryGetValue(_confirming, out var previous)) previous.Label.text = "Apply";
        _confirming = name;
        _confirmUntil = until;
        if (name != null && _applyButtons.TryGetValue(name, out var button)) button.Label.text = "Confirm";
    }

    public override void Refresh()
    {
        if (!Visible) return;
        if (_confirming != null && Time.realtimeSinceStartupAsDouble >= _confirmUntil)
        {
            SetConfirming(null, 0);
            _message = null;
        }
        string status = _message ?? LoadoutService.Status;
        if (_status.text != status) _status.text = status;
    }
}
