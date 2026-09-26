using System.Linq;
using IC2.Engine.Cities.Capture;
using IC2.Engine.Core;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Cities.Capture;

/// <summary>
/// T86 Done-when 2: one test per effect in the Scope's own numbered list, every one driven through the
/// real <see cref="CityCaptureResolver.Capture"/> command — never <see cref="ConquestCascade.Apply"/>
/// called directly — so a mutation to <see cref="CityCaptureResolver"/>'s own wiring into
/// <see cref="ConquestTrigger"/>/<see cref="ConquestCascade"/> is caught here too.
/// </summary>
public sealed class ConquestCascadeTests
{
    private const string Loser = "loser";
    private const string Winner = "winner";

    private static Ruleset Ruleset => CaptureTestbed.Ruleset;

    /// <summary>
    /// The always-conquering baseline: the loser owns the captured city plus 4 others (5 total), so
    /// losing the captured one leaves 4 -- under <see cref="CaptureRules.ConquestCityCountThreshold"/>
    /// (6) -- and the loser's capital is deliberately one of the surviving 4 cities, never the captured
    /// one, to keep every test in this file on the simpler non-capital trigger, not the capital-move
    /// branch <see cref="ConquestTriggerTests"/> already covers.
    /// </summary>
    private static (GameState State, string CapturedCityId) BuildConqueringScenario(
        int loserTreasury = 0, int loserUnity = 668, int winnerUnity = 600)
    {
        var capturedCity = CaptureTestbed.City(
            "captured", "Captured", 0, 0, Loser, Loser, loyalty: 30, fortificationCode: 0,
            populationThousands: 10, maxPopulationThousands: 20, tribute: 10);
        var capitalCity = CaptureTestbed.City(
            "capital", "Capital", 5, 5, Loser, Loser, loyalty: 90, fortificationCode: 0,
            populationThousands: 8, maxPopulationThousands: 20, tribute: 4);

        // 3 more filler cities, loyalty 90 (above CascadeLoyaltyThreshold) so the regular defection
        // cascade never sweeps one away and silently changes the loser's own remaining city count.
        var fillers = new[]
        {
            CaptureTestbed.City("filler-0", "Filler 0", 1000, 1000, Loser, Loser, 90, 0, 5, 10, 0),
            CaptureTestbed.City("filler-1", "Filler 1", 1001, 1000, Loser, Loser, 90, 0, 5, 10, 0),
            CaptureTestbed.City("filler-2", "Filler 2", 1002, 1000, Loser, Loser, 90, 0, 5, 10, 0),
        };

        var loser = CaptureTestbed.Nation(Loser, treasury: loserTreasury, unity: loserUnity, capitalCityId: "capital");
        var winner = CaptureTestbed.Nation(Winner, unity: winnerUnity);
        var attacker = CaptureTestbed.Army(
            "army", Winner, 0, 0, morale: 50, CaptureTestbed.Unit("heavy_infantry", 1_000_000));

        var allCities = new[] { capturedCity, capitalCity }.Concat(fillers).ToArray();
        var state = CaptureTestbed.StateWith(new[] { loser, winner }, allCities, new[] { attacker });

        return (state, "captured");
    }

    private static GameState Capture((GameState State, string CapturedCityId) scenario, IEventSink events) =>
        CityCaptureResolver.Capture(
            scenario.State, "army", scenario.CapturedCityId, Ruleset,
            CaptureTestbed.ArcherUnitTypeId, CaptureTestbed.FortifyOrderId, events);

    // ---- Effect 1: every loser city moves to the winner, with the conquest's own loyalty formula. ----

    [Fact]
    public void EveryLoserCity_MovesToTheWinner()
    {
        var scenario = BuildConqueringScenario();
        var result = Capture(scenario, NullEventSink.Instance);

        foreach (var id in new[] { "captured", "capital", "filler-0", "filler-1", "filler-2" })
        {
            Assert.Equal(Winner, result.CityById(id)!.Owner);
        }

        Assert.Equal(5, result.CountCitiesOwnedBy(Winner));
        Assert.Equal(0, result.CountCitiesOwnedBy(Loser));
    }

    /// <summary>
    /// Effect 1's own non-allegiant loyalty formula, on a city the captured city's own siege never
    /// touches (a filler, not "captured" itself, which the single-city capture formula already sets
    /// before conquest's own loop -- see this class' own remarks on <see cref="ConquestCascade"/>):
    /// min(70, max(40, 100 - L)), plus one independent Random(6) draw derived from GameState.RandomSeed
    /// and the city's own id.
    /// </summary>
    [Fact]
    public void EveryLoserCity_GetsTheConquestLoyaltyFormula_NonAllegiant()
    {
        var scenario = BuildConqueringScenario();
        var seed = scenario.State.RandomSeed;
        var result = Capture(scenario, NullEventSink.Instance);

        // filler-0's own allegiance is Loser (set by CaptureTestbed.City's owner==allegiance default),
        // which differs from the new owner (Winner): non-allegiant branch, L = 90 -> target = 100-90=10,
        // clamped up to the floor 40.
        var expectedBonus = SplitMix64Rng.ForStream(seed, "capture.conquestLoyalty").ForStream("filler-0")
            .NextInt(Ruleset.Capture.ConquestLoyaltyRandomBonusMax);
        Assert.Equal(40 + expectedBonus, result.CityById("filler-0")!.Loyalty);
    }

    /// <summary>The allegiant branch of the same formula: min(80, 120 - L).</summary>
    [Fact]
    public void EveryLoserCity_GetsTheConquestLoyaltyFormula_Allegiant()
    {
        var capturedCity = CaptureTestbed.City(
            "captured", "Captured", 0, 0, Loser, Loser, loyalty: 30, fortificationCode: 0,
            populationThousands: 10, maxPopulationThousands: 20, tribute: 10);
        var capitalCity = CaptureTestbed.City(
            "capital", "Capital", 5, 5, Loser, Loser, loyalty: 90, fortificationCode: 0,
            populationThousands: 8, maxPopulationThousands: 20, tribute: 4);
        // Allegiance already matches the winner -- the allegiant clamp fires.
        var allegiantFiller = CaptureTestbed.City(
            "allegiant-filler", "Allegiant Filler", 1000, 1000, Loser, Winner, loyalty: 50, fortificationCode: 0,
            populationThousands: 5, maxPopulationThousands: 10, tribute: 0);

        var loser = CaptureTestbed.Nation(Loser, unity: 668, capitalCityId: "capital");
        var winner = CaptureTestbed.Nation(Winner);
        var attacker = CaptureTestbed.Army(
            "army", Winner, 0, 0, morale: 50, CaptureTestbed.Unit("heavy_infantry", 1_000_000));

        var state = CaptureTestbed.StateWith(
            new[] { loser, winner }, new[] { capturedCity, capitalCity, allegiantFiller }, new[] { attacker });
        var seed = state.RandomSeed;

        var result = CityCaptureResolver.Capture(
            state, "army", "captured", Ruleset, CaptureTestbed.ArcherUnitTypeId, CaptureTestbed.FortifyOrderId,
            NullEventSink.Instance);

        // min(80, 120 - 50) = min(80, 70) = 70.
        var expectedBonus = SplitMix64Rng.ForStream(seed, "capture.conquestLoyalty").ForStream("allegiant-filler")
            .NextInt(Ruleset.Capture.ConquestLoyaltyRandomBonusMax);
        Assert.Equal(70 + expectedBonus, result.CityById("allegiant-filler")!.Loyalty);
    }

    /// <summary>Effect 1's own per-city credits: wealth, tax base and treasury, on the same filler as the loyalty test above.</summary>
    [Fact]
    public void EveryLoserCity_CreditsTheWinnersWealthTaxBaseAndTreasury()
    {
        var scenario = BuildConqueringScenario();
        var winnerBefore = scenario.State.NationById(Winner)!;
        var filler0 = scenario.State.CityById("filler-0")!; // tribute 0, contribution 0 -- credits nothing extra.
        var capital = scenario.State.CityById("capital")!; // tribute 4, population 8, max 20 -> contribution 4*8/20 = 1.

        var result = Capture(scenario, NullEventSink.Instance);
        var winnerAfter = result.NationById(Winner)!;

        // "captured" (tribute 10, pop 10, max 20 -> contribution 5) is credited by Capture's own
        // single-city formula (CaptureTreasuryCreditMultiplier 4, TaxBaseContributionMultiplier 4); the
        // conquest cascade's own loop then separately credits "capital" (contribution 1) and the three
        // fillers (contribution 0 each) through its own multipliers (ConquestTreasuryCreditMultiplier 6,
        // the same generic TaxBaseContributionMultiplier 4, WealthPerPopulationThousand for wealth).
        var economy = Ruleset.Economy;
        var expectedTreasury = winnerBefore.Treasury
            + (5 * Ruleset.Capture.CaptureTreasuryCreditMultiplier) // "captured"
            + (1 * Ruleset.Capture.ConquestTreasuryCreditMultiplier); // "capital" (fillers contribute 0)
        var expectedTaxBase = winnerBefore.TaxBase
            + (5 * economy.TaxBaseContributionMultiplier)
            + (1 * economy.TaxBaseContributionMultiplier);
        var expectedWealthDelta = (10 * economy.WealthPerPopulationThousand) // "captured"
            + (8 * economy.WealthPerPopulationThousand) // "capital"
            + (3 * 5 * economy.WealthPerPopulationThousand); // 3 fillers, population 5 each

        Assert.Equal(expectedTreasury, winnerAfter.Treasury);
        Assert.Equal(expectedTaxBase, winnerAfter.TaxBase);
        Assert.Equal(winnerBefore.Wealth + expectedWealthDelta, winnerAfter.Wealth);
    }

    // ---- Effect 2: winner unity += 50, capped at EconomyRules.UnityCap. ----

    [Fact]
    public void WinnerUnity_GainsFifty_Uncapped()
    {
        var scenario = BuildConqueringScenario(winnerUnity: 600);
        var result = Capture(scenario, NullEventSink.Instance);

        // 600, plus the ordinary single-city capture's own +9 (CaptureUnityGain, from transferring
        // "captured" itself, before conquest even evaluates), plus conquest's own separate +50.
        Assert.Equal(
            600 + Ruleset.Capture.CaptureUnityGain + Ruleset.Capture.ConquestWinnerUnityGain,
            result.NationById(Winner)!.Unity);
    }

    [Fact]
    public void WinnerUnity_IsCappedAtTheUnityCap()
    {
        var scenario = BuildConqueringScenario(winnerUnity: Ruleset.Economy.UnityCap - 10);
        var result = Capture(scenario, NullEventSink.Instance);

        Assert.Equal(Ruleset.Economy.UnityCap, result.NationById(Winner)!.Unity);
    }

    // ---- Effect 3: the loser's positive treasury is copied (not moved) to the winner. ----

    [Fact]
    public void APositiveLoserTreasury_IsCopiedToTheWinner_AndTheLosersOwnBalanceIsUnchanged()
    {
        var scenario = BuildConqueringScenario(loserTreasury: 500);
        var winnerBefore = scenario.State.NationById(Winner)!;

        var result = Capture(scenario, NullEventSink.Instance);

        // The winner's own per-city credits still apply on top -- isolate the treasury-copy term itself
        // by checking it is at least 500 more than it would otherwise be. A separate, treasury-0 scenario
        // (the other tests in this file) already pins the per-city credit alone.
        var winnerAfter = result.NationById(Winner)!;
        var zeroTreasuryScenario = BuildConqueringScenario(loserTreasury: 0);
        var zeroTreasuryResult = Capture(zeroTreasuryScenario, NullEventSink.Instance);
        var winnerWithNoCopy = zeroTreasuryResult.NationById(Winner)!;

        Assert.Equal(winnerWithNoCopy.Treasury + 500, winnerAfter.Treasury);

        // The loser's own treasury is copied, not moved -- it still reads 500, not 0.
        Assert.Equal(500, result.NationById(Loser)!.Treasury);
        Assert.True(winnerBefore.Treasury < winnerAfter.Treasury);
    }

    [Fact]
    public void ANonPositiveLoserTreasury_IsNotCopied()
    {
        var scenario = BuildConqueringScenario(loserTreasury: 0);
        var zeroResult = Capture(scenario, NullEventSink.Instance);

        var negativeScenario = BuildConqueringScenario(loserTreasury: -200);
        var negativeResult = Capture(negativeScenario, NullEventSink.Instance);

        // Same winner treasury whether the loser's own balance is 0 or negative -- neither is copied.
        Assert.Equal(zeroResult.NationById(Winner)!.Treasury, negativeResult.NationById(Winner)!.Treasury);
        Assert.Equal(-200, negativeResult.NationById(Loser)!.Treasury);
    }

    // ---- Effect 4a: the relation reset (T69's helper), reused as-is. ----

    [Fact]
    public void TheRelationReset_RunsOnConquest()
    {
        const string Bystander = "bystander";
        var scenario = BuildConqueringScenario();
        var bystander = CaptureTestbed.Nation(Bystander);
        var stateWithBystander = scenario.State with
        {
            Nations = ValueList.From(scenario.State.Nations.Append(bystander)),
        };
        var codes = Ruleset.Diplomacy.StateCodes;
        stateWithBystander = stateWithBystander with
        {
            Relations = DiplomaticRelations
                .Uniform(ValueList.Of(Loser, Winner, Bystander), codes.Peace)
                .WithRelation(Loser, Bystander, codes.Alliance),
        };

        var result = Capture((stateWithBystander, scenario.CapturedCityId), NullEventSink.Instance);

        Assert.Equal(Ruleset.Diplomacy.CooldownAfterBrokenAlliance, result.Relations.Get(Loser, Bystander));
    }

    // ---- Effect 4b: the neighbour merge -- every nation that bordered the loser, except the winner,
    // becomes a neighbour of the winner, both ways. The loser's own entry is untouched. ----

    [Fact]
    public void TheNeighbourMerge_AddsTheLosersOtherNeighboursToTheWinner_BothWays()
    {
        const string Bystander = "bystander";
        var scenario = BuildConqueringScenario();
        var bystander = CaptureTestbed.Nation(Bystander);
        var stateWithNeighbours = scenario.State with
        {
            Nations = ValueList.From(scenario.State.Nations.Append(bystander)),
            Neighbours = ValueList.Of(
                new NationNeighbours(Loser, ValueList.Of(Bystander)),
                new NationNeighbours(Winner, ValueList<string>.Empty),
                new NationNeighbours(Bystander, ValueList.Of(Loser))),
        };

        var result = Capture((stateWithNeighbours, scenario.CapturedCityId), NullEventSink.Instance);

        var neighbours = result.Neighbours!;
        var winnerEntry = neighbours.Single(e => e.NationId == Winner);
        var bystanderEntry = neighbours.Single(e => e.NationId == Bystander);
        var loserEntry = neighbours.Single(e => e.NationId == Loser);

        Assert.Contains(Bystander, winnerEntry.NeighbourIds);
        Assert.Contains(Winner, bystanderEntry.NeighbourIds);
        // The loser's own entry is never touched -- it still lists Bystander (dat-neighbour-mask.md §4).
        Assert.Contains(Bystander, loserEntry.NeighbourIds);
    }

    /// <summary>
    /// T86 Done-when 3: "merged on conquest only (a test for defection shows no merge)". A defection that
    /// takes a nation's last city never merges neighbours, even though it eliminates the nation exactly
    /// as conquest does -- <c>dat-neighbour-mask.md</c> §3's own whole-program scan finds no <c>+0x46</c>
    /// access anywhere in <c>FUN_0044BED8</c>.
    /// </summary>
    [Fact]
    public void Defection_OfTheLastCity_DoesNotMergeNeighbours()
    {
        const string Doomed = "doomed-defect-neighbours";
        const string NewOwner = "new-owner-defect-neighbours";
        const string Bystander = "bystander-defect-neighbours";

        var city = CaptureTestbed.City("c1", "City", 0, 0, Doomed, Doomed, 30, 0, 10, 20, 5);
        var doomed = CaptureTestbed.Nation(Doomed, unity: 668, capitalCityId: "c1");
        var newOwner = CaptureTestbed.Nation(NewOwner);
        var bystander = CaptureTestbed.Nation(Bystander);

        var state = CaptureTestbed.StateWith(new[] { doomed, newOwner, bystander }, new[] { city }) with
        {
            Neighbours = ValueList.Of(
                new NationNeighbours(Doomed, ValueList.Of(Bystander)),
                new NationNeighbours(NewOwner, ValueList<string>.Empty),
                new NationNeighbours(Bystander, ValueList.Of(Doomed))),
        };

        var result = CityCaptureResolver.Defect(state, "c1", NewOwner, Ruleset, NullEventSink.Instance);

        Assert.True(result.NationById(Doomed)!.Eliminated);
        var newOwnerEntry = result.Neighbours!.Single(e => e.NationId == NewOwner);
        Assert.DoesNotContain(Bystander, newOwnerEntry.NeighbourIds);
        var bystanderEntry = result.Neighbours!.Single(e => e.NationId == Bystander);
        Assert.DoesNotContain(NewOwner, bystanderEntry.NeighbourIds);
    }

    // ---- Effect 5: forces disposed (T84's own helper), the winner receiving any fleet under construction. ----

    [Fact]
    public void LoserForces_AreDisposed_AndAConstructionFleetGoesToTheWinner()
    {
        var capturedCity = CaptureTestbed.City(
            "captured", "Captured", 0, 0, Loser, Loser, loyalty: 30, fortificationCode: 0,
            populationThousands: 10, maxPopulationThousands: 20, tribute: 10);
        var capitalCity = CaptureTestbed.City(
            "capital", "Capital", 5, 5, Loser, Loser, loyalty: 90, fortificationCode: 0,
            populationThousands: 8, maxPopulationThousands: 20, tribute: 4);
        var fillers = new[]
        {
            CaptureTestbed.City("filler-0", "Filler 0", 1000, 1000, Loser, Loser, 90, 0, 5, 10, 0),
            CaptureTestbed.City("filler-1", "Filler 1", 1001, 1000, Loser, Loser, 90, 0, 5, 10, 0),
            CaptureTestbed.City("filler-2", "Filler 2", 1002, 1000, Loser, Loser, 90, 0, 5, 10, 0),
        };

        var loser = CaptureTestbed.Nation(Loser, unity: 668, capitalCityId: "capital");
        var winner = CaptureTestbed.Nation(Winner);
        var attacker = CaptureTestbed.Army(
            "army", Winner, 0, 0, morale: 50, CaptureTestbed.Unit("heavy_infantry", 1_000_000));
        var loserArmy = CaptureTestbed.Army("loser-army", Loser, 20, 20, morale: 40, CaptureTestbed.Unit("light_infantry", 300));
        var loserFleet = EliminationForcesTestbed.Fleet("loser-fleet", Loser, 21, 21);
        var loserConstructionFleet = EliminationForcesTestbed.Fleet("loser-construction-fleet", Loser, 0, 0) with
        {
            ConstructionTicksRemaining = Ruleset.Naval.ConstructionTicks,
            BuildCityId = "capital",
        };

        var allCities = new[] { capturedCity, capitalCity }.Concat(fillers).ToArray();
        var state = EliminationForcesTestbed.StateWith(
            new[] { loser, winner },
            allCities,
            new[] { attacker, loserArmy },
            new[] { loserFleet, loserConstructionFleet });

        var result = CityCaptureResolver.Capture(
            state, "army", "captured", Ruleset, CaptureTestbed.ArcherUnitTypeId, CaptureTestbed.FortifyOrderId,
            NullEventSink.Instance);

        Assert.True(result.NationById(Loser)!.Eliminated);
        Assert.Null(result.ArmyById("loser-army"));
        Assert.Null(result.FleetById("loser-fleet"));
        var transferred = result.FleetById("loser-construction-fleet");
        Assert.NotNull(transferred);
        Assert.Equal(Winner, transferred!.Nation);
        Assert.True(transferred.IsUnderConstruction);
    }

    // ---- Effect 8: all of the loser's recruitment slots are wiped, not only ones at the captured city. ----

    [Fact]
    public void AllOfTheLosersRecruitmentSlots_AreWiped_EvenAtCitiesOtherThanTheCapturedOne()
    {
        var slotAtCapturedCity = new RecruitmentSlot("captured", "heavy_infantry", 400, StateCode: 4);
        var slotAtAnotherCity = new RecruitmentSlot("filler-0", "heavy_infantry", 300, StateCode: 4);
        var zeroTroopSlot = new RecruitmentSlot("filler-1", "light_infantry", 0, StateCode: 4);

        var capturedCity = CaptureTestbed.City(
            "captured", "Captured", 0, 0, Loser, Loser, loyalty: 30, fortificationCode: 0,
            populationThousands: 10, maxPopulationThousands: 20, tribute: 10);
        var capitalCity = CaptureTestbed.City(
            "capital", "Capital", 5, 5, Loser, Loser, loyalty: 90, fortificationCode: 0,
            populationThousands: 8, maxPopulationThousands: 20, tribute: 4);
        var fillers = new[]
        {
            CaptureTestbed.City("filler-0", "Filler 0", 1000, 1000, Loser, Loser, 90, 0, 5, 10, 0),
            CaptureTestbed.City("filler-1", "Filler 1", 1001, 1000, Loser, Loser, 90, 0, 5, 10, 0),
            CaptureTestbed.City("filler-2", "Filler 2", 1002, 1000, Loser, Loser, 90, 0, 5, 10, 0),
        };

        var loser = CaptureTestbed.Nation(
            Loser, unity: 668, capitalCityId: "capital",
            recruitmentSlots: ValueList.From(new[] { slotAtCapturedCity, slotAtAnotherCity, zeroTroopSlot }));
        var winner = CaptureTestbed.Nation(Winner);
        var attacker = CaptureTestbed.Army(
            "army", Winner, 0, 0, morale: 50, CaptureTestbed.Unit("heavy_infantry", 1_000_000));

        var allCities = new[] { capturedCity, capitalCity }.Concat(fillers).ToArray();
        var state = CaptureTestbed.StateWith(new[] { loser, winner }, allCities, new[] { attacker });

        var result = CityCaptureResolver.Capture(
            state, "army", "captured", Ruleset, CaptureTestbed.ArcherUnitTypeId, CaptureTestbed.FortifyOrderId,
            NullEventSink.Instance);

        Assert.Empty(result.NationById(Loser)!.RecruitmentSlots);
    }

    // ---- The loyalty draw is reproducible from the same seed. ----

    [Fact]
    public void TheConquestLoyaltyDraw_IsReproducibleFromTheSameSeed()
    {
        var scenario1 = BuildConqueringScenario();
        var scenario2 = BuildConqueringScenario();
        Assert.Equal(scenario1.State.RandomSeed, scenario2.State.RandomSeed); // same fixture, same seed.

        var result1 = Capture(scenario1, NullEventSink.Instance);
        var result2 = Capture(scenario2, NullEventSink.Instance);

        Assert.Equal(result1.CityById("filler-0")!.Loyalty, result2.CityById("filler-0")!.Loyalty);
        Assert.Equal(result1.CityById("filler-1")!.Loyalty, result2.CityById("filler-1")!.Loyalty);
        Assert.Equal(result1.CityById("capital")!.Loyalty, result2.CityById("capital")!.Loyalty);
    }
}
