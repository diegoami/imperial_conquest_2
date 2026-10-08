using IC2.Engine.Core;
using IC2.Engine.Economy;
using IC2.Engine.Model;
using IC2.Engine.Recruitment;
using IC2.Engine.Recruitment.Commands;
using IC2.Engine.Serialization;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Recruitment;

/// <summary>
/// <c>docs/tasks/T108.md</c> "Disband a recruitment slot", Done-when 1 through 4 and 7 — the
/// recruit-table half of <c>TArmyRecruits_DisbandUnits</c> (<c>0x004553B0</c>) at the command seam,
/// on the real, committed <c>classical-mediterranean</c> scenario the original's own dialog runs
/// over. Provenance for every rule asserted here is in
/// <see cref="DisbandRecruitmentSlotCommand"/>'s remarks; the numbers all come from the state and
/// the ruleset, never a literal.
/// </summary>
public sealed class DisbandRecruitmentSlotCommandTests
{
    private const string Nation = "rome";
    private const string CityId = "rome";

    private static readonly Lazy<ResolvedScenario> LazyClassical = new(
        () => GameDataRepository.Load(ModelTestPaths.DataRoot).Resolve("classical-mediterranean"));

    private static ResolvedScenario Classical => LazyClassical.Value;

    /// <summary>A dispatcher over the real engine assembly on the classical scenario.</summary>
    private static CommandDispatcher NewDispatcher(IEventSink? sink = null) => new(
        SystemRegistry.FromEngineAssembly(), Classical.Ruleset, Classical.World, sink ?? NullEventSink.Instance);

    private static GameState ClassicalInitial() =>
        GameStateFactory.CreateInitial(Classical.World, Classical.Ruleset, Classical.Scenario);

    /// <summary>
    /// Done-when 1: after <c>recruit-standing</c> in Rome, <c>disband-slot</c> on that slot removes
    /// it; the treasury is unchanged by the disband (the read gives no refund), while the treasury's
    /// fall at <c>recruit-standing</c> stays as it was.
    /// </summary>
    [Fact]
    public void DisbandSlot_removes_the_slot_leaves_the_treasury_alone_and_keeps_the_recruit_fall()
    {
        const string unitType = "light_infantry";
        const int troops = 200; // tests/fixtures/cli/success.txt's own recruit-standing line.
        var dispatcher = NewDispatcher();
        var initial = ClassicalInitial();
        var romeInitial = initial.NationById(Nation)!;
        var expectedCost = StandingRecruitmentCost.InitialCost(troops, unitType, Classical.Ruleset);

        var recruited = dispatcher.Dispatch(
            initial, new RecruitStandingUnitCommand(Nation, CityId, unitType, troops));
        Assert.True(recruited.IsAccepted);

        var afterRecruit = recruited.State.NationById(Nation)!;
        var slot = Assert.Single(afterRecruit.RecruitmentSlots);
        Assert.Equal(expectedCost, romeInitial.Treasury - afterRecruit.Treasury);

        var disbanded = dispatcher.Dispatch(recruited.State, new DisbandRecruitmentSlotCommand(Nation, 0));
        Assert.True(disbanded.IsAccepted);

        var afterDisband = disbanded.State.NationById(Nation)!;
        Assert.Empty(afterDisband.RecruitmentSlots);
        Assert.Equal(afterRecruit.Treasury, afterDisband.Treasury);
        Assert.Equal(romeInitial.Treasury - expectedCost, afterDisband.Treasury);

        // The disband is a deletion with no other side effect on the roster's neighbours: the state
        // carries the slot nowhere else, so only the nation record moved.
        Assert.Equal(recruited.State.Armies, disbanded.State.Armies);
        Assert.Equal(recruited.State.Cities, disbanded.State.Cities);
        Assert.Equal(troops, slot.Troops);
    }

    /// <summary>
    /// Done-when 4, ready half: a slot raised to the human seat's readiness threshold is disbanded
    /// with no refund either — the read found no difference between a ready slot and an unready one,
    /// and the handler has no branch on readiness to test, so this drives the same path with the
    /// other StateCode and asserts the same treasury outcome.
    /// </summary>
    [Fact]
    public void A_ready_slot_is_disbanded_with_no_refund()
    {
        var ruleset = Classical.Ruleset;
        var dispatcher = NewDispatcher();
        var initial = ClassicalInitial();

        var recruited = dispatcher.Dispatch(
            initial, new RecruitStandingUnitCommand(Nation, CityId, "light_infantry", 200));
        Assert.True(recruited.IsAccepted);

        var ready = recruited.State.NationById(Nation)!.RecruitmentSlots[0] with
        {
            StateCode = ruleset.Recruitment.MobilizationMinStateCodeHumanSeat,
        };
        var madeReady = recruited.State with
        {
            Nations = ValueList.From(recruited.State.Nations.Select(n =>
                n.Id == Nation ? n with { RecruitmentSlots = ValueList.Of(ready) } : n)),
        };
        var treasuryBefore = madeReady.NationById(Nation)!.Treasury;

        var disbanded = dispatcher.Dispatch(madeReady, new DisbandRecruitmentSlotCommand(Nation, 0));
        Assert.True(disbanded.IsAccepted);

        Assert.Empty(disbanded.State.NationById(Nation)!.RecruitmentSlots);
        Assert.Equal(treasuryBefore, disbanded.State.NationById(Nation)!.Treasury);
    }

    /// <summary>
    /// Done-when 3: an index out of range — and an index that is valid only in <em>another</em>
    /// nation's table, which is out of range for the issuer because the verb reads only its own
    /// slots — is rejected with the state unchanged.
    /// </summary>
    [Fact]
    public void An_out_of_range_or_foreign_index_is_rejected_and_changes_nothing()
    {
        var dispatcher = NewDispatcher();
        var initial = ClassicalInitial();

        var recruited = dispatcher.Dispatch(
            initial, new RecruitStandingUnitCommand(Nation, CityId, "light_infantry", 200));
        Assert.True(recruited.IsAccepted);

        var beyond = dispatcher.Dispatch(recruited.State, new DisbandRecruitmentSlotCommand(Nation, 1));
        Assert.False(beyond.IsAccepted);
        Assert.Equal(DisbandRecruitmentSlotRejections.UnknownSlot, beyond.Code);
        Assert.Same(recruited.State, beyond.State);

        var negative = dispatcher.Dispatch(recruited.State, new DisbandRecruitmentSlotCommand(Nation, -1));
        Assert.False(negative.IsAccepted);
        Assert.Equal(DisbandRecruitmentSlotRejections.UnknownSlot, negative.Code);
        Assert.Same(recruited.State, negative.State);

        // 'macedonia' holds no slots of its own here, so index 0 — valid in Rome's table — is out of
        // range for it: a slot of another nation cannot be disbanded through one's own verb. The
        // seat is handed to macedonia first because the dispatcher only takes orders from the active
        // seat, exactly as the CLI does.
        var macedoniaSeat = Array.IndexOf(recruited.State.TurnOrder.ToArray(), "macedonia");
        var macedoniasTurn = recruited.State with { ActiveSeatIndex = macedoniaSeat };
        var foreign = dispatcher.Dispatch(macedoniasTurn, new DisbandRecruitmentSlotCommand("macedonia", 0));
        Assert.False(foreign.IsAccepted);
        Assert.Equal(DisbandRecruitmentSlotRejections.UnknownSlot, foreign.Code);
        Assert.Same(macedoniasTurn, foreign.State);
    }

    /// <summary>
    /// Done-when 7: after <c>disband-slot</c> on a slot of <c>t</c> troops, the nation's
    /// <see cref="NationState.MobilizedPercent"/> equals <c>max(0, m − 1 − t × 1000 / wealth)</c>
    /// from its value <c>m</c> before — every term read from the state and the ruleset, none a
    /// literal, and the expression written out rather than borrowed from
    /// <see cref="MobilizationRate"/> so the rule is asserted, not restated.
    /// </summary>
    [Fact]
    public void DisbandSlot_applies_the_mobilisation_fall_computed_from_the_state()
    {
        var ruleset = Classical.Ruleset;
        var dispatcher = NewDispatcher();
        var initial = ClassicalInitial();

        var recruited = dispatcher.Dispatch(
            initial, new RecruitStandingUnitCommand(Nation, CityId, "heavy_infantry", 3_200));
        Assert.True(recruited.IsAccepted);

        var before = recruited.State.NationById(Nation)!;
        var slot = Assert.Single(before.RecruitmentSlots);

        var disbanded = dispatcher.Dispatch(recruited.State, new DisbandRecruitmentSlotCommand(Nation, 0));
        Assert.True(disbanded.IsAccepted);

        var after = disbanded.State.NationById(Nation)!;
        var step = ruleset.Recruitment.MobilizationRateOrderStep;
        var troopTerm = slot.Troops * ruleset.Recruitment.MobilizationRateWealthScale / before.Wealth;
        var expected = Math.Max(0, before.MobilizedPercent - step - troopTerm);
        Assert.Equal(expected, after.MobilizedPercent);

        // The fall is a real one, not a restatement of a zero move: the order raised it first.
        var initialNation = initial.NationById(Nation)!;
        Assert.NotEqual(initialNation.MobilizedPercent, before.MobilizedPercent);
    }

    /// <summary>
    /// Done-when 7's floor: a nation whose mobilisation is already low enough that the fall would
    /// take it below 0 keeps 0 — the original's <c>FUN_00448fd8</c> <c>max</c> against 0.
    /// </summary>
    [Fact]
    public void A_fall_below_zero_leaves_the_mobilisation_at_zero()
    {
        var dispatcher = NewDispatcher();
        var initial = ClassicalInitial();

        var recruited = dispatcher.Dispatch(
            initial, new RecruitStandingUnitCommand(Nation, CityId, "heavy_infantry", 3_200));
        Assert.True(recruited.IsAccepted);

        var lowered = recruited.State with
        {
            Nations = ValueList.From(recruited.State.Nations.Select(n =>
                n.Id == Nation ? n with { MobilizedPercent = 0 } : n)),
        };
        var before = lowered.NationById(Nation)!;
        var slot = Assert.Single(before.RecruitmentSlots);

        var disbanded = dispatcher.Dispatch(lowered, new DisbandRecruitmentSlotCommand(Nation, 0));
        Assert.True(disbanded.IsAccepted);

        var troopTerm = slot.Troops * Classical.Ruleset.Recruitment.MobilizationRateWealthScale / before.Wealth;
        Assert.True(before.MobilizedPercent - Classical.Ruleset.Recruitment.MobilizationRateOrderStep - troopTerm < 0);
        Assert.Equal(0, disbanded.State.NationById(Nation)!.MobilizedPercent);
    }

    /// <summary>
    /// Done-when 2: the next quarter's upkeep bill no longer includes the slot. Both runs start from
    /// the same state on the last week before a quarter and fire the same quarterly boundary; the
    /// only difference is the disband, so the difference between Rome's two treasury changes is
    /// exactly <see cref="GarrisonUpkeep"/> — the quarterly system's own result — on the disbanded
    /// slot, and nothing else.
    /// </summary>
    [Fact]
    public void The_next_quarters_bill_no_longer_includes_the_disbanded_slot()
    {
        var ruleset = Classical.Ruleset;
        var boundary = ClassicalOnTheLastWeekBeforeAQuarter();

        var kept = NewDispatcher().Dispatch(
            boundary, new RecruitStandingUnitCommand(Nation, CityId, "light_infantry", 200));
        Assert.True(kept.IsAccepted);
        var slot = Assert.Single(kept.State.NationById(Nation)!.RecruitmentSlots);

        var disbanded = NewDispatcher().Dispatch(kept.State, new DisbandRecruitmentSlotCommand(Nation, 0));
        Assert.True(disbanded.IsAccepted);

        var keptAfter = FireQuarter(kept.State);
        var disbandedAfter = FireQuarter(disbanded.State);

        var keptFall = kept.State.NationById(Nation)!.Treasury - keptAfter.NationById(Nation)!.Treasury;
        var disbandedFall = disbanded.State.NationById(Nation)!.Treasury - disbandedAfter.NationById(Nation)!.Treasury;

        var slotUpkeep = GarrisonUpkeep.Compute(new[] { slot }, ruleset);
        Assert.True(slotUpkeep > 0);
        Assert.Equal(slotUpkeep, keptFall - disbandedFall);
    }

    /// <summary>
    /// The quarter-boundary machinery <c>BalanceSheetTests</c> uses, narrowed to what this test
    /// needs: Rome is made the human seat with every nation's unity raised, so no AI deposition
    /// touches a treasury on the way to the boundary.
    /// </summary>
    private static GameState ClassicalOnTheLastWeekBeforeAQuarter()
    {
        var ruleset = Classical.Ruleset;
        var state = ClassicalInitial() with
        {
            Nations = ValueList.From(ClassicalInitial().Nations.Select(n => n.Id == Nation
                ? n with { Control = SeatControl.Human, Unity = 600 }
                : n with { Unity = 600 })),
        };

        var coordinator = new TurnCoordinator(
            SystemRegistry.FromEngineAssembly(), ruleset, Classical.World, NullEventSink.Instance);
        while (state.Calendar.Week != ruleset.Calendar.SeasonAdvanceFromWeek)
        {
            state = coordinator.RunRoundTick(state).State;
        }

        return state;
    }

    private static GameState FireQuarter(GameState state)
    {
        var coordinator = new TurnCoordinator(
            SystemRegistry.FromEngineAssembly(), Classical.Ruleset, Classical.World, NullEventSink.Instance);
        return coordinator.FireQuarterBoundary(state, state.Calendar.SeasonIndex);
    }

    /// <summary>
    /// This task's own Hazards note: slot indexes shift when a slot is removed. After disbanding slot
    /// 0 of two, the surviving entry is exactly the old slot 1, and <c>mobilize</c> on index 0 finds
    /// it — the readiness system and the mobilize verb read the compacted list, so the shift leaves
    /// them pointing at the right slot. On the toy scenario's mobilization fixture, whose city and
    /// army placement the accepted mobilize path is already known to work over.
    /// </summary>
    [Fact]
    public void After_disbanding_slot_zero_of_two_mobilize_finds_the_old_slot_one()
    {
        var world = MobilizationFixture.OpenWorld(20, 20);
        var army = MobilizationFixture.Army("army-0", "north", 11, 10, new[] { MobilizationFixture.Unit("1st Foot Battalion") });
        var before = MobilizationFixture.WithSlots(
            RecruitmentTestbed.InitialState() with
            {
                Cities = ValueList.Of(MobilizationFixture.City("training-city", 10, 10, "north")),
                Armies = ValueList.Of(army),
            },
            "north",
            new RecruitmentSlot("training-city", "light_infantry", 1_000, 24),
            new RecruitmentSlot("training-city", "archers", 3_500, 24));

        var dispatcher = MobilizationFixture.DispatcherOn(world);

        var disbanded = dispatcher.Dispatch(before, new DisbandRecruitmentSlotCommand("north", 0));
        Assert.True(disbanded.IsAccepted);

        var surviving = Assert.Single(disbanded.State.NationById("north")!.RecruitmentSlots);
        Assert.Equal("archers", surviving.UnitTypeId);

        var mobilized = dispatcher.Dispatch(disbanded.State, new MobilizeRecruitSlotCommand("north", 0, "new-army"));
        Assert.True(mobilized.IsAccepted);
        var landed = mobilized.State.ArmyById("army-0")!.Units.Last(u => u.Troops > 0);
        Assert.Equal("archers", landed.UnitTypeId);
        Assert.Equal(3_500, landed.Troops);
    }
}
