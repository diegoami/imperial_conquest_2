using IC2.Engine.Calendar;
using IC2.Engine.Core;
using IC2.Engine.Economy;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Economy;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T37 City supply production and famine unrest", <see
/// cref="WeeklyCitySupplySystem"/> wired against real <see cref="GameState"/> through the real <see
/// cref="TurnCoordinator"/> — the only way to reach an <see cref="IGameSystem"/> at all, since
/// <see cref="SystemContext"/>'s constructor is internal and no test builds one directly. Covers
/// Done-when 4 (the "asserted end to end" requirement <see cref="CityWeeklySupplyTests"/> cannot meet on
/// its own), Done-when 5's city-index-order requirement, and Done-when 6's quarter-boundary ordering.
/// </summary>
public sealed class WeeklyCitySupplySystemTests
{
    private const int Spring = 0;
    private const int Summer = 1;
    private const int Winter = 3;

    private static GameState WithArmyAt(GameState state, string armyId, int x, int y, string nation) =>
        state with
        {
            Armies = ValueList.From(state.Armies.Select(a => a.Id == armyId
                ? a with { X = x, Y = y, Nation = nation, AboardFleetId = null, CoveredTileCode = 2 }
                : a)),
        };

    private static GameState AtWar(GameState state, string a, string b) =>
        state with { Relations = state.Relations.WithRelation(a, b, EconomyTestbed.Ruleset.Diplomacy.StateCodes.War) };

    private static GameState WithSeason(GameState state, int seasonIndex) =>
        state with { Calendar = state.Calendar with { SeasonIndex = seasonIndex } };

    // ---- Done when 4: "asserted end to end" -- a real hostile army placed adjacent, run through the
    // registered WeeklyCitySupplySystem, never a threatened literal passed to the rule. ----

    [Fact]
    public void AThreatenedArx_GainsNothingInSummer_ButStillLosesInWinter_WithARealAdjacentHostileArmy()
    {
        var coordinator = EconomyTestbed.CoordinatorOnly(null, typeof(WeeklyCitySupplySystem));

        // south-army-1 lands one cell from Arx (2,1) and is put at war with Arx's owner (north) -- the
        // same real HostileArmyAdjacent.IsThreatened path DoD 9's growth check uses, reused here, never
        // a `threatened: true` literal handed to CityWeeklySupply directly.
        GameState ThreatenedArxAt(int seasonIndex)
        {
            var state = EconomyTestbed.InitialState();
            state = WithArmyAt(state, "south-army-1", x: 2, y: 2, nation: "south");
            state = AtWar(state, "north", "south");
            return WithSeason(state, seasonIndex);
        }

        var summerResult = coordinator.RunRoundTick(ThreatenedArxAt(Summer)).State;
        Assert.Equal(400, summerResult.CityById("arx")!.SupplyTons); // unchanged: gain clamped to <= 0.

        var winterResult = coordinator.RunRoundTick(ThreatenedArxAt(Winter)).State;
        // pop 220, mob 20 (toy defaults): s = 220*(20-40)/10 = -440; inc = -440 - (-440*20/200) = -396.
        Assert.Equal(4, winterResult.CityById("arx")!.SupplyTons); // 400 - 396 -- still loses.
    }

    [Fact]
    public void TheSameCityOneCellFurtherAway_IsNotThreatened_AndGrowsNormallyInSummer()
    {
        var coordinator = EconomyTestbed.CoordinatorOnly(null, typeof(WeeklyCitySupplySystem));

        var state = EconomyTestbed.InitialState();
        state = WithArmyAt(state, "south-army-1", x: 2, y: 3, nation: "south"); // two cells from Arx (2,1).
        state = AtWar(state, "north", "south");
        state = WithSeason(state, Summer);

        var result = coordinator.RunRoundTick(state).State;

        // pop 220, mob 20: s = 220*40/10 = 880; inc = 880 - 880*20/200 = 792; capped at pop*10 = 2200.
        Assert.Equal(1192, result.CityById("arx")!.SupplyTons); // 400 + 792, not clamped -- a real gain.
    }

    // ---- Done when 5, the system-level half: exactly one draw per qualifying city, in city (list)
    // index order -- proved by independently re-deriving this system's own real RNG stream (the same
    // SplitMix64Rng.ForStream(turnSeed, StreamNameFor(...)) the coordinator itself constructs) and
    // replaying it in city-list order, skipping the one city that does not qualify. ----

    [Fact]
    public void FamineDraws_HappenOnlyForQualifyingCities_InCityListOrder()
    {
        var state = EconomyTestbed.InitialState();

        // Arx and Meridia end this Winter turn's production at exactly 0 (qualify); Portus keeps stock
        // left over (does not qualify). Cities list order is [arx, portus, meridia], so a correct
        // implementation draws for arx, skips portus, then draws for meridia.
        var nations = state.Nations.Select(n => n with { MobilizedPercent = 0 }); // isolates s = pop*(v-40)/10.
        state = state with { Nations = ValueList.From(nations) };

        var cities = state.Cities.Select(c => c.Id switch
        {
            // pop 10, Winter (v=20), mob 0: s = 10*(20-40)/10 = -20; starting at 20 ends exactly at 0.
            "arx" => c with { PopulationThousands = 10, MaxPopulationThousands = 100, SupplyTons = 20 },
            "meridia" => c with { PopulationThousands = 10, MaxPopulationThousands = 100, SupplyTons = 20 },
            // Same formula, but starting well above zero: ends at 80, still holding stock.
            "portus" => c with { PopulationThousands = 10, MaxPopulationThousands = 100, SupplyTons = 100 },
            _ => c,
        });
        state = state with { Cities = ValueList.From(cities), Armies = ValueList<ArmyState>.Empty };
        state = WithSeason(state, Winter);

        // A fixed seed chosen (see this file's history) so this stream's first three NextChance(1,3)
        // draws are True, True, False: distinct enough at positions 2 and 3 that a wrong implementation
        // which also drew for the non-qualifying Portus (consuming position 2 for Portus and shifting
        // Meridia to position 3) is guaranteed to disagree with the assertions below, not just
        // coincidentally happen to match them.
        state = state with { RandomSeed = 1 };

        Assert.Equal(new[] { "arx", "portus", "meridia" }, state.Cities.Select(c => c.Id));

        var coordinator = EconomyTestbed.CoordinatorOnly(null, typeof(WeeklyCitySupplySystem));
        var result = coordinator.RunRoundTick(state).State;

        Assert.Equal(0, result.CityById("arx")!.SupplyTons);
        Assert.Equal(80, result.CityById("portus")!.SupplyTons); // did not qualify: no draw either way.
        Assert.Equal(0, result.CityById("meridia")!.SupplyTons);

        // Re-derive exactly the stream WeeklyCitySupplySystem drew from, and replay it in city-list
        // order over only the two qualifying cities -- the same construction TurnCoordinator.Run uses
        // internally (RngStreams.Advance the root seed once per RunRoundTick, then ForStream by this
        // system's declared id and phase).
        var turnSeed = RngStreams.Advance(state.RandomSeed);
        var expectedStream = SplitMix64Rng.ForStream(
            turnSeed, TurnCoordinator.StreamNameFor("economy.city-supply-production", TurnPhase.CityTick));

        var arxDraw = expectedStream.NextChance(1, EconomyTestbed.Ruleset.Economy.FamineLoyaltyLossProbabilityDenominator);
        var meridiaDraw = expectedStream.NextChance(1, EconomyTestbed.Ruleset.Economy.FamineLoyaltyLossProbabilityDenominator);

        var arxBefore = state.CityById("arx")!.Loyalty;
        var meridiaBefore = state.CityById("meridia")!.Loyalty;
        var portusBefore = state.CityById("portus")!.Loyalty;

        Assert.Equal(arxBefore + (arxDraw ? -1 : 0), result.CityById("arx")!.Loyalty);
        Assert.Equal(meridiaBefore + (meridiaDraw ? -1 : 0), result.CityById("meridia")!.Loyalty);
        Assert.Equal(portusBefore, result.CityById("portus")!.Loyalty); // never drew: never changes.
    }

    // ---- Done when 6: across a scripted Winter -> Spring wrap, the step uses Winter's value and the
    // population from before T35's quarterly growth. ----

    [Fact]
    public void AcrossAWinterToSpringWrap_TheStepUsesWintersValue_AndThePreGrowthPopulation()
    {
        var state = EconomyTestbed.InitialState();

        // Arx's numbers, reused from QuarterlyEconomyStepOrderTests: gap/tax/mobilization chosen so
        // growing this quarter (40 -> 66) is itself proven elsewhere; here they also make "used the
        // grown population" (66) and "used the new season's value" (Spring, v=50) each predict a
        // different, wrong result from the correct one (below).
        var cities = state.Cities.Select(c => c.Id switch
        {
            "arx" => c with { PopulationThousands = 40, MaxPopulationThousands = 160, Tribute = 160, SupplyTons = 100 },
            "portus" => c with { Owner = "south", Allegiance = "south" }, // out of the way, as in that test.
            _ => c,
        });
        state = state with
        {
            Cities = ValueList.From(cities),
            Armies = ValueList<ArmyState>.Empty,
            Fleets = ValueList<FleetState>.Empty,
        };

        var nations = state.Nations.Select(n => n.Id == "north"
            ? n with { TaxRatePercent = 0, MobilizedPercent = 50, Unity = 500 }
            : n);
        state = state with { Nations = ValueList.From(nations) };

        // Week 11 (SeasonAdvanceFromWeek) in Winter: this round's advance is the one that wraps to Spring.
        state = state with
        {
            Calendar = state.Calendar with { Week = EconomyTestbed.Ruleset.Calendar.SeasonAdvanceFromWeek, SeasonIndex = Winter },
        };

        var coordinator = EconomyTestbed.CoordinatorOnly(
            null, typeof(WeeklyCitySupplySystem), typeof(CalendarSystem), typeof(QuarterlyCityEconomySystem));
        var after = coordinator.RunRoundTick(state).State;

        var arxAfter = after.CityById("arx")!;

        // pop 40 (pre-growth), Winter (v=20), mob 50 (pre-decay): s = 40*(20-40)/10 = -80;
        // inc = -80 - (-80*50/200) = -60; 100 - 60 = 40.
        // Using the grown population (66) instead would give 1; using Spring's value (50) instead of
        // Winter's would give 130 -- both clearly distinct from the correct 40.
        Assert.Equal(40, arxAfter.SupplyTons);
        Assert.Equal(66, arxAfter.PopulationThousands); // growth still ran, after supply production, in the same round.
        Assert.Equal(Spring, after.Calendar.SeasonIndex); // confirms the wrap actually happened.
    }
}
