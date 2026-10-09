using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Recruitment;
using IC2.Engine.Recruitment.Commands;
using Xunit;

namespace IC2.Engine.Tests.Recruitment;

/// <summary>
/// T155 (#515, #904) Done-when 1: the engine's capital-or-threshold refusal, with the two refusal
/// tests that fail when the check is removed and the regression tests that pass either way. The
/// threshold is the ruleset's own
/// <see cref="RecruitmentRules.RecruitTownMinFortificationPercent"/> (75 in the shipped toy ruleset —
/// see <see cref="RecruitTownThresholdCorpusTests"/>), never a literal here.
/// </summary>
/// <remarks>
/// The toy world is the fixture: <c>arx</c> is north's capital (fortification word 60) and
/// <c>portus</c> is its non-capital (shipped word 340, a 3-point order pending over 40%); the tests
/// below rewrite <c>portus</c>'s word (and, for the capital case, <c>arx</c>'s) to pin each boundary.
/// </remarks>
public sealed class RecruitTownGateTests
{
    private const string Nation = "north";
    private const string Capital = "arx";
    private const string NonCapital = "portus";

    private static int Threshold => RecruitmentTestbed.Ruleset.Recruitment.RecruitTownMinFortificationPercent;

    private static CityOrderRule FortifyRule =>
        RecruitmentTestbed.Ruleset.CityOrders.Orders.First(o => string.Equals(o.Id, "fortify", StringComparison.Ordinal));

    private static GameState StateWithFort(string cityId, int fortificationCode)
    {
        var state = RecruitmentTestbed.InitialState();
        var city = state.CityById(cityId)!;
        return RecruitmentTestbed.WithCity(state, city with { FortificationCode = fortificationCode });
    }

    private static CommandResult Recruit(GameState state, string cityId) =>
        RecruitmentTestbed.Dispatcher().Dispatch(
            state, new RecruitStandingUnitCommand(Nation, cityId, "light_cavalry", 1_400));

    // ---- The refusal tests: each fails when the new check is removed. ----

    [Fact]
    public void A_non_capital_town_at_74_with_no_pending_order_is_refused()
    {
        Assert.Equal(75, Threshold);
        var before = StateWithFort(NonCapital, Threshold - 1);

        var result = Recruit(before, NonCapital);

        Assert.Equal(RecruitStandingUnitRejections.IneligibleRecruitmentTown, result.Code);
        Assert.Same(before, result.State);

        // The reason names the town and the rule (Done-when 1).
        var message = result.Rejection!.Message;
        Assert.Contains("Portus", message, StringComparison.Ordinal);
        Assert.Contains("capital", message, StringComparison.Ordinal);
        Assert.Contains("at least 75", message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_non_capital_town_at_0_is_refused()
    {
        var before = StateWithFort(NonCapital, 0);

        var result = Recruit(before, NonCapital);

        Assert.Equal(RecruitStandingUnitRejections.IneligibleRecruitmentTown, result.Code);
        Assert.Same(before, result.State);
        Assert.Contains("Portus", result.Rejection!.Message, StringComparison.Ordinal);
    }

    // ---- Regression tests: pass with or without the check. ----

    [Fact]
    public void A_non_capital_town_at_exactly_the_threshold_recruits()
    {
        var before = StateWithFort(NonCapital, Threshold);

        var result = Recruit(before, NonCapital);

        Assert.True(result.IsAccepted);
        var slot = Assert.Single(result.State.NationById(Nation)!.RecruitmentSlots);
        Assert.Equal(NonCapital, slot.TargetCityId);
    }

    [Fact]
    public void A_non_capital_town_with_a_pending_fortify_order_recruits_whatever_its_current_level()
    {
        // Current 60% + a 3-point order pending: the raw word is 360 (≥ 100), and RecruitUnit's raw
        // comparison passes it whatever the current level — the settled reading of #515's stage-2
        // comment item 1.
        const int current = 60;
        var word = FortificationCode.WithOrder(current, 3, FortifyRule);
        Assert.Equal(360, word);
        var before = StateWithFort(NonCapital, word);

        var result = Recruit(before, NonCapital);

        Assert.True(result.IsAccepted);
    }

    [Fact]
    public void The_capital_recruits_at_any_fortification()
    {
        var before = StateWithFort(Capital, 10);

        var result = Recruit(before, Capital);

        Assert.True(result.IsAccepted);
        var slot = Assert.Single(result.State.NationById(Nation)!.RecruitmentSlots);
        Assert.Equal(Capital, slot.TargetCityId);
    }

    /// <summary>
    /// Done-when 1's last regression row: a town that falls from 80% to 60% keeps the units already
    /// in training — they are not removed, and <c>mobilize</c> and <c>disband-slot</c> on them still
    /// succeed. The gate is on <em>new</em> orders only.
    /// </summary>
    [Fact]
    public void A_town_that_falls_below_the_threshold_keeps_its_training_slots_and_mobilize_and_disband_still_succeed()
    {
        // A synthetic town on open ground beside one of north's own armies, so mobilize has its
        // receiving army (the same shape MobilizeRecruitSlotCommandHandlerTests uses). north's capital
        // is arx, so this town is a non-capital.
        var world = MobilizationFixture.OpenWorld(20, 20);
        var state = RecruitmentTestbed.InitialState() with
        {
            Cities = ValueList.Of(
                MobilizationFixture.City("training-city", 10, 10, Nation) with { FortificationCode = 80 }),
            Armies = ValueList.Of(
                MobilizationFixture.Army("army-0", Nation, 11, 10, new[] { MobilizationFixture.Unit("1st Foot Battalion") })),
        };
        var dispatcher = MobilizationFixture.DispatcherOn(world);

        // Two orders at 80% — the town is eligible then.
        var first = dispatcher.Dispatch(
            state, new RecruitStandingUnitCommand(Nation, "training-city", "light_cavalry", 1_400));
        Assert.True(first.IsAccepted);
        var second = dispatcher.Dispatch(
            first.State, new RecruitStandingUnitCommand(Nation, "training-city", "light_cavalry", 1_400));
        Assert.True(second.IsAccepted);

        // Both regiments finish training (the readiness system's own counter, at the human seat's
        // threshold — a fresh slot at StateCode 0 is not yet ready to mobilize).
        var trained = RecruitmentTestbed.WithNation(
            second.State,
            second.State.NationById(Nation)! with
            {
                RecruitmentSlots = ValueList.From(second.State.NationById(Nation)!.RecruitmentSlots
                    .Select(slot => slot with { StateCode = 24 })),
            });

        // The fall: 80% → 60%, the new-order gate now refusing this town.
        var fallen = RecruitmentTestbed.WithCity(
            trained, trained.CityById("training-city")! with { FortificationCode = 60 });
        Assert.False(RecruitmentEligibility.MayTakeOrder(
            fallen.CityById("training-city")!, fallen.NationById(Nation)!, RecruitmentTestbed.Ruleset));

        // The two units in training are kept.
        var slots = fallen.NationById(Nation)!.RecruitmentSlots;
        Assert.Equal(2, slots.Count);
        Assert.All(slots, slot => Assert.Equal("training-city", slot.TargetCityId));

        // Mobilize still succeeds (the ready slot at index 0 joins the adjacent army).
        var mobilized = dispatcher.Dispatch(fallen, new MobilizeRecruitSlotCommand(Nation, 0, "overflow-army"));
        Assert.True(mobilized.IsAccepted);

        // And disband still succeeds on what remains.
        var disbanded = dispatcher.Dispatch(mobilized.State, new DisbandRecruitmentSlotCommand(Nation, 0));
        Assert.True(disbanded.IsAccepted);
        Assert.Empty(disbanded.State.NationById(Nation)!.RecruitmentSlots);
    }

    // ---- Done-when 3's "two predicates, one home": listed ≠ may-order. ----

    [Fact]
    public void A_town_listed_only_for_its_units_in_training_may_not_take_a_new_order()
    {
        // 60% and no pending order: below the threshold, not the capital — but one unit in training,
        // so FUN_004544E0's list shows it (to mobilize or disband from), while RecruitUnit refuses a
        // new order.
        var initial = StateWithFort(NonCapital, 60);
        var withSlot = RecruitmentTestbed.WithNation(
            initial,
            initial.NationById(Nation)! with
            {
                RecruitmentSlots = ValueList.Of(new RecruitmentSlot(NonCapital, "archers", 3_500, 0)),
            });
        var city = withSlot.CityById(NonCapital)!;
        var nation = withSlot.NationById(Nation)!;

        Assert.True(RecruitmentEligibility.IsListedInDialog(city, nation, RecruitmentTestbed.Ruleset));
        Assert.False(RecruitmentEligibility.MayTakeOrder(city, nation, RecruitmentTestbed.Ruleset));

        // And the engine refuses the new order with the shared reason.
        var result = Recruit(withSlot, NonCapital);
        Assert.Equal(RecruitStandingUnitRejections.IneligibleRecruitmentTown, result.Code);
    }

    [Fact]
    public void The_dialog_list_shows_the_capital_a_fortified_town_and_a_training_town()
    {
        var ruleset = RecruitmentTestbed.Ruleset;
        var state = StateWithFort(NonCapital, 0); // portus: 0%, no training → not listed, not orderable.
        var city = state.CityById(NonCapital)!;
        var nation = state.NationById(Nation)!;

        Assert.True(RecruitmentEligibility.IsListedInDialog(state.CityById(Capital)!, nation, ruleset));
        Assert.False(RecruitmentEligibility.IsListedInDialog(city, nation, ruleset));
        Assert.False(RecruitmentEligibility.MayTakeOrder(city, nation, ruleset));

        // At exactly the threshold the town is both listed and orderable.
        var fortified = StateWithFort(NonCapital, Threshold);
        Assert.True(RecruitmentEligibility.IsListedInDialog(fortified.CityById(NonCapital)!, nation, ruleset));
        Assert.True(RecruitmentEligibility.MayTakeOrder(fortified.CityById(NonCapital)!, nation, ruleset));
    }

    /// <summary>
    /// Done-when 4's model half, without Godot: the Recruit unit dialog model's town list is
    /// FUN_004544E0's, and its Recruit gate is the order's own — a town listed only for its units in
    /// training carries the rule's reason instead of an offered order.
    /// </summary>
    [Fact]
    public void The_dialog_models_town_list_follows_the_two_predicates()
    {
        var ruleset = RecruitmentTestbed.Ruleset;

        // portus at 60% with one unit in training: listed, but no new order.
        var initial = StateWithFort(NonCapital, 60);
        var state = RecruitmentTestbed.WithNation(
            initial,
            initial.NationById(Nation)! with
            {
                RecruitmentSlots = ValueList.Of(new RecruitmentSlot(NonCapital, "archers", 3_500, 0)),
            });
        var model = IC2.Slice.UI.RecruitUnitDialogModel.ForActiveNation(state, ruleset, RecruitmentTestbed.World);

        Assert.Contains(model.ListedCities, c => string.Equals(c.Id, NonCapital, StringComparison.Ordinal));
        Assert.Contains(model.ListedCities, c => string.Equals(c.Id, Capital, StringComparison.Ordinal));
        Assert.False(model.CanRecruitAt(NonCapital));
        Assert.Contains("may not take a new recruitment order", model.RecruitRefusalReasonAt(NonCapital), StringComparison.Ordinal);
        Assert.True(model.CanRecruitAt(Capital));
        Assert.Null(model.RecruitRefusalReasonAt(Capital));

        // With no unit in training the below-threshold town drops off the list entirely.
        var bare = StateWithFort(NonCapital, 60);
        var bareModel = IC2.Slice.UI.RecruitUnitDialogModel.ForActiveNation(bare, ruleset, RecruitmentTestbed.World);
        Assert.DoesNotContain(bareModel.ListedCities, c => string.Equals(c.Id, NonCapital, StringComparison.Ordinal));
    }

    /// <summary>
    /// The raw-word / decoded-level boundary: a pending order (word 360 = 3 points over 60%) lets the
    /// town take an order (RecruitUnit compares the raw word), but the dialog lists by the decoded
    /// current level, so it is not listed. 100 is a finished 100% (listed); 160 is 60% with a pending
    /// order (not listed).
    /// </summary>
    [Theory]
    [InlineData(360, false, true)]
    [InlineData(160, false, true)]
    [InlineData(100, true, true)]
    public void The_dialog_lists_by_the_decoded_level_while_the_order_gate_reads_the_raw_word(
        int word, bool listed, bool mayOrder)
    {
        var ruleset = RecruitmentTestbed.Ruleset;
        var state = StateWithFort(NonCapital, word);
        var city = state.CityById(NonCapital)!;
        var nation = state.NationById(Nation)!;

        Assert.Equal(listed, RecruitmentEligibility.IsListedInDialog(city, nation, ruleset));
        Assert.Equal(mayOrder, RecruitmentEligibility.MayTakeOrder(city, nation, ruleset));

        var model = IC2.Slice.UI.RecruitUnitDialogModel.ForActiveNation(state, ruleset, RecruitmentTestbed.World);
        Assert.Equal(
            listed,
            model.ListedCities.Any(c => string.Equals(c.Id, NonCapital, StringComparison.Ordinal)));
    }
}
