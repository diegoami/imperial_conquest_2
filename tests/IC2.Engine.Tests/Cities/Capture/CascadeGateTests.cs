using System.Linq;
using IC2.Engine.Cities.Capture;
using IC2.Engine.Core;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Cities.Capture;

/// <summary>
/// <c>FUN_0044ba1c</c>'s own confirmed cascade gate, each term pinned independently: not any of the
/// sixteen nations' own <see cref="NationState.CapitalCityId"/> (<c>FUN_0044b8d0</c>; T90/#409, replacing
/// T17's invented <see cref="CityState.UnderSiege"/> reading — see the "no siege gate" tests below), within
/// <see cref="CaptureRules.CascadeDistanceMax"/>, the new owner's unity under
/// <see cref="CaptureRules.CascadeUnityThreshold"/>, the candidate's own loyalty under
/// <see cref="CaptureRules.CascadeLoyaltyThreshold"/>, and the allegiant-sympathy defense divisor
/// (<see cref="CaptureRules.CascadeAllegiantDefenseDivisor"/>). Every fixture here holds every other term
/// fixed and varies exactly one, so a mutation to any single gate is caught by exactly one test —
/// <c>CityCaptureResolver.cs</c>'s own review round found three of these four gates had no test at all
/// (review round 1, findings B2/B3) and every boundary was unpinned (N1).
/// </summary>
public sealed class CascadeGateTests
{
    private const string OldOwner = "old";
    private const string NewOwner = "new";
    private const string CapturedId = "captured";
    private const string CandidateId = "candidate";

    /// <summary>
    /// The always-qualifying baseline every other test in this file perturbs exactly one field of:
    /// candidate loyalty 10 (well under the 65 threshold), same allegiance as the old owner (no ÷3
    /// divisor), distance 1 (well under the 10 threshold), not under siege, not any nation's capital, new
    /// owner unity 500 (well under the 650 threshold), and an attacker strong enough (50,000 troops,
    /// morale 80 → <c>SiegeStrength.Attacker</c> 50,000) that the candidate's own weak defense (1,700,
    /// undivided) never blocks the outcome on its own.
    /// </summary>
    /// <param name="candidateCapitalOfNationId">
    /// When set, some nation's <see cref="NationState.CapitalCityId"/> is pointed at the candidate --
    /// <see cref="OldOwner"/> to make it the loser's own capital, or any other id to add a third nation
    /// (optionally <paramref name="capitalNationEliminated"/>) whose stale capital pointer still names a
    /// city <see cref="OldOwner"/> owns. <c>FUN_0044b8d0</c> gates on every nation's capital pointer alike,
    /// so this is one knob for all three Done-when-1 cases.
    /// </param>
    private static (GameState State, Ruleset Ruleset) BuildScenario(
        int candidateLoyalty = 10,
        string? candidateAllegiance = null,
        int candidateDistance = 1,
        bool candidateUnderSiege = false,
        int newOwnerUnity = 500,
        int attackerTroops = 50_000,
        int attackerMorale = 80,
        string? candidateCapitalOfNationId = null,
        bool capitalNationEliminated = false)
    {
        var ruleset = CaptureTestbed.Ruleset;

        var captured = CaptureTestbed.City(
            CapturedId, "Captured", 0, 0, OldOwner, OldOwner,
            loyalty: 10, fortificationCode: 0, populationThousands: 1, maxPopulationThousands: 10, tribute: 0);

        var candidate = CaptureTestbed.City(
            CandidateId, "Candidate", candidateDistance, 0, OldOwner, candidateAllegiance ?? OldOwner,
            candidateLoyalty, fortificationCode: 0, populationThousands: 1, maxPopulationThousands: 10, tribute: 0,
            underSiege: candidateUnderSiege);

        var oldOwnerIsCandidateCapital = string.Equals(candidateCapitalOfNationId, OldOwner, StringComparison.Ordinal);
        var oldOwner = CaptureTestbed.Nation(OldOwner, capitalCityId: oldOwnerIsCandidateCapital ? CandidateId : null);
        var newOwner = CaptureTestbed.Nation(NewOwner, unity: newOwnerUnity);
        var attacker = CaptureTestbed.Army(
            "army", NewOwner, 0, 0, attackerMorale, CaptureTestbed.Unit("heavy_infantry", attackerTroops));

        var nations = new List<NationState> { oldOwner, newOwner };
        if (candidateCapitalOfNationId is { } thirdNationId && !oldOwnerIsCandidateCapital)
        {
            nations.Add(CaptureTestbed.Nation(thirdNationId, capitalCityId: CandidateId, eliminated: capitalNationEliminated));
        }

        // T86: 5 filler cities, far enough away (CascadeDistanceMax is 10) that none is itself cascade-
        // eligible, so the old owner keeps 6 cities after "captured" is transferred (candidate + 5
        // fillers) -- at or above CaptureRules.ConquestCityCountThreshold, so the conquest cascade never
        // fires and overrides whatever this file's own cascade-gate tests are checking. Without this,
        // every "does not defect" test here would see "candidate" taken anyway by conquest (the old
        // owner would otherwise be left with well under 6 cities), which is a different mechanism from
        // the one under test.
        var fillerCities = CaptureTestbed.FillerCities(OldOwner, 5, startX: 1000, y: 1000).ToArray();

        var state = CaptureTestbed.StateWith(
            nations, new[] { captured, candidate }.Concat(fillerCities), new[] { attacker });

        return (state, ruleset);
    }

    private static bool CandidateDefected(GameState state, Ruleset ruleset)
    {
        var sink = new RecordingEventSink();
        var result = CityCaptureResolver.Capture(
            state, "army", CapturedId, ruleset, CaptureTestbed.ArcherUnitTypeId, CaptureTestbed.FortifyOrderId, sink);

        var defected = sink.Events.OfType<CityDefectsToNation>().Any(e => e.CityName == "Candidate");
        Assert.Equal(defected, !string.Equals(result.CityById(CandidateId)!.Owner, OldOwner, System.StringComparison.Ordinal));
        return defected;
    }

    /// <summary>The baseline itself qualifies -- confirms every other test's "does not defect" result is the gate firing, not a broken fixture.</summary>
    [Fact]
    public void Baseline_Defects()
    {
        var (state, ruleset) = BuildScenario();
        Assert.True(CandidateDefected(state, ruleset));
    }

    // ---- Done-when 1: the capital gate (FUN_0044b8d0). A candidate that is some nation's capital never
    // defects, regardless of which nation, and regardless of whether that nation is still alive. ----

    /// <summary>The loser's own capital.</summary>
    [Fact]
    public void LosersOwnCapital_ExcludesTheCandidateFromTheCascade()
    {
        var (state, ruleset) = BuildScenario(candidateCapitalOfNationId: OldOwner);
        Assert.False(CandidateDefected(state, ruleset));
    }

    /// <summary>A third (living) nation's capital, a city the loser itself owns.</summary>
    [Fact]
    public void ThirdNationsCapital_OwnedByTheLoser_ExcludesTheCandidateFromTheCascade()
    {
        var (state, ruleset) = BuildScenario(candidateCapitalOfNationId: "third");
        Assert.False(CandidateDefected(state, ruleset));
    }

    /// <summary>
    /// An eliminated nation's capital field, left stale by T86's corrected <c>Defect</c> (which never
    /// clears <see cref="NationState.CapitalCityId"/> on elimination -- <c>NationElimination</c>'s own
    /// remarks), still naming a city the loser now owns. The engine's state can hold this directly: an
    /// eliminated nation's record is kept, not removed, and its capital pointer is never rewritten.
    /// </summary>
    [Fact]
    public void EliminatedNationsStaleCapital_OwnedByTheLoser_ExcludesTheCandidateFromTheCascade()
    {
        var (state, ruleset) = BuildScenario(candidateCapitalOfNationId: "gone", capitalNationEliminated: true);
        Assert.False(CandidateDefected(state, ruleset));
    }

    // ---- Done-when 2: no siege gate. FUN_0044ba1c's own sweep never reads CityState.UnderSiege at all --
    // T17 invented that reading in the capital gate's place. A besieged, non-capital candidate that passes
    // every other gate now defects like any other. Restoring the old UnderSiege gate fails this test. ----

    [Fact]
    public void UnderSiege_NoLongerExcludesTheCandidateFromTheCascade()
    {
        var (state, ruleset) = BuildScenario(candidateUnderSiege: true);
        Assert.True(CandidateDefected(state, ruleset));
    }

    // ---- B2 / N1: distance gate, exclusive threshold (Chebyshev < 10, i.e. >= 10 excludes). ----

    [Fact]
    public void Distance_OneBelowTheMax_Defects()
    {
        var (state, ruleset) = BuildScenario(candidateDistance: 9);
        Assert.True(CandidateDefected(state, ruleset));
    }

    [Fact]
    public void Distance_AtTheMax_DoesNotDefect()
    {
        var (state, ruleset) = BuildScenario(candidateDistance: 10);
        Assert.False(CandidateDefected(state, ruleset));
    }

    // ---- B2 / N1: unity gate, exclusive threshold (new owner unity < 650, i.e. >= 650 excludes). The
    // gate reads the new owner's unity AFTER the captured city's own +9 capture credit
    // (CaptureRules.CaptureUnityGain) has already applied -- the cascade runs at the end of Capture, not
    // before it -- so the input here is offset by -9 to land the gate's own read exactly on the boundary
    // under test. ----

    [Fact]
    public void Unity_OneBelowTheThreshold_Defects()
    {
        var (state, ruleset) = BuildScenario(newOwnerUnity: 649 - 9);
        Assert.True(CandidateDefected(state, ruleset));
    }

    [Fact]
    public void Unity_AtTheThreshold_DoesNotDefect()
    {
        var (state, ruleset) = BuildScenario(newOwnerUnity: 650 - 9);
        Assert.False(CandidateDefected(state, ruleset));
    }

    // ---- B2 / N1: loyalty gate, exclusive threshold (candidate loyalty < 65, i.e. >= 65 excludes). ----

    [Fact]
    public void Loyalty_OneBelowTheThreshold_Defects()
    {
        var (state, ruleset) = BuildScenario(candidateLoyalty: 64);
        Assert.True(CandidateDefected(state, ruleset));
    }

    [Fact]
    public void Loyalty_AtTheThreshold_DoesNotDefect()
    {
        var (state, ruleset) = BuildScenario(candidateLoyalty: 65);
        Assert.False(CandidateDefected(state, ruleset));
    }

    // ---- B2: the allegiant-sympathy defense divisor. A weaker attacker (troops 1,000, morale 50 ->
    // SiegeStrength.Attacker 600) than the baseline's, so the candidate's own undivided defense (1,700)
    // does NOT qualify on its own -- only the ÷3 divisor (566, when the candidate's allegiance already
    // matches the new owner) brings it under the attacker's strength. ----

    [Fact]
    public void AllegiantCandidate_DefenseIsHalvedByTheDivisor_AndQualifies()
    {
        var (state, ruleset) = BuildScenario(candidateAllegiance: NewOwner, attackerTroops: 1000, attackerMorale: 50);
        Assert.True(CandidateDefected(state, ruleset));
    }

    /// <summary>
    /// The same fixture, but the candidate's allegiance is the OLD owner's instead: the divisor never
    /// fires, the undivided defense (1,700) is not below the (weaker) attacker's strength (600), and the
    /// candidate does not defect. Mutation proof for the divisor: neutralising it (<c>/= 1</c> instead of
    /// <c>/= CascadeAllegiantDefenseDivisor</c>) makes <see cref="AllegiantCandidate_DefenseIsHalvedByTheDivisor_AndQualifies"/>
    /// fail while this test stays green either way -- the pair together is what catches it.
    /// </summary>
    [Fact]
    public void NonAllegiantCandidate_DefenseIsNotDivided_AndDoesNotQualify()
    {
        var (state, ruleset) = BuildScenario(candidateAllegiance: OldOwner, attackerTroops: 1000, attackerMorale: 50);
        Assert.False(CandidateDefected(state, ruleset));
    }
}
