using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Engine.Serialization;
using IC2.Slice.UI;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Ui;

/// <summary>
/// T111 Done-when 1 and 7: the Army dialogs' Godot-free models pin each dialog's limits and the command
/// lines it composes, against the engine's own validators, and pin the regiment quality caption.
/// </summary>
/// <remarks>
/// The state is built on the shipped toy world, which supplies the ruleset the limits are read from; only
/// the two armies (one of them one-unit) are scripted. Inserting the state's own armies into the toy
/// world is the same scenario-variant-in-code seam <c>godot/Checks/SupplyArmyCheck.cs</c> uses, so the
/// composed lines are submitted through a real <see cref="GameSession"/> and judged by the real engine,
/// never re-derived here.
/// </remarks>
public sealed class ArmyDialogModelsTests
{
    private const string NationId = "rome";
    private const string ArmyAId = "t111-a";
    private const string ArmyBId = "t111-b";
    private const string OneUnitArmyId = "t111-lonely";
    private const string NewArmyId = "t111-a-split";

    private static readonly Lazy<ResolvedScenario> LazyClassical = new(
        () => GameDataRepository.Load(ModelTestPaths.DataRoot).Resolve("classical-mediterranean"));

    private static ResolvedScenario Classical => LazyClassical.Value;

    private static GameSession Session(int aSupply = 200, int bSupply = 10, int aMoney = 200, int bMoney = 100)
    {
        var resolved = Classical;
        var armies = resolved.World.StartingArmies.ToList();

        // (100,100) and (100,99) are adjacent on the classical map's own free ground (the same cells
        // SupplyArmyCheck uses); (110,110) is the one-unit army used for the split refusal.
        armies.Add(new StartingArmy(
            ArmyAId, NationId, X: 100, Y: 100, Morale: 70, Money: aMoney, SupplyTons: aSupply, Moves: 8,
            Units: ValueList.Of(new[]
            {
                new UnitSlot(0, "heavy_infantry", 5_000, 6, "1st Guards Battalion"),
                new UnitSlot(0, "archers", 3_000, 8, "2nd Bowmen Battalion"),
            })));
        armies.Add(new StartingArmy(
            ArmyBId, NationId, X: 100, Y: 99, Morale: 70, Money: bMoney, SupplyTons: bSupply, Moves: 8,
            Units: ValueList.Of(new[]
            {
                new UnitSlot(0, "light_cavalry", 4_000, 7, "3rd Lancers Battalion"),
            })));
        armies.Add(new StartingArmy(
            OneUnitArmyId, NationId, X: 110, Y: 110, Morale: 70, Money: 0, SupplyTons: 0, Moves: 8,
            Units: ValueList.Of(new[]
            {
                new UnitSlot(0, "light_infantry", 1_000, 6, "4th Foot Battalion"),
            })));

        var world = resolved.World with { StartingArmies = ValueList.From(armies) };
        return new GameSession(world, resolved.Ruleset, resolved.Scenario, seedOverride: 1, humanSeatNationId: NationId);
    }

    private static ArmyTransferModel Transfer(GameSession session, Ruleset? ruleset = null) =>
        ArmyTransferModel.ForArmies(
            session.State.ArmyById(ArmyAId)!, session.State.ArmyById(ArmyBId)!, ruleset ?? session.Ruleset);

    // ---- Done-when 1: ArmyTransferModel ----

    [Fact]
    public void Transfer_composes_exactly_one_army_transfer_for_units_moved_both_ways_and_netted_resources()
    {
        var session = Session();
        var model = Transfer(session);

        model.StageUnitToPartner(0); // A's heavy infantry to B.
        model.StageUnitBack(0);      // B's light cavalry back to A.
        model.AdjustSupply(ArmyDialogModels.SupplyStepTons);       // +10
        model.AdjustSupply(ArmyDialogModels.SupplyStepTons);       // +10
        model.AdjustSupply(-ArmyDialogModels.SupplyStepTons);      // -10, net +10
        model.AdjustMoney(ArmyDialogModels.MoneyLargeStepTalents); // +100
        model.AdjustMoney(-30);                                    // net +70

        Assert.Equal(
            "army-transfer t111-a t111-b units=0 back-units=0 supply=10 money=70",
            model.ComposeOk());

        // The engine's own validator accepts exactly that one line, and each unit ends in its new army.
        var output = session.Submit(model.ComposeOk()!);
        Assert.Contains(output.Lines, line => line.Contains("armies.army-transfer accepted", StringComparison.Ordinal));

        var a = session.State.ArmyById(ArmyAId)!;
        var b = session.State.ArmyById(ArmyBId)!;
        Assert.Contains(b.Units, unit => unit.UnitTypeId == "heavy_infantry" && unit.Troops == 5_000);
        Assert.Contains(a.Units, unit => unit.UnitTypeId == "light_cavalry" && unit.Troops == 4_000);
        Assert.DoesNotContain(a.Units, unit => unit.UnitTypeId == "heavy_infantry");
        Assert.DoesNotContain(b.Units, unit => unit.UnitTypeId == "light_cavalry");
    }

    [Fact]
    public void Transfer_nets_a_negative_balance_to_the_back_direction()
    {
        var session = Session(aSupply: 0, aMoney: 200);
        var model = Transfer(session);

        model.AdjustSupply(-ArmyDialogModels.SupplyStepTons);   // B -> A, 10
        model.AdjustMoney(-ArmyDialogModels.MoneyStepTalents);  // B -> A, 10

        Assert.Equal(
            "army-transfer t111-a t111-b back-supply=10 back-money=10",
            model.ComposeOk());
    }

    [Fact]
    public void Transfer_composes_nothing_when_nothing_is_staged_and_cancel_submits_nothing()
    {
        var session = Session();
        var model = Transfer(session);

        Assert.Null(model.ComposeOk());
        Assert.Null(model.Cancel());
        Assert.Null(model.ComposeOk());
    }

    [Fact]
    public void The_transfer_spinners_read_the_ruleset_limits_not_a_literal()
    {
        var session = Session(aSupply: 5_000);
        var defaultModel = Transfer(session);
        var widened = session.Ruleset with
        {
            Economy = session.Ruleset.Economy with
            {
                SupplyDialogArmyCapacityBonus = 1_000,
                PurseCapPerUnit = 50,
            },
        };
        var widenedModel = Transfer(session, widened);

        // B's room: 4000 / 100 + 1 - 10 = 31 by default, +1000 with the widened bonus.
        Assert.Equal(31, defaultModel.MaxSupplyToPartner);
        Assert.Equal(1_030, widenedModel.MaxSupplyToPartner);

        // A purse cap of 50 against B's 100 talents leaves no room, so the widened cap is read.
        Assert.Equal(200, defaultModel.MaxMoneyToPartner);
        Assert.Equal(0, widenedModel.MaxMoneyToPartner);
    }

    // ---- Done-when 1: SplitArmyModel ----

    [Fact]
    public void Split_army_is_refused_under_two_units_by_both_the_model_and_the_engine()
    {
        var session = Session();
        var model = SplitArmyModel.ForArmy(
            session.State, session.State.ArmyById(OneUnitArmyId)!, session.Ruleset, NewArmyId);

        Assert.False(model.CanSplit);
        Assert.Equal(ArmyDialogModels.SplitOneUnitRefusal, model.RefusalMessage);
        Assert.Null(model.ComposeOk());

        // The engine's own validator refuses the same command with the same code.
        var output = session.Submit($"split-army {OneUnitArmyId} {NewArmyId} 0");
        Assert.Contains(
            output.Lines,
            line => line.Contains("armies.too-few-units-to-split", StringComparison.Ordinal));
        Assert.Null(session.State.ArmyById(NewArmyId));
    }

    [Fact]
    public void Split_army_stages_one_unit_and_composes_one_split_army()
    {
        var session = Session();
        var model = SplitArmyModel.ForArmy(
            session.State, session.State.ArmyById(ArmyAId)!, session.Ruleset, NewArmyId);

        Assert.True(model.CanSplit);
        model.StageUnit(1);
        Assert.Equal($"split-army {ArmyAId} {NewArmyId} 1", model.ComposeOk());
        Assert.Null(model.Cancel());
    }

    // ---- Done-when 1: Join armies with no partner ----

    [Fact]
    public void Join_armies_composes_nothing_without_a_partner()
    {
        var session = Session();

        Assert.Null(ArmyDialogModels.JoinArmiesLine(ArmyAId, partner: null));
        Assert.Equal(
            $"join-armies {ArmyAId} {ArmyBId}",
            ArmyDialogModels.JoinArmiesLine(ArmyAId, session.State.ArmyById(ArmyBId)));
    }

    // ---- Done-when 1: ChangeUnitsModel ----

    [Fact]
    public void Change_units_composes_each_single_unit_verb()
    {
        var session = Session();
        var model = ChangeUnitsModel.ForArmy(session.State.ArmyById(ArmyAId)!);

        Assert.Equal($"rename-unit {ArmyAId} 1 Legio I", model.RenameLine(1, "Legio I"));
        Assert.Equal($"split-unit {ArmyAId} 0 1000", model.SplitUnitLine(0, 1_000));
        Assert.Equal($"join-units {ArmyAId} 0 1", model.JoinUnitsLine(0, 1));
        Assert.Equal($"disband-unit {ArmyAId} 0", model.DisbandUnitLine(0));

        var output = session.Submit(model.RenameLine(1, "Legio I"));
        Assert.Contains(output.Lines, line => line.Contains("armies.rename-unit accepted", StringComparison.Ordinal));
        Assert.Equal("Legio I", session.State.ArmyById(ArmyAId)!.Units[1].Name);
    }

    // ---- Done-when 7: the quality captions ----

    [Theory]
    [InlineData(5, "poor")]
    [InlineData(6, "average")]
    [InlineData(7, "good")]
    [InlineData(8, "very good")]
    [InlineData(9, "elite")]
    [InlineData(4, "4")]
    [InlineData(0, "0")]
    public void Quality_caption_maps_the_roster_tiers_and_falls_back_to_the_number(int quality, string expected)
    {
        Assert.Equal(expected, ArmyDialogModels.QualityCaption(quality));
    }
}
