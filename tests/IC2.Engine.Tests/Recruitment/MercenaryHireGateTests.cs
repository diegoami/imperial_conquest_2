using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Recruitment;
using IC2.Engine.Recruitment.Commands;
using IC2.Engine.Tests.Core;
using Xunit;

namespace IC2.Engine.Tests.Recruitment;

/// <summary>
/// T143 (bug #755): an accepted mercenary hire passes <see cref="MercenaryHireCost.Compute"/> as a
/// <strong>minimum-purse gate</strong> and takes nothing — the army's own purse and the nation's treasury
/// are both unchanged, and the consumed pool slot is the only state the hire writes besides the unit.
/// </summary>
/// <remarks>
/// The two gate numbers are the brief's own scenarios: light infantry 3,868 troops at quality 8 gives
/// <c>(3868 × 1 / 1000) × 8 = 24</c>, heavy cavalry 960 at quality 9 gives
/// <c>(960 × 4 / 1000) × 9 = 27</c>. The shipped toy ruleset's <c>economyPurses</c> is
/// <c>perUnitPurses</c>, i.e. <c>classical-faithful</c>; the last test repeats the first hire with the
/// <c>improved</c> value and expects the same, because the hire does not read the flag at all.
/// </remarks>
public sealed class MercenaryHireGateTests
{
    private const string ArmyId = "north-army-1";
    private const string NationId = "north";

    private static readonly MercenaryPoolSlot LightInfantryOffer = new(
        SlotIndex: 33, X: 2, Y: 1, NameLabel: 11, UnitTypeId: "light_infantry", Troops: 3868, Quality: 8);

    private static readonly MercenaryPoolSlot HeavyCavalryOffer = new(
        SlotIndex: 34, X: 2, Y: 1, NameLabel: 38, UnitTypeId: "heavy_cavalry", Troops: 960, Quality: 9);

    private static CommandDispatcher Dispatcher(Ruleset? ruleset = null, IEventSink? sink = null) => new(
        SystemRegistry.FromEngineAssembly(),
        ruleset ?? RecruitmentTestbed.Ruleset,
        CoreTestbed.Toy.World,
        sink ?? NullEventSink.Instance);

    private static GameState WithArmyMoney(GameState state, int money) =>
        RecruitmentTestbed.WithArmy(state, state.ArmyById(ArmyId)! with { Money = money });

    [Fact]
    public void A_light_infantry_hire_passes_its_gate_of_24_and_leaves_the_purse_at_30()
    {
        Assert.Equal(EconomyPurseModel.PerUnitPurses, RecruitmentTestbed.Ruleset.Flags.EconomyPurses);

        var sink = new RecordingEventSink();
        var dispatcher = Dispatcher(sink: sink);
        var initial = RecruitmentTestbed.InitialState();
        var army = initial.ArmyById(ArmyId)!;
        var nation = initial.NationById(NationId)!;

        var gate = MercenaryHireCost.Compute(
            LightInfantryOffer.Troops, LightInfantryOffer.UnitTypeId, LightInfantryOffer.Quality,
            RecruitmentTestbed.Ruleset);
        Assert.Equal(24, gate);

        var before = RecruitmentTestbed.WithMercenaryPool(WithArmyMoney(initial, 30), LightInfantryOffer);
        var result = dispatcher.Dispatch(
            before, new HireMercenaryCommand(before.ActiveNationId, army.Id, LightInfantryOffer.SlotIndex));

        Assert.True(result.IsAccepted, result.ToString());

        // The unit is appended.
        var updatedArmy = result.State.ArmyById(army.Id)!;
        Assert.Equal(army.Units.Count + 1, updatedArmy.Units.Count);
        var hiredUnit = Assert.Single(updatedArmy.Units, u => u.Troops == LightInfantryOffer.Troops);
        Assert.True(hiredUnit.IsMercenary);
        Assert.Equal(LightInfantryOffer.NameLabel, hiredUnit.MercenaryLabel);
        Assert.Equal(LightInfantryOffer.UnitTypeId, hiredUnit.UnitTypeId);
        Assert.Equal(LightInfantryOffer.Quality, hiredUnit.Quality);

        // Bug #755: the purse is still 30 — the gate is not a charge.
        Assert.Equal(30, updatedArmy.Money);

        // And the treasury is unchanged.
        Assert.Equal(nation.Treasury, result.State.NationById(nation.Id)!.Treasury);

        // The hired pool slot is consumed.
        Assert.Empty(result.State.MercenaryPool);

        // Done-when 4: the event carries the gate, not talents paid.
        var hired = Assert.IsType<MercenaryHired>(Assert.Single(sink.Events));
        Assert.Equal(24, hired.HireGate);
    }

    [Fact]
    public void The_report_second_hire_passes_the_heavy_cavalry_gate_of_27_and_leaves_the_purse_at_30()
    {
        var dispatcher = Dispatcher();
        var initial = RecruitmentTestbed.InitialState();
        var army = initial.ArmyById(ArmyId)!;
        var nation = initial.NationById(NationId)!;

        var gate = MercenaryHireCost.Compute(
            HeavyCavalryOffer.Troops, HeavyCavalryOffer.UnitTypeId, HeavyCavalryOffer.Quality,
            RecruitmentTestbed.Ruleset);
        Assert.Equal(27, gate);

        var before = RecruitmentTestbed.WithMercenaryPool(WithArmyMoney(initial, 30), HeavyCavalryOffer);
        var result = dispatcher.Dispatch(
            before, new HireMercenaryCommand(before.ActiveNationId, army.Id, HeavyCavalryOffer.SlotIndex));

        Assert.True(result.IsAccepted, result.ToString());
        var updatedArmy = result.State.ArmyById(army.Id)!;
        Assert.Single(updatedArmy.Units, u => u.Troops == HeavyCavalryOffer.Troops);
        Assert.Equal(30, updatedArmy.Money);
        Assert.Equal(nation.Treasury, result.State.NationById(nation.Id)!.Treasury);
    }

    [Fact]
    public void An_army_holding_20_talents_is_refused_at_the_gate_of_24_and_changes_nothing()
    {
        var dispatcher = Dispatcher();
        var initial = RecruitmentTestbed.InitialState();
        var army = initial.ArmyById(ArmyId)!;

        var gate = MercenaryHireCost.Compute(
            LightInfantryOffer.Troops, LightInfantryOffer.UnitTypeId, LightInfantryOffer.Quality,
            RecruitmentTestbed.Ruleset);
        Assert.Equal(24, gate);

        var before = RecruitmentTestbed.WithMercenaryPool(WithArmyMoney(initial, 20), LightInfantryOffer);
        var result = dispatcher.Dispatch(
            before, new HireMercenaryCommand(before.ActiveNationId, army.Id, LightInfantryOffer.SlotIndex));

        Assert.Equal(HireMercenaryRejections.InsufficientMoney, result.Code);
        Assert.Same(before, result.State);
    }

    [Fact]
    public void Under_improved_the_same_hire_also_leaves_the_purse_at_30()
    {
        var improved = RecruitmentTestbed.Ruleset with
        {
            Flags = RecruitmentTestbed.Ruleset.Flags with { EconomyPurses = EconomyPurseModel.CentralTreasury },
        };
        Assert.Equal(EconomyPurseModel.CentralTreasury, improved.Flags.EconomyPurses);

        var dispatcher = Dispatcher(improved);
        var initial = RecruitmentTestbed.InitialState();
        var army = initial.ArmyById(ArmyId)!;
        var nation = initial.NationById(NationId)!;

        var gate = MercenaryHireCost.Compute(
            LightInfantryOffer.Troops, LightInfantryOffer.UnitTypeId, LightInfantryOffer.Quality, improved);
        Assert.Equal(24, gate);

        var before = RecruitmentTestbed.WithMercenaryPool(WithArmyMoney(initial, 30), LightInfantryOffer);
        var result = dispatcher.Dispatch(
            before, new HireMercenaryCommand(before.ActiveNationId, army.Id, LightInfantryOffer.SlotIndex));

        Assert.True(result.IsAccepted, result.ToString());
        var updatedArmy = result.State.ArmyById(army.Id)!;
        Assert.Single(updatedArmy.Units, u => u.Troops == LightInfantryOffer.Troops);
        Assert.Equal(30, updatedArmy.Money);
        Assert.Equal(nation.Treasury, result.State.NationById(nation.Id)!.Treasury);
    }
}
