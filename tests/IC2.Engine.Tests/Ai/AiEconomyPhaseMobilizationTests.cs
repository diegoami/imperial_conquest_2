using IC2.Engine.Ai;
using IC2.Engine.Model;
using IC2.Engine.Recruitment.Commands;
using IC2.Engine.Tests.Battle.Commands;
using Xunit;
using CaptureFixtures = IC2.Engine.Tests.Cities.Capture.CaptureTestbed;
using RecruitmentTestFixtures = IC2.Engine.Tests.Recruitment.MobilizationFixture;

namespace IC2.Engine.Tests.Ai;

/// <summary>
/// T57: the AI side of T55's <see cref="MobilizeRecruitSlotCommand"/>, in <see cref="AiEconomyPhase"/>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why a dedicated wide-open world.</strong> These tests need to control exactly which cells
/// around a training city are free, so a receiving-army search can be pushed into both of its outcomes
/// (an existing army takes the unit, or a new one is created) and into its "neither" failure. The shipped
/// toy world mixes sea and city tiles inside its own 8×6 extent, which would make those placements
/// accidental; <see cref="RecruitmentTestFixtures.OpenWorld"/> is the same fixture T55's own
/// command-level tests use for exactly this reason, reused here rather than re-declared.
/// </para>
/// <para>
/// <strong>Every provenance-bearing number comes from the ruleset, never a re-typed literal.</strong> The
/// AI-seat readiness threshold is read as <c>Recruitment.MobilizationMinStateCodeAiSeat</c> (toy ruleset:
/// <c>24</c>) rather than written in as <c>24</c>, so a ruleset change moves these tests with it instead
/// of silently decoupling from the rule they pin.
/// </para>
/// </remarks>
public sealed class AiEconomyPhaseMobilizationTests
{
    private const string Acting = AiScriptedStates.Attacker;
    private const string Other = AiScriptedStates.Defender;

    /// <summary>Well above any battalion's cost, so a "no recruit candidate" assertion is never about affordability.</summary>
    private const int Treasury = 5000;

    private const string HomeCityId = "home";

    private static RecruitmentRules Recruitment => AiScriptedStates.Ruleset.Recruitment;

    /// <summary>30×30 of open plain -- large enough that a city's 3×3 neighbourhood is never near an edge.</summary>
    private static World OpenWorld => RecruitmentTestFixtures.OpenWorld(30, 30);

    [Fact]
    public void A_fully_ready_slot_produces_a_mobilize_candidate_at_the_base_weight()
    {
        var candidates = Propose(OneSlotState(Recruitment.MobilizationMinStateCodeAiSeat));

        var mobilize = Assert.Single(candidates, c => c.Kind == "mobilize");
        Assert.Equal(AiWeights.MobilizeReadyRecruitBaseScore, mobilize.Score);

        var command = Assert.IsType<MobilizeRecruitSlotCommand>(Assert.Single(mobilize.Commands));
        Assert.Equal(Acting, command.IssuingNationId);
        Assert.Equal(0, command.SlotIndex);
    }

    [Fact]
    public void A_brand_new_slot_produces_no_mobilize_candidate()
    {
        var candidates = Propose(OneSlotState(stateCode: 0));

        Assert.DoesNotContain(candidates, c => c.Kind == "mobilize");
    }

    [Fact]
    public void A_slot_one_weekly_tick_below_the_ai_seats_threshold_produces_no_mobilize_candidate()
    {
        // The weekly tick moves a slot's state by 2 (RecruitmentSlotReadinessSystem); one tick short of
        // the AI seat's own threshold is the sharpest boundary a fixture can pin.
        var candidates = Propose(OneSlotState(Recruitment.MobilizationMinStateCodeAiSeat - 2));

        Assert.DoesNotContain(candidates, c => c.Kind == "mobilize");
    }

    [Fact]
    public void A_slot_ready_only_by_the_human_seats_lower_threshold_still_produces_no_mobilize_candidate()
    {
        // MobilizationReadiness's own boundary: the AI seat is stricter than the human one, and this
        // candidate generator must honour the AI's own threshold, not the human one.
        Assert.True(Recruitment.MobilizationMinStateCodeHumanSeat < Recruitment.MobilizationMinStateCodeAiSeat);

        var candidates = Propose(OneSlotState(Recruitment.MobilizationMinStateCodeHumanSeat));

        Assert.DoesNotContain(candidates, c => c.Kind == "mobilize");
    }

    [Fact]
    public void Mobilization_is_proposed_even_when_the_nation_is_in_debt()
    {
        var candidates = Propose(OneSlotState(Recruitment.MobilizationMinStateCodeAiSeat, treasury: -1));

        Assert.Contains(candidates, c => c.Kind == "mobilize");
        Assert.DoesNotContain(candidates, c => c.Kind == "recruit");
        Assert.DoesNotContain(candidates, c => c.Kind == "fortify");
    }

    [Fact]
    public void A_full_recruitment_table_suppresses_new_orders_but_not_mobilization()
    {
        var slots = new List<RecruitmentSlot>();
        for (var i = 0; i < Recruitment.MaxSlots - 1; i++)
        {
            // Different cities, so AiWeights.MaxOpenRecruitmentOrdersPerCity never confounds this: the
            // table-full gate must be the one doing the suppressing.
            slots.Add(new RecruitmentSlot($"filler-{i}", "light_infantry", 1000, StateCode: 0));
        }

        slots.Add(new RecruitmentSlot(HomeCityId, "light_infantry", 1000, Recruitment.MobilizationMinStateCodeAiSeat));
        Assert.Equal(Recruitment.MaxSlots, slots.Count);

        var candidates = Propose(StateWith(slots, underSiege: false));

        Assert.DoesNotContain(candidates, c => c.Kind == "recruit");
        Assert.Contains(candidates, c => c.Kind == "mobilize");
    }

    [Fact]
    public void A_city_under_siege_is_not_mobilized_at()
    {
        var candidates = Propose(OneSlotState(Recruitment.MobilizationMinStateCodeAiSeat, underSiege: true));

        Assert.DoesNotContain(candidates, c => c.Kind == "mobilize");
    }

    [Fact]
    public void No_existing_army_creates_a_fresh_one_with_an_id_not_already_in_use()
    {
        var state = OneSlotState(Recruitment.MobilizationMinStateCodeAiSeat);

        var candidates = Propose(state);

        var mobilize = Assert.Single(candidates, c => c.Kind == "mobilize");
        var command = (MobilizeRecruitSlotCommand)mobilize.Commands[0];

        Assert.False(string.IsNullOrEmpty(command.NewArmyId));
        Assert.Null(state.ArmyById(command.NewArmyId));
    }

    [Fact]
    public void An_army_id_already_in_use_is_never_reused()
    {
        var slot = new RecruitmentSlot(HomeCityId, "light_infantry", 1000, Recruitment.MobilizationMinStateCodeAiSeat);
        var nations = new[]
        {
            AiScriptedStates.AiNation(Acting, AiScriptedStates.DefaultPersonality, Treasury, HomeCityId)
                with { RecruitmentSlots = ValueList.Of(slot) },
            AiScriptedStates.AiNation(Other, AiScriptedStates.DefaultPersonality, Treasury),
        };
        var cities = new[]
        {
            CaptureFixtures.City(
                HomeCityId, "Home", 5, 5, Acting, Acting, loyalty: 80, fortificationCode: 20,
                populationThousands: 10, maxPopulationThousands: 100, tribute: 10),
        };

        // Parked far from the city, so it is not a receiving-army candidate -- only in the way of the
        // id scan.
        var existingArmy = CaptureFixtures.Army("army-0", Acting, 25, 25, morale: 60);

        var state = AiScriptedStates.WithActiveSeat(
            BattleCommandTestbed.StateWith(nations, cities, new[] { existingArmy }), Acting);

        var candidates = Propose(state);

        var mobilize = Assert.Single(candidates, c => c.Kind == "mobilize");
        var command = (MobilizeRecruitSlotCommand)mobilize.Commands[0];
        Assert.NotEqual("army-0", command.NewArmyId);
        Assert.Null(state.ArmyById(command.NewArmyId));
    }

    [Fact]
    public void An_adjacent_army_with_room_receives_the_unit_and_creates_no_new_one()
    {
        var slot = new RecruitmentSlot(HomeCityId, "light_infantry", 1000, Recruitment.MobilizationMinStateCodeAiSeat);
        var nations = new[]
        {
            AiScriptedStates.AiNation(Acting, AiScriptedStates.DefaultPersonality, Treasury, HomeCityId)
                with { RecruitmentSlots = ValueList.Of(slot) },
            AiScriptedStates.AiNation(Other, AiScriptedStates.DefaultPersonality, Treasury),
        };
        var cities = new[]
        {
            CaptureFixtures.City(
                HomeCityId, "Home", 5, 5, Acting, Acting, loyalty: 80, fortificationCode: 20,
                populationThousands: 10, maxPopulationThousands: 100, tribute: 10),
        };

        // Adjacent (Chebyshev distance 1), so it qualifies under both the human and the AI predicate.
        var receivingArmy = CaptureFixtures.Army(
            "receiving-army", Acting, 6, 6, morale: 60, CaptureFixtures.Unit("light_infantry", 5000));

        var state = AiScriptedStates.WithActiveSeat(
            BattleCommandTestbed.StateWith(nations, cities, new[] { receivingArmy }), Acting);

        var candidates = Propose(state);

        var mobilize = Assert.Single(candidates, c => c.Kind == "mobilize");
        var command = (MobilizeRecruitSlotCommand)mobilize.Commands[0];

        // NewArmyId goes unused when an existing army receives the unit -- MobilizeRecruitSlotCommand's
        // own contract.
        Assert.True(string.IsNullOrEmpty(command.NewArmyId));

        var armyCountBefore = state.Armies.Count;

        // Review round 1, F1: the candidate-level assertion above cannot tell "an existing army received
        // the unit" apart from "nothing happened" -- both leave NewArmyId unused. Driving the command for
        // real through the production dispatcher is what actually pins the promise in this test's own
        // name: the unit lands IN "receiving-army", and no second army is created for it.
        var driven = AiScriptedStates.DriveOneTurn(state);

        Assert.Equal(0, driven.Outcome.CommandsRejected);

        var mobilized = Assert.Single(driven.Events.OfType<RecruitMobilized>());
        Assert.False(mobilized.ArmyWasCreated);
        Assert.Equal("receiving-army", mobilized.ArmyId);

        Assert.Equal(armyCountBefore, driven.Outcome.State.Armies.Count);
        var updatedArmy = driven.Outcome.State.ArmyById("receiving-army");
        Assert.NotNull(updatedArmy);
        Assert.Equal(2, updatedArmy!.Units.Count);
        Assert.Equal(6000, updatedArmy.TotalTroops);
    }

    [Fact]
    public void No_receiving_army_and_no_free_cell_produces_no_candidate()
    {
        var slot = new RecruitmentSlot(HomeCityId, "light_infantry", 1000, Recruitment.MobilizationMinStateCodeAiSeat);
        var nations = new[]
        {
            AiScriptedStates.AiNation(Acting, AiScriptedStates.DefaultPersonality, Treasury, HomeCityId)
                with { RecruitmentSlots = ValueList.Of(slot) },
            AiScriptedStates.AiNation(Other, AiScriptedStates.DefaultPersonality, Treasury),
        };
        var cities = new[]
        {
            CaptureFixtures.City(
                HomeCityId, "Home", 10, 10, Acting, Acting, loyalty: 80, fortificationCode: 20,
                populationThousands: 10, maxPopulationThousands: 100, tribute: 10),
        };

        // Every one of the city's eight neighbours is occupied by a stationary army too far away
        // (Chebyshev distance 1 from the CITY, not from any army) to be a receiving candidate itself --
        // MobilizationReceivingArmy.Accepts needs an ARMY within range of the city, and these are exactly
        // that distance, but they belong to the OTHER nation, so Find still returns null for the acting
        // nation while PlacementCell finds every cell occupied.
        var blockers = new List<ArmyState>();
        var index = 0;
        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0)
                {
                    continue;
                }

                blockers.Add(CaptureFixtures.Army($"blocker-{index++}", Other, 10 + dx, 10 + dy, morale: 50));
            }
        }

        var state = AiScriptedStates.WithActiveSeat(
            BattleCommandTestbed.StateWith(nations, cities, blockers), Acting);

        var candidates = Propose(state);

        Assert.DoesNotContain(candidates, c => c.Kind == "mobilize");
    }

    [Fact]
    public void The_army_cap_declines_to_propose_mobilization_even_though_a_placement_cell_is_free()
    {
        var maxArmies = AiScriptedStates.Ruleset.ArmyManagement.MaxArmies;

        var slot = new RecruitmentSlot(HomeCityId, "light_infantry", 1000, Recruitment.MobilizationMinStateCodeAiSeat);
        var nations = new[]
        {
            AiScriptedStates.AiNation(Acting, AiScriptedStates.DefaultPersonality, Treasury, HomeCityId)
                with { RecruitmentSlots = ValueList.Of(slot) },
            AiScriptedStates.AiNation(Other, AiScriptedStates.DefaultPersonality, Treasury),
        };
        var cities = new[]
        {
            CaptureFixtures.City(
                HomeCityId, "Home", 15, 15, Acting, Acting, loyalty: 80, fortificationCode: 20,
                populationThousands: 10, maxPopulationThousands: 100, tribute: 10),
        };

        // The acting nation has no army anywhere (so TryChooseReceivingArmy's Find always fails), and
        // every one of these maxArmies armies belongs to the OTHER nation and is parked far from the
        // city, so none of them occupies a placement cell either -- MobilizationArmyCreation.PlacementCell
        // would find a free cell if this generator ever called it. That isolates the MaxArmies pre-check
        // as the only thing that can be declining the candidate: this is DoD 3's own invariant
        // (CommandsRejected stays zero because nothing is ever proposed that MaxArmies would refuse).
        var armies = new List<ArmyState>();
        for (var i = 0; i < maxArmies; i++)
        {
            armies.Add(CaptureFixtures.Army($"other-{i}", Other, 0, 0, morale: 50));
        }

        var state = AiScriptedStates.WithActiveSeat(
            BattleCommandTestbed.StateWith(nations, cities, armies), Acting);

        Assert.Equal(maxArmies, state.Armies.Count);

        var candidates = Propose(state);

        Assert.DoesNotContain(candidates, c => c.Kind == "mobilize");
    }

    [Fact]
    public void A_ready_slot_is_mobilized_over_a_full_ai_turn_with_no_rejection()
    {
        var state = OneSlotState(Recruitment.MobilizationMinStateCodeAiSeat);

        var driven = AiScriptedStates.DriveOneTurn(state);

        Assert.Contains("recruitment.mobilize-recruit-slot", driven.IssuedKinds);
        Assert.Equal(0, driven.Outcome.CommandsRejected);
        Assert.Contains(driven.Events, e => e is RecruitMobilized);
    }

    [Fact]
    public void Two_slots_with_only_the_second_ready_propose_that_ones_candidate_and_conserve_the_other()
    {
        var notReadySlot = new RecruitmentSlot(HomeCityId, "light_infantry", 1000, StateCode: 0);
        var readySlot = new RecruitmentSlot(
            HomeCityId, "light_infantry", 3000, Recruitment.MobilizationMinStateCodeAiSeat);

        // In debt, so ProposeRecruitment/ProposeFortification never fire: a driven turn's only action is
        // the one candidate this generates. Otherwise a same-city recruit order (still under
        // AiWeights.MaxOpenRecruitmentOrdersPerCity) could add a THIRD slot and confound the "the
        // untouched slot survives verbatim" assertion below.
        var state = StateWith(new List<RecruitmentSlot> { notReadySlot, readySlot }, underSiege: false, treasury: -1);

        var candidates = Propose(state);

        var mobilize = Assert.Single(candidates, c => c.Kind == "mobilize");
        var command = (MobilizeRecruitSlotCommand)mobilize.Commands[0];
        Assert.Equal(1, command.SlotIndex);

        var driven = AiScriptedStates.DriveOneTurn(state);

        Assert.Equal(0, driven.Outcome.CommandsRejected);

        var mobilized = Assert.Single(driven.Events.OfType<RecruitMobilized>());
        Assert.Equal(3000, mobilized.Troops);
        Assert.Equal(
            Recruitment.MobilizationMinStateCodeAiSeat / Recruitment.MobilizationQualityDivisor,
            mobilized.Quality);
        Assert.Equal(6, mobilized.Quality);

        var remainingSlots = driven.Outcome.State.NationById(Acting)!.RecruitmentSlots;
        var untouched = Assert.Single(remainingSlots);
        Assert.Equal(notReadySlot, untouched);
    }

    [Fact]
    public void Two_ready_slots_can_propose_the_same_next_army_id_and_a_driven_turn_still_rejects_nothing()
    {
        var slotA = new RecruitmentSlot(HomeCityId, "light_infantry", 1000, Recruitment.MobilizationMinStateCodeAiSeat);
        var slotB = new RecruitmentSlot(HomeCityId, "light_infantry", 2000, Recruitment.MobilizationMinStateCodeAiSeat);

        // In debt for the same reason as above: nothing but the two mobilize candidates is proposed.
        var state = StateWith(new List<RecruitmentSlot> { slotA, slotB }, underSiege: false, treasury: -1);

        var candidates = Propose(state);

        var mobilizeCandidates = candidates.Where(c => c.Kind == "mobilize").ToList();
        Assert.Equal(2, mobilizeCandidates.Count);

        var commandA = (MobilizeRecruitSlotCommand)mobilizeCandidates[0].Commands[0];
        var commandB = (MobilizeRecruitSlotCommand)mobilizeCandidates[1].Commands[0];

        // NextArmyId's own remark, visited directly: "two candidates built from the same unchanged state
        // may propose the same id" -- neither slot has a receiving army yet, so both independently scan
        // the same (untouched) state and land on the same lowest-unused id.
        Assert.Equal(0, commandA.SlotIndex);
        Assert.Equal(1, commandB.SlotIndex);
        Assert.Equal("army-0", commandA.NewArmyId);
        Assert.Equal("army-0", commandB.NewArmyId);

        // The remark's claim of harmlessness: AiTurn dispatches one, regenerates from the state that
        // dispatch produced (where the second slot's real receiving army is now the one just created,
        // adjacent to the city), and the second command it issues is never the stale duplicate id.
        var driven = AiScriptedStates.DriveOneTurn(state);

        Assert.Equal(0, driven.Outcome.CommandsRejected);
        Assert.Equal(2, driven.Events.OfType<RecruitMobilized>().Count());
        Assert.Empty(driven.Outcome.State.NationById(Acting)!.RecruitmentSlots);
        Assert.Equal(3000, driven.Outcome.State.Armies.Sum(a => a.TotalTroops));
    }

    private static List<AiCandidate> Propose(GameState state)
    {
        var view = new AiView(state, AiScriptedStates.Ruleset, OpenWorld, Acting);
        var candidates = new List<AiCandidate>();
        AiEconomyPhase.Propose(view, AiPersonalityProfile.For(state.NationById(Acting)!), candidates);
        return candidates;
    }

    private static GameState OneSlotState(int stateCode, bool underSiege = false, int treasury = Treasury) =>
        StateWith(
            new List<RecruitmentSlot> { new(HomeCityId, "light_infantry", 1000, stateCode) },
            underSiege,
            treasury);

    private static GameState StateWith(List<RecruitmentSlot> slots, bool underSiege, int treasury = Treasury)
    {
        var nations = new[]
        {
            AiScriptedStates.AiNation(Acting, AiScriptedStates.DefaultPersonality, treasury, HomeCityId)
                with { RecruitmentSlots = ValueList.From(slots) },
            AiScriptedStates.AiNation(Other, AiScriptedStates.DefaultPersonality, Treasury),
        };

        var cities = new[]
        {
            CaptureFixtures.City(
                HomeCityId, "Home", 5, 5, Acting, Acting, loyalty: 80, fortificationCode: 20,
                populationThousands: 10, maxPopulationThousands: 100, tribute: 10, underSiege: underSiege),
        };

        var state = BattleCommandTestbed.StateWith(nations, cities);
        return AiScriptedStates.WithActiveSeat(state, Acting);
    }
}
