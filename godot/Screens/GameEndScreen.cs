using Godot;
using IC2.Engine.Presentation;
using IC2.Slice.UI;

namespace IC2.Slice.Screens;

/// <summary>
/// The original's own end-of-game window, <c>THumanFalls</c> — <c>docs/tasks/T138.md</c>: one window per
/// fallen human seat, titled "End of Game", showing the reason text the engine already printed, the
/// leader's years in power and a start-against-end table of the nation's population, cities and money.
/// </summary>
/// <remarks>
/// <para>
/// Built with <see cref="ModalOverlay"/> exactly as <see cref="BattleResultScreen"/> is: a translucent
/// backdrop so the map stays dimly visible behind it. Reads only <see cref="GameEndViewModel"/> — never a
/// <see cref="SeatFall"/> or a <see cref="GameSession"/> directly — so the Godot-free test pins every
/// string this screen renders.
/// </para>
/// <para>
/// <strong>[derived: form]</strong> for the title, the years line and the table of population, cities and
/// money; the layout, the label wording and the game-over line are <c>docs/tasks/T138.md</c>'s own
/// <em>[designed]</em> defaults, each changeable at the visual review. The body text is the engine's,
/// unchanged.
/// </para>
/// </remarks>
public partial class GameEndScreen : Control
{
    public required GameEndViewModel Model { get; init; }

    /// <summary>Raised when the player closes the window — View map or hotseat's Continue.</summary>
    public event Action? Closed;

    /// <summary>Raised when the player chooses Main menu — the same exit File → Close takes.</summary>
    public event Action? MainMenuRequested;

    /// <summary>Exposed for <c>godot/Checks/GameEndCheck.cs</c> to assert the rendered text.</summary>
    public Label TitleLabel { get; private set; } = null!;

    /// <summary>Exposed for the same reason as <see cref="TitleLabel"/>.</summary>
    public Label BodyLabel { get; private set; } = null!;

    /// <summary>The "The game is over." line, present only when the model carries one.</summary>
    public Label? GameOverLabel { get; private set; }

    /// <summary>Exposed for the same reason as <see cref="TitleLabel"/>.</summary>
    public Label YearsLabel { get; private set; } = null!;

    /// <summary>The laid-out start-against-end grid — its header row plus one row per figure.</summary>
    public GridContainer Table { get; private set; } = null!;

    private readonly List<Label> _tableLabels = new();

    public override void _Ready()
    {
        ModalOverlay.Build(out var root, out var content, new Color(0f, 0f, 0f, 0.65f));
        AddChild(root);

        TitleLabel = UiKit.MakeLabel(Model.Title, 22, UiKit.AccentColor);
        content.AddChild(TitleLabel);

        BodyLabel = UiKit.MakeLabel(Model.Body, 16, UiKit.TextColor);
        content.AddChild(BodyLabel);

        if (Model.GameOverLine is { } gameOver)
        {
            GameOverLabel = UiKit.MakeLabel(gameOver, 16, UiKit.TextColor);
            content.AddChild(GameOverLabel);
        }

        YearsLabel = UiKit.MakeLabel(Model.YearsLine, 14, UiKit.MutedTextColor);
        content.AddChild(YearsLabel);

        content.AddChild(new HSeparator());

        Table = new GridContainer { Columns = 3 };
        AddTableCell(Model.NationName, UiKit.TextColor);
        AddTableCell("Start", UiKit.MutedTextColor);
        AddTableCell("End", UiKit.MutedTextColor);
        foreach (var row in Model.Rows)
        {
            AddTableCell(row.Label, UiKit.MutedTextColor);
            AddTableCell(row.Start.ToString(), UiKit.TextColor);
            AddTableCell(row.End.ToString(), UiKit.TextColor);
        }

        content.AddChild(Table);

        content.AddChild(new HSeparator());
        foreach (var button in Model.Buttons)
        {
            content.AddChild(button switch
            {
                GameEndButton.MainMenu => UiKit.MakeButton("Main menu", MainMenu),
                GameEndButton.ViewMap => UiKit.MakeButton("View map", ViewMap),
                GameEndButton.Continue => UiKit.MakeButton("Continue", Continue),
                _ => throw new ArgumentOutOfRangeException(nameof(button), button, "Unknown game-end button."),
            });
        }
    }

    /// <summary>Every laid-out table cell's text, header row first — the check reads what is drawn, not
    /// the model a second time.</summary>
    public IReadOnlyList<string> TableCells() => _tableLabels.Select(label => label.Text).ToArray();

    /// <summary>
    /// Returns to the main menu — what the "Main menu" button does. Public under its own name for the
    /// check, the same convention <see cref="BattleResultScreen.Close"/> uses.
    /// </summary>
    public void MainMenu() => MainMenuRequested?.Invoke();

    /// <summary>Closes the window and leaves the map visible — what "View map" does.</summary>
    public void ViewMap() => Closed?.Invoke();

    /// <summary>Closes the window and lets play go on — what hotseat's "Continue" does.</summary>
    public void Continue() => Closed?.Invoke();

    private void AddTableCell(string text, Color color)
    {
        var label = UiKit.MakeLabel(text, 14, color);
        _tableLabels.Add(label);
        Table.AddChild(label);
    }
}
