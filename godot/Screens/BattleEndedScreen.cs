using Godot;
using IC2.Slice.UI;

namespace IC2.Slice.Screens;

/// <summary>
/// T127: the original's <em>Battle ended</em> window for a tactical battle (<c>TBattleOver_InitializeForm</c>;
/// <c>docs/game-design.md</c>, "Write-back"): the winner line, the per-type and total table (the winner's start and
/// finish, the loser's start), and the money and supplies lines as the original words them. It reuses the
/// battle-result window's frame <strong>[designed]</strong> and reads only <see cref="BattleEndedViewModel"/>.
/// </summary>
/// <remarks>
/// The engine has already written the battle back when this opens (T125's <c>Conclude</c> runs in the call that
/// ended the battle); <em>OK</em> only dismisses the window, after which the screen that owns it shows the peace
/// offer if one is pending.
/// </remarks>
public partial class BattleEndedScreen : Control
{
    /// <summary>What the window shows.</summary>
    public required BattleEndedViewModel Model { get; init; }

    /// <summary>Raised when the player presses OK.</summary>
    public event Action? Closed;

    /// <summary>The title label, for <c>godot/Checks/**</c>.</summary>
    public Label TitleLabel { get; private set; } = null!;

    /// <summary>The winner line, for <c>godot/Checks/**</c>.</summary>
    public Label WinnerLabel { get; private set; } = null!;

    /// <summary>The money line, for <c>godot/Checks/**</c>.</summary>
    public Label MoneyLabel { get; private set; } = null!;

    /// <summary>The supplies line, for <c>godot/Checks/**</c>.</summary>
    public Label SuppliesLabel { get; private set; } = null!;

    /// <summary>The table's cells as shown, row by row, header first, for <c>godot/Checks/**</c>.</summary>
    public IReadOnlyList<string> TableTexts { get; private set; } = Array.Empty<string>();

    public override void _Ready()
    {
        ModalOverlay.Build(out var root, out var content, AppTheme.Scrim, 520f);
        AddChild(root);

        TitleLabel = UiKit.MakeLabel(Model.Title, AppTheme.Header, UiKit.AccentColor);
        content.AddChild(TitleLabel);
        WinnerLabel = UiKit.MakeLabel(Model.WinnerLine, AppTheme.Header, UiKit.TextColor);
        WinnerLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        content.AddChild(WinnerLabel);

        content.AddChild(new HSeparator());
        var table = new GridContainer { Columns = BattleEndedViewModel.Headers.Count };
        table.AddThemeConstantOverride("h_separation", AppTheme.DialogGap);
        table.AddThemeConstantOverride("v_separation", AppTheme.TableRowGap);
        content.AddChild(table);

        var texts = new List<string>();
        foreach (var header in BattleEndedViewModel.Headers)
        {
            AddCell(table, texts, header, UiKit.MutedTextColor, header.Length == 0);
        }

        foreach (var row in Model.Rows)
        {
            var total = row.Label == "Total";
            AddCell(table, texts, row.Label, total ? UiKit.AccentColor : UiKit.TextColor, true);
            AddCell(table, texts, row.WinnerStart.ToString(System.Globalization.CultureInfo.InvariantCulture), UiKit.TextColor, false);
            AddCell(table, texts, row.WinnerFinish.ToString(System.Globalization.CultureInfo.InvariantCulture), UiKit.TextColor, false);
            AddCell(table, texts, row.LoserStart.ToString(System.Globalization.CultureInfo.InvariantCulture), UiKit.TextColor, false);
        }

        TableTexts = texts;

        content.AddChild(new HSeparator());
        MoneyLabel = UiKit.MakeLabel(Model.MoneyLine, AppTheme.Body, UiKit.TextColor);
        content.AddChild(MoneyLabel);
        SuppliesLabel = UiKit.MakeLabel(Model.SuppliesLine, AppTheme.Body, UiKit.TextColor);
        content.AddChild(SuppliesLabel);

        content.AddChild(new HSeparator());
        var ok = UiKit.MakeButton("OK", Close);
        ok.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
        content.AddChild(ok);
        ok.GrabFocus();
    }

    /// <summary>Dismisses the window: what OK does, exposed so a headless check can call it directly.</summary>
    public void Close() => Closed?.Invoke();

    private static void AddCell(GridContainer table, List<string> texts, string text, Color color, bool left)
    {
        var label = UiKit.MakeLabel(text, AppTheme.Body, color);
        label.HorizontalAlignment = left ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        table.AddChild(label);
        texts.Add(text);
    }
}
