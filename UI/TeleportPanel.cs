using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using Object = UnityEngine.Object;
using DragUI.Services;

namespace DragUI.UI;

// Teleports (ScarletTeleports): auto-accept on/off, the teleport requests waiting for an answer (Accept /
// Deny, for when auto-accept is off or missed one, e.g. the requester was in combat), and the player's
// teleports. Personal ones get Go, a hotkey, Here (move it to where you stand) and remove; empty slots up
// to the server's limit take a name and Set here. The server's global teleports get Go and a hotkey.
// Hotkeys are bound like the familiar ones (click the key button, press a key) and can't take a key a
// familiar or the V Blood list already uses. Pop out opens the teleports as a small window to keep open.
internal sealed class TeleportPanel : Window
{
    const float PanelWidth = 470f;
    const float RowHeight = 30f;
    const float ToggleWidth = 70f;
    const float PendingY = ContentTop + 58f;
    const float PendingTop = PendingY + 26f;
    const float PendingHeight = RowHeight * 3;
    const float ListY = PendingTop + PendingHeight + 12f;
    const float ListTop = ListY + 26f;
    const float StatusHeight = 22f;
    const float FooterHeight = 44f;
    const float GoWidth = 44f, KeyWidth = 78f, HereWidth = 54f, RemoveWidth = 26f, SetWidth = 80f;
    const double NoteSeconds = 5;

    readonly UIFactory.ButtonRef _toggle;
    readonly TextMeshProUGUI _autoInfo;
    readonly TextMeshProUGUI _listHeading;
    readonly TextMeshProUGUI _status;
    readonly UIFactory.ScrollRef _pendingScroll;
    readonly UIFactory.ScrollRef _listScroll;
    readonly TextMeshProUGUI _pendingEmpty;
    readonly ConfirmDialog _prompt;
    readonly List<RectTransform> _pendingRows = new();
    readonly Dictionary<string, TextMeshProUGUI> _pendingTimers = new(StringComparer.OrdinalIgnoreCase);
    readonly List<RectTransform> _listRows = new();
    readonly Dictionary<string, UIFactory.ButtonRef> _keyButtons = new(StringComparer.OrdinalIgnoreCase);
    string _pendingSignature = string.Empty;
    string _capturing; // teleport waiting for a key press
    string _note;      // a hotkey problem, shown instead of the status for a few seconds
    double _noteUntil;

    /// <summary>True while waiting for the player to press a key to bind (game keys are locked meanwhile).</summary>
    public bool Capturing => _capturing != null;

    public override Vector2 SavedPosition
    {
        get => Settings.TeleportPanelPosition;
        set => Settings.TeleportPanelPosition = value;
    }

    /// <param name="togglePopout">Opens / closes the teleport pop-out (quick teleport buttons).</param>
    public TeleportPanel(Transform parent, Action togglePopout)
        : base("Teleports", parent, "Teleport", "ScarletTeleports", PanelWidth, 600f)
    {
        var autoHeading = Heading("AUTO-ACCEPT REQUESTS", ContentTop + 4);
        autoHeading.rectTransform.sizeDelta = new Vector2(Width - 28 - ToggleWidth - 10, 20);
        _toggle = UIFactory.CreateButton("Toggle", Root, "Off", 13, () => AutoTeleport.SetEnabled(!AutoTeleport.Enabled), radius: 7f);
        _toggle.Rect.Place(Width - 12 - ToggleWidth, ContentTop, ToggleWidth, 28);
        _autoInfo = UIFactory.CreateText("AutoInfo", Root, string.Empty, 11, Theme.TextDim);
        _autoInfo.enableWordWrapping = true;
        _autoInfo.rectTransform.Place(14, ContentTop + 30, Width - 28, 26);

        Heading("PENDING REQUESTS", PendingY);
        UIFactory.CreateDivider(Root, 12, PendingY + 22, Width - 24);
        _pendingScroll = UIFactory.CreateScrollView("Pending", Root);
        _pendingScroll.Scroll.GetComponent<RectTransform>().Place(Inset, PendingTop, Width - Inset * 2, PendingHeight);
        _pendingEmpty = UIFactory.CreateText("Empty", _pendingScroll.Content, "No teleport requests waiting.", 12, Theme.TextDim);
        _pendingEmpty.rectTransform.Place(10, 4, Width - 40, RowHeight - 6);

        _listHeading = Heading("MY TELEPORTS", ListY);
        _listHeading.rectTransform.sizeDelta = new Vector2(Width - 28 - 176, 20);
        var refresh = UIFactory.CreateButton("Refresh", Root, "Refresh", 12, TeleportStore.Refresh, radius: 6f);
        refresh.Rect.Place(Width - 12 - 80, ListY - 3, 80, 22);
        var popout = UIFactory.CreateButton("PopOut", Root, "Pop out", 12, togglePopout, radius: 6f);
        popout.Rect.Place(Width - 12 - 80 - 6 - 80, ListY - 3, 80, 22);
        UIFactory.CreateDivider(Root, 12, ListY + 22, Width - 24);
        _listScroll = UIFactory.CreateScrollView("List", Root);
        _listScroll.Scroll.GetComponent<RectTransform>().Fill(Inset, ListTop, 8, FooterHeight + StatusHeight);

        _status = UIFactory.CreateText("Status", Root, string.Empty, 12, Theme.TextDim);
        _status.overflowMode = TextOverflowModes.Ellipsis;
        _status.rectTransform.Place(14, Height - FooterHeight - StatusHeight, Width - 28, StatusHeight);

        var footer = UIFactory.CreateText("Footer", Root,
            "Uses ScarletTeleports' .stp commands (tpa / tpd, tp, stp, rtp, ltp). Here moves a teleport to where you stand. " +
            "Hotkeys: click a key button and press a key (Esc cancels, Backspace clears).",
            11, Theme.TextDim);
        footer.enableWordWrapping = true;
        footer.rectTransform.Place(14, Height - FooterHeight, Width - 28, FooterHeight - 4);

        _prompt = new ConfirmDialog(Root);
        RebuildList();
    }

    TextMeshProUGUI Heading(string text, float y)
    {
        var heading = UIFactory.CreateText(text, Root, text, 11, Theme.TextDim);
        heading.characterSpacing = 3f;
        heading.overflowMode = TextOverflowModes.Ellipsis;
        heading.rectTransform.Place(14, y, Width - 28, 20);
        return heading;
    }

    protected override void OnShown()
    {
        _prompt.Hide();
        TeleportStore.Refresh();
        base.OnShown();
    }

    // ---------------------------------------------------------------- pending requests

    void RebuildPending(IReadOnlyList<AutoTeleport.Request> requests)
    {
        foreach (var row in _pendingRows) Object.Destroy(row.gameObject);
        _pendingRows.Clear();
        _pendingTimers.Clear();

        float rowWidth = Width - Inset * 2 - 14;
        for (int i = 0; i < requests.Count; i++)
        {
            string name = requests[i].Name;
            var background = UIFactory.CreatePanel(name, _pendingScroll.Content, i % 2 == 1 ? Theme.RowAlt : new Color(0, 0, 0, 0), raycast: false);
            var rect = background.rectTransform.Place(2, i * RowHeight, rowWidth, RowHeight);

            var label = UIFactory.CreateText("Name", rect, name, 13, Theme.Text);
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.rectTransform.Place(10, 0, rowWidth - 210, RowHeight);
            var timer = UIFactory.CreateText("Timer", rect, string.Empty, 12, Theme.Respawning, TextAlignmentOptions.MidlineRight);
            timer.rectTransform.Place(rowWidth - 196, 0, 40, RowHeight);
            _pendingTimers[name] = timer;

            var deny = UIFactory.CreateButton("Deny", rect, "Deny", 12, () => AutoTeleport.Deny(name), radius: 6f);
            deny.Rect.Place(rowWidth - 6 - 64, 4, 64, RowHeight - 8);
            var accept = UIFactory.CreateButton("Accept", rect, "Accept", 12, () => AutoTeleport.AcceptManually(name), radius: 6f);
            accept.Rect.Place(rowWidth - 6 - 64 - 6 - 76, 4, 76, RowHeight - 8);
            accept.SetColors(Theme.Accent, Theme.AccentHover);
            _pendingRows.Add(rect);
        }
        _pendingEmpty.gameObject.SetActive(requests.Count == 0);
        _pendingScroll.Content.sizeDelta = new Vector2(0f, Math.Max(1, requests.Count) * RowHeight);
    }

    // ---------------------------------------------------------------- teleports

    /// <summary>Recreate the teleport rows (after the list, limit or hotkeys change).</summary>
    public void RebuildList()
    {
        foreach (var row in _listRows) Object.Destroy(row.gameObject);
        _listRows.Clear();
        _keyButtons.Clear();

        float rowWidth = Width - 30;
        int index = 0;
        var personal = TeleportStore.Personal;
        int slots = Math.Max(TeleportStore.SlotCount, personal.Count);
        for (int i = 0; i < slots; i++, index++)
            _listRows.Add(i < personal.Count ? PersonalRow(personal[i], index, rowWidth) : EmptySlotRow(index, rowWidth));

        if (TeleportStore.Global.Count > 0)
        {
            var header = UIFactory.CreateText("ServerHeading", _listScroll.Content, "SERVER TELEPORTS", 11, Theme.TextDim);
            header.characterSpacing = 3f;
            header.rectTransform.Place(10, index * RowHeight + 8, rowWidth, RowHeight - 8);
            _listRows.Add(header.rectTransform);
            index++;
            foreach (var name in TeleportStore.Global) _listRows.Add(GlobalRow(name, index++, rowWidth));
        }
        _listScroll.Content.sizeDelta = new Vector2(0f, Math.Max(1, index) * RowHeight);

        string limit = TeleportStore.KnownMax is { } max ? $"{personal.Count} of {max}" : $"{personal.Count}, server limit not known yet";
        _listHeading.text = TeleportStore.Listed ? $"MY TELEPORTS  ·  {limit}" : "MY TELEPORTS  ·  reading...";
        if (_capturing != null && !_keyButtons.ContainsKey(_capturing)) _capturing = null;
        foreach (var name in _keyButtons.Keys) StyleKey(name);
    }

    RectTransform RowBackground(string name, int index, float rowWidth)
    {
        var background = UIFactory.CreatePanel(name, _listScroll.Content, index % 2 == 1 ? Theme.RowAlt : new Color(0, 0, 0, 0), raycast: false);
        return background.rectTransform.Place(2, index * RowHeight, rowWidth, RowHeight);
    }

    void NameLabel(RectTransform rect, string text, float width, Color color)
    {
        var label = UIFactory.CreateText("Name", rect, text, 13, color);
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.rectTransform.Place(10, 0, width, RowHeight);
    }

    RectTransform PersonalRow(string name, int index, float rowWidth)
    {
        var rect = RowBackground(name, index, rowWidth);
        float x = rowWidth - 6 - RemoveWidth;
        var remove = UIFactory.CreateButton("Remove", rect, "x", 13, () => _prompt.Show($"Remove {name}?",
            $"This deletes your {name} teleport on the server (.stp rtp). Its slot frees up for a new one.",
            "Remove", () => TeleportStore.Remove(name)), radius: 6f);
        remove.Rect.Place(x, 4, RemoveWidth, RowHeight - 8);
        remove.SetColors(Theme.Button, Theme.AccentHover);
        x -= 6 + HereWidth;
        var here = UIFactory.CreateButton("Here", rect, "Here", 12, () => _prompt.Show($"Move {name} here?",
            $"This moves your {name} teleport to where you're standing now. Its old spot is lost.",
            "Move here", () => TeleportStore.ResetHere(name)), radius: 6f);
        here.Rect.Place(x, 4, HereWidth, RowHeight - 8);
        x = KeyAndGo(rect, name, x);
        NameLabel(rect, name, x - 16, Theme.Text);
        return rect;
    }

    RectTransform GlobalRow(string name, int index, float rowWidth)
    {
        var rect = RowBackground(name, index, rowWidth);
        float x = KeyAndGo(rect, name, rowWidth);
        NameLabel(rect, name, x - 16, Theme.Text);
        return rect;
    }

    // The hotkey and Go buttons, placed right to left ending at x; returns where they start.
    float KeyAndGo(RectTransform rect, string name, float x)
    {
        x -= 6 + KeyWidth;
        var key = UIFactory.CreateButton("Hotkey", rect, string.Empty, 11, () => ToggleCapture(name), radius: 6f);
        key.Rect.Place(x, 4, KeyWidth, RowHeight - 8);
        _keyButtons[name] = key;
        x -= 6 + GoWidth;
        var go = UIFactory.CreateButton("Go", rect, "Go", 12, () => TeleportStore.Teleport(name), radius: 6f);
        go.Rect.Place(x, 4, GoWidth, RowHeight - 8);
        go.SetColors(Theme.Accent, Theme.AccentHover);
        return x;
    }

    RectTransform EmptySlotRow(int index, float rowWidth)
    {
        var rect = RowBackground("Empty slot", index, rowWidth);
        TMP_InputField input = null;
        input = UIFactory.CreateInputField("Name", rect, "Empty slot: name a teleport...", 12, text => SetHere(input), characterLimit: 24);
        input.GetComponent<RectTransform>().Place(8, 3, rowWidth - 8 - SetWidth - 12, RowHeight - 6);
        var set = UIFactory.CreateButton("Set", rect, "Set here", 12, () => SetHere(input), radius: 6f);
        set.Rect.Place(rowWidth - 6 - SetWidth, 4, SetWidth, RowHeight - 8);
        return rect;
    }

    void SetHere(TMP_InputField input)
    {
        if (!TeleportStore.Set(input.text)) return;
        input.text = string.Empty;
        input.DeactivateInputField();
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
    }

    // ---------------------------------------------------------------- hotkeys

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
        if (string.Equals(_capturing, name, StringComparison.OrdinalIgnoreCase))
        {
            button.Label.text = "Press a key";
            button.Label.color = Theme.Text;
            button.SetColors(Theme.Accent, Theme.AccentHover);
            return;
        }
        var hotkey = TeleportStore.GetHotkey(name);
        button.Label.text = hotkey.IsNone ? "+ key" : hotkey.ToString();
        button.Label.color = hotkey.IsNone ? Theme.TextDim : Theme.Text;
        button.SetColors(Theme.Button, Theme.ButtonHover);
    }

    /// <summary>Per frame: finish a pending key capture. Runs after the hotkeys are polled, so the key
    /// that's being bound doesn't also teleport.</summary>
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
            TeleportStore.SetHotkey(name, Hotkey.None);
        }
        else if (Hotkey.TryCapture(out var hotkey))
        {
            string name = _capturing;
            StopCapture();
            string problem = TeleportStore.SetHotkey(name, hotkey);
            if (problem != null)
            {
                _note = problem;
                _noteUntil = Time.realtimeSinceStartupAsDouble + NoteSeconds;
            }
        }
    }

    public override void Refresh()
    {
        if (!Visible) return;
        bool on = AutoTeleport.Enabled;
        string style = on ? "on" : "off";
        if (_toggle.Style != style)
        {
            _toggle.Style = style;
            _toggle.Label.text = on ? "On" : "Off";
            if (on) _toggle.SetColors(Theme.Accent, Theme.AccentHover);
            else _toggle.SetColors(Theme.Button, Theme.ButtonHover);
        }
        string only = Settings.AutoTeleportOnlyFrom;
        _autoInfo.text = (on ? "Requests are accepted as they arrive" : "Off: requests wait below (and in the control bar's notification slot)")
                         + (on && !string.IsNullOrWhiteSpace(only) ? $", only from {only}." : ".");

        var requests = AutoTeleport.Requests;
        string signature = string.Join("|", requests.Select(r => r.Name));
        if (signature != _pendingSignature)
        {
            _pendingSignature = signature;
            RebuildPending(requests);
        }
        foreach (var request in requests)
            if (_pendingTimers.TryGetValue(request.Name, out var timer)) timer.text = $"{(int)request.SecondsLeft}s";

        bool noted = _note != null && Time.realtimeSinceStartupAsDouble < _noteUntil;
        _status.text = noted ? _note : TeleportStore.Listing ? "Reading your teleports..." : TeleportStore.Status;
        _status.color = noted ? Theme.Due : Theme.TextDim;
    }
}
