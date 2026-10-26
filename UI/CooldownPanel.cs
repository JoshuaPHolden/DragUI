using DragUI.Services;
using TMPro;
using UnityEngine;

namespace DragUI.UI;

// Cooldown tester: use skills and see their real cooldowns next to the base, plus the bonus that result
// implies (as a recovery rate and as a straight reduction) against the bonus the client shows. Tracking
// runs while the window is open.
internal sealed class CooldownPanel : Window
{
    const float PanelWidth = 720f;
    const float RowHeight = 24f;
    const int Rows = 12;
    const float HeaderY = ContentTop + 52f;
    const float RowsTop = HeaderY + 24f;
    const float FooterHeight = 46f;

    // ability, type, base, game, last, average, remaining, bonus as rate, bonus as reduction
    static readonly float[] ColX = { 14f, 186f, 238f, 296f, 354f, 414f, 494f, 556f, 636f };
    static readonly float[] ColW = { 168f, 50f, 54f, 54f, 56f, 76f, 58f, 76f, 70f };

    readonly TextMeshProUGUI _summary;
    readonly TextMeshProUGUI[][] _cells = new TextMeshProUGUI[Rows][];
    readonly TextMeshProUGUI _empty;

    public override Vector2 SavedPosition
    {
        get => Settings.CooldownPanelPosition;
        set => Settings.CooldownPanelPosition = value;
    }

    public CooldownPanel(Transform parent)
        : base("Cooldowns", parent, "Cooldown tester", "use a skill, see its real cooldown", PanelWidth, RowsTop + Rows * RowHeight + FooterHeight + 8f)
    {
        _summary = UIFactory.CreateText("Summary", Root, string.Empty, 12, Theme.TextDim);
        _summary.enableWordWrapping = true;
        _summary.rectTransform.Place(14, ContentTop - 4, Width - 130, 48);
        var reset = UIFactory.CreateButton("Reset", Root, "Reset", 12, CooldownTester.Reset, radius: 12f);
        reset.Rect.Place(Width - 100, ContentTop, 88, 24);

        string[] headers = { "Ability", "Type", "Base", "Game", "Last", "Avg (n)", "Left", "As rate", "As reduct." };
        for (int c = 0; c < headers.Length; c++)
        {
            var label = UIFactory.CreateText(headers[c], Root, headers[c].ToUpperInvariant(), 10, Theme.TextDim,
                c == 0 ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.MidlineRight);
            label.characterSpacing = 1.5f;
            label.rectTransform.Place(ColX[c], HeaderY, ColW[c], 20);
        }
        UIFactory.CreateDivider(Root, 12, HeaderY + 20, Width - 24);

        for (int r = 0; r < Rows; r++)
        {
            _cells[r] = new TextMeshProUGUI[ColX.Length];
            for (int c = 0; c < ColX.Length; c++)
            {
                var cell = UIFactory.CreateText($"C{r}_{c}", Root, string.Empty, 12, Theme.Text,
                    c == 0 ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.MidlineRight);
                cell.rectTransform.Place(ColX[c], RowsTop + r * RowHeight, ColW[c], RowHeight);
                _cells[r][c] = cell;
            }
        }
        _empty = UIFactory.CreateText("Empty", Root, "Use a weapon skill or spell - it shows up here, and its cooldown is timed until it's ready again.",
            12, Theme.TextDim);
        _empty.rectTransform.Place(14, RowsTop, Width - 28, RowHeight);

        var footer = UIFactory.CreateText("Footer", Root,
            "Base = the skill's unmodified cooldown. Game = what the game applied. Last/Avg = measured until ready. " +
            "As rate / As reduct. = the bonus that measurement implies if the stat works as base ÷ (1 + bonus) or base × (1 − bonus). " +
            "Green = more than your client's stat shows (bonuses beyond the cap, applied by the server).", 11, Theme.TextDim);
        footer.enableWordWrapping = true;
        footer.rectTransform.Place(14, Height - FooterHeight, Width - 28, FooterHeight - 4);
    }

    public override void Refresh()
    {
        if (!Visible) return;
        var rates = CooldownTester.ClientRates();
        _summary.text = $"Your client's cooldown stats:  weapon <b>{rates.Weapon * 100:0.#}%</b> · spell <b>{rates.Spell * 100:0.#}%</b> · " +
                        $"ultimate {rates.Ultimate * 100:0.#}% · all {rates.All * 100:0.#}%\n" +
                        "Compare a skill's implied bonus with its stat: the closer column is how the stat works.";

        var abilities = CooldownTester.Abilities.ToList();
        _empty.gameObject.SetActive(abilities.Count == 0);
        for (int r = 0; r < Rows; r++)
        {
            var cells = _cells[r];
            if (r >= abilities.Count)
            {
                foreach (var cell in cells) cell.text = string.Empty;
                continue;
            }
            var a = abilities[r];
            float stat = a.Kind switch
            {
                CooldownTester.Kind.Weapon => rates.Weapon,
                CooldownTester.Kind.Spell => rates.Spell,
                _ => float.NaN,
            };
            float? measured = a.AverageMeasured;
            double left = CooldownTester.Remaining(a);

            cells[0].text = a.Name;
            cells[1].text = a.Kind.ToString().ToLowerInvariant();
            cells[2].text = Seconds(a.BaseCooldown);
            cells[3].text = Seconds(a.GameCooldown);
            cells[4].text = a.LastMeasured is { } last ? Seconds(last) + (a.Adjusted ? "*" : "") : "-";
            cells[5].text = measured is { } avg ? $"{Seconds(avg)} ({a.Samples.Count})" : "-";
            cells[6].text = a.Running ? $"{left:0.0}s" : "ready";
            cells[6].color = a.Running ? Theme.Respawning : Theme.Alive;

            if (measured is { } m && m > 0 && a.BaseCooldown > 0)
            {
                float asRate = a.BaseCooldown / m - 1f;
                float asReduction = 1f - m / a.BaseCooldown;
                cells[7].text = $"{asRate * 100:0.#}%";
                cells[8].text = $"{asReduction * 100:0.#}%";
                cells[7].color = Highlight(asRate, stat);
                cells[8].color = Highlight(asReduction, stat);
            }
            else
            {
                cells[7].text = cells[8].text = "-";
                cells[7].color = cells[8].color = Theme.TextDim;
            }
        }
    }

    // Green when the measurement implies more bonus than the client's stat shows.
    static Color Highlight(float implied, float stat) =>
        float.IsNaN(stat) ? Theme.Text : implied > stat + 0.02f ? Theme.Alive : Theme.Text;

    static string Seconds(float s) => s > 0 ? $"{s:0.00}s" : "-";
}
