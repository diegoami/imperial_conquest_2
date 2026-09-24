using IC2.Engine.Ai;
using IC2.Engine.Economy;
using IC2.Engine.Model;
using IC2.Engine.Tests.Battle;
using IC2.Engine.Tests.Battle.Commands;
using Xunit;
using CaptureFixtures = IC2.Engine.Tests.Cities.Capture.CaptureTestbed;

namespace IC2.Engine.Tests.Ai;

/// <summary>
/// <c>docs/task-catalogue.md</c> T22 Done-when 5: "<em>every AI turn, each AI army calls T38's automatic
/// resupply against every non-hostile city within 4 tiles (<c>FUN_0044F31C</c> → <c>FUN_0044E41C</c>),
/// and each AI fleet calls the fleet version. The pass never re-implements the caps. One scripted-state
/// test covers each.</em>"
/// </summary>
/// <remarks>
/// <para>
/// <strong>The "never re-implements the caps" half is what these tests are really for.</strong> Asserting
/// that an army gained some supply would pass against a re-implementation that got the cap slightly
/// wrong. So every expected number below is produced by calling
/// <see cref="AutomaticResupply.ForArmy"/> / <see cref="AutomaticResupply.ForFleet"/> — T38's merged
/// functions, the ones the pass itself calls — and asserting the pass agrees with them exactly, down to
/// the talent. If the pass ever grew a cap of its own, these fail.
/// </para>
/// <para>
/// <strong>And the radius is read from the ruleset, never written as a 4.</strong>
/// <see cref="The_radius_comes_from_the_ruleset_not_from_a_literal"/> moves a city to exactly the
/// ruleset's radius and to one tile beyond it, and asserts the boundary lands where the data says.
/// </para>
/// </remarks>
public sealed class AiResupplyPassTests
{
    private static Ruleset Ruleset => AiScriptedStates.Ruleset;

    private const string ArmyNation = AiScriptedStates.Attacker;
    private const string OtherNation = AiScriptedStates.Defender;

    /// <summary>
    /// A state with one army, one fleet and one city per nation, each unit deliberately short of supply
    /// so a transfer has something to move.
    /// </summary>
    private static GameState SupplyState(
        int armyX = 1,
        int armyY = 1,
        int fleetX = 2,
        int fleetY = 1,
        int ownCityX = 1,
        int ownCityY = 0,
        int foreignCityX = 7,
        int foreignCityY = 5,
        int cityStock = 500,
        int armyMoney = 1000,
        int fleetMoney = 1000)
    {
        var nations = new[]
        {
            AiScriptedStates.AiNation(ArmyNation, AiScriptedStates.DefaultPersonality, treasury: 5000),
            AiScriptedStates.AiNation(OtherNation, AiScriptedStates.DefaultPersonality, treasury: 5000),
        };

        var cities = new[]
        {
            CaptureFixtures.City(
                "own-city", "Own City", ownCityX, ownCityY, ArmyNation, ArmyNation,
                loyalty: 80, fortificationCode: 50, populationThousands: 100,
                maxPopulationThousands: 200, tribute: 10) with { SupplyTons = cityStock },
            CaptureFixtures.City(
                "foreign-city", "Foreign City", foreignCityX, foreignCityY, OtherNation, OtherNation,
                loyalty: 80, fortificationCode: 50, populationThousands: 100,
                maxPopulationThousands: 200, tribute: 10) with { SupplyTons = cityStock },
        };

        var armies = new[]
        {
            CaptureFixtures.Army(
                    "supply-army", ArmyNation, armyX, armyY, morale: 60,
                    CaptureFixtures.Unit("light_infantry", 15000))
                with { Moves = 0, Money = armyMoney, SupplyTons = 0 },
        };

        var fleets = new[]
        {
            BattleTestbed.Fleet("supply-fleet", ArmyNation, fleetX, fleetY, ships: 10, conditionPercent: 100)
                with { Moves = 0, Money = fleetMoney, SupplyTons = 0 },
        };

        return AiScriptedStates.WithActiveSeat(
            BattleCommandTestbed.StateWith(nations, cities, armies, fleets), ArmyNation);
    }

    [Fact]
    public void An_army_resupplies_from_its_own_city_with_exactly_T38s_numbers()
    {
        // No fleet: this test is about the army, and a fleet drawing on the same city would make the
        // city's expected stock a sum of two transfers rather than the one under test.
        var state = SupplyState() with { Fleets = ValueList<FleetState>.Empty };
        var expected = AutomaticResupply.ForArmy(
            state.ArmyById("supply-army")!,
            state.CityById("own-city")!,
            state.NationById(ArmyNation)!,
            state.NationById(ArmyNation)!,
            Ruleset);

        var result = AiResupplyPass.Run(state, Ruleset, ArmyNation);

        Assert.Equal(expected.Army.SupplyTons, result.State.ArmyById("supply-army")!.SupplyTons);
        Assert.Equal(expected.City.SupplyTons, result.State.CityById("own-city")!.SupplyTons);

        // The cap itself: troops div ArmySupplyTonsPerTroops, read off T38's own capacity function.
        Assert.Equal(
            SupplyCapacity.ArmyCapacityTons(15000, Ruleset),
            result.State.ArmyById("supply-army")!.SupplyTons);
    }

    [Fact]
    public void A_fleet_resupplies_from_its_own_city_with_exactly_T38s_numbers()
    {
        var state = SupplyState();
        var result = AiResupplyPass.Run(state, Ruleset, ArmyNation);

        Assert.Equal(1, result.FleetTransfers);
        Assert.Equal(
            SupplyCapacity.FleetCapacityTons(10, Ruleset),
            result.State.FleetById("supply-fleet")!.SupplyTons);
    }

    /// <summary>
    /// The pass pays at a foreign city and takes nothing free — and pays exactly what T38 says, with the
    /// selling nation's treasury credited by the same amount.
    /// </summary>
    [Fact]
    public void A_foreign_non_hostile_city_sells_rather_than_gives()
    {
        // Only the foreign city at (7,5) is within the radius; the own city is parked in the far corner.
        var state = SupplyState(armyX: 5, armyY: 4, fleetX: 6, fleetY: 4, ownCityX: 0, ownCityY: 0);
        var sellerBefore = state.NationById(OtherNation)!.Treasury;

        var result = AiResupplyPass.Run(state, Ruleset, ArmyNation);

        Assert.True(result.TalentsPaid > 0, "a foreign purchase must cost something");
        Assert.Equal(
            sellerBefore + result.TalentsPaid, result.State.NationById(OtherNation)!.Treasury);
    }

    [Fact]
    public void A_hostile_city_supplies_nothing()
    {
        var state = SupplyState(armyX: 5, armyY: 4, fleetX: 6, fleetY: 4, ownCityX: 0, ownCityY: 0);
        var atWar = BattleCommandTestbed.AtWar(state, ArmyNation, OtherNation);

        var result = AiResupplyPass.Run(atWar, Ruleset, ArmyNation);

        Assert.Equal(0, result.ArmyTransfers);
        Assert.Equal(0, result.FleetTransfers);
        Assert.Equal(0, result.State.ArmyById("supply-army")!.SupplyTons);
        Assert.True(
            AiSubstantiveState.AreEquivalent(atWar, result.State),
            "a pass that finds no provider must leave the state untouched");
    }

    /// <summary>
    /// The boundary, read off the ruleset rather than written as a 4: a city at exactly
    /// <c>AutoResupplyRadiusTiles</c> supplies, and one tile further does not.
    /// </summary>
    [Fact]
    public void The_radius_comes_from_the_ruleset_not_from_a_literal()
    {
        var radius = Ruleset.Economy.AutoResupplyRadiusTiles;

        // Chebyshev, so "at the radius" is a pure x offset of exactly `radius` from the army.
        var atRadius = SupplyState(
            armyX: 1, armyY: 2, fleetX: 0, fleetY: 0, ownCityX: 1 + radius, ownCityY: 2,
            foreignCityX: 7, foreignCityY: 5);
        var justBeyond = SupplyState(
            armyX: 1, armyY: 2, fleetX: 0, fleetY: 0, ownCityX: 1 + radius + 1, ownCityY: 2,
            foreignCityX: 7, foreignCityY: 5);

        Assert.Equal(1, AiResupplyPass.Run(atRadius, Ruleset, ArmyNation).ArmyTransfers);
        Assert.Equal(0, AiResupplyPass.Run(justBeyond, Ruleset, ArmyNation).ArmyTransfers);
    }

    /// <summary>
    /// An embarked army is off the map (the original's <c>-1</c> covered-tile sentinel) and is supplied
    /// by the fleet carrying it, not by a city it has no position relative to.
    /// </summary>
    [Fact]
    public void An_embarked_army_is_skipped()
    {
        var state = SupplyState();
        var embarked = state with
        {
            Armies = ValueList.Of(
                state.ArmyById("supply-army")! with { AboardFleetId = "supply-fleet", CoveredTileCode = null }),
            Fleets = ValueList.Of(state.FleetById("supply-fleet")! with { CarriedArmyId = "supply-army" }),
        };

        var result = AiResupplyPass.Run(embarked, Ruleset, ArmyNation);

        Assert.Equal(0, result.ArmyTransfers);
        Assert.Equal(0, result.State.ArmyById("supply-army")!.SupplyTons);
        Assert.Equal(1, result.FleetTransfers);
    }

    [Fact]
    public void A_fleet_still_under_construction_is_skipped()
    {
        var state = SupplyState();
        var building = state with
        {
            Fleets = ValueList.Of(
                state.FleetById("supply-fleet")! with
                {
                    ConstructionTicksRemaining = Ruleset.Naval.ConstructionTicks,
                    BuildCityId = "own-city",
                }),
        };

        var result = AiResupplyPass.Run(building, Ruleset, ArmyNation);

        Assert.Equal(0, result.FleetTransfers);
    }

    /// <summary>
    /// The pass runs inside a real AI turn, not only when a test calls it directly — the Done-when line
    /// says "<em>every AI turn</em>", and this is the assertion that says so.
    /// </summary>
    [Fact]
    public void The_pass_runs_as_part_of_every_ai_turn()
    {
        var state = SupplyState();

        var driven = AiScriptedStates.DriveOneTurn(state);

        Assert.Equal(
            SupplyCapacity.ArmyCapacityTons(15000, Ruleset),
            driven.Outcome.State.ArmyById("supply-army")!.SupplyTons);
        Assert.Equal(
            SupplyCapacity.FleetCapacityTons(10, Ruleset),
            driven.Outcome.State.FleetById("supply-fleet")!.SupplyTons);
        Assert.Contains(driven.Outcome.Log, line => line.StartsWith("resupply:", StringComparison.Ordinal));
    }

    /// <summary>
    /// T38's own-city path is <em>not</em> floored at zero: an army over its cap gives the surplus back
    /// to the city. The pass must inherit that rather than quietly clamping it, which is the single
    /// easiest place for a caller to "helpfully" diverge from the merged rule.
    /// </summary>
    [Fact]
    public void An_over_capacity_army_gives_supply_back_to_its_own_city()
    {
        var capacity = SupplyCapacity.ArmyCapacityTons(15000, Ruleset);
        var state = SupplyState();
        var overSupplied = state with
        {
            Armies = ValueList.Of(state.ArmyById("supply-army")! with { SupplyTons = capacity + 40 }),

            // No fleet, so TonsMoved reports this army's giveback and nothing else.
            Fleets = ValueList<FleetState>.Empty,
        };
        var cityBefore = overSupplied.CityById("own-city")!.SupplyTons;

        var result = AiResupplyPass.Run(overSupplied, Ruleset, ArmyNation);

        Assert.Equal(capacity, result.State.ArmyById("supply-army")!.SupplyTons);
        Assert.Equal(cityBefore + 40, result.State.CityById("own-city")!.SupplyTons);
        Assert.Equal(-40, result.ArmyGivebackTons());
    }

    /// <summary>
    /// Ordering is load-bearing, and stated: two armies drawing on one city's limited stock get served in
    /// <see cref="GameState.Armies"/> order, so the first takes what it can and the second takes what is
    /// left. A pass that computed every transfer against the starting state would over-draw the city.
    /// </summary>
    [Fact]
    public void A_citys_stock_is_shared_in_state_order_not_duplicated()
    {
        var state = SupplyState(cityStock: 200);
        var twoArmies = state with
        {
            Armies = ValueList.Of(
                state.ArmyById("supply-army")!,
                state.ArmyById("supply-army")! with { Id = "supply-army-2" }),
            Fleets = ValueList<FleetState>.Empty,
        };

        var result = AiResupplyPass.Run(twoArmies, Ruleset, ArmyNation);

        var first = result.State.ArmyById("supply-army")!.SupplyTons;
        var second = result.State.ArmyById("supply-army-2")!.SupplyTons;
        var city = result.State.CityById("own-city")!.SupplyTons;

        Assert.Equal(SupplyCapacity.ArmyCapacityTons(15000, Ruleset), first);
        Assert.Equal(200 - first - second, city);
        Assert.True(city >= 0, $"the city cannot go below zero stock; it holds {city}");
    }

    /// <summary>
    /// <c>docs/task-catalogue.md</c> T60 Done-when 3, and the whole of issue #259's root cause: an army
    /// already at its supply capacity is paired with no city, so <see cref="AutomaticResupply"/>'s purse
    /// hygiene does not move <see cref="EconomyRules.AutoResupplyPurseTopUpAmount"/> out of the
    /// treasury for a visit that delivered nothing.
    /// </summary>
    /// <remarks>
    /// The treasury here is deliberately smaller than one top-up grant, which is the soak's own
    /// situation: <c>toy-3city</c>'s two nations start on 450 and 500 talents against a grant of 500. So
    /// the old behaviour did not merely shave the treasury, it emptied it on the AI's first turn, and
    /// <see cref="AiEconomyPhase.TurnBudget"/> was at or below zero for the rest of the game.
    /// </remarks>
    [Fact]
    public void An_army_already_at_capacity_is_not_paired_with_a_city_at_all()
    {
        var capacity = SupplyCapacity.ArmyCapacityTons(15000, Ruleset);
        var grant = Ruleset.Economy.AutoResupplyPurseTopUpAmount;
        var treasury = grant - 50;

        var state = SupplyState(armyMoney: 0) with { Fleets = ValueList<FleetState>.Empty };
        state = state with
        {
            Armies = ValueList.Of(state.ArmyById("supply-army")! with { SupplyTons = capacity }),
            Nations = ValueList.Of(
                state.NationById(ArmyNation)! with { Treasury = treasury },
                state.NationById(OtherNation)!),
        };

        var result = AiResupplyPass.Run(state, Ruleset, ArmyNation);

        Assert.Equal(0, result.ArmyTransfers);
        Assert.Equal(0, result.TonsMoved);
        Assert.Equal(treasury, result.State.NationById(ArmyNation)!.Treasury);
        Assert.Equal(0, result.State.ArmyById("supply-army")!.Money);
        Assert.Equal(capacity, result.State.ArmyById("supply-army")!.SupplyTons);
        Assert.True(
            AiSubstantiveState.AreEquivalent(state, result.State),
            "a pass with nothing to move must leave the state untouched");
    }

    /// <summary>
    /// The fleet half of the same guard (follow-up <see href="https://github.com/diegoami/imperial_conquest_2/issues/272">#272</see>
    /// N2): both existing zero-ton cases above (<see cref="A_hostile_city_supplies_nothing"/> and
    /// <see cref="An_embarked_army_is_skipped"/>) leave <c>Fleets</c> empty, so nothing has ever pinned the
    /// fleet loop's own <c>MovesNothing</c> check. A fleet already at its supply capacity, paired with a
    /// city, must not have T38's purse hygiene grant it a top-up for a visit that moved nothing.
    /// </summary>
    [Fact]
    public void A_fleet_already_at_capacity_is_not_paired_with_a_city_at_all()
    {
        var capacity = SupplyCapacity.FleetCapacityTons(10, Ruleset);
        var grant = Ruleset.Economy.AutoResupplyPurseTopUpAmount;
        var treasury = grant - 50;

        var state = SupplyState(fleetMoney: 0) with { Armies = ValueList<ArmyState>.Empty };
        state = state with
        {
            Fleets = ValueList.Of(state.FleetById("supply-fleet")! with { SupplyTons = capacity }),
            Nations = ValueList.Of(
                state.NationById(ArmyNation)! with { Treasury = treasury },
                state.NationById(OtherNation)!),
        };

        var result = AiResupplyPass.Run(state, Ruleset, ArmyNation);

        Assert.Equal(0, result.FleetTransfers);
        Assert.Equal(0, result.TonsMoved);
        Assert.Equal(treasury, result.State.NationById(ArmyNation)!.Treasury);
        Assert.Equal(0, result.State.FleetById("supply-fleet")!.Money);
        Assert.Equal(capacity, result.State.FleetById("supply-fleet")!.SupplyTons);
        Assert.True(
            AiSubstantiveState.AreEquivalent(state, result.State),
            "a pass with nothing to move must leave the state untouched");
    }

    /// <summary>
    /// The other half of the same rule, so the skip cannot quietly become "the AI never tops up a purse
    /// again": a transfer that <em>does</em> move supply is applied whole, purse hygiene included.
    /// </summary>
    [Fact]
    public void A_transfer_that_moves_supply_still_carries_T38s_purse_hygiene()
    {
        var grant = Ruleset.Economy.AutoResupplyPurseTopUpAmount;
        var treasury = grant * 4;

        var state = SupplyState(armyMoney: 0) with { Fleets = ValueList<FleetState>.Empty };
        state = state with
        {
            Nations = ValueList.Of(
                state.NationById(ArmyNation)! with { Treasury = treasury },
                state.NationById(OtherNation)!),
        };

        var result = AiResupplyPass.Run(state, Ruleset, ArmyNation);

        Assert.Equal(1, result.ArmyTransfers);
        Assert.True(result.TonsMoved > 0, "the army started empty, so supply must have moved");
        Assert.Equal(grant, result.State.ArmyById("supply-army")!.Money);
        Assert.Equal(treasury - grant, result.State.NationById(ArmyNation)!.Treasury);
    }
}

/// <summary>Small readability helper for the giveback assertion above.</summary>
internal static class AiResupplyResultExtensions
{
    /// <summary>The pass's net admitted tons, which is negative when supply flowed back to the city.</summary>
    public static int ArmyGivebackTons(this AiResupplyPass.Result result) => result.TonsMoved;
}
