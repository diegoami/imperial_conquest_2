using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Engine.Recruitment;
using IC2.Engine.Serialization;
using IC2.Slice.UI;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Ui;

/// <summary>
/// Fix #513's reproduction: the game screen never showed a nation's regiments in training, and its
/// "Mobilize first ready slot" button always issued <c>mobilize 0 …</c> whatever slot 0's state was.
/// </summary>
/// <remarks>
/// <para>
/// Every state here is a real <see cref="GameState"/> built by <see cref="GameSession"/> over the
/// shipped <c>classical-mediterranean</c> world/ruleset as Rome (the CLI's <c>--seat rome</c> shape),
/// and every slot is issued through the engine's own <c>recruit-standing</c> verb
/// (<see cref="Recruitment.RecruitStandingUnitCommand"/> through the dispatcher). Slots are made ready
/// by ending turns through <see cref="GameSession.Submit"/> — the weekly
/// <c>recruitment.slot-readiness</c> tick — never by editing a state code by hand.
/// </para>
/// <para>
/// <strong>One assertion's list order cannot come from play, and this is why.</strong> The engine
/// appends every recruited slot at <c>StateCode 0</c>
/// (<c>RecruitStandingUnitCommandHandler</c>) and <c>RecruitmentSlotReadinessSystem</c> advances every
/// slot of a nation in lockstep, so a lower index is never less ready than a higher one in a state
/// reached by playing — slot 0 is ready whenever any slot is. <c>The_mobilize_choice_picks_a_ready_slot_over_an_unready_slot_zero</c>
/// therefore arranges two engine-produced slot records (each issued by <c>recruit-standing</c>, one of
/// them advanced to readiness by <c>end</c>) into the order an <em>imported</em> table can hold: an
/// <c>OriginalSaveImporter</c> table keeps the DAT's own per-slot state codes in file order, which need
/// not be monotone. The scan the fix adds is exactly what makes that imported case correct, and the
/// arranged state is only the ordering — no state code, troop count or unit type is invented.
/// </para>
/// <para>
/// <strong>Why the fix is still worth its test:</strong> the reachable half is decisive on its own. With
/// one unready slot, the old button issued <c>mobilize 0</c> and the handler refused it (silently, per
/// #484); the fixed choice is disabled and says "No recruitment slot is ready." — assertions
/// <c>The_mobilize_choice_reports_none_ready_instead_of_issuing_an_unready_slot_zero</c> pins. A
/// mutation that restores the blind index 0 fails that test.
/// </para>
/// </remarks>
public sealed class RecruitmentPanelViewModelTests
{
    private const string RomeId = "rome";
    private const string RomeCityId = "rome";
    private const string CapuaCityId = "capua";

    private static ResolvedScenario Classical() =>
        GameDataRepository.Load(ModelTestPaths.DataRoot).Resolve("classical-mediterranean");

    /// <summary>A Rome-seated session over the shipped classical pair, the CLI's <c>--seat rome</c> shape.</summary>
    private static GameSession RomeSession(ulong seed = 1)
    {
        var classical = Classical();
        return new GameSession(
            classical.World, classical.Ruleset, classical.Scenario, seedOverride: seed, humanSeatNationId: RomeId);
    }

    private static NationState RomeNation(GameSession session) => session.State.NationById(RomeId)!;

    /// <summary>Whether the engine's own gate says this nation may mobilize the given slot.</summary>
    private static bool EngineSaysReady(GameSession session, RecruitmentSlot slot) =>
        MobilizationReadiness.IsReady(
            slot.StateCode, RomeNation(session).Control, session.Ruleset.Recruitment, session.Ruleset.Flags.SeatAsymmetry);

    private static void Recruit(GameSession session, string cityId, string unitTypeId, int troops)
    {
        var output = session.Submit($"recruit-standing {cityId} {unitTypeId} {troops}");
        Assert.Contains(
            "recruitment.recruit-standing-unit accepted.",
            output.Lines,
            StringComparer.Ordinal);
    }

    private static void EndTurns(GameSession session, int turns)
    {
        for (var i = 0; i < turns; i++)
        {
            session.Submit("end");
        }
    }

    /// <summary>
    /// Returns <paramref name="state"/> with Rome's queue replaced by <paramref name="slots"/> — the one
    /// arrangement helper, used only by the imported-order test whose remarks say why.
    /// </summary>
    private static GameState WithRomeSlots(GameState state, params RecruitmentSlot[] slots) =>
        state with
        {
            Nations = ValueList.From(state.Nations.Select(nation =>
                string.Equals(nation.Id, RomeId, StringComparison.Ordinal)
                    ? nation with { RecruitmentSlots = ValueList.Of(slots) }
                    : nation)),
        };

    /// <summary>
    /// The city panel's view-model: a slot trains at its own city only, and its readiness moves with the
    /// engine's weekly tick — 8 weeks out at ordering time on classical-faithful (threshold 16, step 2),
    /// 4 weeks halfway, then "ready".
    /// </summary>
    [Fact]
    public void The_city_panel_lists_the_regiments_training_at_that_city_with_readiness()
    {
        var session = RomeSession();

        // The shipped classical-faithful numbers this test's own readiness steps are read from:
        // human-threshold 16 and +2 a week, so 16 / 2 = 8 weeks from an order. Pinned here so a data
        // change that broke the step would fail this test rather than silently change its arithmetic.
        Assert.Equal(16, session.Ruleset.Recruitment.MobilizationMinStateCodeHumanSeat);
        Assert.Equal(2, session.Ruleset.Calendar.CityUnitStateCodeStep);

        Recruit(session, RomeCityId, "light_infantry", 200);
        Recruit(session, CapuaCityId, "archers", 400);

        var rows = RecruitmentPanelViewModel.TrainingAtCity(session.State, session.Ruleset, RomeId, RomeCityId);
        var row = Assert.Single(rows);
        Assert.Equal("light_infantry", row.UnitTypeId);
        Assert.Equal(200, row.Troops);
        Assert.Equal(RomeCityId, row.TargetCityId);
        Assert.False(row.IsReady);
        Assert.Equal(8, row.WeeksUntilReady);
        Assert.Equal("8 weeks", row.ReadinessText);

        // Capua's regiment is Capua's, not Rome's city panel's.
        Assert.DoesNotContain(rows, r => string.Equals(r.TargetCityId, CapuaCityId, StringComparison.Ordinal));

        EndTurns(session, 4);
        row = Assert.Single(RecruitmentPanelViewModel.TrainingAtCity(session.State, session.Ruleset, RomeId, RomeCityId));
        Assert.False(row.IsReady);
        Assert.Equal(4, row.WeeksUntilReady);
        Assert.Equal("4 weeks", row.ReadinessText);

        EndTurns(session, 4);
        row = Assert.Single(RecruitmentPanelViewModel.TrainingAtCity(session.State, session.Ruleset, RomeId, RomeCityId));
        Assert.True(row.IsReady);
        Assert.Equal("ready", row.ReadinessText);
        Assert.True(EngineSaysReady(session, RomeNation(session).RecruitmentSlots[0]));
    }

    /// <summary>The nation overview's view-model lists every regiment, at every city, with type, troops
    /// and readiness.</summary>
    [Fact]
    public void The_nation_overview_lists_every_regiment_in_training()
    {
        var session = RomeSession();
        Recruit(session, RomeCityId, "light_infantry", 200);
        Recruit(session, CapuaCityId, "archers", 400);

        var rows = RecruitmentPanelViewModel.TrainingForNation(session.State, session.Ruleset, RomeId);

        Assert.Equal(2, rows.Count);
        Assert.Equal("light_infantry", rows[0].UnitTypeId);
        Assert.Equal(200, rows[0].Troops);
        Assert.Equal(RomeCityId, rows[0].TargetCityId);
        Assert.Equal("8 weeks", rows[0].ReadinessText);
        Assert.Equal("archers", rows[1].UnitTypeId);
        Assert.Equal(400, rows[1].Troops);
        Assert.Equal(CapuaCityId, rows[1].TargetCityId);
        Assert.Equal("8 weeks", rows[1].ReadinessText);

        EndTurns(session, 8);
        rows = RecruitmentPanelViewModel.TrainingForNation(session.State, session.Ruleset, RomeId);
        Assert.All(rows, r => Assert.True(r.IsReady));
        Assert.All(rows, r => Assert.Equal("ready", r.ReadinessText));
    }

    /// <summary>
    /// The regression that #484 hid: nothing is ready yet, so the choice must not name slot 0 — it must
    /// report none ready. (The old button issued <c>mobilize 0</c> here and the handler refused it.)
    /// </summary>
    [Fact]
    public void The_mobilize_choice_reports_none_ready_instead_of_issuing_an_unready_slot_zero()
    {
        var session = RomeSession();
        Recruit(session, RomeCityId, "light_infantry", 200);

        // The slot the old code would have blindly mobilized is real, present, and unready.
        var slotZero = RomeNation(session).RecruitmentSlots[0];
        Assert.Equal(0, slotZero.StateCode);
        Assert.False(EngineSaysReady(session, slotZero));

        var choice = RecruitmentPanelViewModel.ChooseMobilization(session.State, session.Ruleset, RomeId);

        Assert.False(choice.IsEnabled);
        Assert.Null(choice.SlotIndex);
        Assert.Equal(RecruitmentPanelViewModel.NoReadySlotReason, choice.Reason);
    }

    /// <summary>
    /// The scan's direction over the arrangement an imported table can hold: with slot 0 unready and
    /// slot 1 ready, the choice is slot 1. The list order is arranged; every slot value is
    /// engine-produced (see this class's remarks).
    /// </summary>
    [Fact]
    public void The_mobilize_choice_picks_a_ready_slot_over_an_unready_slot_zero()
    {
        var session = RomeSession();
        Recruit(session, RomeCityId, "light_infantry", 200);
        Recruit(session, CapuaCityId, "archers", 400);

        EndTurns(session, 8);
        var ready = RomeNation(session).RecruitmentSlots[1];
        Assert.True(EngineSaysReady(session, ready));

        // A fresh order after the tick is still at 0 and stays so: the unready half of the arrangement.
        Recruit(session, RomeCityId, "heavy_infantry", 200);
        var unready = RomeNation(session).RecruitmentSlots[2];
        Assert.Equal(0, unready.StateCode);
        Assert.False(EngineSaysReady(session, unready));

        var arranged = WithRomeSlots(session.State, unready, ready);
        var choice = RecruitmentPanelViewModel.ChooseMobilization(arranged, session.Ruleset, RomeId);

        Assert.True(choice.IsEnabled);
        Assert.Equal(1, choice.SlotIndex);
        Assert.Null(choice.Reason);
    }

    /// <summary>
    /// The reachable mirror of that case, through play only: ready slot 0, an unready later slot, and the
    /// choice is the first ready one — never the unready one.
    /// </summary>
    [Fact]
    public void The_mobilize_choice_picks_the_first_ready_slot_over_a_later_unready_one()
    {
        var session = RomeSession();
        Recruit(session, RomeCityId, "light_infantry", 200);
        EndTurns(session, 8);
        Recruit(session, CapuaCityId, "archers", 400);

        var slots = RomeNation(session).RecruitmentSlots;
        Assert.True(EngineSaysReady(session, slots[0]));
        Assert.False(EngineSaysReady(session, slots[1]));

        var choice = RecruitmentPanelViewModel.ChooseMobilization(session.State, session.Ruleset, RomeId);

        Assert.True(choice.IsEnabled);
        Assert.Equal(0, choice.SlotIndex);
    }
}
