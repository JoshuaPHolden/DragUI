using System.Text.RegularExpressions;
using ProjectM.Network;
using UnityEngine;

namespace DragUI.Services;

// Every familiar the player has unlocked, across all Bloodcraft boxes. Bloodcraft only lists one box
// at a time, so a scan runs its commands in turn and reads the replies (hidden from chat while it runs):
//   .fam l             "<box>:" then "<n>| <name>[*] [level][prestige]"  (also tells which box is active)
//   .fam boxes         "Familiar Boxes:" then the box names, comma-separated, a few per line
//   .fam cb "<box>"    "Box Selected - <box>", then .fam l for that box
// and then selects the box that was active before. Deleting is .fam r <n> in the familiar's box: it
// lists that box again first and only removes if the familiar is still at a number, since a stale
// number would permanently delete a different one.
internal static class FamiliarCatalog
{
    const double QuietSeconds = 0.4;   // a reply is complete once nothing more arrives for this long
    const double TimeoutSeconds = 4;

    public sealed class Familiar
    {
        public string Name;
        public string Box;
        public int Number; // 1-based position in its box (what .fam r takes)
        public int Level;
        public int Prestige;
        public bool Shiny;
    }

    sealed class Step
    {
        public string Command;
        public Action Done;
    }

    static readonly Regex _richText = new("<[^>]*>", RegexOptions.Compiled);
    static readonly Regex _listHeader = new(@"^<color=white>(?<box>[^<]*)</color>:$", RegexOptions.Compiled);
    static readonly Regex _listEntry = new(
        @"^<color=yellow>(?<n>\d+)</color>\| <color=green>(?<name>.*?)</color>(?<shiny><color=[^>]*>\*</color>)? \[<color=white>(?<level>\d+)</color>\](?:\[<color=#90EE90>(?<prestige>\d+)</color>\])?$",
        RegexOptions.Compiled);
    static readonly Regex _boxBatch = new(@"^<color=white>[^<]*</color>(?:, <color=white>[^<]*</color>)*$", RegexOptions.Compiled);
    static readonly Regex _boxSelected = new(@"^Box Selected - <color=white>(?<box>[^<]*)</color>$", RegexOptions.Compiled);
    static readonly Regex _removed = new(@"^<color=green>(?<name>.*?)</color> removed from <color=white>(?<box>[^<]*)</color>\.$", RegexOptions.Compiled);

    static readonly string[] _otherReplies =
    {
        "Couldn't find active box!", "Couldn't find box!", "You don't have any unlocks yet!", "Familiars are not enabled.",
        "Couldn't find active familiar box to remove from...", "Invalid choice, please use",
    };

    static readonly List<Familiar> _all = new();
    static readonly Queue<Step> _steps = new();
    static Step _current;
    static double _sentAt, _lastReplyAt;
    static bool _gotReply;

    // Filled from the replies to the step that's running.
    static readonly List<string> _boxes = new();
    static bool _boxesHeaderSeen;
    static string _listedBox;
    static readonly List<Familiar> _listed = new();
    static string _activeBox;      // box selected before the scan/delete started
    static string _removedName;

    public static IReadOnlyList<Familiar> All => _all;
    public static bool Busy => _current != null || _steps.Count > 0;
    /// <summary>True once a scan has finished this session.</summary>
    public static bool Scanned { get; private set; }
    public static string Status { get; private set; } = string.Empty;

    /// <summary>Raised (on the main thread) when the list changes.</summary>
    public static event Action Changed;

    /// <summary>List every box and combine them. False if a scan or delete is already running.</summary>
    public static bool Scan()
    {
        if (Busy || !Core.InGame) return false;
        _all.Clear();
        _activeBox = null;
        Status = "Listing boxes...";
        Changed?.Invoke();

        Enqueue(".fam l", () => _activeBox = _listedBox);
        Enqueue(".fam boxes", () =>
        {
            var boxes = _boxes.ToList();
            if (boxes.Count == 0)
            {
                Finish("No familiar boxes found.");
                return;
            }
            for (int i = 0; i < boxes.Count; i++)
            {
                string box = boxes[i];
                int index = i + 1;
                Enqueue($".fam cb \"{box}\"", () => Status = $"Reading box {index} of {boxes.Count}: {box}...");
                Enqueue(".fam l", () =>
                {
                    if (_listedBox != box) return; // selecting it failed
                    _all.AddRange(_listed);
                    Changed?.Invoke();
                });
            }
            RestoreActiveBox(() =>
            {
                Scanned = true;
                Finish($"{_all.Count} familiars in {boxes.Count} boxes.");
            });
        });
        return true;
    }

    /// <summary>Permanently remove <paramref name="familiar"/> from its box (.fam r).</summary>
    public static bool Delete(Familiar familiar)
    {
        if (Busy || !Core.InGame) return false;
        Status = $"Deleting {familiar.Name}...";
        _activeBox = null;

        Enqueue(".fam l", () => _activeBox = _listedBox);
        Enqueue($".fam cb \"{familiar.Box}\"", null);
        Enqueue(".fam l", () =>
        {
            // Find it again by name: the numbers shift whenever the box changes.
            var match = _listedBox == familiar.Box
                ? _listed.FirstOrDefault(f => f.Number == familiar.Number && f.Name == familiar.Name)
                  ?? _listed.FirstOrDefault(f => f.Name == familiar.Name && f.Level == familiar.Level)
                : null;
            if (match == null)
            {
                RestoreActiveBox(() => Finish($"Didn't delete {familiar.Name}: it's not in {familiar.Box} any more. List All again."));
                return;
            }
            _removedName = null;
            Enqueue($".fam r {match.Number}", () =>
            {
                if (_removedName == familiar.Name)
                {
                    _all.Remove(familiar);
                    foreach (var other in _all)
                        if (other.Box == familiar.Box && other.Number > match.Number) other.Number--;
                }
                RestoreActiveBox(() => Finish(_removedName == familiar.Name
                    ? $"Deleted {familiar.Name} from {familiar.Box}."
                    : $"Bloodcraft didn't confirm deleting {familiar.Name}."));
            });
        });
        return true;
    }

    static void RestoreActiveBox(Action then)
    {
        if (!string.IsNullOrEmpty(_activeBox)) Enqueue($".fam cb \"{_activeBox}\"", then);
        else then();
    }

    static void Finish(string status)
    {
        Status = status;
        Changed?.Invoke();
    }

    static void Enqueue(string command, Action done) => _steps.Enqueue(new Step { Command = command, Done = done });

    /// <summary>Per frame while in game: finish the running step once its reply is complete, then send the next.</summary>
    public static void Tick()
    {
        double now = Time.realtimeSinceStartupAsDouble;
        if (_current != null)
        {
            bool complete = _gotReply ? now - _lastReplyAt >= QuietSeconds : now - _sentAt >= TimeoutSeconds;
            if (!complete) return;
            var step = _current;
            _current = null;
            try
            {
                step.Done?.Invoke();
            }
            catch (Exception ex)
            {
                Core.Log.LogWarning($"Familiar list step failed: {ex.Message}");
            }
        }

        if (_current != null || _steps.Count == 0) return;
        _current = _steps.Dequeue();
        _boxes.Clear();
        _boxesHeaderSeen = false;
        _listedBox = null;
        _listed.Clear();
        _gotReply = false;
        _sentAt = now;
        if (!ChatCommandSender.TrySend(_current.Command))
        {
            _steps.Clear();
            _current = null;
            Finish("Couldn't send the command (not in game?).");
        }
    }

    /// <summary>Read Bloodcraft's replies while a step runs. True = ours, hide it from chat.</summary>
    public static bool OnServerMessage(ServerChatMessageType type, string message)
    {
        if (_current == null || type != ServerChatMessageType.System || string.IsNullOrEmpty(message)) return false;
        if (!Read(message.Trim())) return false;
        _gotReply = true;
        _lastReplyAt = Time.realtimeSinceStartupAsDouble;
        return true;
    }

    static bool Read(string raw)
    {
        Match match;
        if ((match = _listEntry.Match(raw)).Success)
        {
            _listed.Add(new Familiar
            {
                Name = _richText.Replace(match.Groups["name"].Value, string.Empty).Trim(),
                Box = _listedBox,
                Number = int.Parse(match.Groups["n"].Value),
                Level = int.Parse(match.Groups["level"].Value),
                Prestige = match.Groups["prestige"].Success ? int.Parse(match.Groups["prestige"].Value) : 0,
                Shiny = match.Groups["shiny"].Success,
            });
            return true;
        }
        if ((match = _listHeader.Match(raw)).Success)
        {
            _listedBox = match.Groups["box"].Value;
            return true;
        }
        if (raw == "Familiar Boxes:")
        {
            _boxesHeaderSeen = true;
            return true;
        }
        if (_boxesHeaderSeen && _boxBatch.IsMatch(raw))
        {
            foreach (var box in _richText.Replace(raw, string.Empty).Split(", "))
                if (box.Length > 0) _boxes.Add(box);
            return true;
        }
        if (_boxSelected.IsMatch(raw)) return true;
        if ((match = _removed.Match(raw)).Success)
        {
            _removedName = _richText.Replace(match.Groups["name"].Value, string.Empty).Trim();
            return true;
        }
        return _otherReplies.Any(r => raw.StartsWith(r, StringComparison.Ordinal));
    }

    public static void OnWorldTeardown()
    {
        _steps.Clear();
        _current = null;
        _all.Clear();
        Scanned = false;
        Status = string.Empty;
    }
}
