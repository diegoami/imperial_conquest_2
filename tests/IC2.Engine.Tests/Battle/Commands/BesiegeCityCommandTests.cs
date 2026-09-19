using IC2.Engine.Battle;
using IC2.Engine.Battle.Commands;
using IC2.Engine.Cities.Capture;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Cities.Capture;
using Xunit;
using static IC2.Engine.Tests.Battle.Commands.BattleCommandTestbed;

namespace IC2.Engine.Tests.Battle.Commands;

/// <summary>
/// <c>docs/task-catalogue.md</c> T54, Done-when 2: <see cref="BesiegeCityCommand"/> dispatches to
/// <see cref="InstantBattleResolver.ResolveSiege"/> and routes the result through
/// <see cref="CityCaptureResolver.ResolveOutcome"/>, so capture, the defection cascade and elimination
/// all run — and this is the first path in <c>src/</c> that reaches any of them.
/// </summary>
/// <remarks>
/// As in <see cref="AttackArmyCommandTests"/>, no capture or siege <em>number</em> is asserted here: T16
/// and T17 own those and pin them in their own suites. What is pinned is that the command produces
/// exactly what those two merged calls produce in that order, and that the gates are decided first.
/// </remarks>
public sealed class BesiegeCityCommandTests
{
    private const string BesiegerId = "north-besieger";
    private const string TargetCityId = "meridia";

    /// <summary>
    /// The toy scenario's own map: north's army stands on (3, 3), the tile directly north of south's
    /// city <c>meridia</c> at (3, 4) — adjacent, which is the only way a siege is ever ordered, since no
    /// army can stand on a city's tile.
    /// </summary>
    private static GameState AtPeaceFixture(int troops = 40_000)
    {
        var besieger = BattleTestbed.Army(
            BesiegerId, NorthNationId, 3, 3, morale: 68, money: 0, supplyTons: 0,
            BattleTestbed.Unit("heavy_infantry", troops, 6, "1st Guards Battalion"),
            BattleTestbed.Unit("archers", 3_000, 6, "1st Bowmen Battalion"));

        return BattleTestbed.StateWith(armies: new[] { besieger });
    }

    private static GameState Fixture(int troops = 40_000) =>
        AtWar(AtPeaceFixture(troops), NorthNationId, SouthNationId);

    private static BesiegeCityCommand Besiege(string army = BesiegerId, string city = TargetCityId) =>
        new(NorthNationId, army, city);

    /// <summary>
    /// Done-when 2: the command's accepted state is, field for field, what
    /// <see cref="InstantBattleResolver.ResolveSiege"/> followed by
    /// <see cref="CityCaptureResolver.ResolveOutcome"/> produce on the dispatcher's own random stream.
    /// A re-derived term anywhere in either half moves this.
    /// </summary>
    [Fact]
    public void DoD02_TheCommandReturnsTheTwoMergedResolversOwnOutcome()
    {
        var state = Fixture(troops: 400_000);
        var command = Besiege();

        var siege = InstantBattleResolver.ResolveSiege(
            state, BesiegerId, TargetCityId, ToyRuleset, RngFor(state, command.Kind),
            CaptureTestbed.ArcherUnitTypeId, CaptureTestbed.FortifyOrderId, NullEventSink.Instance);
        var direct = CityCaptureResolver.ResolveOutcome(
            siege.State, siege.Result, ToyRuleset,
            CaptureTestbed.ArcherUnitTypeId, CaptureTestbed.FortifyOrderId, NullEventSink.Instance);

        var sink = new RecordingEventSink();
        var result = Dispatcher(sink).Dispatch(state, command);

        Assert.True(result.IsAccepted, result.ToString());
        Assert.Equal(
            GameJson.Serialize(direct),
            GameJson.Serialize(result.State with { RandomSeed = direct.RandomSeed }));

        var resolved = Assert.Single(sink.Events.OfType<BattleResolved>());
        Assert.Equal(BattleKind.Siege, resolved.Result.Kind);
        Assert.Equal(siege.Result, resolved.Result);
    }

    /// <summary>
    /// Done-when 2, the capture half: a siege the besieger wins transfers the city through T17's own
    /// path, publishing the confirmed <c>"falls to"</c> event.
    /// </summary>
    [Fact]
    public void DoD02_AWonSiegeCapturesTheCityThroughT17sOwnPath()
    {
        var sink = new RecordingEventSink();
        var result = Dispatcher(sink).Dispatch(Fixture(troops: 400_000), Besiege());

        Assert.True(result.IsAccepted, result.ToString());
        Assert.Equal(NorthNationId, result.State.CityById(TargetCityId)!.Owner);
        Assert.Single(sink.Events.OfType<CityFallsToNation>());
    }

    /// <summary>
    /// The other half of the same routing: a siege the defender holds publishes T17's confirmed
    /// <c>"fails to capture"</c> event and leaves the city where it was — while the attempt's own
    /// attrition (T17 Done-when 5: every attempt, win or lose) still applies. Without both halves, the
    /// test above would pass against a handler that only ever called <see cref="CityCaptureResolver"/>
    /// on a win.
    /// </summary>
    [Fact]
    public void DoD02_ALostSiegeStillRunsTheOutcomePathAndKeepsTheCity()
    {
        var state = Fixture(troops: 1_000);
        var sink = new RecordingEventSink();

        var result = Dispatcher(sink).Dispatch(state, Besiege());

        Assert.True(result.IsAccepted, result.ToString());
        Assert.Equal(SouthNationId, result.State.CityById(TargetCityId)!.Owner);
        Assert.Single(sink.Events.OfType<CityFailsToBeCaptured>());
        Assert.Equal(0, result.State.ArmyById(BesiegerId)!.Moves);
    }

    /// <summary>
    /// <strong>The delete-then-dangle probe</strong>
    /// (<c>docs/build-process.md</c> §4.2 gate 5), on the first production path that can eliminate a
    /// nation: the victim loses its last city and is eliminated, while a third nation and its own city
    /// come back byte-identical and nothing in the resulting state refers to anything that was removed.
    /// </summary>
    [Fact]
    public void DoD02_ACaptureThroughTheCommandEliminatesOneNationAndLeavesNothingDangling()
    {
        const string LastCityId = "lastcity";
        var doomed = CaptureTestbed.Nation(SouthNationId, unity: 668, capitalCityId: LastCityId);
        var conqueror = CaptureTestbed.Nation(NorthNationId, unity: 500, capitalCityId: "north-capital");
        var bystander = CaptureTestbed.Nation("east", unity: 500, capitalCityId: "east-capital");

        var lastCity = CaptureTestbed.City(
            LastCityId, "Last City", 3, 4, SouthNationId, SouthNationId,
            loyalty: 40, fortificationCode: 0, populationThousands: 10, maxPopulationThousands: 20, tribute: 5);
        var northCapital = CaptureTestbed.City(
            "north-capital", "North Capital", 0, 0, NorthNationId, NorthNationId,
            loyalty: 80, fortificationCode: 0, populationThousands: 15, maxPopulationThousands: 30, tribute: 5);
        var bystanderCity = CaptureTestbed.City(
            "east-capital", "East Capital", 7, 0, "east", "east",
            loyalty: 80, fortificationCode: 0, populationThousands: 15, maxPopulationThousands: 30, tribute: 5);

        var besieger = BattleTestbed.Army(
            BesiegerId, NorthNationId, 3, 3, morale: 68, money: 0, supplyTons: 0,
            BattleTestbed.Unit("heavy_infantry", 400_000, 6, "1st Guards Battalion"));

        var state = AtWar(
            StateWith(
                new[] { conqueror, doomed, bystander },
                new[] { lastCity, northCapital, bystanderCity },
                new[] { besieger }),
            NorthNationId,
            SouthNationId);

        var sink = new RecordingEventSink();
        var result = Dispatcher(sink).Dispatch(state, new BesiegeCityCommand(NorthNationId, BesiegerId, LastCityId));

        Assert.True(result.IsAccepted, result.ToString());

        var eliminated = result.State.NationById(SouthNationId)!;
        Assert.True(eliminated.Eliminated);
        Assert.Null(eliminated.CapitalCityId);
        Assert.Single(sink.Events.OfType<NationConquered>());

        // The two-entity probe: the uninvolved nation and its city are the very same records.
        Assert.Equal(bystander, result.State.NationById("east"));
        Assert.Equal(bystanderCity, result.State.CityById("east-capital"));

        // Nothing dangles: the captured city is still present (it changed owner, it was not deleted),
        // every city's owner and allegiance resolve to a nation the state still carries, and every
        // recruitment slot still names a city that exists.
        Assert.NotNull(result.State.CityById(LastCityId));
        Assert.Equal(NorthNationId, result.State.CityById(LastCityId)!.Owner);
        foreach (var city in result.State.Cities)
        {
            Assert.NotNull(result.State.NationById(city.Owner));
            Assert.NotNull(result.State.NationById(city.Allegiance));
        }

        foreach (var nation in result.State.Nations)
        {
            foreach (var slot in nation.RecruitmentSlots)
            {
                Assert.NotNull(result.State.CityById(slot.TargetCityId));
            }
        }

        foreach (var army in result.State.Armies)
        {
            Assert.NotNull(result.State.NationById(army.Nation));
        }
    }

    /// <summary>Done-when 2's gates, on Done-when 1's discipline: refused before the resolvers run, state untouched.</summary>
    [Fact]
    public void DoD02_EveryGateRefusesBeforeAnythingResolves()
    {
        var state = Fixture();

        AssertRefused(state, Besiege(army: "no-such-army"), BesiegeCityRejections.UnknownArmy);
        AssertRefused(state, Besiege(city: "no-such-city"), BesiegeCityRejections.UnknownCity);
        AssertRefused(state, Besiege(city: "arx"), BesiegeCityRejections.OwnCity);
        AssertRefused(AtPeaceFixture(), Besiege(), BesiegeCityRejections.NotAtWar);

        var moved = state with
        {
            Armies = ValueList.From(state.Armies.Select(a => a with { X = 0, Y = 0 })),
        };
        AssertRefused(moved, Besiege(), BesiegeCityRejections.NotAdjacent);

        var spent = state with
        {
            Armies = ValueList.From(state.Armies.Select(a => a with { Moves = 0 })),
        };
        AssertRefused(spent, Besiege(), BesiegeCityRejections.NoMovesLeft);

        var carrier = BattleTestbed.Fleet("north-fleet", NorthNationId, 3, 3, ships: 30, conditionPercent: 90);
        var embarked = state with
        {
            Fleets = ValueList.Of(carrier),
            Armies = ValueList.From(state.Armies.Select(a =>
                a with { AboardFleetId = "north-fleet", CoveredTileCode = null })),
        };
        AssertRefused(embarked, Besiege(), BesiegeCityRejections.AttackerEmbarked);
    }

    /// <summary>
    /// The two ids the merged resolvers take as parameters are resolved from the loaded ruleset, and a
    /// ruleset that carries neither is refused rather than throwing out of
    /// <see cref="Strength.SiegeStrength.Attacker"/> — see <see cref="BattleCommandRuleset"/> for why
    /// these are looked up at all.
    /// </summary>
    [Fact]
    public void ARulesetMissingEitherResolverIdIsRefusedRatherThanThrowing()
    {
        var state = Fixture();

        var withoutArchers = ToyRuleset with
        {
            UnitTypes = ValueList.From(ToyRuleset.UnitTypes.Where(u => u.Id != BattleCommandRuleset.ArcherUnitTypeId)),
        };
        Assert.Equal(
            BesiegeCityRejections.NoArcherUnitType,
            AttackLegality.Check(state, withoutArchers, Besiege())!.Code);

        var withoutFortifyOrder = ToyRuleset with
        {
            CityOrders = ToyRuleset.CityOrders with
            {
                Orders = ValueList.From(ToyRuleset.CityOrders.Orders.Where(o => !o.WipedBySiegeAttempt)),
            },
        };
        Assert.Equal(
            BesiegeCityRejections.NoFortificationOrder,
            AttackLegality.Check(state, withoutFortifyOrder, Besiege())!.Code);

        // And the shipped ruleset resolves both, by behaviour rather than by a hardcoded id.
        Assert.Equal(BattleCommandRuleset.ArcherUnitTypeId, BattleCommandRuleset.ArcherUnitTypeIdIn(ToyRuleset));
        Assert.Equal(CaptureTestbed.FortifyOrderId, BattleCommandRuleset.FortificationOrderIdIn(ToyRuleset));
    }

    private static void AssertRefused(GameState state, BesiegeCityCommand command, RejectionCode expected)
    {
        var sink = new RecordingEventSink();
        var result = Dispatcher(sink).Dispatch(state, command);

        Assert.True(result.IsRejected, result.ToString());
        Assert.Equal(expected, result.Code);
        Assert.Same(state, result.State);
        Assert.Empty(sink.Events);
    }
}
