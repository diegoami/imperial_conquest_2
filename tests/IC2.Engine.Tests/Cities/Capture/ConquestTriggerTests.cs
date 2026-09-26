using System.Linq;
using IC2.Engine.Cities.Capture;
using IC2.Engine.Core;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Cities.Capture;

/// <summary>
/// T86 Done-when 1: <c>FUN_0044BB18</c>'s own post-cascade checks (<c>decompiled-elimination-cleanup.md</c>
/// §4), each boundary pinned independently, driven through the real <see cref="CityCaptureResolver.Capture"/>
/// command — never <see cref="ConquestTrigger"/> called directly, so a mutation to <see cref="CityCaptureResolver"/>'s
/// own wiring (which state it evaluates the trigger against, in which order) is caught here too, the same
/// reasoning <c>CascadeGateTests</c>' own remarks give for testing through <c>Capture</c>.
/// </summary>
public sealed class ConquestTriggerTests
{
    private const string OldOwner = "old";
    private const string NewOwner = "new";

    private static Ruleset Ruleset => CaptureTestbed.Ruleset;

    // Loyalty 90 -- at or above CaptureRules.CascadeLoyaltyThreshold (65) -- so a filler city never
    // qualifies for CityCaptureResolver's own regular defection cascade regardless of its distance from
    // the attacker; without this, the cascade could sweep a filler away in the very same call, before
    // the conquest trigger this file tests even runs, silently changing the city counts these scenarios
    // are built around.
    private static CityState Filler(string id, int x, int y) => CaptureTestbed.City(
        id, id, x, y, OldOwner, OldOwner, loyalty: 90, fortificationCode: 0,
        populationThousands: 10, maxPopulationThousands: 20, tribute: 0);

    private static (GameState State, string CapturedCityId) BuildScenario(
        bool captureCapital, int fillerCount, int unity, int?[]? fillerXCoordinates = null)
    {
        var ruleset = Ruleset;
        var capitalId = "capital";
        // Loyalty 90, the same reason Filler uses it: when the capital itself is not the city being
        // captured, it must not be swept away by the regular defection cascade too (it starts within
        // CascadeDistanceMax of the attacker in every non-capital scenario here), which would silently
        // change these scenarios' own carefully-built city counts.
        var capital = CaptureTestbed.City(
            capitalId, "Capital", 0, 0, OldOwner, OldOwner, loyalty: 90, fortificationCode: 0,
            populationThousands: 10, maxPopulationThousands: 20, tribute: 0);

        var fillers = new List<CityState>();
        for (var i = 0; i < fillerCount; i++)
        {
            var x = fillerXCoordinates is not null && i < fillerXCoordinates.Length && fillerXCoordinates[i] is { } explicitX
                ? explicitX
                : 1; // close to the capital by default -- never itself a qualifying capital-move destination.
            fillers.Add(Filler($"filler-{i}", x, 0));
        }

        var oldOwner = CaptureTestbed.Nation(OldOwner, unity: unity, capitalCityId: capitalId);
        var newOwner = CaptureTestbed.Nation(NewOwner);
        var capturedCityId = captureCapital ? capitalId : "filler-0";
        var attackerX = captureCapital ? 0 : 1;
        var attacker = CaptureTestbed.Army(
            "army", NewOwner, attackerX, 0, morale: 50, CaptureTestbed.Unit("heavy_infantry", 1_000_000));

        var allCities = new[] { capital }.Concat(fillers).ToArray();
        var state = CaptureTestbed.StateWith(new[] { oldOwner, newOwner }, allCities, new[] { attacker });

        return (state, capturedCityId);
    }

    private static GameState Capture((GameState State, string CapturedCityId) scenario, RecordingEventSink sink) =>
        CityCaptureResolver.Capture(
            scenario.State, "army", scenario.CapturedCityId, Ruleset,
            CaptureTestbed.ArcherUnitTypeId, CaptureTestbed.FortifyOrderId, sink);

    // ---- Non-capital capture: cityCount < ConquestCityCountThreshold (6) conquers. ----

    [Fact]
    public void NonCapitalCapture_LeavingSixCities_DoesNotConquer()
    {
        // 1 capital + 6 fillers = 7 total; losing one filler leaves 6 (capital + 5 remaining fillers).
        var scenario = BuildScenario(captureCapital: false, fillerCount: 6, unity: 668);
        var sink = new RecordingEventSink();
        var result = Capture(scenario, sink);

        Assert.False(result.NationById(OldOwner)!.Eliminated);
        Assert.Equal(6, result.CountCitiesOwnedBy(OldOwner));
        Assert.Empty(sink.Events.OfType<NationConquered>());
    }

    [Fact]
    public void NonCapitalCapture_LeavingFiveCities_Conquers()
    {
        // 1 capital + 5 fillers = 6 total; losing one filler leaves 5 (capital + 4 remaining fillers).
        var scenario = BuildScenario(captureCapital: false, fillerCount: 5, unity: 668);
        var sink = new RecordingEventSink();
        var result = Capture(scenario, sink);

        Assert.True(result.NationById(OldOwner)!.Eliminated);
        Assert.Equal(0, result.CountCitiesOwnedBy(OldOwner));
        Assert.Single(sink.Events.OfType<NationConquered>());
    }

    // ---- Capital capture, unity gate: > CapitalMoveUnityThreshold (400) attempts a move; at or below, conquered outright. ----

    [Fact]
    public void CapitalCapture_AtUnityFourHundred_DoesNotAttemptAMove_AndConquers()
    {
        var rules = Ruleset.Capture;
        // 8 total (capital + 7 fillers), one far enough to be a valid destination if an attempt were
        // made -- proving the unity gate alone blocks the attempt, not a missing destination.
        var scenario = BuildScenario(
            captureCapital: true, fillerCount: 7, unity: 400 + rules.CaptureUnityLoss,
            fillerXCoordinates: new int?[] { 20 });
        var sink = new RecordingEventSink();
        var result = Capture(scenario, sink);

        Assert.True(result.NationById(OldOwner)!.Eliminated);
        Assert.Empty(sink.Events.OfType<NationCapitalMoved>());
        Assert.Single(sink.Events.OfType<NationConquered>());
    }

    [Fact]
    public void CapitalCapture_AtUnityFourHundredAndOne_AttemptsAMove_AndSucceeds()
    {
        var rules = Ruleset.Capture;
        var scenario = BuildScenario(
            captureCapital: true, fillerCount: 7, unity: 401 + rules.CaptureUnityLoss,
            fillerXCoordinates: new int?[] { 20 });
        var sink = new RecordingEventSink();
        var result = Capture(scenario, sink);

        Assert.False(result.NationById(OldOwner)!.Eliminated);
        Assert.Single(sink.Events.OfType<NationCapitalMoved>());
        Assert.Empty(sink.Events.OfType<NationConquered>());
        Assert.Equal("filler-0", result.NationById(OldOwner)!.CapitalCityId);
        // Unity: 401 (post capture-unity-loss) - 50 (the capital-move attempt's own unconditional cost).
        Assert.Equal(401 - rules.CapitalMoveUnityLoss, result.NationById(OldOwner)!.Unity);
    }

    // ---- Capital capture, city-count gate: > CapitalMoveCityCountThreshold (6) attempts a move. ----

    [Fact]
    public void CapitalCapture_WithSixCitiesRemaining_DoesNotAttemptAMove_AndConquers()
    {
        // 7 total (capital + 6 fillers); losing the capital leaves exactly 6 -- not over the threshold.
        var scenario = BuildScenario(
            captureCapital: true, fillerCount: 6, unity: 668, fillerXCoordinates: new int?[] { 20 });
        var sink = new RecordingEventSink();
        var result = Capture(scenario, sink);

        Assert.True(result.NationById(OldOwner)!.Eliminated);
        Assert.Empty(sink.Events.OfType<NationCapitalMoved>());
    }

    [Fact]
    public void CapitalCapture_WithSevenCitiesRemaining_AttemptsAMove_AndSucceeds()
    {
        // 8 total (capital + 7 fillers); losing the capital leaves exactly 7 -- over the threshold.
        var scenario = BuildScenario(
            captureCapital: true, fillerCount: 7, unity: 668, fillerXCoordinates: new int?[] { 20 });
        var sink = new RecordingEventSink();
        var result = Capture(scenario, sink);

        Assert.False(result.NationById(OldOwner)!.Eliminated);
        Assert.Single(sink.Events.OfType<NationCapitalMoved>());
    }

    // ---- Capital move, destination distance: > CapitalMoveMinDistanceTiles (10) from the fallen capital. ----

    [Fact]
    public void CapitalMove_WithNoCityMoreThanTenTilesAway_Conquers()
    {
        // Farthest filler at exactly 10 tiles (Chebyshev) from the fallen capital (0,0) -- not over the
        // threshold, so no destination qualifies.
        var scenario = BuildScenario(
            captureCapital: true, fillerCount: 7, unity: 668, fillerXCoordinates: new int?[] { 10 });
        var sink = new RecordingEventSink();
        var result = Capture(scenario, sink);

        Assert.True(result.NationById(OldOwner)!.Eliminated);
        Assert.Empty(sink.Events.OfType<NationCapitalMoved>());
        Assert.Single(sink.Events.OfType<NationConquered>());
        // The attempt still cost 50 unity before conquest's own effects ran -- but conquest itself
        // resets unity to EliminationUnityReset, so that intermediate value is not independently
        // observable here; ConquestCascadeTests pins the reset itself.
    }

    [Fact]
    public void CapitalMove_WithACityElevenTilesAway_Moves()
    {
        var rules = Ruleset.Capture;
        // Farthest filler at 11 tiles -- one past the threshold, so it qualifies as a destination.
        var scenario = BuildScenario(
            captureCapital: true, fillerCount: 7, unity: 668, fillerXCoordinates: new int?[] { 11 });
        var sink = new RecordingEventSink();
        var result = Capture(scenario, sink);

        Assert.False(result.NationById(OldOwner)!.Eliminated);
        var moved = Assert.Single(sink.Events.OfType<NationCapitalMoved>());
        Assert.Equal(OldOwner, moved.Nation);
        Assert.Equal("filler-0", result.NationById(OldOwner)!.CapitalCityId);
        // 668, less the forced capture's own unity loss (every capture pays this, capital or not), less
        // the capital-move attempt's own separate cost.
        Assert.Equal(668 - rules.CaptureUnityLoss - rules.CapitalMoveUnityLoss, result.NationById(OldOwner)!.Unity);
    }

    // ---- Capital move, destination scoring (review round 1, B1: research dcd8fd7, FUN_0044BD2C :50234-50285). ----
    // A qualifying distance alone is not enough: the candidate must also score strictly above 0 on
    // (strength / CapitalMoveStrengthDivisor) / distance, and among equally-scored candidates the one
    // earliest in GameState.Cities' own stable (city-table) order wins.

    [Fact]
    public void CapitalMove_WhenTheOnlyQualifyingDestinationScoresExactlyZero_Conquers()
    {
        // A filler 2000 tiles away still passes the >10 distance filter, but this filler's own defender
        // strength (loyalty 90, fortification 0, population 10 -- the same stats every other scenario in
        // this file uses) floors to (90*150 + 10*200) / CapitalMoveStrengthDivisor(10) = 1550, and
        // 1550 / 2000 truncates to exactly 0. The score comparison is strictly '>' against a bestScore
        // that starts at 0, so a 0 score must not win -- this kills a mutation that weakens the
        // comparison to '>=', which would let a 0-scoring city move the capital instead of conquering.
        var scenario = BuildScenario(
            captureCapital: true, fillerCount: 7, unity: 668, fillerXCoordinates: new int?[] { 2000 });
        var sink = new RecordingEventSink();
        var result = Capture(scenario, sink);

        Assert.True(result.NationById(OldOwner)!.Eliminated);
        Assert.Empty(sink.Events.OfType<NationCapitalMoved>());
        Assert.Single(sink.Events.OfType<NationConquered>());
    }

    [Fact]
    public void CapitalMove_WithTwoEquallyScoredDestinations_PicksTheEarlierOneInCityTableOrder()
    {
        // filler-0 and filler-1 are both at 11 tiles, both with the same fixed filler stats, so both
        // score identically. The winner must be the FIRST of the two encountered in GameState.Cities'
        // own stable order (filler-0, since fillers are appended in index order and the capital comes
        // first) -- a strictly-greater-than comparison against the running best only replaces it on a
        // strictly better score, so a later equally-scored city never displaces an earlier one. This
        // kills a mutation that reverses the candidate search order (the reviewer's ME4): under a
        // reversed scan, filler-1 would be found first and would keep the tie instead.
        var scenario = BuildScenario(
            captureCapital: true, fillerCount: 7, unity: 668, fillerXCoordinates: new int?[] { 11, 11 });
        var sink = new RecordingEventSink();
        var result = Capture(scenario, sink);

        Assert.False(result.NationById(OldOwner)!.Eliminated);
        var moved = Assert.Single(sink.Events.OfType<NationCapitalMoved>());
        Assert.Equal(OldOwner, moved.Nation);
        Assert.Equal("filler-0", result.NationById(OldOwner)!.CapitalCityId);
    }

    /// <summary>
    /// Review round 2, B4: every prior scenario in this file was a tie, a single candidate, or a score
    /// of 0 -- none pinned "the higher score wins over list order" (Done-when 1's own added bullet), so
    /// a mutation that stops the search at the first positive-scoring candidate (the reviewer's own MS1,
    /// <c>if (score &gt; bestScore &amp;&amp; best is null)</c>) survived the whole suite.
    /// </summary>
    [Fact]
    public void CapitalMove_WithALowerScoringCandidateBeforeAHigherScoringOne_PicksTheHigherScore()
    {
        // filler-0 (table order first) sits 20 tiles away: score floor(1550/20) = 77. filler-1 (table
        // order second) sits 11 tiles away: score floor(1550/11) = 140 -- higher, despite coming later.
        // The correct destination is filler-1; MS1 would incorrectly keep filler-0, the first city to
        // score above 0.
        var scenario = BuildScenario(
            captureCapital: true, fillerCount: 7, unity: 668, fillerXCoordinates: new int?[] { 20, 11 });
        var sink = new RecordingEventSink();
        var result = Capture(scenario, sink);

        Assert.False(result.NationById(OldOwner)!.Eliminated);
        var moved = Assert.Single(sink.Events.OfType<NationCapitalMoved>());
        Assert.Equal(OldOwner, moved.Nation);
        Assert.Equal("filler-1", result.NationById(OldOwner)!.CapitalCityId);
    }

    // ---- The new capital's own boosts (plan PR #410, 2026-09-26: research dcd8fd7, FUN_0044BD2C :50286-50295). ----

    [Fact]
    public void CapitalMove_Success_AppliesAllFiveNewCapitalBoosts()
    {
        // Every filler in this file starts at loyalty 90, fortification 0, population 10, maximum
        // population 20, tribute 0 -- none of which is anywhere near CapitalMoveNewCapitalStatCap (99),
        // so this pins the five gain constants themselves, unclamped.
        var rules = Ruleset.Capture;
        var scenario = BuildScenario(
            captureCapital: true, fillerCount: 7, unity: 668, fillerXCoordinates: new int?[] { 11 });
        var sink = new RecordingEventSink();
        var result = Capture(scenario, sink);

        var destination = result.CityById("filler-0")!;
        Assert.Equal(90 + rules.CapitalMoveNewCapitalLoyaltyGain, destination.Loyalty);
        Assert.Equal(0 + rules.CapitalMoveNewCapitalFortificationGain, destination.FortificationCode);
        Assert.Equal(10 + rules.CapitalMoveNewCapitalPopulationGain, destination.PopulationThousands);
        Assert.Equal(20 + rules.CapitalMoveNewCapitalMaxPopulationGain, destination.MaxPopulationThousands);
        Assert.Equal(0 + rules.CapitalMoveNewCapitalTributeGain, destination.Tribute);
    }

    [Fact]
    public void CapitalMove_Success_AtTheirCap_LoyaltyNinetyOneAndFortificationEightyNineBothReachNinetyNine()
    {
        // The exact boundary review round 1's plan amendment asks for: 91 + 8 = 99 and 89 + 10 = 99,
        // landing exactly on CapitalMoveNewCapitalStatCap without needing the clamp to do any work --
        // proving the cap's own VALUE (99) is right. CapitalMove_Success_AboveTheirCap_ClampsToTheSharedStatCap
        // below separately proves the clamp itself is active (a mutation deleting Math.Min would not be
        // caught here, since 91 + 8 and 89 + 10 both land on 99 unclamped).
        var rules = Ruleset.Capture;
        var (state, destination) = BuildDestinationBoostScenario(destinationLoyalty: 91, destinationFortificationCode: 89);
        var sink = new RecordingEventSink();
        var result = CityCaptureResolver.Capture(
            state, "army", "capital", Ruleset, CaptureTestbed.ArcherUnitTypeId, CaptureTestbed.FortifyOrderId, sink);

        Assert.False(result.NationById(OldOwner)!.Eliminated);
        Assert.Equal(destination, result.NationById(OldOwner)!.CapitalCityId);
        var destinationCity = result.CityById(destination)!;
        Assert.Equal(rules.CapitalMoveNewCapitalStatCap, destinationCity.Loyalty);
        Assert.Equal(rules.CapitalMoveNewCapitalStatCap, destinationCity.FortificationCode);
    }

    [Fact]
    public void CapitalMove_Success_AboveTheirCap_ClampsToTheSharedStatCap()
    {
        // 95 + 8 = 103 and 95 + 10 = 105, both past the cap -- unlike the exact-boundary test above, a
        // mutation that deletes the Math.Min clamp entirely changes this test's own outcome (103/105
        // instead of 99/99), so this is the one that actually kills that mutation.
        var rules = Ruleset.Capture;
        var (state, destination) = BuildDestinationBoostScenario(destinationLoyalty: 95, destinationFortificationCode: 95);
        var sink = new RecordingEventSink();
        var result = CityCaptureResolver.Capture(
            state, "army", "capital", Ruleset, CaptureTestbed.ArcherUnitTypeId, CaptureTestbed.FortifyOrderId, sink);

        var destinationCity = result.CityById(destination)!;
        Assert.Equal(rules.CapitalMoveNewCapitalStatCap, destinationCity.Loyalty);
        Assert.Equal(rules.CapitalMoveNewCapitalStatCap, destinationCity.FortificationCode);
    }

    [Fact]
    public void CapitalMove_Success_DiscardsAPendingFortificationOrderAtTheNewCapital()
    {
        // A fortification code of 150 -- above the fortify order's own MaxPercent (100) -- encodes an
        // order in progress (1 point pending, 50% already finished: FUN_0044A98C's own dual encoding).
        // The boost is applied to the RAW word (150 + 10 = 160), then clamped to the cap (99), which both
        // discards the pending order and leaves the new capital's own finished percentage at the cap --
        // exactly the addendum's own "the move discards it" remark.
        var rules = Ruleset.Capture;
        var (state, destination) = BuildDestinationBoostScenario(destinationLoyalty: 90, destinationFortificationCode: 150);
        var sink = new RecordingEventSink();
        var result = CityCaptureResolver.Capture(
            state, "army", "capital", Ruleset, CaptureTestbed.ArcherUnitTypeId, CaptureTestbed.FortifyOrderId, sink);

        var destinationCity = result.CityById(destination)!;
        Assert.Equal(rules.CapitalMoveNewCapitalStatCap, destinationCity.FortificationCode);
        Assert.False(FortificationCode.IsOrderInProgress(destinationCity.FortificationCode, Ruleset.CityOrders.Orders[0]));
        Assert.Equal(rules.CapitalMoveNewCapitalStatCap, FortificationCode.FinishedPercent(destinationCity.FortificationCode, Ruleset.CityOrders.Orders[0]));
    }

    [Fact]
    public void CapitalMove_WhenTheAttemptFails_AppliesNoBoostToAnyCity()
    {
        // filler-0 -- the one city that would have qualified as a destination one tile closer -- is
        // still transferred to the winner by the conquest cascade that fires instead (which rewrites
        // Owner and Loyalty through its own, separate mass-transfer formula), but the cascade never
        // touches fortification, population, maximum population or tribute (ConquestCascade's own Apply
        // only ever writes Owner and Loyalty on a transferred city). All four staying at their original
        // filler values proves none of the capital-move boosts fired when the attempt itself failed.
        var scenario = BuildScenario(
            captureCapital: true, fillerCount: 7, unity: 668, fillerXCoordinates: new int?[] { 10 });
        var sink = new RecordingEventSink();
        var result = Capture(scenario, sink);

        Assert.True(result.NationById(OldOwner)!.Eliminated);
        Assert.Empty(sink.Events.OfType<NationCapitalMoved>());

        var candidate = result.CityById("filler-0")!;
        Assert.Equal(0, candidate.FortificationCode);
        Assert.Equal(10, candidate.PopulationThousands);
        Assert.Equal(20, candidate.MaxPopulationThousands);
        Assert.Equal(0, candidate.Tribute);
    }

    /// <summary>
    /// A capital-capture scenario built around one specific destination city (11 tiles away, so it
    /// always qualifies and always wins the score), with a caller-chosen starting loyalty and
    /// fortification code -- for the boost-cap tests above, which need starting values <see cref="Filler"/>
    /// does not give them.
    /// </summary>
    private static (GameState State, string DestinationId) BuildDestinationBoostScenario(
        int destinationLoyalty, int destinationFortificationCode)
    {
        const string capitalId = "capital";
        const string destinationId = "destination";
        var capital = CaptureTestbed.City(
            capitalId, "Capital", 0, 0, OldOwner, OldOwner, loyalty: 90, fortificationCode: 0,
            populationThousands: 10, maxPopulationThousands: 20, tribute: 0);
        var destinationCity = CaptureTestbed.City(
            destinationId, "Destination", 11, 0, OldOwner, OldOwner,
            loyalty: destinationLoyalty, fortificationCode: destinationFortificationCode,
            populationThousands: 10, maxPopulationThousands: 20, tribute: 0);

        var fillers = new List<CityState>();
        for (var i = 0; i < 6; i++)
        {
            fillers.Add(Filler($"filler-{i}", 1, 0));
        }

        var oldOwner = CaptureTestbed.Nation(OldOwner, unity: 668, capitalCityId: capitalId);
        var newOwner = CaptureTestbed.Nation(NewOwner);
        var attacker = CaptureTestbed.Army(
            "army", NewOwner, 0, 0, morale: 50, CaptureTestbed.Unit("heavy_infantry", 1_000_000));

        var allCities = new[] { capital, destinationCity }.Concat(fillers).ToArray();
        var state = CaptureTestbed.StateWith(new[] { oldOwner, newOwner }, allCities, new[] { attacker });
        return (state, destinationId);
    }

    // ---- T91 Done-when 3 (bug #409 S4): FindCapitalMoveDestination's own defender-strength scoring
    // applies the x5/3 capital bonus to ANY nation's capital, not just OldOwner's own -- the same
    // CapitalOwnership.IsAnyNationsCapital predicate RunCascade's gate and InstantBattleResolver's own
    // siege-strength call share. Two candidates at the SAME qualifying distance (11 tiles) so the winner is
    // decided purely by score, never by city-table order: a plain "competitor" at the shipped Filler stats
    // (score 140, hand-computed below) and a "stale" candidate that only a third, ELIMINATED nation's own
    // stale CapitalCityId names -- never OldOwner's own capital, which is "capital", captured this same
    // call. ----

    [Fact]
    public void CapitalMove_DestinationScoring_AppliesTheCapitalBonusForAnotherNationsStaleCapital_AtLoyaltySixty()
    {
        // Stale candidate, loyalty 60: weighted sum 60x150 + 10x200 = 11,000; x5/3 (capital, loyalty > 59)
        // = 18,333; /10 = 1,833; /11 = 166 -- above the competitor's own 140, so the stale candidate wins
        // despite its OWN owner (OldOwner) never holding it as a capital.
        var (state, staleCandidateId, _) = BuildLoyaltyBoundaryDestinationScenario(staleCandidateLoyalty: 60);
        var sink = new RecordingEventSink();
        var result = CityCaptureResolver.Capture(
            state, "army", "capital", Ruleset, CaptureTestbed.ArcherUnitTypeId, CaptureTestbed.FortifyOrderId, sink);

        Assert.False(result.NationById(OldOwner)!.Eliminated);
        var moved = Assert.Single(sink.Events.OfType<NationCapitalMoved>());
        Assert.Equal(OldOwner, moved.Nation);
        Assert.Equal(staleCandidateId, result.NationById(OldOwner)!.CapitalCityId);
    }

    [Fact]
    public void CapitalMove_DestinationScoring_LoyaltyBoundary_AtFiftyNineTheBonusDoesNotApply_CompetitorWins()
    {
        // Stale candidate, loyalty 59: weighted sum 59x150 + 10x200 = 10,850; NO bonus (loyalty not > 59):
        // /10 = 1,085; /11 = 98 -- below the competitor's own 140, so the competitor wins even though the
        // stale candidate IS some (eliminated) nation's capital. Checking only OldOwner's own capital would
        // also give "no bonus" here (already correct); the decisive half of this boundary is the loyalty-60
        // test above, where the SAME "any nation's capital" reading DOES apply the bonus.
        var (state, _, competitorId) = BuildLoyaltyBoundaryDestinationScenario(staleCandidateLoyalty: 59);
        var sink = new RecordingEventSink();
        var result = CityCaptureResolver.Capture(
            state, "army", "capital", Ruleset, CaptureTestbed.ArcherUnitTypeId, CaptureTestbed.FortifyOrderId, sink);

        Assert.False(result.NationById(OldOwner)!.Eliminated);
        Assert.Equal(competitorId, result.NationById(OldOwner)!.CapitalCityId);
    }

    /// <summary>
    /// Two candidates at the same qualifying distance (11 tiles, past <see cref="CaptureRules.CapitalMoveMinDistanceTiles"/>):
    /// a plain "competitor" at the shipped <see cref="Filler"/> stats (loyalty 90, fortification 0,
    /// population 10 -- score 140, hand-computed in each test above), and a "stale" candidate at
    /// <paramref name="staleCandidateLoyalty"/> that only a third, ELIMINATED nation's own
    /// <see cref="NationState.CapitalCityId"/> still names.
    /// </summary>
    private static (GameState State, string StaleCandidateId, string CompetitorId) BuildLoyaltyBoundaryDestinationScenario(
        int staleCandidateLoyalty)
    {
        const string capitalId = "capital";
        const string staleCandidateId = "stale-candidate";
        const string competitorId = "competitor";
        const string staleNationId = "gone";

        var capital = CaptureTestbed.City(
            capitalId, "Capital", 0, 0, OldOwner, OldOwner, loyalty: 90, fortificationCode: 0,
            populationThousands: 10, maxPopulationThousands: 20, tribute: 0);

        // 11 tiles by Chebyshev either way -- (0,11) and (11,0) -- both past CapitalMoveMinDistanceTiles (10).
        var staleCandidate = CaptureTestbed.City(
            staleCandidateId, "Stale Candidate", 0, 11, OldOwner, OldOwner,
            loyalty: staleCandidateLoyalty, fortificationCode: 0, populationThousands: 10, maxPopulationThousands: 20, tribute: 0);
        var competitor = Filler(competitorId, 11, 0);

        var fillers = new List<CityState>();
        for (var i = 0; i < 5; i++)
        {
            fillers.Add(Filler($"filler-{i}", 1, 0));
        }

        var oldOwner = CaptureTestbed.Nation(OldOwner, unity: 668, capitalCityId: capitalId);
        var newOwner = CaptureTestbed.Nation(NewOwner);
        // #409 S4/T91: FUN_0044B8D0 has no liveness check -- an eliminated nation's own stale capital
        // pointer gates the x5/3 exactly like a living one's.
        var staleNation = CaptureTestbed.Nation(staleNationId, capitalCityId: staleCandidateId, eliminated: true);
        var attacker = CaptureTestbed.Army(
            "army", NewOwner, 0, 0, morale: 50, CaptureTestbed.Unit("heavy_infantry", 1_000_000));

        var allCities = new[] { capital, staleCandidate, competitor }.Concat(fillers).ToArray();
        var state = CaptureTestbed.StateWith(new[] { oldOwner, newOwner, staleNation }, allCities, new[] { attacker });
        return (state, staleCandidateId, competitorId);
    }
}
