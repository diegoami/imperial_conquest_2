using System.Linq;
using IC2.Engine.Cities.Capture;
using IC2.Engine.Core;
using IC2.Engine.Diplomacy;
using IC2.Engine.Model;
using IC2.Engine.Persistence;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Persistence;
using Xunit;

namespace IC2.Engine.Tests.Cities.Capture;

/// <summary>
/// T92 (bug #424) Done-when 1 and 2: <c>FUN_0044BB18</c> has no liveness test of its own -- a capture whose
/// own regular defection cascade (<see cref="CityCaptureResolver.RunCascade"/>) takes the loser's LAST
/// other city in the same call still reaches <see cref="ConquestCascade.Apply"/>, exactly like any other
/// sub-<see cref="CaptureRules.ConquestCityCountThreshold"/> capture -- the guard <c>ConquestTrigger.Evaluate</c>
/// used to return early on is gone. Every test here drives that through the real
/// <see cref="CityCaptureResolver.Capture"/> command: never <see cref="ConquestTrigger"/> or
/// <see cref="ConquestCascade"/> called directly, so a mutation to <see cref="CityCaptureResolver"/>'s own
/// wiring is caught here too (the same reasoning <c>ConquestCascadeTests</c>' own remarks give).
/// </summary>
/// <remarks>
/// <strong>The scenario.</strong> "loser" owns exactly two cities: "loser-target" (the besieged city
/// "winner" captures directly) and "loser-last" (weak, low-loyalty, within cascade range, so the SAME
/// call's own regular cascade defects it to "winner" too). Losing both empties "loser" to zero cities
/// through <see cref="CityCaptureResolver.Defect"/>'s own elimination path -- <em>before</em>
/// <see cref="ConquestTrigger.Evaluate"/> ever runs against it -- so by the time this method's own
/// post-cascade check reads "loser", it is already <see cref="NationState.Eliminated"/>. Bug #424 is
/// exactly this: <see cref="ConquestCascade.Apply"/> now still runs against it anyway, matching
/// <c>FUN_0044C528</c>'s own unconditional reach. "loser-target" is deliberately not "loser"'s own capital
/// (<see cref="NationState.CapitalCityId"/> is left <see langword="null"/>) so this stays the plain
/// "&lt; 6 cities" branch, not the capital-move-or-conquest branch <c>ConquestTriggerTests</c> and
/// <c>StaleCapitalPostSweepBranchTests</c> already cover.
/// </remarks>
public sealed class ConquestAfterEmptyingSweepTests
{
    private const string Loser = "loser";
    private const string Winner = "winner";
    private const string Bystander = "bystander";

    private static Ruleset Ruleset => CaptureTestbed.Ruleset;

    /// <summary>
    /// Builds the scenario this whole file shares. "bystander" is a third, uninvolved nation that borders
    /// "loser" before this capture -- Done-when 2's own "two or three nations" probe, and the one nation
    /// the neighbour merge (Scope item 5) has anything to actually merge onto "winner".
    /// </summary>
    private static (GameState State, string CapturedCityId) BuildScenario()
    {
        var loserTarget = CaptureTestbed.City(
            "loser-target", "Loser Target", 0, 0, Loser, Loser,
            loyalty: 40, fortificationCode: 0, populationThousands: 10, maxPopulationThousands: 20, tribute: 0);

        // Loyalty 30 (< CascadeLoyaltyThreshold, 65), weak (low population, no fortification), well within
        // CascadeDistanceMax (10) of the besieging army at (0,0) -- qualifies for the SAME call's own
        // regular defection cascade, and its own defense is far below the attacker's own strength below.
        var loserLast = CaptureTestbed.City(
            "loser-last", "Loser Last", 1, 1, Loser, Loser,
            loyalty: 30, fortificationCode: 0, populationThousands: 1, maxPopulationThousands: 10, tribute: 0);

        var winnerHome = CaptureTestbed.City(
            "winner-home", "Winner Home", 900, 900, Winner, Winner,
            loyalty: 90, fortificationCode: 0, populationThousands: 10, maxPopulationThousands: 20, tribute: 0);

        var bystanderCity = CaptureTestbed.City(
            "bystander-city", "Bystander City", -900, -900, Bystander, Bystander,
            loyalty: 80, fortificationCode: 0, populationThousands: 10, maxPopulationThousands: 20, tribute: 0);

        // Unity 660: after this capture's own -15 (645), strictly under CascadeUnityThreshold (650), so
        // the cascade below actually fires against "loser-last". Treasury 500 (positive) -- Scope item 3,
        // "the treasury copy" -- and Tribute 0 on both of the loser's own cities (CityTaxContribution.Compute
        // is tribute × population / maxPopulation, so 0 either way) keeps every capture/defection credit
        // this scenario's own transfers pay at exactly 0, isolating the conquest's own +50 (Scope item 2)
        // and treasury copy (item 3) as the only unity/treasury deltas "winner" sees.
        //
        // One recruitment slot targeting "winner-home" -- a city neither captured nor defected in this
        // scenario, so it survives both Capture's own WithoutSlotsTargeting("loser-target") and Defect's
        // own WithoutTroopSlotsTargeting("loser-last") untouched, right up until the conquest's own blanket
        // wipe (Scope item 8) clears it.
        var loser = CaptureTestbed.Nation(
            Loser, treasury: 500, unity: 660, capitalCityId: null,
            recruitmentSlots: ValueList.Of(new RecruitmentSlot("winner-home", "heavy_infantry", 100, 0)));
        var winner = CaptureTestbed.Nation(Winner, treasury: 0, unity: 100, capitalCityId: "winner-home");
        var bystander = CaptureTestbed.Nation(Bystander, treasury: 0, unity: 500, capitalCityId: "bystander-city");

        var attacker = CaptureTestbed.Army(
            "winner-army", Winner, 0, 0, morale: 50, CaptureTestbed.Unit("heavy_infantry", 1_000_000));
        // "loser"'s own army: T84's force disposal (Scope item 5's own EliminationForces.Dispose) has
        // something to delete -- Done-when 2's own "T84 fires ... as the decompile orders" probe.
        var loserArmy = CaptureTestbed.Army(
            "loser-army", Loser, 500, 500, morale: 50, CaptureTestbed.Unit("light_infantry", 100));

        var state = CaptureTestbed.StateWith(
            new[] { loser, winner, bystander },
            new[] { loserTarget, loserLast, winnerHome, bystanderCity },
            new[] { attacker, loserArmy });

        state = state with
        {
            TurnOrder = ValueList.Of(Loser, Winner, Bystander),
            ActiveSeatIndex = 0,
            Relations = DiplomaticRelations.Uniform(
                ValueList.Of(Loser, Winner, Bystander), Ruleset.Diplomacy.StateCodes.Peace),
            // "loser" borders "bystander", nobody else -- Scope item 4's own neighbour merge has exactly
            // one id to move onto "winner", and "loser"'s own entry (never touched by the merge) still
            // lists "bystander" afterwards.
            Neighbours = ValueList.Of(
                new NationNeighbours(Loser, ValueList.Of(Bystander)),
                new NationNeighbours(Winner, ValueList<string>.Empty),
                new NationNeighbours(Bystander, ValueList.Of(Loser))),
        };

        // Trade with "bystander" -- one of the three positive relation states -- so Done-when 2's own
        // double-fire probe (below) has something a first reset could only cool down, and a second reset
        // could only then finish clearing to outright peace.
        var codes = Ruleset.Diplomacy.StateCodes;
        state = state with { Relations = state.Relations.WithRelation(Loser, Bystander, codes.Trade) };

        return (state, "loser-target");
    }

    private static GameState Capture((GameState State, string CapturedCityId) scenario, IEventSink events) =>
        CityCaptureResolver.Capture(
            scenario.State, "winner-army", scenario.CapturedCityId, Ruleset,
            CaptureTestbed.ArcherUnitTypeId, CaptureTestbed.FortifyOrderId, events);

    /// <summary>
    /// Done-when 1: one test pinning each <c>FUN_0044C528</c> effect for this exact case -- the sentinel,
    /// the banner, the +50, the treasury, the merge, the conquered-by and the slots.
    /// </summary>
    [Fact]
    public void Capture_ThatEmptiesTheLoserThroughItsOwnSweep_RunsTheConquest()
    {
        var scenario = BuildScenario();
        var sink = new RecordingEventSink();
        var result = Capture(scenario, sink);

        var loser = result.NationById(Loser)!;
        var winner = result.NationById(Winner)!;

        // "loser-last" defected during the cascade (a CityDefectsToNation, not a capture) before the
        // conquest ran at all -- confirms this scenario actually reaches zero cities the way its own
        // remarks say, not some other route.
        Assert.Single(sink.Events.OfType<CityDefectsToNation>());
        Assert.Equal(Winner, result.CityById("loser-last")!.Owner);
        Assert.Equal(Winner, result.CityById("loser-target")!.Owner);
        Assert.Equal(0, result.CountCitiesOwnedBy(Loser));

        // 1. The capital sentinel: already null on entry (never "loser"'s own capital), and still null --
        // ConquestCascade's own explicit write, not merely untouched.
        Assert.Null(loser.CapitalCityId);

        // 2. The "X conquers Y." banner: exactly one NationConquered, "winner" over "loser".
        var conquered = Assert.Single(sink.Events.OfType<NationConquered>());
        Assert.Equal("winner", conquered.ConqueringNation);
        Assert.Equal("loser", conquered.ConqueredNation);

        // 3. The winner's own +50 unity: 100 (start) + 9 (this capture) + 3 (loser-last's own defection)
        // + 50 (the conquest) = 162, nowhere near economy.unityCap (990).
        Assert.Equal(162, winner.Unity);

        // 4. The treasury copy: both of "loser"'s own cities have tribute 0, so every capture/defection
        // credit "winner" earns from them is 0 -- the only treasury delta left is "loser"'s own 500,
        // copied (not moved -- this type's own remarks on the quirk) onto "winner"'s starting 0.
        Assert.Equal(500, winner.Treasury);

        // 6. Conquered-by: the capturer ("winner"), not "loser-last"'s own receiver -- here the same
        // nation either way, so see the elimination-hooks test below for the case that tells them apart.
        Assert.Equal(Winner, loser.ConqueredBy);

        // 8. Every recruitment slot's troops zeroed: "loser"'s one slot (targeting "winner-home", never
        // captured or defected this scenario, so neither per-city removal above ever touched it) is gone.
        Assert.Empty(loser.RecruitmentSlots);
    }

    /// <summary>
    /// Done-when 1 (Scope item 4, the neighbour merge) and Hazards ("the conquest's city transfer is
    /// empty"): split out of the effects test above only for its own focused remark.
    /// </summary>
    [Fact]
    public void Capture_ThatEmptiesTheLoserThroughItsOwnSweep_MergesTheLosersNeighbourOntoTheWinner()
    {
        var scenario = BuildScenario();
        var result = Capture(scenario, new RecordingEventSink());

        // "winner" gains "bystander" as a neighbour, both ways; "loser"'s own entry is untouched (it still
        // lists "bystander" too -- MergeNeighbours never touches the loser's own entry, see that method's
        // own remarks; a later rebirth, out of this task's Owns, would see it again).
        Assert.Equal(new[] { Bystander }, GetNeighbourIds(result, Winner));
        Assert.Equal(new[] { Loser }, GetNeighbourIds(result, Bystander).Where(id => id != Winner));
        Assert.Contains(Winner, GetNeighbourIds(result, Bystander));
        Assert.Equal(new[] { Bystander }, GetNeighbourIds(result, Loser));
    }

    /// <summary>
    /// Hazards: "Every city the sweep moved already belongs to the capturer, so the conquest's city
    /// transfer is empty." Both of "loser"'s own cities already read <c>Owner == Winner</c> by the time
    /// <see cref="ConquestCascade.Apply"/> runs -- its own city loop finds nothing of "loser"'s left to
    /// move, matching the empty transfer the Hazards note calls out.
    /// </summary>
    [Fact]
    public void Capture_ThatEmptiesTheLoserThroughItsOwnSweep_HasNoLoserCitiesLeftForTheConquestToTransfer()
    {
        var scenario = BuildScenario();
        var result = Capture(scenario, new RecordingEventSink());

        Assert.DoesNotContain(result.Cities, c => string.Equals(c.Owner, Loser, System.StringComparison.Ordinal));
    }

    private static string[] GetNeighbourIds(GameState state, string nationId) =>
        state.Neighbours!.Single(n => n.NationId == nationId).NeighbourIds.ToArray();

    /// <summary>
    /// Done-when 2: when both blocks run (the defection's own elimination, then the conquest), T69's
    /// relation reset and T84's force disposal each fire exactly as the decompile orders them -- twice,
    /// once from each block, same as <c>FUN_0044BED8</c>'s own elimination block and <c>FUN_0044C528</c>
    /// each independently looping every relation slot with no liveness check of their own. The second
    /// firing is idempotent for T84 (nothing of "loser"'s is left to dispose of the second time -- the
    /// first already deleted everything), but directly OBSERVABLE for T69: a relation already reset to a
    /// broken-relation cooldown by the first firing is not one of the three positive states any more, so
    /// the second firing's own mapping (<see cref="RelationTransitions.ResetAllOnElimination"/>'s own
    /// remarks) sends it straight to peace instead of leaving it on the cooldown. A single firing (the
    /// pre-#424 engine, which never reached <see cref="ConquestCascade.Apply"/> for an already-eliminated
    /// loser) would leave "loser"-"bystander" on Trade's own cooldown (-8); this proves both ran.
    /// </summary>
    [Fact]
    public void Capture_ThatEmptiesTheLoserThroughItsOwnSweep_FiresRelationResetAndForceDisposalTwice()
    {
        var scenario = BuildScenario();
        var result = Capture(scenario, new RecordingEventSink());

        // T69, fired twice: Trade -> (first firing) its own cooldown -> (second firing) peace outright,
        // not left sitting on the cooldown a single firing would leave.
        var codes = Ruleset.Diplomacy.StateCodes;
        Assert.Equal(codes.Peace, result.Relations.Get(Loser, Bystander));

        // T84, fired twice, idempotent the second time: "loser"'s own army is gone (the first firing's own
        // effect), and staying gone is exactly what a harmless second firing over an already-empty set
        // looks like -- there is no OTHER army of "loser"'s left for a bug in a would-be single-fire
        // reproduction to leave behind instead, so this is the same assertion either way; the PR names T84
        // as firing twice on the strength of the code path (ConquestCascade.Apply's own unconditional
        // EliminationForces.Dispose call), not a distinguishing assertion here.
        Assert.Null(result.ArmyById("loser-army"));
        Assert.DoesNotContain(result.Armies, a => string.Equals(a.Nation, Loser, System.StringComparison.Ordinal));

        // "loser" itself: eliminated, unity reset, conquered-by the capturer -- NationElimination's own
        // reset (fired first, during the defection) and ConquestCascade's own final write (fired second)
        // agree on every one of these, so this cannot tell the two apart either; see the class remarks.
        var loser = result.NationById(Loser)!;
        Assert.True(loser.Eliminated);
        Assert.Equal(Ruleset.Capture.EliminationUnityReset, loser.Unity);
        Assert.Equal(Winner, loser.ConqueredBy);
    }

    /// <summary>
    /// Done-when 2's own closing probe: two elimination blocks running in the same capture is still a
    /// well-formed state -- no dangling reference, no resource conservation or cap violation
    /// <see cref="GameDataValidation.Validate"/> would reject, and every field survives a
    /// <see cref="SaveManager"/> round trip unchanged. The three-nation scenario itself (Done-when 2's own
    /// "two or three nations") is <see cref="BuildScenario"/>; "bystander" is the untouched third.
    /// </summary>
    [Fact]
    public void Capture_ThatEmptiesTheLoserThroughItsOwnSweep_ValidatesAndRoundTripsThroughSaveManager()
    {
        var scenario = BuildScenario();
        var result = Capture(scenario, new RecordingEventSink());

        // The two-entity probe: "bystander"'s own city and unity are untouched (only its neighbour list
        // and its relation with "loser" change, both asserted elsewhere in this file).
        Assert.Equal(500, result.NationById(Bystander)!.Unity);
        Assert.Equal(Bystander, result.CityById("bystander-city")!.Owner);

        GameDataValidation.Validate("t92-conquest-after-emptying-sweep-probe", result);

        var toy = PersistenceTestbed.Toy;
        var save = new SaveGame(
            SchemaVersion: result.SchemaVersion, Id: "t92-conquest-after-emptying-sweep-probe",
            Label: "T92 conquest-after-emptying-sweep probe", ScenarioId: result.ScenarioId,
            WorldId: result.WorldId, RulesetId: result.RulesetId, State: result);
        var text = SaveManager.Serialize(save);
        var reloaded = SaveManager.Load("t92-conquest-after-emptying-sweep-probe.json", text, toy.World, toy.Ruleset);

        Assert.Equal(result, reloaded.State);
    }
}
