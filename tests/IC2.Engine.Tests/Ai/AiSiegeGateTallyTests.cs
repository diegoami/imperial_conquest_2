using IC2.Engine.Ai;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Recruitment.Commands;
using IC2.Engine.Tests.Battle;
using IC2.Engine.Tests.Battle.Commands;
using Xunit;
using CaptureFixtures = IC2.Engine.Tests.Cities.Capture.CaptureTestbed;

namespace IC2.Engine.Tests.Ai;

/// <summary>
/// <c>docs/task-catalogue.md</c> T60 Done-when 1: the three gates past ownership in
/// <c>AiMilitaryPhase.ProposeSieges</c> are told apart by a counter rather than inferred from silence.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why this file is unit tests and not only the soak.</strong>
/// <see cref="AiSiegeDiagnosticsTests"/> reports the distribution across the fifty seeds, which is the
/// deliverable; but a distribution is only worth reading if each counter means what it says. The soak's
/// toy data happens to exercise exactly one of the three outcomes (the ratio gate), so these build the
/// other two — an army that is never adjacent, and a siege the legality gate refuses — from scripted
/// states, and assert the tally tells them apart.
/// </para>
/// <para>
/// <strong>And that the tally cannot change the game.</strong>
/// <see cref="A_turn_played_with_a_tally_plays_the_same_game_as_one_without"/> runs the same seeded turn
/// twice, once with the tally and once without, and asserts the two states are identical. That is the
/// claim <see cref="AiSiegeGateTally"/>'s own remarks make, and it is what makes the instrumentation
/// admissible at all: an observer that perturbed the decision would be measuring something other than
/// the AI this task is diagnosing.
/// </para>
/// </remarks>
public sealed class AiSiegeGateTallyTests
{
    private const string Acting = AiScriptedStates.Attacker;
    private const string Other = AiScriptedStates.Defender;

    private static Ruleset Ruleset => AiScriptedStates.Ruleset;

    /// <summary>
    /// Gate (c): the army is three tiles from the only enemy city, so the adjacency test short-circuits
    /// and nothing downstream is measured at all. The tally has nothing to say, and says nothing.
    /// </summary>
    [Fact]
    public void An_army_that_never_reaches_adjacency_records_no_gate_at_all()
    {
        var tally = ProposeWith(ArmyBesideCity(armyX: 0, armyY: 4), out _);

        Assert.Equal(0, tally.Adjacent);
        Assert.Equal(0, tally.RejectedByLegality);
        Assert.Equal(0, tally.RejectedByRatio);
        Assert.Equal(0, tally.Proposed);
        Assert.True(tally.IsEmpty, "nothing was observed, so there is no line to write");
        Assert.Null(tally.Describe());
    }

    /// <summary>
    /// Gate (b), the soak's case: adjacent and legal, but the city is far too strong. The tally records
    /// the rejection <em>and</em> the three numbers the gate compared, which is what turns "the AI did
    /// not besiege" into a diagnosis.
    /// </summary>
    [Fact]
    public void An_adjacent_army_too_weak_to_win_is_recorded_against_the_ratio_gate()
    {
        var tally = ProposeWith(ArmyBesideCity(troops: 1000), out var candidates);

        Assert.Equal(1, tally.Adjacent);
        Assert.Equal(0, tally.RejectedByLegality);
        Assert.Equal(1, tally.RejectedByRatio);
        Assert.Equal(0, tally.Proposed);
        Assert.DoesNotContain(candidates, c => string.Equals(c.Kind, "besiege", StringComparison.Ordinal));

        // The closest-attempt snapshot is the evidence, so it has to be the real comparison and not a
        // placeholder: below the bar, naming the pair, on the same permille scale the gate used.
        Assert.Equal("besieger", tally.BestArmyId);
        Assert.Equal("defender-city", tally.BestCityId);
        Assert.True(tally.BestAttackerPower > 0, "a real army has a real siege strength");
        Assert.True(tally.BestDefenderPower > 0, "a real city has a real defender strength");
        Assert.True(
            tally.BestRatioPermille < tally.BestRequiredRatioPermille,
            $"the gate rejected, so {tally.BestRatioPermille} must be under {tally.BestRequiredRatioPermille}");
        Assert.Contains("below ratio", tally.Describe()!, StringComparison.Ordinal);
    }

    /// <summary>The same adjacency with an army strong enough: the gate passes and a candidate exists.</summary>
    [Fact]
    public void An_adjacent_army_strong_enough_is_recorded_as_a_proposal()
    {
        var tally = ProposeWith(ArmyBesideCity(troops: 400000), out var candidates);

        Assert.Equal(1, tally.Adjacent);
        Assert.Equal(0, tally.RejectedByRatio);
        Assert.Equal(1, tally.Proposed);
        Assert.Contains(candidates, c => string.Equals(c.Kind, "besiege", StringComparison.Ordinal));
        Assert.True(tally.BestRatioPermille >= tally.BestRequiredRatioPermille);
    }

    /// <summary>
    /// Gate (a) has a counter of its own, and the line it writes says so — which is what lets the
    /// soak's report distinguish "the siege was illegal" from "the siege was too weak". The soak
    /// reports gate (a) at zero, and a zero is only worth reporting if a non-zero would have shown.
    /// </summary>
    [Fact]
    public void The_legality_gate_is_reported_separately_from_the_ratio_gate()
    {
        var tally = new AiSiegeGateTally();
        tally.RecordLegalityRejection();

        Assert.Equal(1, tally.Adjacent);
        Assert.Equal(1, tally.RejectedByLegality);
        Assert.Equal(0, tally.RejectedByRatio);
        Assert.Equal(0, tally.Proposed);

        var line = tally.Describe()!;
        Assert.Contains("1 adjacent, 1 illegal, 0 below ratio, 0 proposed", line, StringComparison.Ordinal);

        // No pair was measured, so no closest-attempt snapshot is claimed. A line that invented one
        // would read as evidence.
        Assert.DoesNotContain("closest", line, StringComparison.Ordinal);
        Assert.Equal(-1, tally.BestRatioPermille);
    }

    /// <summary>
    /// An army with no moves is filtered out by <c>AiMilitaryPhase.Propose</c> before the siege gates
    /// run at all, so it contributes neither a candidate nor an adjacency observation. Stated here
    /// because it is the one way an adjacent army can be invisible to the tally, and a reader of the
    /// soak's counts needs to know it.
    /// </summary>
    [Fact]
    public void An_adjacent_army_with_no_moves_is_filtered_out_before_the_gates()
    {
        var state = ArmyBesideCity(troops: 400000);
        var spent = new ArmyState[state.Armies.Count];
        for (var i = 0; i < state.Armies.Count; i++)
        {
            spent[i] = string.Equals(state.Armies[i].Id, "besieger", StringComparison.Ordinal)
                ? state.Armies[i] with { Moves = 0 }
                : state.Armies[i];
        }

        var tally = ProposeWith(state with { Armies = ValueList<ArmyState>.Of(spent) }, out var candidates);

        Assert.DoesNotContain(candidates, c => string.Equals(c.Kind, "besiege", StringComparison.Ordinal));
        Assert.Equal(0, tally.Adjacent);
    }

    /// <summary>
    /// The admissibility claim: the tally is written to and never read, so the turn it observes is the
    /// turn that would have happened anyway.
    /// </summary>
    [Fact]
    public void A_turn_played_with_a_tally_plays_the_same_game_as_one_without()
    {
        var state = ArmyBesideCity(troops: 1000);

        var withTally = new List<AiCandidate>();
        var withoutTally = new List<AiCandidate>();
        Propose(state, withTally, new AiSiegeGateTally());
        Propose(state, withoutTally, null);

        Assert.Equal(withoutTally.Count, withTally.Count);
        for (var i = 0; i < withTally.Count; i++)
        {
            Assert.Equal(withoutTally[i].Kind, withTally[i].Kind);
            Assert.Equal(withoutTally[i].Score, withTally[i].Score);
            Assert.Equal(withoutTally[i].Rationale, withTally[i].Rationale);
        }
    }

    /// <summary>
    /// <c>docs/task-catalogue.md</c> T22 Done-when 1, follow-up
    /// <see href="https://github.com/diegoami/imperial_conquest_2/issues/272">#272</see> N3:
    /// <see cref="AiTurn.Run"/> feeds <see cref="AiSiegeGateTally"/> on the turn's first proposal pass
    /// only (<c>action == 0</c>). This drives a real, multi-action turn — two ready mobilization slots
    /// force the action loop around several times, since <c>AiTurn</c> dispatches one candidate and
    /// re-proposes rather than dispatching every candidate at once: the driven turn actually dispatches
    /// four commands (two <c>mobilize</c>s, then the newly mobilized army's own <c>approach</c> march,
    /// then a <c>propose-alliance</c>) over five proposal passes (review round 1, N1) — while a weak,
    /// permanently adjacent besieger's siege situation never changes across any of them. If the tally
    /// were fed on every pass rather than only the first, this one standing adjacency would be counted
    /// five times over, not twice.
    /// </summary>
    [Fact]
    public void AiTurn_feeds_the_siege_gate_tally_on_the_first_proposal_pass_only()
    {
        const string homeCityId = "home";
        var recruitment = Ruleset.Recruitment;
        var slotA = new RecruitmentSlot(homeCityId, "light_infantry", 1000, recruitment.MobilizationMinStateCodeAiSeat);
        var slotB = new RecruitmentSlot(homeCityId, "light_infantry", 2000, recruitment.MobilizationMinStateCodeAiSeat);

        var nations = new[]
        {
            AiScriptedStates.AiNation(Acting, AiScriptedStates.DefaultPersonality, treasury: -1, capitalCityId: homeCityId)
                with { RecruitmentSlots = ValueList.Of(slotA, slotB) },
            AiScriptedStates.AiNation(Other, AiScriptedStates.DefaultPersonality, capitalCityId: "defender-city"),
        };

        var cities = new[]
        {
            CaptureFixtures.City(
                homeCityId, "Home", 0, 0, Acting, Acting,
                loyalty: 90, fortificationCode: 100, populationThousands: 10,
                maxPopulationThousands: 100, tribute: 10),
            CaptureFixtures.City(
                "defender-city", "Defender City", 7, 5, Other, Other,
                loyalty: 90, fortificationCode: 100, populationThousands: 200,
                maxPopulationThousands: 200, tribute: 10),
        };

        // The soak's own case: adjacent, and far too weak to win the ratio gate. Nothing about this
        // army's situation changes across the turn, so it proposes no candidate on any pass -- it is
        // pure tally bait, never chosen and never moved.
        var armies = new[]
        {
            CaptureFixtures.Army("weak-besieger", Acting, 6, 5, morale: 60,
                    CaptureFixtures.Unit("archers", 1000))
                with { Moves = 5 },
        };

        var state = AiScriptedStates.WithActiveSeat(
            BattleCommandTestbed.StateWith(nations, cities, armies), Acting);

        var driven = AiScriptedStates.DriveOneTurn(state);

        // Sanity: the turn really did take more than one dispatched action -- both mobilize slots landed
        // (whatever else the turn went on to do afterward, such as the newly mobilized army marching or
        // a diplomacy candidate winning a later pass; see this test's own remarks and review round 1, N1).
        Assert.Equal(0, driven.Outcome.CommandsRejected);
        Assert.Equal(2, driven.Events.OfType<RecruitMobilized>().Count());
        Assert.Empty(driven.Outcome.State.NationById(Acting)!.RecruitmentSlots);

        var gateLine = Assert.Single(
            driven.Outcome.Log, l => l.StartsWith("siege gates: ", StringComparison.Ordinal));
        Assert.Contains(
            "1 adjacent, 0 illegal, 1 below ratio, 0 proposed", gateLine, StringComparison.Ordinal);
    }

    private static bool HasBesiegeCandidate(GameState state)
    {
        var candidates = new List<AiCandidate>();
        Propose(state, candidates, null);
        foreach (var candidate in candidates)
        {
            if (string.Equals(candidate.Kind, "besiege", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static AiSiegeGateTally ProposeWith(GameState state, out List<AiCandidate> candidates)
    {
        var tally = new AiSiegeGateTally();
        candidates = new List<AiCandidate>();
        Propose(state, candidates, tally);
        return tally;
    }

    private static void Propose(GameState state, List<AiCandidate> into, AiSiegeGateTally? tally) =>
        AiMilitaryPhase.Propose(
            new AiView(state, Ruleset, AiScriptedStates.World, Acting),
            AiPersonalityProfile.For(state.NationById(Acting)!),
            SplitMix64Rng.ForStream(1, "ai.turn"),
            Array.Empty<string>(),
            into,
            tally);

    /// <summary>
    /// One army of <paramref name="troops"/> archers next to one enemy city, plus an own city so the
    /// acting nation is not eliminated. The army's position is a parameter so the same fixture serves
    /// the adjacent and the far cases.
    /// </summary>
    /// <remarks>
    /// Archers because <c>SiegeStrength.Attacker</c> multiplies them by
    /// <c>SiegeRules.ArcherStrengthMultiplier</c>, so the two troop counts below sit far on either side
    /// of the gate without either being a number parked next to the threshold: 1,000 archers are an
    /// order of magnitude under the city, 400,000 an order of magnitude over it.
    /// </remarks>
    private static GameState ArmyBesideCity(int troops = 1000, int armyX = 6, int armyY = 5)
    {
        var nations = new[]
        {
            AiScriptedStates.AiNation(
                Acting, AiScriptedStates.DefaultPersonality, capitalCityId: "acting-city"),
            AiScriptedStates.AiNation(
                Other, AiScriptedStates.DefaultPersonality, capitalCityId: "defender-city"),
        };

        var cities = new[]
        {
            CaptureFixtures.City(
                "acting-city", "Acting City", 1, 0, Acting, Acting,
                loyalty: 90, fortificationCode: 100, populationThousands: 200,
                maxPopulationThousands: 200, tribute: 10),
            CaptureFixtures.City(
                "defender-city", "Defender City", 7, 5, Other, Other,
                loyalty: 90, fortificationCode: 100, populationThousands: 200,
                maxPopulationThousands: 200, tribute: 10),
        };

        var armies = new[]
        {
            CaptureFixtures.Army("besieger", Acting, armyX, armyY, morale: 60,
                    CaptureFixtures.Unit("archers", troops))
                with { Moves = 5 },
        };

        return AiScriptedStates.WithActiveSeat(
            BattleCommandTestbed.StateWith(nations, cities, armies), Acting);
    }
}
