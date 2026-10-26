using TMPro;
using UnityEngine;
using DragUI.Services;

namespace DragUI.UI;

// Server Settings -> Noctveil: things specific to the Noctveil server. For now its Apex / Prime boss
// alarm: on/off, a test chime, the next boss and the schedule (next one highlighted). The alarm's
// teleport button goes into the control bar's notification slot.
internal sealed class NoctveilPanel : Window
{
    const float PanelWidth = 420f;
    const float RowHeight = 26f;
    const float ToggleWidth = 70f;
    const float TestWidth = 96f;
    const float StatusY = ContentTop + 32f;
    const float InfoY = StatusY + 24f;
    const float ScheduleY = InfoY + 40f;
    const float ListTop = ScheduleY + 26f;
    const float FooterHeight = 44f;

    sealed class Row
    {
        public BossAlarm.ScheduledEvent Event;
        public TextMeshProUGUI Time;
        public TextMeshProUGUI Label;
    }

    readonly UIFactory.ButtonRef _toggle;
    readonly TextMeshProUGUI _status;
    readonly List<Row> _rows = new();

    public override Vector2 SavedPosition
    {
        get => Settings.NoctveilPanelPosition;
        set => Settings.NoctveilPanelPosition = value;
    }

    public NoctveilPanel(Transform parent)
        : base("Noctveil", parent, "Noctveil", "server settings", PanelWidth, 520f)
    {
        var heading = UIFactory.CreateText("Heading", Root, "APEX / PRIME BOSS ALARM", 11, Theme.TextDim);
        heading.characterSpacing = 4f;
        heading.rectTransform.Place(14, ContentTop + 4, Width - 28 - ToggleWidth - TestWidth - 16, 20);

        _toggle = UIFactory.CreateButton("Toggle", Root, "Off", 13, () => BossAlarm.SetEnabled(!BossAlarm.Enabled), radius: 7f);
        _toggle.Rect.Place(Width - 12 - ToggleWidth, ContentTop, ToggleWidth, 28);
        var test = UIFactory.CreateButton("Test", Root, "Test sound", 12, AlarmSound.Play, radius: 7f);
        test.Rect.Place(Width - 12 - ToggleWidth - 8 - TestWidth, ContentTop, TestWidth, 28);

        _status = UIFactory.CreateText("Status", Root, string.Empty, 13, Theme.Text);
        _status.rectTransform.Place(14, StatusY, Width - 28, 22);

        var info = UIFactory.CreateText("Info", Root,
            $"Chimes {Settings.AlarmLeadMinutes:0.#} min before each spawn and puts a teleport button in the control bar's " +
            $"notification slot, until {Settings.AlarmButtonMinutes:0.#} min after the spawn.",
            11, Theme.TextDim);
        info.enableWordWrapping = true;
        info.rectTransform.Place(14, InfoY, Width - 28, 36);

        var schedule = UIFactory.CreateText("Schedule", Root, $"SCHEDULE  ·  {BossAlarm.TimeZoneName}", 11, Theme.TextDim);
        schedule.characterSpacing = 2f;
        schedule.overflowMode = TextOverflowModes.Ellipsis;
        schedule.rectTransform.Place(14, ScheduleY, Width - 28, 20);
        UIFactory.CreateDivider(Root, 12, ScheduleY + 22, Width - 24);

        var scroll = UIFactory.CreateScrollView("List", Root);
        scroll.Scroll.GetComponent<RectTransform>().Fill(Inset, ListTop, 8, FooterHeight);
        var events = BossAlarm.Schedule.OrderBy(e => e.TimeOfDay).ToList();
        for (int i = 0; i < events.Count; i++) _rows.Add(CreateRow(scroll.Content, events[i], i));
        scroll.Content.sizeDelta = new Vector2(0f, Math.Max(1, events.Count) * RowHeight);
        if (events.Count == 0)
        {
            var empty = UIFactory.CreateText("Empty", scroll.Content, "No bosses in the schedule file.", 12, Theme.TextDim);
            empty.rectTransform.Place(10, 4, Width - 40, RowHeight);
        }

        var footer = UIFactory.CreateText("Footer", Root,
            $"Schedule: BepInEx/config/DragUI/{Path.GetFileName(BossAlarm.SchedulePath)} (edit while the game is closed). " +
            "Volume, lead time and time zone: the BossAlarm section of the .cfg.",
            11, Theme.TextDim);
        footer.enableWordWrapping = true;
        footer.rectTransform.Place(14, Height - FooterHeight, Width - 28, FooterHeight - 4);
    }

    Row CreateRow(Transform parent, BossAlarm.ScheduledEvent e, int index)
    {
        var background = UIFactory.CreatePanel(e.Label, parent, index % 2 == 1 ? Theme.RowAlt : new Color(0, 0, 0, 0), raycast: false);
        var rect = background.rectTransform.Place(2, index * RowHeight, Width - 30, RowHeight);

        var time = UIFactory.CreateText("Time", rect, $"{(int)e.TimeOfDay.TotalHours:00}:{e.TimeOfDay.Minutes:00}", 13, Theme.TextDim);
        time.rectTransform.Place(10, 0, 52, RowHeight);
        var label = UIFactory.CreateText("Label", rect, e.Label, 13, Theme.Text);
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.rectTransform.Place(66, 0, 190, RowHeight);
        var command = UIFactory.CreateText("Command", rect, e.Command, 11, Theme.TextDim, TextAlignmentOptions.MidlineRight);
        command.overflowMode = TextOverflowModes.Ellipsis;
        command.rectTransform.Place(260, 0, Width - 30 - 270, RowHeight);
        return new Row { Event = e, Time = time, Label = label };
    }

    public override void Refresh()
    {
        if (!Visible) return;
        bool on = BossAlarm.Enabled;
        string style = on ? "on" : "off";
        if (_toggle.Style != style)
        {
            _toggle.Style = style;
            _toggle.Label.text = on ? "On" : "Off";
            if (on) _toggle.SetColors(Theme.Accent, Theme.AccentHover);
            else _toggle.SetColors(Theme.Button, Theme.ButtonHover);
        }
        _status.text = BossAlarm.StatusText();

        var next = on ? BossAlarm.Next?.Event : null;
        foreach (var row in _rows)
        {
            bool isNext = row.Event == next;
            row.Label.color = isNext ? Theme.AccentHover : Theme.Text;
            row.Time.color = isNext ? Theme.AccentHover : Theme.TextDim;
        }
    }
}
