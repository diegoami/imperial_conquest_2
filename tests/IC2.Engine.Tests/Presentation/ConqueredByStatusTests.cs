using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Engine.Tests.Core;
using Xunit;
using CaptureFixtures = IC2.Engine.Tests.Cities.Capture.CaptureTestbed;

namespace IC2.Engine.Tests.Presentation;

/// <summary>
/// T86 Done-when 6 (Owns amendment PR #408): "conquered-by is saved, loaded and shown by status."
/// Driven through a real siege, not a hand-built <see cref="GameState"/> -- <see cref="GameSession"/> has
/// no way to inject one directly (<see cref="GameSession.State"/> has a private setter), and this task's
/// own Owns list is limited to showing the field, not adding one. <c>south</c> owns exactly one city
/// ("meridia", its own capital), so a successful siege there both falls to <c>north</c> and immediately
/// conquers <c>south</c> outright (0 remaining cities, under <see cref="CaptureRules.ConquestCityCountThreshold"/>).
/// </summary>
/// <remarks>
/// The shipped toy world's own starting armies cannot win a siege at all (issue #267 -- their populations
/// are far below what either city's defence needs), and <c>besiege-city</c> requires the attacker adjacent
/// to its target (<see cref="Battle.Commands.AttackLegality.AreAdjacent"/>), which the toy world's own
/// <c>north-army-1</c> is not (two tiles from <c>meridia</c>). This fixture follows
/// <c>GameSessionCommandsTests.HandleEnd_prints_the_dash_wrapped_elimination_from_an_ai_seats_own_turn</c>'s
/// own precedent (a single 400,000-archer force, already proven to clear any siege in the toy world) but
/// places it directly on the bordering tile, so one direct <c>besiege-city</c> command -- composing the
/// peace-to-war declaration and resolving the siege in the same call, per
/// <c>GameSessionCommandsTests.Besiege_city_composes_declare_war_when_the_two_nations_are_at_peace</c> --
/// is enough, with no march and no AI turn needed.
/// </remarks>
public sealed class ConqueredByStatusTests
{
    private const string AttackerArmyId = "north-overwhelming-army";

    private static GameSession NewSessionWithAnOverwhelmingAdjacentArmy(string? humanSeatNationId = null)
    {
        var toy = CoreTestbed.Toy;
        var world = toy.World with
        {
            StartingArmies = ValueList.Of(
                new StartingArmy(
                    AttackerArmyId, "north", X: 3, Y: 3, Morale: 60, Money: 0, SupplyTons: 0,
                    Moves: 1, Units: ValueList.Of(CaptureFixtures.Unit("archers", 400_000)))),
        };

        return new GameSession(world, toy.Ruleset, toy.Scenario, humanSeatNationId: humanSeatNationId);
    }

    [Fact]
    public void Status_shows_a_conquered_nations_own_conqueror()
    {
        var session = NewSessionWithAnOverwhelmingAdjacentArmy();

        session.Submit($"besiege-city {AttackerArmyId} meridia");

        Assert.True(session.State.NationById("south")!.Eliminated);
        Assert.Equal("north", session.State.NationById("south")!.ConqueredBy);

        var output = session.Submit("status");

        // The exact "Nations:" listing prefix, not a bare substring match -- south's own line names
        // north too ("conquered by Northern League (north)"), which would otherwise also match a loose
        // "contains Northern League (north" search meant for north's own summary line below.
        var southLine = Assert.Single(output.Lines, line => line.StartsWith("  Southern League (south,", StringComparison.Ordinal));
        Assert.Contains("eliminated", southLine, StringComparison.Ordinal);
        Assert.Contains("conquered by Northern League (north)", southLine, StringComparison.Ordinal);

        // The survivor's own line carries neither clause.
        var northLine = Assert.Single(output.Lines, line => line.StartsWith("  Northern League (north,", StringComparison.Ordinal));
        Assert.DoesNotContain("eliminated", northLine, StringComparison.Ordinal);
        Assert.DoesNotContain("conquered by", northLine, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>status mine</c> shares the same rendering helper (<c>GameSessionRendering.NationStatusSuffix</c>)
    /// as <c>status</c>'s own "Nations:" listing, so this test's own job is narrower than
    /// <see cref="Status_shows_a_conquered_nations_own_conqueror"/>'s: proving <c>status mine</c> shows
    /// nothing extra for a nation that has conquered another, not re-proving the conquered nation's own
    /// clause a second time through a heavier fixture. Viewing the *conquered* seat's own <c>status mine</c>
    /// needs a human seat other than the winner's, which -- since a manually issued command is always
    /// attributed to whichever seat is currently active, and this session's own AI turn for any seat other
    /// than the designated human auto-plays ahead of the first command reaching it (<see cref="GameSession"/>'s
    /// own pending-prelude mechanism) -- would need an AI-driven conquest, not a directly issued one; that
    /// shape (a scripted AI war declaration under a specific seed) is already <c>GameSessionCommandsTests.HandleEnd_prints_the_dash_wrapped_elimination_from_an_ai_seats_own_turn</c>'s
    /// own territory, not duplicated here.
    /// </summary>
    [Fact]
    public void Status_mine_shows_no_conqueredBy_clause_for_the_winner_itself()
    {
        var session = NewSessionWithAnOverwhelmingAdjacentArmy(humanSeatNationId: "north");

        session.Submit($"besiege-city {AttackerArmyId} meridia");
        Assert.True(session.State.NationById("south")!.Eliminated);

        var output = session.Submit("status mine");

        var headerLine = Assert.Single(output.Lines, line => line.StartsWith("Nation:", StringComparison.Ordinal));
        Assert.DoesNotContain("eliminated", headerLine, StringComparison.Ordinal);
        Assert.DoesNotContain("conquered by", headerLine, StringComparison.Ordinal);
    }
}
