using IC2.Engine.Model;
using IC2.Engine.Presentation;

namespace IC2.Slice.Screens;

/// <summary>
/// One button the game-end window offers — <c>docs/tasks/T138.md</c> scope item (2)/(3): Main menu and
/// View map once the game is over, Continue while another human seat remains.
/// </summary>
public enum GameEndButton
{
    /// <summary>Returns to the main menu, as File → Close does, with no "leave the game?" question.</summary>
    MainMenu,

    /// <summary>Closes the window; the map stays visible and the engine refuses every further order.</summary>
    ViewMap,

    /// <summary>Hotseat's single button: play goes on as the session already continues it.</summary>
    Continue,
}

/// <summary>
/// One row of the start-against-end table — <c>THumanFalls_InitializeForm</c>'s
/// <c>lbl_nat1/2</c>, <c>lbl_pop1/2</c>, <c>lbl_cities1/2</c>, <c>lbl_money1/2</c> pairs, copied as
/// <c>docs/tasks/T138.md</c>'s user decision of 2026-10-05 ("Copy the original") asks.
/// </summary>
/// <param name="Label">The row's label — "Population", "Cities" or "Money".</param>
/// <param name="Start">The nation's figure at the game's start (<see cref="NationState.PopulationAtStart"/> and siblings).</param>
/// <param name="End">The figure <see cref="SeatFall"/> recorded at the fall.</param>
public sealed record GameEndRow(string Label, int Start, int End);

/// <summary>
/// Everything the game-end window says and offers for one fallen seat, Godot-free — <c>docs/tasks/T138.md</c>
/// owns list. So <c>tests/IC2.Engine.Tests/Ui/Screens/GameEndViewModelTests.cs</c> can pin the title, the
/// body, the years-in-power line, the table and the buttons without the Godot SDK, exactly the seam
/// <see cref="BattleResultViewModel"/> established for the battle-result screen.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The body is the engine's own line, never a copy.</strong> <see cref="Body"/> is
/// <see cref="SeatFall.Text"/>, the very string the session printed for the fall — one of
/// <c>HumanLeaderFallsMessage</c>'s five, misspellings included. Nothing here retypes it.
/// </para>
/// <para>
/// <strong>The title and the table's labels are copied from the form.</strong> <c>THumanFalls</c> is
/// titled "End of Game" and carries a start-against-end table of population, cities and money; no
/// screenshot shows it filled in, so the layout and the exact label wording are
/// <c>docs/tasks/T138.md</c>'s own <em>[designed]</em> values, each a default the user may change at the
/// visual review.
/// </para>
/// </remarks>
/// <param name="Title">The window's title, <see cref="EndOfGameTitle"/>.</param>
/// <param name="Body">The engine's own fall text.</param>
/// <param name="GameOverLine">
/// "The game is over." when no human seat can give orders any more, otherwise <see langword="null"/>.
/// </param>
/// <param name="YearsLine">The leader's years in power, over the nation's name.</param>
/// <param name="NationName">The nation's display name, which the table's header row names.</param>
/// <param name="Rows">The three start-against-end rows, in table order.</param>
/// <param name="Buttons">The buttons this window offers, in order.</param>
public sealed record GameEndViewModel(
    string Title,
    string Body,
    string? GameOverLine,
    string YearsLine,
    string NationName,
    IReadOnlyList<GameEndRow> Rows,
    IReadOnlyList<GameEndButton> Buttons)
{
    /// <summary><c>THumanFalls</c>'s own caption <strong>[derived: form]</strong>.</summary>
    public const string EndOfGameTitle = "End of Game";

    /// <summary>
    /// The game-over line, <c>docs/tasks/T138.md</c>'s own <em>[designed]</em> wording for the state that
    /// leaves no human seat able to give orders.
    /// </summary>
    public const string GameOverText = "The game is over.";

    /// <summary>
    /// Builds the window's model for one fall: the engine's text, the years in power and the
    /// start-against-end table.
    /// </summary>
    /// <param name="fall">The seat's fall, whose end figures seed the table's End column.</param>
    /// <param name="nation">
    /// The same nation's live state — the start figures and the display name come from it. Eliminated and
    /// deposed nations stay in <see cref="GameState.Nations"/>, so this is always available.
    /// </param>
    /// <param name="ruleset">
    /// Supplies <see cref="CalendarRules.StartYearBc"/> for the years line — never a C# literal.
    /// </param>
    /// <param name="yearBc">The current <see cref="CalendarState.YearBc"/> at the fall.</param>
    /// <param name="gameOver">
    /// <see cref="GameSession.IsGameOver"/> once the call returned: whether this window adds
    /// <see cref="GameOverText"/> and offers Main menu / View map rather than Continue.
    /// </param>
    public static GameEndViewModel FromFall(
        SeatFall fall,
        NationState nation,
        Ruleset ruleset,
        int yearBc,
        bool gameOver)
    {
        ArgumentNullException.ThrowIfNull(fall);
        ArgumentNullException.ThrowIfNull(nation);
        ArgumentNullException.ThrowIfNull(ruleset);

        var years = ruleset.Calendar.StartYearBc - yearBc;
        var buttons = gameOver
            ? new[] { GameEndButton.MainMenu, GameEndButton.ViewMap }
            : new[] { GameEndButton.Continue };

        return new GameEndViewModel(
            EndOfGameTitle,
            fall.Text,
            gameOver ? GameOverText : null,
            $"{nation.LeaderName} ruled {nation.Name} for {years} years.",
            nation.Name,
            new[]
            {
                new GameEndRow("Population", nation.PopulationAtStart, fall.EndPopulation),
                new GameEndRow("Cities", nation.CityCountAtStart, fall.EndCityCount),
                new GameEndRow("Money", nation.TreasuryAtStart, fall.EndTreasury),
            },
            buttons);
    }
}
