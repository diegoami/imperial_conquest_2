using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Engine.Serialization;
using IC2.Slice.Screens;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Ui.Screens;

/// <summary>
/// <c>docs/tasks/T145.md</c> Done-when 1 and 2: the game-end window's model is the original's own text and
/// table, pinned against the windows of <c>2026-10-05-end-of-game-screens.md</c> without the Godot SDK.
/// </summary>
/// <remarks>
/// The states are Rome and Gaul of the shipped <c>classical-mediterranean</c> scenario under
/// <c>classical-faithful</c>, whose own start figures the report's windows carry: Rome's wealth 2,577,000
/// and 25 cities at New Game, Gaul's start wealth 1,512,000 and 28 cities. The staged values (the fall's
/// end wealth, treasury and year) are the report's W1, W4 and W13 rows.
/// </remarks>
public sealed class GameEndViewModelTests
{
    private static readonly Lazy<ResolvedScenario> LazyClassical = new(
        () => GameDataRepository.Load(ModelTestPaths.DataRoot).Resolve("classical-mediterranean"));

    private static readonly Lazy<GameState> LazyState = new(
        () => GameStateFactory.CreateInitial(
            LazyClassical.Value.World, LazyClassical.Value.Ruleset, LazyClassical.Value.Scenario));

    private static Ruleset Ruleset => LazyClassical.Value.Ruleset;

    private static NationState Rome => LazyState.Value.NationById("rome")!;

    private static NationState Gaul => LazyState.Value.NationById("gaul")!;

    /// <summary>
    /// W1: a seat deposed for debt at 270 BC. The first line names the leader it had at the fall, the
    /// reason is the engine's text, the years line is the two-space "short time" form, both column titles
    /// read 270 BC, and the start column is the nation's New Game triple while the end column is the
    /// fall's — the treasury negative in the original's formatter.
    /// </summary>
    [Fact]
    public void W1_Rome_deposed_for_debt_shows_the_originals_lines_and_table()
    {
        var model = GameEndViewModel.FromFall(W1Fall(), Rome, Ruleset, gameOver: false);

        Assert.Equal("End of Game", model.Title);
        Assert.Equal("The game is over for Appius Claudius the leader of Rome.", model.LeaderLine);
        Assert.Equal("Your army have deposed you because they have not been paid.", model.Body);
        Assert.Equal("Your  short time in power in Rome produced these changes.", model.YearsLine);
        Assert.Equal("Rome in 270 BC.", model.StartTitle);
        Assert.Equal("Rome in 270 BC.", model.EndTitle);
        Assert.Equal(
            new[] { "Population   2,577,000", "Cities   25", "Treasury    2,200 talents" },
            model.StartRows);
        Assert.Equal(
            new[] { "Population   2,577,000", "Cities   25", "Treasury  - 30,000 talents" },
            model.EndRows);
    }

    /// <summary>
    /// W4: the same Rome at 250 BC. The years line is 20, the right column title is the fall's year, and
    /// the right column carries the fall's grown wealth and treasury while the left keeps New Game's.
    /// </summary>
    [Fact]
    public void W4_Rome_at_250_BC_shows_20_years_and_the_ends_own_figures()
    {
        var fall = W1Fall() with { YearBc = 250, EndWealth = 2_601_000, EndTreasury = 2_221 };

        var model = GameEndViewModel.FromFall(fall, Rome, Ruleset, gameOver: false);

        Assert.Equal("Your 20 years in power in Rome produced these changes.", model.YearsLine);
        Assert.Equal("Rome in 270 BC.", model.StartTitle);
        Assert.Equal("Rome in 250 BC.", model.EndTitle);
        Assert.Equal(new[] { "Population   2,577,000", "Cities   25", "Treasury    2,200 talents" }, model.StartRows);
        Assert.Equal(new[] { "Population   2,601,000", "Cities   25", "Treasury    2,221 talents" }, model.EndRows);
    }

    /// <summary>
    /// The years line's boundary is the original's <c>year &lt; 269</c> test (:56418): 269 BC still reads
    /// "short time" and 268 BC the first <c>N years</c> line, so <c>1 years</c> never appears.
    /// </summary>
    [Theory]
    [InlineData(269, "Your  short time in power in Rome produced these changes.")]
    [InlineData(268, "Your 2 years in power in Rome produced these changes.")]
    public void The_years_line_boundary_matches_the_originals_269_BC_test(int yearBc, string expected)
    {
        var model = GameEndViewModel.FromFall(
            W1Fall() with { YearBc = yearBc }, Rome, Ruleset, gameOver: false);

        Assert.Equal(expected, model.YearsLine);
    }

    /// <summary>
    /// W13: Gaul conquered by Rome. The reason names the captor, and the right column is the fall's own
    /// wealth and treasury with the designed difference of a zero city count (the clone keeps no leftover
    /// count word).
    /// </summary>
    [Fact]
    public void W13_Gaul_conquered_shows_the_captor_and_the_recorded_city_count()
    {
        var fall = new SeatFall(
            "gaul",
            SeatFallReason.Conquered,
            "rome",
            "Your nation has been conquerred by Rome.",
            LeaderName: "Hengest",
            YearBc: 270,
            EndWealth: 1_497_000,
            EndCityCount: 0,
            EndTreasury: 315);

        var model = GameEndViewModel.FromFall(fall, Gaul, Ruleset, gameOver: false);

        Assert.Equal("The game is over for Hengest the leader of Gaul.", model.LeaderLine);
        Assert.Equal("Your nation has been conquerred by Rome.", model.Body);
        Assert.Equal(
            new[] { "Population   1,497,000", "Cities   0", "Treasury    315 talents" },
            model.EndRows);
    }

    /// <summary>
    /// The first line reads the <see cref="SeatFall.LeaderName"/>, not the nation's live leader: a nation
    /// whose leader changed after the fall still prints the one it had.
    /// </summary>
    [Fact]
    public void The_first_line_names_the_leader_at_the_fall_not_the_nations_current_one()
    {
        var afterTheFall = Rome with { LeaderName = "Antiochus" };

        var model = GameEndViewModel.FromFall(W1Fall(), afterTheFall, Ruleset, gameOver: false);

        Assert.Equal("The game is over for Appius Claudius the leader of Rome.", model.LeaderLine);
    }

    /// <summary>
    /// The buttons are T138's, the user's recorded difference: Main menu and View map once no human seat
    /// remains, Continue alone while another does.
    /// </summary>
    [Fact]
    public void The_buttons_keep_T138s_recorded_difference()
    {
        var gameOver = GameEndViewModel.FromFall(W1Fall(), Rome, Ruleset, gameOver: true);
        Assert.Equal(new[] { GameEndButton.MainMenu, GameEndButton.ViewMap }, gameOver.Buttons);

        var hotseat = GameEndViewModel.FromFall(W1Fall(), Rome, Ruleset, gameOver: false);
        Assert.Equal(new[] { GameEndButton.Continue }, hotseat.Buttons);
    }

    /// <summary>
    /// No string of T138's designed wording remains in the view model: no "The game is over." line, no
    /// "&lt;leader&gt; ruled ..." years line, no Start/End/Money header or rows.
    /// </summary>
    [Fact]
    public void No_string_of_T138s_designed_wording_remains()
    {
        var model = GameEndViewModel.FromFall(W1Fall(), Rome, Ruleset, gameOver: false);
        var strings = new[]
        {
            model.Title, model.LeaderLine, model.Body, model.YearsLine, model.StartTitle, model.EndTitle,
        }.Concat(model.StartRows).Concat(model.EndRows).ToArray();

        Assert.DoesNotContain("The game is over.", strings);
        Assert.DoesNotContain("Start", strings);
        Assert.DoesNotContain("End", strings);
        Assert.DoesNotContain("Money", strings);
        Assert.DoesNotContain(strings, s => s.Contains("ruled", StringComparison.Ordinal));
    }

    /// <summary>
    /// Done-when 2: the original's number formatter, <c>FUN_00448e74</c> — three spaces (with a dash at
    /// index 1 for a negative), the absolute value with thousands commas, no trailing spaces.
    /// </summary>
    [Theory]
    [InlineData(0, "   0")]
    [InlineData(999, "   999")]
    [InlineData(1_000, "   1,000")]
    [InlineData(-64, " - 64")]
    [InlineData(-30_000, " - 30,000")]
    [InlineData(16_944_000, "   16,944,000")]
    public void The_number_formatter_matches_the_originals_fun_00448e74(int value, string expected)
    {
        Assert.Equal(expected, GameEndViewModel.FormatNumber(value));
    }

    // ---- fixtures ----

    /// <summary>W1's fall: Rome, Appius Claudius, deposed for debt at 270 BC with a staged −30,000 treasury.</summary>
    private static SeatFall W1Fall() => new(
        "rome",
        SeatFallReason.Unpaid,
        null,
        "Your army have deposed you because they have not been paid.",
        LeaderName: "Appius Claudius",
        YearBc: 270,
        EndWealth: 2_577_000,
        EndCityCount: 25,
        EndTreasury: -30_000);
}
