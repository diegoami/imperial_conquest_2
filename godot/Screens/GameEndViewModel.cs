using System.Globalization;
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
/// Everything the game-end window says and offers for one fallen seat, Godot-free — <c>docs/tasks/T138.md</c>
/// owns list, corrected by <c>docs/tasks/T145.md</c> to the original's own text and table. So
/// <c>tests/IC2.Engine.Tests/Ui/Screens/GameEndViewModelTests.cs</c> can pin the title, the leader line,
/// the reason, the years-in-power line, the two column titles, the six row strings and the buttons
/// without the Godot SDK, exactly the seam <see cref="BattleResultViewModel"/> established for the
/// battle-result screen.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The reason is the engine's own line, never a copy.</strong> <see cref="Body"/> is
/// <see cref="SeatFall.Text"/>, the very string the session printed for the fall — one of
/// <c>HumanLeaderFallsMessage</c>'s five, misspellings included. Nothing here retypes it.
/// </para>
/// <para>
/// <strong>The rest is the original's own text.</strong> The caption is <c>THumanFalls</c>'s, and the
/// first line, the years line, the two column titles and the row labels are the label strings
/// <c>THumanFalls_InitializeForm</c> builds (<c>2026-10-05-end-of-game-screens.md</c>, "What the window
/// writes, label by label"), read from the state: the leader and the year from the
/// <see cref="SeatFall"/>, the end wealth, city count and treasury from it too, and the start wealth as
/// <see cref="NationState.PopulationAtStart"/> × <see cref="EconomyRules.WealthPerPopulationThousand"/>.
/// </para>
/// </remarks>
/// <param name="Title">The window's caption, <see cref="EndOfGameTitle"/>.</param>
/// <param name="LeaderLine">The first line, <c>lbl_result1</c>: the leader at the fall, over the nation's name.</param>
/// <param name="Body">The engine's own fall text, <c>lbl_result2</c>.</param>
/// <param name="YearsLine">The years-in-power line, <c>lbl_changes</c>.</param>
/// <param name="StartTitle">The left column's title, <c>lbl_nat1</c>.</param>
/// <param name="EndTitle">The right column's title, <c>lbl_nat2</c>.</param>
/// <param name="StartRows">The left column's three rows, in table order (<c>lbl_pop1</c>, <c>lbl_cities1</c>, <c>lbl_money1</c>).</param>
/// <param name="EndRows">The right column's three rows, in table order (<c>lbl_pop2</c>, <c>lbl_cities2</c>, <c>lbl_money2</c>).</param>
/// <param name="Buttons">The buttons this window offers, in order.</param>
public sealed record GameEndViewModel(
    string Title,
    string LeaderLine,
    string Body,
    string YearsLine,
    string StartTitle,
    string EndTitle,
    IReadOnlyList<string> StartRows,
    IReadOnlyList<string> EndRows,
    IReadOnlyList<GameEndButton> Buttons)
{
    /// <summary><c>THumanFalls</c>'s own caption <strong>[derived: form]</strong>.</summary>
    public const string EndOfGameTitle = "End of Game";

    /// <summary>
    /// The original's number formatter, <c>FUN_00448e74</c> (:47544): a three-character sign field —
    /// three spaces for a non-negative value, <c>" - "</c> for a negative — then the absolute value with
    /// a comma before each group of three digits, and no trailing spaces
    /// <strong>[derived: code, <c>FUN_00448e74</c> (:47544) and <c>FUN_00448f18</c> (:47581); Wine
    /// candidate: the memory strings <c>Treasury    2,200 talents</c> and <c>Treasury  - 30,000
    /// talents</c>, <c>2026-10-05-end-of-game-screens.md</c>]</strong>. So 2,200 reads
    /// <c>"   2,200"</c> and −30,000 reads <c>" - 30,000"</c>.
    /// </summary>
    public static string FormatNumber(int value)
    {
        var magnitude = Math.Abs((long)value).ToString("N0", CultureInfo.InvariantCulture);
        return value < 0 ? $" - {magnitude}" : $"   {magnitude}";
    }

    /// <summary>
    /// Builds the window's model for one fall: the leader line, the engine's reason text, the years in
    /// power and the two-column start-against-end table, all read from the state.
    /// </summary>
    /// <param name="fall">
    /// The seat's fall: its <see cref="SeatFall.LeaderName"/> and <see cref="SeatFall.YearBc"/> fill the
    /// first line and the years line, and its end wealth, city count and treasury the right column.
    /// </param>
    /// <param name="nation">
    /// The same nation's live state — the display name and the start figures come from it. Eliminated and
    /// deposed nations stay in <see cref="GameState.Nations"/>, so this is always available.
    /// </param>
    /// <param name="ruleset">
    /// Supplies <see cref="CalendarRules.StartYearBc"/> for the column title and the years line, and
    /// <see cref="EconomyRules.WealthPerPopulationThousand"/> for the start wealth — never a C# literal.
    /// </param>
    /// <param name="gameOver">
    /// <see cref="GameSession.IsGameOver"/> once the call returned: whether this window offers Main menu /
    /// View map rather than Continue. The original's own captions for that state are not copied (the user's
    /// recorded difference on <c>docs/tasks/T145.md</c>).
    /// </param>
    public static GameEndViewModel FromFall(
        SeatFall fall,
        NationState nation,
        Ruleset ruleset,
        bool gameOver)
    {
        ArgumentNullException.ThrowIfNull(fall);
        ArgumentNullException.ThrowIfNull(nation);
        ArgumentNullException.ThrowIfNull(ruleset);

        var startYearBc = ruleset.Calendar.StartYearBc;
        var startWealth = nation.PopulationAtStart * ruleset.Economy.WealthPerPopulationThousand;
        var buttons = gameOver
            ? new[] { GameEndButton.MainMenu, GameEndButton.ViewMap }
            : new[] { GameEndButton.Continue };

        // "N years" only below startYearBc - 1; otherwise the literal " short time " after "Your ", which
        // is the two spaces the original's own string carries (:56418-56419).
        var yearsLine = fall.YearBc < startYearBc - 1
            ? $"Your {startYearBc - fall.YearBc} years in power in {nation.Name} produced these changes."
            : $"Your  short time in power in {nation.Name} produced these changes.";

        return new GameEndViewModel(
            EndOfGameTitle,
            $"The game is over for {fall.LeaderName} the leader of {nation.Name}.",
            fall.Text,
            yearsLine,
            $"{nation.Name} in {startYearBc} BC.",
            $"{nation.Name} in {fall.YearBc} BC.",
            new[]
            {
                $"Population{FormatNumber(startWealth)}",
                $"Cities   {nation.CityCountAtStart}",
                $"Treasury {FormatNumber(nation.TreasuryAtStart)} talents",
            },
            new[]
            {
                $"Population{FormatNumber(fall.EndWealth)}",
                $"Cities   {fall.EndCityCount}",
                $"Treasury {FormatNumber(fall.EndTreasury)} talents",
            },
            buttons);
    }
}
