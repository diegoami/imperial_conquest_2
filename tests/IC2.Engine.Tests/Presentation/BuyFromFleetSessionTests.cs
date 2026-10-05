using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Engine.Tests.Core;
using Xunit;

namespace IC2.Engine.Tests.Presentation;

/// <summary>
/// T134 Done-when 3: the 5-token <c>buy &lt;army&gt; fleet &lt;fleet&gt; &lt;tons&gt;</c> form reaches
/// <see cref="GameSession"/> — the fleet provider <c>BuySupplyCommand.ProviderFleetId</c> (issue #147)
/// already supports but which the session never built — while the 4-token city form prints exactly what
/// it always did.
/// </summary>
/// <remarks>
/// <strong>A scripted state, injected through T95's save constructor.</strong> The toy world and ruleset
/// are the committed ones; the buying army is given room and money, and two own fleets are added at
/// Chebyshev distance 1 and 2 from it. The session is then built from a <see cref="SaveGame"/> wrapping
/// that state, so both the dispatch and the reply line under test are the real production path.
/// </remarks>
public sealed class BuyFromFleetSessionTests
{
    private const string ArmyId = "north-army-1";
    private const string NationId = "north";
    private const string ProviderFleetId = "t134-provider-fleet";
    private const string FarFleetId = "t134-far-fleet";
    private const string OwnCityId = "arx";

    /// <summary>The army at (3,2); the provider fleet at (4,2) is one tile away, the far one at (5,2) is two.</summary>
    private static GameState Scripted()
    {
        var initial = CoreTestbed.InitialState();
        var army = initial.ArmyById(ArmyId)! with { SupplyTons = 100, Money = 100 };
        var provider = Fleet(ProviderFleetId, X: 4, Y: 2, supplyTons: 200);
        var far = Fleet(FarFleetId, X: 5, Y: 2, supplyTons: 200);

        return initial with
        {
            Armies = ValueList.From(initial.Armies.Select(a =>
                string.Equals(a.Id, army.Id, StringComparison.Ordinal) ? army : a)),
            Fleets = ValueList.From(initial.Fleets.Append(provider).Append(far)),
        };
    }

    private static FleetState Fleet(string id, int X, int Y, int supplyTons) => new(
        id, NationId, X, Y, Moves: 4, Ships: 10, ConditionPercent: 100, Money: 0, SupplyTons: supplyTons,
        ConstructionTicksRemaining: null, BuildCityId: null, CarriedArmyId: null, CoveredTileCode: null);

    private static GameSession SessionFor(GameState state) => new(
        CoreTestbed.Toy.World,
        CoreTestbed.Toy.Ruleset,
        CoreTestbed.Toy.Scenario,
        new SaveGame(
            SchemaVersion: GameDataSchema.CurrentVersion,
            Id: "t134-fleet-buy",
            Label: "T134 fleet-provider probe",
            ScenarioId: state.ScenarioId,
            WorldId: state.WorldId,
            RulesetId: state.RulesetId,
            State: state));

    /// <summary>The session's own rendered answer to a <c>buy</c>, or a loud failure naming the refusal.</summary>
    private static string PurchaseLine(SessionOutput output)
    {
        foreach (var line in output.Lines)
        {
            if (line.Contains("bought", StringComparison.Ordinal))
            {
                return line;
            }
        }

        Assert.Fail("The session refused the purchase: " + string.Join(" | ", output.Lines));
        return string.Empty;
    }

    [Fact]
    public void Buying_from_an_adjacent_fleet_moves_the_tons_and_no_money_and_prints_the_fleet_line()
    {
        var session = SessionFor(Scripted());
        var before = session.State;

        var line = PurchaseLine(session.Submit($"buy {ArmyId} fleet {ProviderFleetId} 50"));

        Assert.Equal(
            $"{ArmyId} bought 50 tons of supply from fleet {ProviderFleetId}, free.",
            line);

        var after = session.State;
        Assert.Equal(before.ArmyById(ArmyId)!.SupplyTons + 50, after.ArmyById(ArmyId)!.SupplyTons);
        Assert.Equal(before.FleetById(ProviderFleetId)!.SupplyTons - 50, after.FleetById(ProviderFleetId)!.SupplyTons);

        // Free: neither purse moved.
        Assert.Equal(before.ArmyById(ArmyId)!.Money, after.ArmyById(ArmyId)!.Money);
        Assert.Equal(before.NationById(NationId)!.Treasury, after.NationById(NationId)!.Treasury);
    }

    [Fact]
    public void A_fleet_two_tiles_away_is_refused_with_the_not_within_range_code()
    {
        var session = SessionFor(Scripted());
        var before = session.State;

        var output = session.Submit($"buy {ArmyId} fleet {FarFleetId} 50");

        Assert.Contains(
            "Purchase rejected (supply.provider-fleet-not-within-range):",
            string.Join("\n", output.Lines),
            StringComparison.Ordinal);
        Assert.Same(before, session.State);
    }

    /// <summary>The 4-token city form still prints exactly the line T41 established, unchanged by T134.</summary>
    [Fact]
    public void The_city_form_prints_exactly_what_it_printed_before()
    {
        var session = SessionFor(Scripted());

        var line = PurchaseLine(session.Submit($"buy {ArmyId} {OwnCityId} 10"));

        Assert.Equal(
            $"{ArmyId} bought 10 tons of supply at {OwnCityId}, free at your own city.",
            line);
    }
}
