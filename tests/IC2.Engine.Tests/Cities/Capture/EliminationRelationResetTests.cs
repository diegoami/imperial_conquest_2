using System.Linq;
using IC2.Engine.Cities.Capture;
using IC2.Engine.Core;
using IC2.Engine.Diplomacy;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Cities.Capture;

/// <summary>
/// Bug #199 (T69) Done-when 4, rework round 1: the original resets every relation an eliminated nation
/// holds — see <see cref="RelationTransitions.ResetAllOnElimination"/>'s own remarks for the decompiled
/// citations. Exercised directly against <see cref="CityCaptureResolver.Capture"/> and
/// <see cref="CityCaptureResolver.Defect"/>, the two production call sites (~:181 and ~:252 at 889b325).
/// The one integration-level check that the real command-dispatched siege path resets a relation too
/// lives in <c>tests/IC2.Engine.Tests/Diplomacy/EliminationDiplomacyTests.cs</c>.
/// </summary>
public sealed class EliminationRelationResetTests
{
    private static Ruleset Ruleset => CaptureTestbed.Ruleset;

    /// <summary>
    /// <see cref="CaptureTestbed.StateWith"/> keeps the shipped toy scenario's own two-nation relation
    /// matrix, which does not cover whatever nation ids a fixture here invents. This rebuilds it, all at
    /// peace, over exactly <paramref name="nationIds"/> — the same reasoning
    /// <c>BattleCommandTestbed.StateWith</c> documents for its own tests.
    /// </summary>
    private static GameState WithRelationMatrix(GameState state, params string[] nationIds) =>
        state with
        {
            Relations = DiplomaticRelations.Uniform(ValueList.From(nationIds), Ruleset.Diplomacy.StateCodes.Peace),
        };

    /// <summary>
    /// DoD 4, bullets 1 and 2: a nation eliminated by a forced capture has its trade, alliance and war
    /// relations reset to their broken-relation cooldowns, in both directions of the matrix — and a
    /// relation between two nations that survive is untouched (the two-entity probe).
    /// </summary>
    [Fact]
    public void Capture_OfTheLastCity_ResetsEveryRelationTheEliminatedNationHeld()
    {
        const string Doomed = "doomed";
        const string Conqueror = "conqueror";
        const string TradePartner = "trade-partner";
        const string Ally = "ally";
        const string Enemy = "enemy";
        const string Bystander1 = "bystander-1";
        const string Bystander2 = "bystander-2";

        var doomed = CaptureTestbed.Nation(Doomed, unity: 668, capitalCityId: "lastcity");
        var conqueror = CaptureTestbed.Nation(Conqueror);
        var tradePartner = CaptureTestbed.Nation(TradePartner);
        var ally = CaptureTestbed.Nation(Ally);
        var enemy = CaptureTestbed.Nation(Enemy);
        var bystander1 = CaptureTestbed.Nation(Bystander1);
        var bystander2 = CaptureTestbed.Nation(Bystander2);

        var city = CaptureTestbed.City(
            "lastcity", "Last City", 0, 0, Doomed, Doomed,
            loyalty: 40, fortificationCode: 0, populationThousands: 10, maxPopulationThousands: 20, tribute: 5);
        var attacker = CaptureTestbed.Army(
            "army", Conqueror, 0, 0, morale: 50, CaptureTestbed.Unit("heavy_infantry", 400_000));

        var state = CaptureTestbed.StateWith(
            new[] { doomed, conqueror, tradePartner, ally, enemy, bystander1, bystander2 },
            new[] { city },
            new[] { attacker });
        state = WithRelationMatrix(state, Doomed, Conqueror, TradePartner, Ally, Enemy, Bystander1, Bystander2);

        var codes = Ruleset.Diplomacy.StateCodes;
        state = state with
        {
            Relations = state.Relations
                .WithRelation(Doomed, TradePartner, codes.Trade)
                .WithRelation(Doomed, Ally, codes.Alliance)
                .WithRelation(Doomed, Enemy, codes.War)
                .WithRelation(Bystander1, Bystander2, codes.Trade),
        };

        var result = CityCaptureResolver.Capture(
            state, "army", "lastcity", Ruleset, CaptureTestbed.ArcherUnitTypeId, CaptureTestbed.FortifyOrderId,
            new RecordingEventSink());

        Assert.True(result.NationById(Doomed)!.Eliminated);

        Assert.Equal(Ruleset.Diplomacy.CooldownAfterBrokenTrade, result.Relations.Get(Doomed, TradePartner));
        Assert.Equal(Ruleset.Diplomacy.CooldownAfterBrokenAlliance, result.Relations.Get(Doomed, Ally));
        Assert.Equal(Ruleset.Diplomacy.CooldownAfterEndedWar, result.Relations.Get(Doomed, Enemy));

        // Symmetric: the matrix's own [b][a] cell moved too, not only [a][b].
        Assert.Equal(Ruleset.Diplomacy.CooldownAfterBrokenTrade, result.Relations.Get(TradePartner, Doomed));
        Assert.Equal(Ruleset.Diplomacy.CooldownAfterBrokenAlliance, result.Relations.Get(Ally, Doomed));
        Assert.Equal(Ruleset.Diplomacy.CooldownAfterEndedWar, result.Relations.Get(Enemy, Doomed));

        // The two-entity probe: a relation between two nations that both survive is untouched.
        Assert.Equal(codes.Trade, result.Relations.Get(Bystander1, Bystander2));
    }

    /// <summary>
    /// DoD 4, bullet 3: the same reset, reached through <see cref="CityCaptureResolver.Defect"/> — plus
    /// the two-entity probe bullet 3 asks for literally ("the same holds through Defect"), not only bullet
    /// 1's Capture-only coverage (re-review round 2, R1's non-blocking extra).
    /// </summary>
    [Fact]
    public void Defect_OfTheLastCity_ResetsEveryRelationTheEliminatedNationHeld()
    {
        const string Doomed = "doomed-by-defection";
        const string NewOwner = "new-owner";
        const string Ally = "an-ally";
        const string Bystander1 = "defect-bystander-1";
        const string Bystander2 = "defect-bystander-2";

        var doomed = CaptureTestbed.Nation(Doomed, unity: 668, capitalCityId: "lastcity-d");
        var newOwner = CaptureTestbed.Nation(NewOwner);
        var ally = CaptureTestbed.Nation(Ally);
        var bystander1 = CaptureTestbed.Nation(Bystander1);
        var bystander2 = CaptureTestbed.Nation(Bystander2);

        var city = CaptureTestbed.City(
            "lastcity-d", "Last City", 0, 0, Doomed, Doomed,
            loyalty: 30, fortificationCode: 0, populationThousands: 10, maxPopulationThousands: 20, tribute: 5);

        var state = CaptureTestbed.StateWith(
            new[] { doomed, newOwner, ally, bystander1, bystander2 }, new[] { city });
        state = WithRelationMatrix(state, Doomed, NewOwner, Ally, Bystander1, Bystander2);

        var codes = Ruleset.Diplomacy.StateCodes;
        state = state with
        {
            Relations = state.Relations
                .WithRelation(Doomed, Ally, codes.Alliance)
                .WithRelation(Bystander1, Bystander2, codes.Trade),
        };

        var result = CityCaptureResolver.Defect(state, "lastcity-d", NewOwner, Ruleset, new RecordingEventSink());

        Assert.True(result.NationById(Doomed)!.Eliminated);
        Assert.Equal(Ruleset.Diplomacy.CooldownAfterBrokenAlliance, result.Relations.Get(Doomed, Ally));
        Assert.Equal(Ruleset.Diplomacy.CooldownAfterBrokenAlliance, result.Relations.Get(Ally, Doomed));

        // The two-entity probe: a relation between two nations that both survive is untouched.
        Assert.Equal(codes.Trade, result.Relations.Get(Bystander1, Bystander2));
    }

    /// <summary>
    /// DoD 4, bullet 4 (the "still owns a city" half of the <c>JustEliminated == false</c> gate): capturing
    /// one of a two-city nation's cities does not eliminate it, so its relations are left exactly as they
    /// were.
    /// </summary>
    [Fact]
    public void Capture_WhenTheOldOwnerStillOwnsAnotherCity_LeavesRelationsUnchanged()
    {
        const string NotYetDoomed = "not-yet-doomed";
        const string Conqueror = "conqueror-partial";
        const string TradePartner = "surviving-trade-partner";

        // T86: the captured city is deliberately NOT the capital ("second-ntd" is), so this stays the
        // plain non-capital "fewer than 6 cities" trigger, not the capital-move-or-conquest branch --
        // simpler, and this test is about the "still owns enough cities" gate, not capital handling.
        var notYetDoomed = CaptureTestbed.Nation(NotYetDoomed, unity: 668, capitalCityId: "second-ntd");
        var conqueror = CaptureTestbed.Nation(Conqueror);
        var tradePartner = CaptureTestbed.Nation(TradePartner);

        var capturedCity = CaptureTestbed.City(
            "capital-ntd", "Capital", 0, 0, NotYetDoomed, NotYetDoomed,
            loyalty: 40, fortificationCode: 0, populationThousands: 10, maxPopulationThousands: 20, tribute: 5);
        var secondCity = CaptureTestbed.City(
            "second-ntd", "Second City", 50, 50, NotYetDoomed, NotYetDoomed,
            loyalty: 40, fortificationCode: 0, populationThousands: 10, maxPopulationThousands: 20, tribute: 5);
        var attacker = CaptureTestbed.Army(
            "army", Conqueror, 0, 0, morale: 50, CaptureTestbed.Unit("heavy_infantry", 400_000));

        // T86: 5 filler cities, far from the attacker, so NotYetDoomed keeps 6 cities after losing
        // "capital-ntd" (second-ntd + 5 fillers) -- at CaptureRules.ConquestCityCountThreshold, not below
        // it, so the conquest cascade never fires and this stays the plain "still owns a city" case DoD
        // 4 bullet 4 is actually about, not conquest sweeping the rest away too.
        var fillerCities = CaptureTestbed.FillerCities(NotYetDoomed, 5, startX: 1000, y: 1000);

        var state = CaptureTestbed.StateWith(
            new[] { notYetDoomed, conqueror, tradePartner },
            new[] { capturedCity, secondCity }.Concat(fillerCities),
            new[] { attacker });
        state = WithRelationMatrix(state, NotYetDoomed, Conqueror, TradePartner);

        var codes = Ruleset.Diplomacy.StateCodes;
        state = state with { Relations = state.Relations.WithRelation(NotYetDoomed, TradePartner, codes.Trade) };

        var result = CityCaptureResolver.Capture(
            state, "army", "capital-ntd", Ruleset, CaptureTestbed.ArcherUnitTypeId, CaptureTestbed.FortifyOrderId,
            new RecordingEventSink());

        Assert.False(result.NationById(NotYetDoomed)!.Eliminated);
        Assert.Equal(codes.Trade, result.Relations.Get(NotYetDoomed, TradePartner));
    }

    /// <summary>
    /// DoD 4, bullet 4 (the "still owns a city" half of the <c>JustEliminated == false</c> gate), reached
    /// through <see cref="CityCaptureResolver.Defect"/> — re-review round 2, R1: this gate had no test at
    /// the <c>Defect</c> call site. Removing only that site's <c>if (oldOwnerEliminated)</c> guard let the
    /// whole suite (204 relevant tests) stay green, because every existing <c>JustEliminated == false</c>
    /// test went through <c>Capture</c>. Without the guard, a nation that loses one of its two cities by
    /// defection would have its trade broken (the reviewer's own reproduction: expected 1, got −8) even
    /// though it is still alive.
    /// </summary>
    [Fact]
    public void Defect_WhenTheOldOwnerStillOwnsAnotherCity_LeavesRelationsUnchanged()
    {
        const string NotYetDoomed = "not-yet-doomed-defection";
        const string NewOwner = "new-owner-partial";
        const string TradePartner = "surviving-trade-partner-defection";

        var notYetDoomed = CaptureTestbed.Nation(NotYetDoomed, unity: 668, capitalCityId: "capital-ntd-d");
        var newOwner = CaptureTestbed.Nation(NewOwner);
        var tradePartner = CaptureTestbed.Nation(TradePartner);

        var defectingCity = CaptureTestbed.City(
            "capital-ntd-d", "Capital", 0, 0, NotYetDoomed, NotYetDoomed,
            loyalty: 30, fortificationCode: 0, populationThousands: 10, maxPopulationThousands: 20, tribute: 5);
        var secondCity = CaptureTestbed.City(
            "second-ntd-d", "Second City", 50, 50, NotYetDoomed, NotYetDoomed,
            loyalty: 40, fortificationCode: 0, populationThousands: 10, maxPopulationThousands: 20, tribute: 5);

        var state = CaptureTestbed.StateWith(
            new[] { notYetDoomed, newOwner, tradePartner }, new[] { defectingCity, secondCity });
        state = WithRelationMatrix(state, NotYetDoomed, NewOwner, TradePartner);

        var codes = Ruleset.Diplomacy.StateCodes;
        state = state with { Relations = state.Relations.WithRelation(NotYetDoomed, TradePartner, codes.Trade) };

        var result = CityCaptureResolver.Defect(state, "capital-ntd-d", NewOwner, Ruleset, new RecordingEventSink());

        Assert.False(result.NationById(NotYetDoomed)!.Eliminated);
        Assert.Equal(codes.Trade, result.Relations.Get(NotYetDoomed, TradePartner));
    }

    /// <summary>
    /// DoD 4, bullet 4 (the "already eliminated" half of the gate): contrived but directly exercises
    /// <c>JustEliminated</c>'s idempotency one layer above <c>NationElimination</c> itself, mirroring
    /// <c>EliminationTests.ApplyIfLastCityLost_AlreadyEliminated_ReturnsUnchangedAndNotJustEliminated</c> —
    /// a nation already marked eliminated that (only for this test) still owns the city being captured, so
    /// the reset must not fire a second time. The Hazards note in <c>docs/tasks/T69.md</c> is explicit that
    /// no live game state can reach this: an eliminated nation owns no city, so no later capture can ever
    /// name it as the old owner.
    /// </summary>
    [Fact]
    public void Capture_OfACityOwnedByAnAlreadyEliminatedNation_LeavesRelationsUnchanged()
    {
        const string AlreadyDoomed = "already-doomed";
        const string Conqueror = "conqueror-already";
        const string TradePartner = "trade-partner-already";

        var alreadyDoomed = CaptureTestbed.Nation(AlreadyDoomed, unity: 0, eliminated: true);
        var conqueror = CaptureTestbed.Nation(Conqueror);
        var tradePartner = CaptureTestbed.Nation(TradePartner);

        var city = CaptureTestbed.City(
            "already-doomed-city", "City", 0, 0, AlreadyDoomed, AlreadyDoomed,
            loyalty: 40, fortificationCode: 0, populationThousands: 10, maxPopulationThousands: 20, tribute: 5);
        var attacker = CaptureTestbed.Army(
            "army", Conqueror, 0, 0, morale: 50, CaptureTestbed.Unit("heavy_infantry", 400_000));

        var state = CaptureTestbed.StateWith(
            new[] { alreadyDoomed, conqueror, tradePartner }, new[] { city }, new[] { attacker });
        state = WithRelationMatrix(state, AlreadyDoomed, Conqueror, TradePartner);

        var codes = Ruleset.Diplomacy.StateCodes;
        state = state with { Relations = state.Relations.WithRelation(AlreadyDoomed, TradePartner, codes.Trade) };

        var result = CityCaptureResolver.Capture(
            state, "army", "already-doomed-city", Ruleset, CaptureTestbed.ArcherUnitTypeId,
            CaptureTestbed.FortifyOrderId, new RecordingEventSink());

        Assert.Equal(codes.Trade, result.Relations.Get(AlreadyDoomed, TradePartner));
    }

    /// <summary>
    /// DoD 4, bullet 5: a pair already at peace, or already on a cooldown, holds whatever
    /// <c>FUN_00449B40</c> writes for that previous state — <see cref="RelationTransitions.ResetAllOnElimination"/>'s
    /// own remarks quote the decompiled lines (:48488–48504): there is no early-return, so an existing
    /// cooldown is cleared to full peace immediately rather than left to finish cooling down.
    /// </summary>
    [Fact]
    public void Capture_OfTheLastCity_ResetsAPeacefulOrCoolingDownRelation_ToPeace()
    {
        const string Doomed = "doomed-peace-cooldown";
        const string Conqueror = "conqueror-peace-cooldown";
        const string AtPeaceAlready = "already-at-peace";
        const string OnACooldown = "already-on-a-cooldown";

        var doomed = CaptureTestbed.Nation(Doomed, unity: 668, capitalCityId: "lastcity-pc");
        var conqueror = CaptureTestbed.Nation(Conqueror);
        var atPeaceAlready = CaptureTestbed.Nation(AtPeaceAlready);
        var onACooldown = CaptureTestbed.Nation(OnACooldown);

        var city = CaptureTestbed.City(
            "lastcity-pc", "Last City", 0, 0, Doomed, Doomed,
            loyalty: 40, fortificationCode: 0, populationThousands: 10, maxPopulationThousands: 20, tribute: 5);
        var attacker = CaptureTestbed.Army(
            "army", Conqueror, 0, 0, morale: 50, CaptureTestbed.Unit("heavy_infantry", 400_000));

        var state = CaptureTestbed.StateWith(
            new[] { doomed, conqueror, atPeaceAlready, onACooldown }, new[] { city }, new[] { attacker });
        state = WithRelationMatrix(state, Doomed, Conqueror, AtPeaceAlready, OnACooldown);

        var codes = Ruleset.Diplomacy.StateCodes;
        state = state with
        {
            // AtPeaceAlready is left at the uniform matrix's own peace (0): nothing to set.
            Relations = state.Relations.WithRelation(Doomed, OnACooldown, -11),
        };

        var result = CityCaptureResolver.Capture(
            state, "army", "lastcity-pc", Ruleset, CaptureTestbed.ArcherUnitTypeId, CaptureTestbed.FortifyOrderId,
            new RecordingEventSink());

        Assert.Equal(codes.Peace, result.Relations.Get(Doomed, AtPeaceAlready));
        Assert.Equal(codes.Peace, result.Relations.Get(Doomed, OnACooldown));
    }

    /// <summary>
    /// Rework round 2: T84 (bug #366) merged and edits the very same two <see cref="CityCaptureResolver"/>
    /// call sites this task does, to delete an eliminated nation's armies and launched fleets and hand its
    /// under-construction fleets to the receiver (<see cref="EliminationForces.Dispose"/>). Both must hold
    /// together in the one state a real elimination produces: the relation reset (this task's own) and the
    /// force disposal (T84's own).
    /// </summary>
    [Fact]
    public void Capture_OfTheLastCity_ResetsRelations_AndDisposesForces_Together()
    {
        const string Doomed = "doomed-combined";
        const string Conqueror = "conqueror-combined";
        const string TradePartner = "trade-partner-combined";

        var doomed = CaptureTestbed.Nation(Doomed, unity: 668, capitalCityId: "capital-combined");
        var conqueror = CaptureTestbed.Nation(Conqueror);
        var tradePartner = CaptureTestbed.Nation(TradePartner);

        var city = CaptureTestbed.City(
            "capital-combined", "Capital", 0, 0, Doomed, Doomed,
            loyalty: 40, fortificationCode: 0, populationThousands: 10, maxPopulationThousands: 20, tribute: 5);

        var attacker = CaptureTestbed.Army(
            "army", Conqueror, 0, 0, morale: 50, CaptureTestbed.Unit("heavy_infantry", 400_000));
        var doomedArmy = CaptureTestbed.Army(
            "doomed-army", Doomed, 5, 5, morale: 40, CaptureTestbed.Unit("light_infantry", 400));
        var doomedFleet = EliminationForcesTestbed.Fleet("doomed-fleet", Doomed, 3, 3);
        var doomedConstructionFleet = EliminationForcesTestbed.Fleet("doomed-construction-fleet", Doomed, 0, 0) with
        {
            ConstructionTicksRemaining = Ruleset.Naval.ConstructionTicks,
            BuildCityId = "capital-combined",
        };

        // EliminationForcesTestbed.StateWith, not CaptureTestbed.StateWith/WithRelationMatrix: it already
        // rebuilds a relation matrix (and a turn order) sized to the given nations, which this fixture
        // needs anyway.
        var state = EliminationForcesTestbed.StateWith(
            new[] { doomed, conqueror, tradePartner },
            new[] { city },
            new[] { attacker, doomedArmy },
            new[] { doomedFleet, doomedConstructionFleet });

        var codes = Ruleset.Diplomacy.StateCodes;
        state = state with { Relations = state.Relations.WithRelation(Doomed, TradePartner, codes.Trade) };

        var result = CityCaptureResolver.Capture(
            state, "army", "capital-combined", Ruleset, CaptureTestbed.ArcherUnitTypeId, CaptureTestbed.FortifyOrderId,
            new RecordingEventSink());

        Assert.True(result.NationById(Doomed)!.Eliminated);

        // The relation reset (this task's own, RelationTransitions.ResetAllOnElimination).
        Assert.Equal(Ruleset.Diplomacy.CooldownAfterBrokenTrade, result.Relations.Get(Doomed, TradePartner));

        // The force disposal (T84's own, EliminationForces.Dispose) -- in the very same result.
        Assert.Null(result.ArmyById("doomed-army"));
        Assert.Null(result.FleetById("doomed-fleet"));
        var transferredFleet = result.FleetById("doomed-construction-fleet");
        Assert.NotNull(transferredFleet);
        Assert.Equal(Conqueror, transferredFleet!.Nation);
        Assert.True(transferredFleet.IsUnderConstruction);
    }
}
