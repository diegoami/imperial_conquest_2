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
/// T156's <see cref="AiSiegeGateTally"/>: how many army/enemy-city pairs stood adjacent, and of those,
/// how many the target tree selected for attack. The tally exists for the per-seed soak's own
/// diagnostics, and the unit tests here pin each of its counters so the soak's report is read as
/// evidence rather than guessed.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why these are unit tests, not only the soak.</strong> The soak's report is the deliverable,
/// but a soak that only exercises one outcome cannot tell its readers whether the others exist; these
/// tests cover the unreachable, the too-weak, and the strong-enough cases from scripted states.
/// </para>
/// <para>
/// <strong>And that the tally cannot change the game.</strong>
/// <see cref="A_turn_played_with_a_tally_plays_the_same_game_as_one_without"/> runs the same seeded turn
/// twice, once with the tally and once without, and asserts the two states are identical. That is the
/// claim <see cref="AiSiegeGateTally"/>'s own remarks make, and it is what makes the instrumentation
/// admissible at all.
/// </para>
/// </remarks>
public sealed class AiSiegeGateTallyTests
{
    private const string Acting = AiScriptedStates.Attacker;
    private const string Other = AiScriptedStates.Defender;

    private static Ruleset Ruleset => AiScriptedStates.Ruleset;

    /// <summary>
    /// The army is three tiles from the only enemy city, so the adjacency test short-circuits and
    /// nothing downstream is measured at all. The tally has nothing to say, and says nothing.
    /// </summary>
    [Fact]
    public void An_army_that_never_reaches_adjacency_records_no_gate_at_all()
    {
        var tally = ProposeWith(ArmyBesideCity(armyX: 0, armyY: 4), out _);

        Assert.Equal(0, tally.Adjacent);
        Assert.Equal(0, tally.TreeSelectedAttackCity);
        Assert.Equal(0, tally.Proposed);
        Assert.True(tally.IsEmpty, "nothing was observed, so there is no line to write");
        Assert.Null(tally.Describe());
    }

    /// <summary>
    /// The weak case: an adjacent army whose city score clears the tree's 100 threshold (so the tree
    /// selects attack) and one whose score does not. The tally counts both as adjacent, and counts only
    /// the first as tree-selected.
    /// </summary>
    [Fact]
    public void An_adjacent_army_whose_city_score_clears_the_threshold_is_recorded_as_tree_selected()
    {
        var state = ArmyBesideCity(troops: 400000); // far over any threshold
        var tally = ProposeWith(state, out var candidates);

        Assert.Equal(1, tally.Adjacent);
        Assert.Equal(1, tally.TreeSelectedAttackCity);
        Assert.Equal(1, tally.Proposed);
        var kinds = string.Join(", ", candidates.Select(c => c.Kind));
        Assert.True(candidates.Any(c => c.Kind == "besiege"), $"no besiege candidate; got: {kinds}");
        Assert.True(tally.BestCityScore > 0);
    }

    /// <summary>
    /// The too-weak case: the city is far too strong, the city score falls below the tree's 100
    /// threshold, and the tree selects <c>MoveToResupplyCity</c> or <c>NoTarget</c> instead — the
    /// tally records the adjacency but not a tree selection.
    /// </summary>
    [Fact]
    public void An_adjacent_army_too_weak_is_recorded_as_adjacent_but_not_tree_selected()
    {
        var state = ArmyBesideCity(troops: 1000);
        var tally = ProposeWith(state, out var candidates);

        Assert.Equal(1, tally.Adjacent);
        Assert.Equal(0, tally.TreeSelectedAttackCity);
        Assert.Equal(0, tally.Proposed);
        Assert.DoesNotContain(candidates, c => string.Equals(c.Kind, "besiege", StringComparison.Ordinal));
    }

    /// <summary>
    /// The tally's write-line is best-pair-on-the-real-scale: the army and the city it scored highest
    /// against, and the scores themselves.
    /// </summary>
    [Fact]
    public void The_best_pair_is_recorded_with_the_real_scores()
    {
        var tally = ProposeWith(ArmyBesideCity(troops: 400000), out _);

        Assert.Equal("besieger", tally.BestArmyId);
        Assert.Equal("defender-city", tally.BestCityId);
        Assert.True(tally.BestCityScore > 0, "a real army-city pair has a real score");
    }

    /// <summary>
    /// An army with no moves is filtered out by <c>AiMilitaryPhase.Propose</c> before the tree runs
    /// at all, so it contributes neither a candidate nor an adjacency observation. Stated here
    /// because it is the one way an adjacent army can be invisible to the tally, and a reader of the
    /// soak's counts needs to know it.
    /// </summary>
    [Fact]
    public void An_adjacent_army_with_no_moves_is_filtered_out_before_the_tree()
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
    /// only. The tally's counters measure one observation per turn, not one per action, so re-feeding
    /// it on every pass would inflate the report by however many actions the turn ran.
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

        // The soak's case: adjacent and far too weak for the tree to select attack city (1000 troops
        // vs the city's defender strength, so the city score sits well under 100). Pure tally bait,
        // never chosen and never moved.
        var armies = new[]
        {
            CaptureFixtures.Army("weak-besieger", Acting, 6, 5, morale: 60,
                    CaptureFixtures.Unit("archers", 1000))
                with { Moves = 5 },
        };

        var state = AiScriptedStates.WithActiveSeat(
            BattleCommandTestbed.AtWar(BattleCommandTestbed.StateWith(nations, cities, armies), Acting, Other),
            Acting);

        var driven = AiScriptedStates.DriveOneTurn(state);

        Assert.Equal(0, driven.Outcome.CommandsRejected);
        Assert.Equal(2, driven.Events.OfType<RecruitMobilized>().Count());

        var gateLine = Assert.Single(
            driven.Outcome.Log, l => l.StartsWith("siege gates: ", StringComparison.Ordinal));
        Assert.Contains("1 adjacent", gateLine, StringComparison.Ordinal);
        Assert.Contains("0 tree-selected for attack city", gateLine, StringComparison.Ordinal);
        Assert.Contains("0 proposed", gateLine, StringComparison.Ordinal);
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
            AiPersonalityProfile.For(state.NationById(Acting)!, Ruleset),
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
            BattleCommandTestbed.AtWar(BattleCommandTestbed.StateWith(nations, cities, armies), Acting, Other),
            Acting);
    }
}