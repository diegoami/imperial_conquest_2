using System.Linq;
using IC2.Engine.Cities.Capture;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Persistence;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Persistence;
using Xunit;

namespace IC2.Engine.Tests.Cities.Capture;

/// <summary>
/// <c>docs/tasks/T84.md</c> Done-when 1 through 5: <see cref="EliminationForces"/>, exercised both through
/// <see cref="CityCaptureResolver.Capture"/>/<see cref="CityCaptureResolver.Defect"/> (the only two call
/// sites) and directly. Every rule under test traces to
/// <c>RE-imperial-conquest-2/docs/reports/decompiled-elimination-cleanup.md</c> §2, §4 and §6, cited again
/// on <see cref="EliminationForces"/> itself.
/// </summary>
public sealed class EliminationForcesTests
{
    private static Ruleset Ruleset => EliminationForcesTestbed.Ruleset;
    private const string ArcherUnitTypeId = CaptureTestbed.ArcherUnitTypeId;
    private static string FortifyOrderId => CaptureTestbed.FortifyOrderId;

    /// <summary>
    /// Done-when 1. Through <see cref="CityCaptureResolver.Capture"/>: the eliminated nation X had an army
    /// on land, an army standing at another nation's city (the model has no notion of "inside" a city
    /// beyond sharing its tile — there is no garrison/occupancy record separate from the army itself, so
    /// this is that case), an army aboard its own launched fleet, a second launched fleet with no army
    /// aboard, and a fleet still under construction. Afterwards none of X's armies or launched fleets
    /// exist, and the fleet under construction belongs to the capturer with its countdown and build city
    /// unchanged. A third, uninvolved nation (and the city X's stray army merely stands next to) is
    /// untouched — the two-entity probe <c>CityCaptureResolver</c>'s own remarks describe.
    /// </summary>
    [Fact]
    public void Capture_OfTheLastCity_DeletesXsArmiesAndLaunchedFleets_AndTransfersItsConstructionFleet()
    {
        var doomed = CaptureTestbed.Nation("doomed", unity: 668, capitalCityId: "doomed-capital");
        var capturer = CaptureTestbed.Nation("capturer");
        var other = CaptureTestbed.Nation("other", capitalCityId: "other-city");

        var doomedCapital = CaptureTestbed.City(
            "doomed-capital", "Doomed Capital", 0, 0, "doomed", "doomed", 40, 0, 10, 20, 5);
        var otherCity = CaptureTestbed.City(
            "other-city", "Other City", 10, 10, "other", "other", 80, 0, 15, 30, 5);

        var attacker = CaptureTestbed.Army("c-army", "capturer", 0, 0, morale: 50, CaptureTestbed.Unit("heavy_infantry", 1000));
        var xLandArmy = CaptureTestbed.Army("x-land-army", "doomed", 5, 5, morale: 40, CaptureTestbed.Unit("light_infantry", 400));
        // "In a city of another nation": the model tracks an army only by its own (x, y), so an army
        // standing at another nation's city tile is exactly this case (see this test's own summary).
        var xArmyInForeignCity = CaptureTestbed.Army(
            "x-army-in-foreign-city", "doomed", 10, 10, morale: 40, CaptureTestbed.Unit("light_infantry", 200));
        var xEmbarkedArmy = EliminationForcesTestbed.EmbarkedArmy("x-embarked-army", "doomed", "x-fleet-1", 3, 3);

        var xFleet1 = EliminationForcesTestbed.Fleet("x-fleet-1", "doomed", 3, 3, carriedArmyId: "x-embarked-army");
        var xFleet2 = EliminationForcesTestbed.Fleet("x-fleet-2", "doomed", 4, 4);
        var xConstructionFleet = EliminationForcesTestbed.Fleet("x-construction-fleet", "doomed", 0, 0) with
        {
            ConstructionTicksRemaining = Ruleset.Naval.ConstructionTicks,
            BuildCityId = "doomed-capital",
        };

        var state = EliminationForcesTestbed.StateWith(
            new[] { doomed, capturer, other },
            new[] { doomedCapital, otherCity },
            new[] { attacker, xLandArmy, xArmyInForeignCity, xEmbarkedArmy },
            new[] { xFleet1, xFleet2, xConstructionFleet });

        var sink = new RecordingEventSink();
        var result = CityCaptureResolver.Capture(
            state, "c-army", "doomed-capital", Ruleset, ArcherUnitTypeId, FortifyOrderId, sink);

        Assert.True(result.NationById("doomed")!.Eliminated);

        // Every army X owned is gone.
        Assert.Null(result.ArmyById("x-land-army"));
        Assert.Null(result.ArmyById("x-army-in-foreign-city"));
        Assert.Null(result.ArmyById("x-embarked-army"));
        Assert.DoesNotContain(result.Armies, a => string.Equals(a.Nation, "doomed", System.StringComparison.Ordinal));

        // Every launched fleet X owned is gone; the one under construction survives, retagged.
        Assert.Null(result.FleetById("x-fleet-1"));
        Assert.Null(result.FleetById("x-fleet-2"));
        var transferredFleet = result.FleetById("x-construction-fleet");
        Assert.NotNull(transferredFleet);
        Assert.Equal("capturer", transferredFleet!.Nation);
        Assert.Equal(Ruleset.Naval.ConstructionTicks, transferredFleet.ConstructionTicksRemaining);
        Assert.Equal("doomed-capital", transferredFleet.BuildCityId);
        Assert.True(transferredFleet.IsUnderConstruction);

        // The attacker's own army, and the uninvolved third nation and its city, are untouched.
        Assert.Equal(attacker, result.ArmyById("c-army"));
        Assert.Equal(other, result.NationById("other"));
        Assert.Equal(otherCity, result.CityById("other-city"));
    }

    /// <summary>Done-when 2: the same shape, reached through <see cref="CityCaptureResolver.Defect"/> instead, with the construction fleet going to the city's new owner.</summary>
    [Fact]
    public void Defect_OfTheLastCity_DeletesXsArmiesAndLaunchedFleets_AndTransfersItsConstructionFleet()
    {
        var doomed = CaptureTestbed.Nation("doomed", unity: 668, capitalCityId: "doomed-capital");
        var newOwner = CaptureTestbed.Nation("newowner");

        var doomedCapital = CaptureTestbed.City(
            "doomed-capital", "Doomed Capital", 0, 0, "doomed", "doomed", 30, 0, 10, 20, 5);

        var xLandArmy = CaptureTestbed.Army("x-land-army", "doomed", 5, 5, morale: 40, CaptureTestbed.Unit("light_infantry", 400));
        var xEmbarkedArmy = EliminationForcesTestbed.EmbarkedArmy("x-embarked-army", "doomed", "x-fleet-1", 3, 3);

        var xFleet1 = EliminationForcesTestbed.Fleet("x-fleet-1", "doomed", 3, 3, carriedArmyId: "x-embarked-army");
        var xFleet2 = EliminationForcesTestbed.Fleet("x-fleet-2", "doomed", 4, 4);
        var xConstructionFleet = EliminationForcesTestbed.Fleet("x-construction-fleet", "doomed", 0, 0) with
        {
            ConstructionTicksRemaining = Ruleset.Naval.ConstructionTicks,
            BuildCityId = "doomed-capital",
        };

        var state = EliminationForcesTestbed.StateWith(
            new[] { doomed, newOwner },
            new[] { doomedCapital },
            new[] { xLandArmy, xEmbarkedArmy },
            new[] { xFleet1, xFleet2, xConstructionFleet });

        var sink = new RecordingEventSink();
        var result = CityCaptureResolver.Defect(state, "doomed-capital", "newowner", Ruleset, sink);

        Assert.True(result.NationById("doomed")!.Eliminated);
        Assert.Null(result.ArmyById("x-land-army"));
        Assert.Null(result.ArmyById("x-embarked-army"));
        Assert.Null(result.FleetById("x-fleet-1"));
        Assert.Null(result.FleetById("x-fleet-2"));

        var transferredFleet = result.FleetById("x-construction-fleet");
        Assert.NotNull(transferredFleet);
        Assert.Equal("newowner", transferredFleet!.Nation);
        Assert.Equal(Ruleset.Naval.ConstructionTicks, transferredFleet.ConstructionTicksRemaining);
        Assert.Equal("doomed-capital", transferredFleet.BuildCityId);
    }

    /// <summary>
    /// Done-when 3, first half. A surviving nation Y's own army-aboard-its-own-fleet is completely
    /// untouched by X's elimination, no <see cref="ArmyState.AboardFleetId"/> or
    /// <see cref="FleetState.CarriedArmyId"/> names a missing record afterwards
    /// (<see cref="GameDataValidation.Validate"/> would throw <c>UnresolvedReferenceException</c> or
    /// <c>MalformedGameDataException</c> otherwise), and the resulting state round-trips through a save and
    /// load.
    /// </summary>
    [Fact]
    public void Capture_LeavesASurvivingNationsForcesUntouched_AndTheResultRoundTrips()
    {
        var doomed = CaptureTestbed.Nation("doomed", unity: 500, capitalCityId: "doomed-capital");
        var capturer = CaptureTestbed.Nation("capturer");
        var survivor = CaptureTestbed.Nation("survivor", capitalCityId: "survivor-city");

        var doomedCapital = CaptureTestbed.City(
            "doomed-capital", "Doomed Capital", 0, 0, "doomed", "doomed", 40, 0, 10, 20, 5);
        var survivorCity = CaptureTestbed.City(
            "survivor-city", "Survivor City", 20, 20, "survivor", "survivor", 80, 0, 15, 30, 5);

        var attacker = CaptureTestbed.Army("c-army", "capturer", 0, 0, morale: 50, CaptureTestbed.Unit("heavy_infantry", 1000));
        var xEmbarkedArmy = EliminationForcesTestbed.EmbarkedArmy("x-embarked-army", "doomed", "x-fleet", 3, 3);
        var yArmy = EliminationForcesTestbed.EmbarkedArmy("y-army", "survivor", "y-fleet", 20, 20);

        var xFleet = EliminationForcesTestbed.Fleet("x-fleet", "doomed", 3, 3, carriedArmyId: "x-embarked-army");
        var yFleet = EliminationForcesTestbed.Fleet("y-fleet", "survivor", 20, 20, carriedArmyId: "y-army");

        var state = EliminationForcesTestbed.StateWith(
            new[] { doomed, capturer, survivor },
            new[] { doomedCapital, survivorCity },
            new[] { attacker, xEmbarkedArmy, yArmy },
            new[] { xFleet, yFleet });

        var sink = new RecordingEventSink();
        var result = CityCaptureResolver.Capture(
            state, "c-army", "doomed-capital", Ruleset, ArcherUnitTypeId, FortifyOrderId, sink);

        // Y's own army, fleet and the link between them are exactly as they were.
        Assert.Equal(yArmy, result.ArmyById("y-army"));
        Assert.Equal(yFleet, result.FleetById("y-fleet"));

        // The sweep Done-when 3 asks for: every AboardFleetId/CarriedArmyId names a record that exists.
        // GameDataValidation.Validate throws on the first one that does not.
        GameDataValidation.Validate("t84-sweep-after-capture", result);

        // The save/load round trip.
        var toy = PersistenceTestbed.Toy;
        var save = new SaveGame(
            SchemaVersion: result.SchemaVersion,
            Id: "t84-elimination-round-trip",
            Label: "T84 elimination round trip",
            ScenarioId: result.ScenarioId,
            WorldId: result.WorldId,
            RulesetId: result.RulesetId,
            State: result);

        var text = SaveManager.Serialize(save);
        var reloaded = SaveManager.Load("t84-elimination-round-trip.json", text, toy.World, toy.Ruleset);

        Assert.Equal(result, reloaded.State);
    }

    /// <summary>
    /// Done-when 3, second half: cross-nation embarkation. <see cref="Naval.Commands.EmbarkArmyCommandHandler"/>
    /// forbids it through the command path (<c>EmbarkArmyRejections.NotYours</c>: "both the army and the
    /// fleet must belong to the issuing nation"), so this builds the boarded state directly, exactly as
    /// Done-when 3's own "if the engine forbids it, build the state directly and show both anyway" asks.
    /// X's army aboard Y's fleet is deleted and Y's fleet keeps flying with its link cleared; Y's army
    /// aboard X's own launched fleet dies with that fleet when X is eliminated.
    /// </summary>
    [Fact]
    public void Capture_DeletesACrossNationEmbarkedArmy_AndClearsTheOtherFleetsLink_WithoutTouchingTheSurvivor()
    {
        var doomed = CaptureTestbed.Nation("doomed", unity: 500, capitalCityId: "doomed-capital");
        var capturer = CaptureTestbed.Nation("capturer");
        var survivor = CaptureTestbed.Nation("survivor", capitalCityId: "survivor-city");

        var doomedCapital = CaptureTestbed.City(
            "doomed-capital", "Doomed Capital", 0, 0, "doomed", "doomed", 40, 0, 10, 20, 5);
        var survivorCity = CaptureTestbed.City(
            "survivor-city", "Survivor City", 20, 20, "survivor", "survivor", 80, 0, 15, 30, 5);

        var attacker = CaptureTestbed.Army("c-army", "capturer", 0, 0, morale: 50, CaptureTestbed.Unit("heavy_infantry", 1000));

        // X's army boarded on Y's launched fleet.
        var xArmyAboardYsFleet = EliminationForcesTestbed.EmbarkedArmy("x-army-aboard-y", "doomed", "y-fleet", 20, 20);
        // Y's army boarded on X's own launched fleet.
        var yArmyAboardXsFleet = EliminationForcesTestbed.EmbarkedArmy("y-army-aboard-x", "survivor", "x-fleet", 3, 3);

        var xFleet = EliminationForcesTestbed.Fleet("x-fleet", "doomed", 3, 3, carriedArmyId: "y-army-aboard-x");
        var yFleet = EliminationForcesTestbed.Fleet("y-fleet", "survivor", 20, 20, carriedArmyId: "x-army-aboard-y");

        var state = EliminationForcesTestbed.StateWith(
            new[] { doomed, capturer, survivor },
            new[] { doomedCapital, survivorCity },
            new[] { attacker, xArmyAboardYsFleet, yArmyAboardXsFleet },
            new[] { xFleet, yFleet });

        var sink = new RecordingEventSink();
        var result = CityCaptureResolver.Capture(
            state, "c-army", "doomed-capital", Ruleset, ArcherUnitTypeId, FortifyOrderId, sink);

        // X's army dies (it belonged to the eliminated nation), and Y's fleet forgets it but survives.
        Assert.Null(result.ArmyById("x-army-aboard-y"));
        var survivingYFleet = result.FleetById("y-fleet");
        Assert.NotNull(survivingYFleet);
        Assert.Equal("survivor", survivingYFleet!.Nation);
        Assert.Null(survivingYFleet.CarriedArmyId);

        // X's own launched fleet is deleted, and Y's army aboard it dies with it.
        Assert.Null(result.FleetById("x-fleet"));
        Assert.Null(result.ArmyById("y-army-aboard-x"));

        GameDataValidation.Validate("t84-sweep-cross-nation", result);
    }

    /// <summary>
    /// Done-when 4: nothing is refunded by the disposal itself. Every nation's record — including the
    /// eliminated nation's own treasury and the receiving nation's — comes back exactly as it went in;
    /// only <see cref="GameState.Armies"/> and <see cref="GameState.Fleets"/> change. Mutation proof:
    /// deleting the destroy-do-not-transfer rule (crediting the dead army/fleet's money/supplies to
    /// somewhere) would fail this immediately.
    /// </summary>
    [Fact]
    public void Dispose_CreditsNoTreasuryAndTransfersNothingToTheReceiver()
    {
        var doomed = CaptureTestbed.Nation("doomed", treasury: -911);
        var capturer = CaptureTestbed.Nation("capturer", treasury: 200);

        var xArmy = CaptureTestbed.Army("x-army", "doomed", 1, 1, morale: 40, CaptureTestbed.Unit("light_infantry", 300))
            with { Money = 300, SupplyTons = 50 };
        var xFleet = EliminationForcesTestbed.Fleet("x-fleet", "doomed", 1, 1) with { Money = 200, SupplyTons = 80 };

        var state = EliminationForcesTestbed.StateWith(
            new[] { doomed, capturer }, Array.Empty<CityState>(), new[] { xArmy }, new[] { xFleet });

        var result = EliminationForces.Dispose(state, "doomed", "capturer");

        // No nation record changed at all -- in particular, neither treasury moved.
        Assert.Equal(state.Nations, result.Nations);
        Assert.Empty(result.Armies);
        Assert.Empty(result.Fleets);
    }

    /// <summary>
    /// Done-when 5: a capture that leaves X with a city deletes nothing, because the disposal is called
    /// only when <c>JustEliminated</c> is true. Delete the guarding <c>if</c> at either
    /// <see cref="CityCaptureResolver"/> call site and this fails, because X's untouched army/fleets would
    /// vanish from a capture that did not eliminate it.
    /// </summary>
    [Fact]
    public void Capture_ThatLeavesXWithACity_DeletesNothingOfXs()
    {
        var doomed = CaptureTestbed.Nation("doomed", unity: 500, capitalCityId: "doomed-second");
        var capturer = CaptureTestbed.Nation("capturer");

        var doomedFirstCity = CaptureTestbed.City(
            "doomed-first", "Doomed First", 0, 0, "doomed", "doomed", 40, 0, 10, 20, 5);
        var doomedSecondCity = CaptureTestbed.City(
            "doomed-second", "Doomed Second", 30, 30, "doomed", "doomed", 60, 0, 12, 20, 5);

        var attacker = CaptureTestbed.Army("c-army", "capturer", 0, 0, morale: 50, CaptureTestbed.Unit("heavy_infantry", 1000));
        var xArmy = CaptureTestbed.Army("x-army", "doomed", 5, 5, morale: 40, CaptureTestbed.Unit("light_infantry", 400));
        var xFleet = EliminationForcesTestbed.Fleet("x-fleet", "doomed", 4, 4);
        var xConstructionFleet = EliminationForcesTestbed.Fleet("x-construction-fleet", "doomed", 0, 0) with
        {
            ConstructionTicksRemaining = Ruleset.Naval.ConstructionTicks,
            BuildCityId = "doomed-second",
        };

        var state = EliminationForcesTestbed.StateWith(
            new[] { doomed, capturer },
            new[] { doomedFirstCity, doomedSecondCity },
            new[] { attacker, xArmy },
            new[] { xFleet, xConstructionFleet });

        var sink = new RecordingEventSink();
        var result = CityCaptureResolver.Capture(
            state, "c-army", "doomed-first", Ruleset, ArcherUnitTypeId, FortifyOrderId, sink);

        Assert.False(result.NationById("doomed")!.Eliminated);
        Assert.Equal(xArmy, result.ArmyById("x-army"));
        Assert.Equal(xFleet, result.FleetById("x-fleet"));
        Assert.Equal(xConstructionFleet, result.FleetById("x-construction-fleet"));
        Assert.Empty(sink.Events.OfType<NationConquered>());
    }
}
