using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Engine.Tests.Core;
using Xunit;

namespace IC2.Engine.Tests.Presentation;

/// <summary>
/// <c>docs/tasks/T117.md</c> Done-when 5: the <c>army-transfer</c> parser recognizes the three
/// <c>back-</c> options the original's one dialog <c>OK</c> carries (each at most once, whole
/// non-negative unit indexes, whole numbers), every malformed option prints the usage line, and T106's
/// one-way form still parses. The parses are checked through a real <see cref="GameSession"/> over two
/// adjacent <c>north</c> armies, so an accepted line proves the option reached the handler rather than
/// merely not being rejected by the parser.
/// </summary>
public sealed class ArmyTransferParserTests
{
    private const string Usage =
        "Usage: army-transfer <selected> <partner> [units=<i,j,...>] [supply=<tons>] [money=<talents>] "
        + "[back-units=<i,j,...>] [back-supply=<tons>] [back-money=<talents>] [unit-steps=<a0:b,b1:a,...>]";

    /// <summary>
    /// A session paused on <c>north</c> over two adjacent armies, both with 1,000 troops, 20 tons of
    /// supply and a 50-talent purse, so every one-direction and <c>back-</c> form below is genuinely
    /// accepted. A save is the only public way to hand <see cref="GameSession"/> a state the scenario did
    /// not start with (<c>ResumeFrom</c> reads its seats, it does not replay a turn).
    /// </summary>
    private static GameSession Session(int selectedUnits = 1, int partnerUnits = 1)
    {
        var toy = CoreTestbed.Toy;
        var initial = GameStateFactory.CreateInitial(toy.World, toy.Ruleset, toy.Scenario);

        var selected = new ArmyState(
            "parse-a", "north", 1, 1, Moves: 5, Morale: 60, Money: 50, SupplyTons: 20, CoveredTileCode: 2,
            AboardFleetId: null,
            Units: ValueList.From(Enumerable.Range(0, selectedUnits).Select(i => new UnitSlot(0, "light_infantry", 1000, 6, $"Parse A{i}"))));
        var partner = selected with
        {
            Id = "parse-b",
            X = 2,
            Units = ValueList.From(Enumerable.Range(0, partnerUnits).Select(i => new UnitSlot(0, "light_infantry", 1000, 6, $"Parse B{i}"))),
        };

        var state = initial with { Armies = ValueList.Of(selected, partner) };
        var save = new SaveGame(
            SchemaVersion: state.SchemaVersion,
            Id: "117-parse-probe",
            Label: "T117 parser probe",
            ScenarioId: state.ScenarioId,
            WorldId: state.WorldId,
            RulesetId: state.RulesetId,
            State: state);

        return new GameSession(toy.World, toy.Ruleset, toy.Scenario, save);
    }

    [Theory]
    [InlineData("army-transfer parse-a parse-b units=0")]
    [InlineData("army-transfer parse-a parse-b supply=1")]
    [InlineData("army-transfer parse-a parse-b money=1")]
    [InlineData("army-transfer parse-a parse-b back-units=0")]
    [InlineData("army-transfer parse-a parse-b back-supply=1")]
    [InlineData("army-transfer parse-a parse-b back-money=1")]
    [InlineData("army-transfer parse-a parse-b units=0 supply=1 money=1 back-units=0 back-supply=1 back-money=1")]
    [InlineData("army-transfer parse-a parse-b unit-steps=a0:b,b0:a supply=1 money=1")]
    [InlineData("army-transfer parse-a parse-b unit-steps=a0:b,a0:a")]
    public void Every_accepted_one_way_and_back_form_parses_and_dispatches(string line)
    {
        var output = Session().Submit(line);

        Assert.Contains("armies.army-transfer accepted.", output.Lines);
        Assert.DoesNotContain(Usage, output.Lines);
    }

    [Theory]
    [InlineData("army-transfer parse-a parse-b supply=1 supply=2", "a repeated option")]
    [InlineData("army-transfer parse-a parse-b back-units=0 back-units=1", "a repeated back- option")]
    [InlineData("army-transfer parse-a parse-b back-units=-1", "a negative unit index")]
    [InlineData("army-transfer parse-a parse-b back-units=x", "a non-numeric unit index")]
    [InlineData("army-transfer parse-a parse-b back-supply=x", "a non-numeric supply")]
    [InlineData("army-transfer parse-a parse-b back-money=x", "a non-numeric money")]
    [InlineData("army-transfer parse-a parse-b back-money=", "an empty value")]
    [InlineData("army-transfer parse-a parse-b bogus=1", "an unknown key")]
    [InlineData("army-transfer parse-a parse-b back-bogus=1", "an unknown back- key")]
    [InlineData("army-transfer parse-a parse-b units=0 back-units=-1", "one bad index among good options")]
    [InlineData("army-transfer parse-a parse-b unit-steps=a0:b units=0", "ambiguous ordered and batch units")]
    [InlineData("army-transfer parse-a parse-b unit-steps=b0:a back-units=0", "ambiguous ordered and back units")]
    [InlineData("army-transfer parse-a parse-b unit-steps=x0:b", "unknown original side")]
    [InlineData("army-transfer parse-a parse-b unit-steps=a0:x", "unknown destination side")]
    [InlineData("army-transfer parse-a parse-b unit-steps=a-1:b", "negative original identity")]
    [InlineData("army-transfer parse-a parse-b unit-steps=a2147483648:b", "overflowed original identity")]
    [InlineData("army-transfer parse-a parse-b unit-steps=a0:b,", "empty ordered step")]
    [InlineData("army-transfer parse-a parse-b unit-steps=a0:b unit-steps=b0:a", "repeated ordered option")]
    public void Malformed_back_options_print_the_usage_line(string line, string because)
    {
        var output = Session().Submit(line);

        Assert.True(
            output.Lines.Contains(Usage),
            $"'{line}' should print the usage line ({because}); it printed: {string.Join(" | ", output.Lines)}");
    }

    [Fact]
    public void OneWay_form_with_a_missing_army_name_prints_the_usage_line()
    {
        var output = Session().Submit("army-transfer parse-a");

        Assert.Contains(Usage, output.Lines);
    }

    [Fact]
    public void Unknown_ordered_identity_after_a_valid_move_refuses_atomically()
    {
        var session = Session();
        var before = session.State;
        var output = session.Submit("army-transfer parse-a parse-b unit-steps=a0:b,b999:a");
        Assert.Contains(output.Lines, l => l.Contains("army-transfer-unknown-unit-index", StringComparison.Ordinal));
        Assert.Equal(before, session.State);
    }

    [Fact]
    public void Partial_success_reports_moved_count_and_readable_not_moved_reason()
    {
        var session = Session(selectedUnits: 3, partnerUnits: 19);
        var output = session.Submit("army-transfer parse-a parse-b units=0,1");
        Assert.Contains("armies.army-transfer accepted.", output.Lines);
        Assert.Contains("Moved 1 of 2 unit moves.", output.Lines);
        Assert.Contains("Not moved: This army already has 20 units.", output.Lines);
        Assert.DoesNotContain(output.Lines, l => l.Contains("rejected", StringComparison.Ordinal));
        Assert.Equal("Parse A1", session.State.ArmyById("parse-b")!.Units[^1].Name);
        Assert.Equal(new[] { "Parse A0", "Parse A2" }, session.State.ArmyById("parse-a")!.Units.Select(u => u.Name));
    }
}
