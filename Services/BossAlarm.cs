using System.Globalization;
using BepInEx;

namespace DragUI.Services;

// Server-specific boss alarm (Noctveil's Apex/Prime bosses by default; set up in Server Settings ->
// Noctveil): AlarmLeadMinutes before each scheduled spawn a soft chime plays (time to get out of combat)
// and a teleport notice for that boss goes into the control bar's notification slot, flashing at first
// and staying until ButtonMinutes after the spawn (unless a newer notice replaces it). The schedule is a text file so it can be fitted to
// any server; each line is "time | label | command", e.g. "23:00 | Apex Judgement | .stp tp apex".
// Times are wall-clock in Settings.AlarmTimeZone (empty = this PC's time zone).
internal static class BossAlarm
{
    public sealed record ScheduledEvent(TimeSpan TimeOfDay, string Label, string Command);

    /// <summary>One occurrence of a scheduled event; At is the spawn time.</summary>
    public readonly record struct Occurrence(ScheduledEvent Event, DateTime At);

    const string NoticeSource = "BossAlarm";
    const double CheckIntervalSeconds = 1;
    const double TeleportCooldownSeconds = 2;

    static readonly string _directory = Plugin.DataDirectory;
    static readonly string _path = Path.Combine(_directory, "boss-schedule.txt");

    const string DefaultSchedule = """
        # Boss alarm schedule: one boss per line as  spawn time | button label | chat command  (# starts a comment).
        # The alarm sounds BossAlarm.LeadMinutes (default 2) before each spawn time.
        # Times are 24-hour or 12-hour ("23:00" or "11:00 PM") in your PC's time zone, unless
        # BossAlarm.TimeZone is set in the .cfg. Edit this file while the game is closed.
        23:00 | Apex Judgement    | .stp tp apex
        01:00 | Prime Colossus    | .stp tp prime
        03:00 | Apex Desecrator   | .stp tp apex
        05:00 | Prime Cataclysm   | .stp tp prime
        07:00 | Apex Nightwing    | .stp tp apex
        09:00 | Prime Harbinger   | .stp tp prime
        11:00 | Apex Deathrose    | .stp tp apex
        13:00 | Prime Juggernaut  | .stp tp prime
        15:00 | Apex Worldbreaker | .stp tp apex
        17:00 | Prime Punisher    | .stp tp prime
        19:00 | Apex Sentinel     | .stp tp apex
        21:00 | Prime Vanguard    | .stp tp prime
        """;

    static readonly string[] _timeFormats = { "H:mm", "HH:mm", "h:mm tt", "h:mmtt", "h tt", "htt" };

    static readonly List<ScheduledEvent> _schedule = new();
    static TimeZoneInfo _timeZone = TimeZoneInfo.Local;
    static string _timeZoneSource;
    static DateTime _lastChecked;     // schedule-zone time of the last check (alarms fire for events after it)
    static double _nextCheckAt;
    static double _lastTeleportAt = double.NegativeInfinity;
    static Occurrence? _noticeFor;    // occurrence a notice was last posted for (posted once each)

    /// <summary>The boss whose button is showing (from its alarm until ButtonMinutes after it spawns), if any.</summary>
    public static Occurrence? Active { get; private set; }
    /// <summary>True during the first FlashMinutes after the active boss's alarm.</summary>
    public static bool Flashing { get; private set; }
    public static Occurrence? Next { get; private set; }
    public static IReadOnlyList<ScheduledEvent> Schedule => _schedule;
    /// <summary>Where the schedule is read from.</summary>
    public static string SchedulePath => _path;
    /// <summary>The time zone the schedule's times are in.</summary>
    public static string TimeZoneName { get { Now(); return _timeZone.StandardName; } }

    public static bool Enabled => Settings.AlarmEnabled;

    public static void SetEnabled(bool enabled)
    {
        Settings.AlarmEnabled = enabled;
        _lastChecked = default; // don't fire for times that passed while it was off
        Update(force: true);
        if (enabled) AlarmSound.Play(); // so the volume can be checked
        else AlarmSound.Stop();
    }

    public static void Load()
    {
        _schedule.Clear();
        try
        {
            if (!File.Exists(_path))
            {
                Directory.CreateDirectory(_directory);
                File.WriteAllText(_path, DefaultSchedule + Environment.NewLine);
            }

            int lineNumber = 0;
            foreach (var raw in File.ReadAllLines(_path))
            {
                lineNumber++;
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;
                var parts = line.Split('|', StringSplitOptions.TrimEntries);
                if (parts.Length != 3 || parts[1].Length == 0 || !parts[2].StartsWith('.')
                    || !DateTime.TryParseExact(parts[0], _timeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
                {
                    Core.Log.LogWarning($"{_path} line {lineNumber}: expected 'time | label | .command', skipped: {raw}");
                    continue;
                }
                _schedule.Add(new ScheduledEvent(time.TimeOfDay, parts[1], parts[2]));
            }
            Core.Log.LogInfo($"Boss alarm: {_schedule.Count} scheduled bosses loaded.");
        }
        catch (Exception ex)
        {
            Core.Log.LogWarning($"Couldn't read {_path}: {ex.Message}");
        }
    }

    /// <summary>Teleport to the active event's boss.</summary>
    public static void TeleportToActive()
    {
        if (Active is not { } active) return;
        double now = UnityEngine.Time.realtimeSinceStartupAsDouble;
        if (now - _lastTeleportAt < TeleportCooldownSeconds) return;
        if (ChatCommandSender.TrySend(active.Event.Command)) _lastTeleportAt = now;
    }

    public static void Tick() => Update(force: false);

    static void Update(bool force)
    {
        double realNow = UnityEngine.Time.realtimeSinceStartupAsDouble;
        if (!force && realNow < _nextCheckAt) return;
        _nextCheckAt = realNow + CheckIntervalSeconds;

        if (!Enabled || _schedule.Count == 0 || !Core.InGame)
        {
            Active = null;
            Next = null;
            Flashing = false;
            _lastChecked = default;
            _noticeFor = null;
            Notifications.Clear(NoticeSource);
            return;
        }

        DateTime now = Now();
        TimeSpan lead = TimeSpan.FromMinutes(Settings.AlarmLeadMinutes);
        DateTime alarmHorizon = now + lead; // a spawn's alarm has gone off once the spawn is within the lead
        Occurrence? latest = null, next = null;
        foreach (var e in _schedule)
        {
            DateTime today = now.Date + e.TimeOfDay;
            DateTime alarmed = today <= alarmHorizon ? today : today.AddDays(-1); // latest spawn already alarmed
            if (alarmed.AddDays(1) <= alarmHorizon) alarmed = alarmed.AddDays(1); // the lead reaches past midnight
            DateTime upcoming = today > now ? today : today.AddDays(1);
            if (latest is null || alarmed > latest.Value.At) latest = new Occurrence(e, alarmed);
            if (next is null || upcoming < next.Value.At) next = new Occurrence(e, upcoming);

            // Sound once when the clock passes an alarm (not for ones that passed before we were watching).
            DateTime alarmAt = alarmed - lead;
            if (_lastChecked != default && alarmAt > _lastChecked && alarmAt <= now)
            {
                Core.Log.LogInfo($"Boss alarm: {e.Label} spawns at {alarmed:HH:mm}.");
                AlarmSound.Play();
            }
        }
        _lastChecked = now;
        Next = next;

        double sinceAlarm = latest is { } l ? (now - (l.At - lead)).TotalMinutes : double.MaxValue;
        Active = sinceAlarm < lead.TotalMinutes + Settings.AlarmButtonMinutes ? latest : null;
        Flashing = Active != null && sinceAlarm < Settings.AlarmFlashMinutes;
        if (Active is { } active && !Equals(_noticeFor, active)) PostNotice(active);
    }

    // Once per boss: when its alarm goes off, or at login while its button window is still open.
    static void PostNotice(Occurrence occurrence)
    {
        _noticeFor = occurrence;
        Notifications.Post(new Notifications.Notice
        {
            Source = NoticeSource,
            GetText = TeleportLabel,
            OnClick = TeleportToActive,
            IsFlashing = () => Flashing && Equals(Active, occurrence),
            IsOver = () => !Equals(Active, occurrence),
        });
    }

    /// <summary>Notice text: counts down to the spawn while the alarm's lead time runs.</summary>
    public static string TeleportLabel()
    {
        if (Active is not { } active) return "Teleport";
        var wait = Until(active);
        return wait > TimeSpan.Zero
            ? $"TP {active.Event.Label} {(int)wait.TotalMinutes}:{wait.Seconds:00}"
            : $"TP {active.Event.Label}";
    }

    /// <summary>"Next: Apex Judgement at 23:00 (in 42m)", or why there's none.</summary>
    public static string StatusText()
    {
        if (!Enabled) return "Alarm off.";
        if (_schedule.Count == 0) return $"No bosses in {Path.GetFileName(_path)}.";
        if (Next is not { } next) return "Alarm on.";
        return $"Next: {next.Event.Label} at {next.At:HH:mm} (in {FormatWait(Until(next))})";
    }

    public static string FormatWait(TimeSpan wait) =>
        wait.TotalMinutes < 1 ? "<1m" : wait.TotalHours >= 1 ? $"{(int)wait.TotalHours}h{wait.Minutes:00}" : $"{(int)wait.TotalMinutes}m";

    /// <summary>Time left until an occurrence.</summary>
    public static TimeSpan Until(Occurrence occurrence) => occurrence.At - Now();

    static DateTime Now()
    {
        string zone = Settings.AlarmTimeZone;
        if (zone != _timeZoneSource)
        {
            _timeZoneSource = zone;
            _timeZone = TimeZoneInfo.Local;
            if (!string.IsNullOrWhiteSpace(zone))
            {
                try { _timeZone = TimeZoneInfo.FindSystemTimeZoneById(zone.Trim()); }
                catch (Exception ex) { Core.Log.LogWarning($"BossAlarm.TimeZone '{zone}' not found ({ex.Message}); using this PC's time zone."); }
            }
        }
        return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _timeZone);
    }

    public static void OnWorldTeardown()
    {
        Active = null;
        Next = null;
        Flashing = false;
        _lastChecked = default;
        _noticeFor = null;
    }
}
