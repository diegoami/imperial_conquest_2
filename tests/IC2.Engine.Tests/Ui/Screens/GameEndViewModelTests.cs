using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Engine.Tests.Core;
using IC2.Slice.Screens;
using Xunit;

namespace IC2.Engine.Tests.Ui.Screens;

/// <summary>
/// <c>docs/tasks/T138.md</c> Done-when 5: the game-end window's model — its title, the engine's own fall
/// text, the years-in-power line, the start-against-end table and the buttons — pinned without the Godot
/// SDK.
/// </summary>
public sealed class GameEndViewModelTests
{
    /// <summary>
    /// Every one of the original's five branches gets the form's own title and the engine's already-printed
    /// text, character for character — never a second, retyped copy.
    /// </summary>
    [Theory]
    [InlineData(SeatFallReason.AllCities, "You have conquerred the Mediterranean, a unique achievement.")]
    [InlineData(SeatFallReason.HardEndYear, "You have reached the end of your allotted 20 years.")]
    [InlineData(SeatFallReason.Unpopularity, "Your unpopularity has forced the army to overthrow you.")]
    [InlineData(SeatFallReason.Unpaid, "Your army have deposed you because they have not been paid.")]
    [InlineData(SeatFallReason.Conquered, "Your nation has been conquerred by Northern League (north).")]
    public void Each_reason_gets_the_form_title_and_the_engines_own_text(
        SeatFallReason reason, string expectedText)
    {
        var nation = Nation();
        var fall = Fall(reason, text: expectedText);

        var model = GameEndViewModel.FromFall(fall, nation, CoreTestbed.Toy.Ruleset, yearBc: 260, gameOver: false);

        Assert.Equal("End of Game", model.Title);
        Assert.Equal(expectedText, model.Body);
        Assert.Equal(fall.Text, model.Body);
    }

    /// <summary>
    /// Done-when 5: the years line is <c>calendar.startYearBc − YearBc</c>, read from the ruleset's own
    /// start year, not a C# literal — two different start years over the same current year give two
    /// different reign lengths.
    /// </summary>
    [Fact]
    public void The_years_line_is_the_start_year_minus_the_current_year()
    {
        var nation = Nation();
        var fall = Fall(SeatFallReason.AllCities);

        var toy = GameEndViewModel.FromFall(
            fall, nation, CoreTestbed.Toy.Ruleset, yearBc: 260, gameOver: false);

        Assert.Equal($"{nation.LeaderName} ruled {nation.Name} for 10 years.", toy.YearsLine);

        var ruleset = CoreTestbed.Toy.Ruleset with
        {
            Calendar = CoreTestbed.Toy.Ruleset.Calendar with { StartYearBc = 300 },
        };
        var later = GameEndViewModel.FromFall(fall, nation, ruleset, yearBc: 260, gameOver: false);

        Assert.Equal($"{nation.LeaderName} ruled {nation.Name} for 40 years.", later.YearsLine);
    }

    /// <summary>
    /// Done-when 5: the table's Start column is the nation's own start figures and its End column the
    /// <see cref="SeatFall"/>'s, in the original's Population / Cities / Money order; nothing here is a
    /// literal.
    /// </summary>
    [Fact]
    public void The_table_pairs_the_nations_start_figures_with_the_falls_end_figures()
    {
        var nation = Nation();
        var fall = Fall(
            SeatFallReason.Unpaid, endPopulation: 1234, endCityCount: 0, endTreasury: -4321);

        var model = GameEndViewModel.FromFall(fall, nation, CoreTestbed.Toy.Ruleset, yearBc: 260, gameOver: false);

        Assert.Equal(
            new[]
            {
                new GameEndRow("Population", nation.PopulationAtStart, 1234),
                new GameEndRow("Cities", nation.CityCountAtStart, 0),
                new GameEndRow("Money", nation.TreasuryAtStart, -4321),
            },
            model.Rows);
        Assert.Equal(nation.Name, model.NationName);
    }

    /// <summary>
    /// Done-when 5: once no human seat remains, the window adds "The game is over." and offers Main menu
    /// and View map; while another human seat remains (hotseat) it offers Continue alone.
    /// </summary>
    [Fact]
    public void Game_over_adds_its_line_and_its_two_buttons_while_hotseat_offers_Continue_alone()
    {
        var nation = Nation();
        var fall = Fall(SeatFallReason.AllCities);

        var gameOver = GameEndViewModel.FromFall(
            fall, nation, CoreTestbed.Toy.Ruleset, yearBc: 260, gameOver: true);
        Assert.Equal("The game is over.", gameOver.GameOverLine);
        Assert.Equal(new[] { GameEndButton.MainMenu, GameEndButton.ViewMap }, gameOver.Buttons);

        var hotseat = GameEndViewModel.FromFall(
            fall, nation, CoreTestbed.Toy.Ruleset, yearBc: 260, gameOver: false);
        Assert.Null(hotseat.GameOverLine);
        Assert.Equal(new[] { GameEndButton.Continue }, hotseat.Buttons);
    }

    // ---- fixtures ----

    /// <summary>The real toy nation, whose <c>AtStart</c> scores come from <see cref="GameStateFactory"/>.</summary>
    private static NationState Nation() => CoreTestbed.InitialState().NationById("north")!;

    private static SeatFall Fall(
        SeatFallReason reason,
        string text = "Your army have deposed you because they have not been paid.",
        int endPopulation = 100,
        int endCityCount = 1,
        int endTreasury = -100,
        string? conqueror = null) =>
        new("north", reason, conqueror, text, endPopulation, endCityCount, endTreasury);
}
