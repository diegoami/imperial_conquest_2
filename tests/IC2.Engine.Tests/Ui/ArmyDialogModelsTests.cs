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
    private const string ThreeUnitArmyId = "t111-three";
    private const string JoinFourArmyId = "t164-join-four";
    private const string ThreeUnitNewArmyId = "t111-three-split";

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
            ThreeUnitArmyId, NationId, X: 112, Y: 110, Morale: 70, Money: 300, SupplyTons: 200, Moves: 8,
            Units: ValueList.Of(new[]
            {
                new UnitSlot(0, "heavy_infantry", 5_000, 6, "5th Legion"),
                new UnitSlot(0, "archers", 3_000, 8, "6th Bowmen"),
                new UnitSlot(0, "light_cavalry", 2_000, 7, "7th Riders"),
            })));
        armies.Add(new StartingArmy(
            JoinFourArmyId, NationId, X: 114, Y: 110, Morale: 70, Money: 0, SupplyTons: 0, Moves: 8,
            Units: ValueList.Of(new[]
            {
                new UnitSlot(0, "heavy_infantry", 1_000, 6, "J1"),
                new UnitSlot(0, "heavy_infantry", 1_200, 6, "J2"),
                new UnitSlot(0, "heavy_infantry", 1_500, 6, "J3"),
                new UnitSlot(0, "heavy_infantry", 1_700, 6, "J4"),
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
            session.State.ArmyById(ArmyAId)!, session.State.ArmyById(ArmyBId)!, ruleset ?? session.Ruleset,
            session.State, session.World);

    // ---- fix 1070: per-side troop totals and the supply caption ----

    private static int Troops(GameSession session, string armyId) =>
        session.State.ArmyById(armyId)!.Units.Sum(u => u.Troops);

    [Fact]
    public void Transfer_shows_each_sides_troop_total_and_updates_it_when_a_unit_moves()
    {
        var session = Session();
        var model = Transfer(session);
        var a = session.State.ArmyById(ArmyAId)!;
        var b = session.State.ArmyById(ArmyBId)!;

        Assert.Equal(Troops(session, ArmyAId), model.SelectedTroopTotal);
        Assert.Equal(Troops(session, ArmyBId), model.PartnerTroopTotal);

        model.StageUnitToPartner(0);
        Assert.Equal(Troops(session, ArmyAId) - a.Units[0].Troops, model.SelectedTroopTotal);
        Assert.Equal(Troops(session, ArmyBId) + a.Units[0].Troops, model.PartnerTroopTotal);

        model.StageUnitBack(0);
        Assert.Equal(Troops(session, ArmyAId) - a.Units[0].Troops + b.Units[0].Troops, model.SelectedTroopTotal);
        Assert.Equal(Troops(session, ArmyBId) + a.Units[0].Troops - b.Units[0].Troops, model.PartnerTroopTotal);

        model.UnstageUnitToPartner(0);
        model.UnstageUnitBack(0);
        Assert.Equal(Troops(session, ArmyAId), model.SelectedTroopTotal);
        Assert.Equal(Troops(session, ArmyBId), model.PartnerTroopTotal);
    }

    [Fact]
    public void Transfer_supply_caption_names_both_armies_and_shows_each_stock_now_and_after()
    {
        var session = Session(aSupply: 20, bSupply: 10);
        var model = Transfer(session);

        var idle = model.SupplyCaption();
        Assert.Contains(ArmyAId, idle);
        Assert.Contains(ArmyBId, idle);
        Assert.Contains($"{ArmyAId} 20 → 20 t", idle);
        Assert.Contains($"{ArmyBId} 10 → 10 t", idle);

        model.AdjustSupply(ArmyDialogModels.SupplyStepTons);
        var forward = model.SupplyCaption();
        Assert.Contains($"10 t from {ArmyAId} to {ArmyBId}", forward);
        Assert.Contains($"{ArmyAId} 20 → 10 t", forward);
        Assert.Contains($"{ArmyBId} 10 → 20 t", forward);
        Assert.Equal(10, model.SelectedSupplyAfter);
        Assert.Equal(20, model.PartnerSupplyAfter);

        model.AdjustSupply(-2 * ArmyDialogModels.SupplyStepTons);
        var back = model.SupplyCaption();
        Assert.Contains($"10 t from {ArmyBId} to {ArmyAId}", back);
        Assert.Contains($"{ArmyAId} 20 → 30 t", back);
        Assert.Contains($"{ArmyBId} 10 → 0 t", back);
    }

    [Fact]
    public void Transfer_with_an_empty_partner_shows_a_total_of_zero_and_a_sensible_caption()
    {
        var session = Session(bSupply: 0);
        var a = session.State.ArmyById(ArmyAId)!;
        var empty = (session.State.ArmyById(ArmyBId)! with { Units = ValueList.From(Array.Empty<UnitSlot>()) });
        var model = ArmyTransferModel.ForArmies(a, empty, session.Ruleset);

        Assert.Equal(0, model.PartnerTroopTotal);
        Assert.Equal(Troops(session, ArmyAId), model.SelectedTroopTotal);
        Assert.Contains($"{ArmyBId} 0 → 0 t", model.SupplyCaption());
    }

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
    public void Split_army_moving_two_units_with_supply_and_money_composes_exactly_one_split_army()
    {
        var session = Session();
        var army = session.State.ArmyById(ThreeUnitArmyId)!;
        var model = SplitArmyModel.ForArmy(session.State, army, session.Ruleset, ThreeUnitNewArmyId);

        Assert.True(model.CanSplit);
        Assert.Null(model.ComposeOk()); // nothing staged yet.

        model.StageUnit(2);
        model.StageUnit(0);
        model.AdjustSupply(ArmyDialogModels.SupplyStepTons);
        model.AdjustSupply(ArmyDialogModels.SupplyStepTons);
        model.AdjustMoney(ArmyDialogModels.MoneyLargeStepTalents);
        model.AdjustMoney(ArmyDialogModels.MoneyStepTalents);

        Assert.Equal(
            $"split-army {ThreeUnitArmyId} {ThreeUnitNewArmyId} 0,2 supply=20 money=110",
            model.ComposeOk());

        // The engine's own validator accepts exactly that one line: both units and the money land in
        // the new army, and the two armies' supply total is unchanged by the engine's rebalance.
        var totalSupplyBefore = army.SupplyTons;
        var output = session.Submit(model.ComposeOk()!);
        Assert.Contains(output.Lines, line => line.Contains("armies.split-army accepted", StringComparison.Ordinal));
        var parent = session.State.ArmyById(ThreeUnitArmyId)!;
        var child = session.State.ArmyById(ThreeUnitNewArmyId)!;
        Assert.Equal(new[] { "archers" }, parent.Units.Select(u => u.UnitTypeId).ToArray());
        Assert.Equal(
            new[] { "heavy_infantry", "light_cavalry" },
            child.Units.Select(u => u.UnitTypeId).OrderBy(t => t, StringComparer.Ordinal).ToArray());
        Assert.Equal(110, child.Money);
        Assert.Equal(300 - 110, parent.Money);
        Assert.Equal(totalSupplyBefore, parent.SupplyTons + child.SupplyTons);
    }

    [Fact]
    public void Split_army_spinners_clamp_to_the_new_armys_room_and_to_the_purse()
    {
        // A is rich: more supply and money than the new army can take.
        var session = Session(aSupply: 200, aMoney: 5_000);
        var army = session.State.ArmyById(ArmyAId)!;
        var rich = army with { Money = 5_000, SupplyTons = 5_000 };
        var model = SplitArmyModel.ForArmy(session.State, rich, session.Ruleset, NewArmyId);

        // No unit staged: the new army has no room, so supply cannot move; money is bound by the purse.
        model.AdjustSupply(ArmyDialogModels.SupplyLargeStepTons);
        Assert.Equal(0, model.Supply);

        model.StageUnit(1); // archers, 3,000 troops: room = 3000 div 100 + 1.
        var room = IC2.Engine.Economy.SupplyCapacity.ArmyDialogCapacityTons(3_000, session.Ruleset);
        Assert.Equal(room, model.NewArmyRoom);
        for (var i = 0; i < 50; i++)
        {
            model.AdjustSupply(ArmyDialogModels.SupplyLargeStepTons);
            model.AdjustMoney(ArmyDialogModels.MoneyLargeStepTalents);
        }

        Assert.Equal(room, model.Supply);
        Assert.Equal(session.Ruleset.Economy.PurseCapPerUnit, model.Money);
        Assert.Equal(1_000, model.Money);

        // A poor giver bounds both too: A's own supply and money.
        var poor = army with { Money = 40, SupplyTons = 15 };
        var poorModel = SplitArmyModel.ForArmy(session.State, poor, session.Ruleset, NewArmyId);
        poorModel.StageUnit(1);
        for (var i = 0; i < 10; i++)
        {
            poorModel.AdjustSupply(ArmyDialogModels.SupplyStepTons);
            poorModel.AdjustMoney(ArmyDialogModels.MoneyStepTalents);
        }

        Assert.Equal(15, poorModel.Supply);
        Assert.Equal(40, poorModel.Money);

        // Putting the unit back drops the room, so the staged supply follows it down.
        model.UnstageUnit(1);
        Assert.Equal(0, model.Supply);
    }

    [Fact]
    public void Split_army_keeps_one_unit_in_the_selected_army()
    {
        var session = Session();
        var model = SplitArmyModel.ForArmy(
            session.State, session.State.ArmyById(ArmyAId)!, session.Ruleset, NewArmyId);

        model.StageUnit(0);
        model.StageUnit(1); // would leave the selected army empty: ignored.

        Assert.Equal(new[] { 0 }, model.StagedUnits.ToArray());
        Assert.Equal($"split-army {ArmyAId} {NewArmyId} 0", model.ComposeOk());
    }

    [Fact]
    public void Split_army_aboard_a_fleet_is_offered_and_composes_one_split_army()
    {
        var session = Session();
        var aboard = session.State.ArmyById(ThreeUnitArmyId)! with { AboardFleetId = "some-fleet" };
        var model = SplitArmyModel.ForArmy(session.State, aboard, session.Ruleset, ThreeUnitNewArmyId);

        Assert.True(model.CanSplit);
        Assert.Null(model.RefusalMessage);
        model.StageUnit(0);
        Assert.Equal($"split-army {ThreeUnitArmyId} {ThreeUnitNewArmyId} 0", model.ComposeOk());

        // A one-unit army aboard still gets T111's one-unit refusal.
        var lonely = session.State.ArmyById(OneUnitArmyId)! with { AboardFleetId = "some-fleet" };
        var lonelyModel = SplitArmyModel.ForArmy(session.State, lonely, session.Ruleset, NewArmyId);
        Assert.False(lonelyModel.CanSplit);
        Assert.Equal(ArmyDialogModels.SplitOneUnitRefusal, lonelyModel.RefusalMessage);
    }

    [Fact]
    public void Split_army_cancel_composes_nothing()
    {
        var session = Session();
        var model = SplitArmyModel.ForArmy(
            session.State, session.State.ArmyById(ThreeUnitArmyId)!, session.Ruleset, ThreeUnitNewArmyId);
        model.StageUnit(0);
        model.AdjustMoney(ArmyDialogModels.MoneyStepTalents);

        Assert.Null(model.Cancel());
    }

    [Fact]
    public void Split_army_disband_under_either_list_composes_disband_unit_with_the_selected_armys_index()
    {
        var session = Session();
        var army = session.State.ArmyById(ThreeUnitArmyId)!;
        var model = SplitArmyModel.ForArmy(session.State, army, session.Ruleset, ThreeUnitNewArmyId);
        model.StageUnit(2);

        // An unstaged unit (index 1) and a staged one (index 2): both name the selected army's index.
        Assert.Equal($"disband-unit {ThreeUnitArmyId} 1", model.DisbandLine(1));
        Assert.Equal($"disband-unit {ThreeUnitArmyId} 2", model.DisbandLine(2));

        // Disbanding the staged unit 0 drops it from the staging and shifts unit 2 down to index 1.
        model.StageUnit(0);
        var after = army with { Units = ValueList.From(army.Units.Where((_, i) => i != 0).ToList()) };
        model.ApplyDisband(0, after);
        Assert.Equal(new[] { 1 }, model.StagedUnits.ToArray());
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
        Assert.Equal($"join-units {ArmyAId} 0 1 2", model.JoinUnitsLine(0, 1, 2));
        Assert.Equal($"disband-unit {ArmyAId} 0", model.DisbandUnitLine(0));

        var output = session.Submit(model.RenameLine(1, "Legio I"));
        Assert.Contains(output.Lines, line => line.Contains("armies.rename-unit accepted", StringComparison.Ordinal));
        Assert.Equal("Legio I", session.State.ArmyById(ArmyAId)!.Units[1].Name);
    }

    [Fact]
    public void Change_units_join_over_three_selected_units_joins_all_three_through_the_session()
    {
        var session = Session();
        var model = ChangeUnitsModel.ForArmy(session.State.ArmyById(JoinFourArmyId)!);

        var line = model.JoinUnitsLine(0, 1, 2);
        Assert.Equal($"join-units {JoinFourArmyId} 0 1 2", line);
        var output = session.Submit(line);

        Assert.Contains(output.Lines, l => l.Contains("armies.join-units accepted", StringComparison.Ordinal));
        var units = session.State.ArmyById(JoinFourArmyId)!.Units;
        Assert.Equal(2, units.Count);
        Assert.Equal(3_700, units[0].Troops); // 1,000 + 1,200 + 1,500.
        Assert.Equal(1_700, units[1].Troops); // the unselected fourth is untouched.
    }

    [Fact]
    public void Change_units_join_over_four_selected_units_joins_all_four_through_the_session()
    {
        var session = Session();
        var model = ChangeUnitsModel.ForArmy(session.State.ArmyById(JoinFourArmyId)!);

        session.Submit(model.JoinUnitsLine(0, 1, 2, 3));

        var units = session.State.ArmyById(JoinFourArmyId)!.Units;
        Assert.Single(units);
        Assert.Equal(5_400, units[0].Troops);
    }

    [Fact]
    public void Change_units_disband_asks_the_original_prompt_and_composes_one_order_per_selected_unit()
    {
        var session = Session();
        var model = ChangeUnitsModel.ForArmy(session.State.ArmyById(ArmyAId)!);

        // The prompt's own text, the original's singular for one unit and plural above it.
        Assert.Equal("Are you sure you want to disband 1 unit.", ChangeUnitsModel.DisbandPromptText(1));
        Assert.Equal("Are you sure you want to disband 2 units.", ChangeUnitsModel.DisbandPromptText(2));

        // Two selected units: Yes composes one disband order each, highest index first so the earlier
        // removals do not shift the later ones; No composes none.
        int[] selected = { 0, 1 };
        var yes = model.DisbandOrders(selected, confirmed: true);
        Assert.Equal(new[] { $"disband-unit {ArmyAId} 1", $"disband-unit {ArmyAId} 0" }, yes.ToArray());
        Assert.Empty(model.DisbandOrders(selected, confirmed: false));

        // The engine accepts both orders in that order, leaving the army with its last unit.
        foreach (var line in yes)
        {
            session.Submit(line);
        }

        Assert.Single(session.State.ArmyById(ArmyAId)!.Units);
    }

    [Fact]
    public void Change_units_disband_orders_pin_the_empty_single_and_duplicate_selections()
    {
        var session = Session();
        var threeUnit = ChangeUnitsModel.ForArmy(session.State.ArmyById(ThreeUnitArmyId)!);

        // Nothing selected: Yes composes no order at all (the dialog returns before it prompts).
        Assert.Empty(threeUnit.DisbandOrders(Array.Empty<int>(), confirmed: true));

        // One selected unit: exactly one order, that unit's own index.
        Assert.Equal(
            new[] { $"disband-unit {ThreeUnitArmyId} 1" },
            threeUnit.DisbandOrders(new[] { 1 }, confirmed: true).ToArray());

        // A duplicate selection collapses to one order per unit; the surviving multi-selection is still
        // highest index first. Without Distinct, the duplicate 1 would produce a second disband-unit 1.
        Assert.Equal(
            new[]
            {
                $"disband-unit {ThreeUnitArmyId} 2",
                $"disband-unit {ThreeUnitArmyId} 1",
                $"disband-unit {ThreeUnitArmyId} 0",
            },
            threeUnit.DisbandOrders(new[] { 0, 1, 1, 2 }, confirmed: true).ToArray());
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

    // ---- fix 1070 rework 1: the caption's "after" is what the real command leaves ----

    private static (int A, int B) RealSupplies(GameSession session, string line)
    {
        var lines = session.Submit(line).Lines;
        Assert.Contains(lines, l => l.Contains("army-transfer accepted", StringComparison.Ordinal));
        return (session.State.ArmyById(ArmyAId)?.SupplyTons ?? 0, session.State.ArmyById(ArmyBId)?.SupplyTons ?? 0);
    }

    [Fact]
    public void Transfer_caption_after_includes_the_commands_rebalancing_for_a_moved_unit()
    {
        // Moving the 5,000-troop unit leaves A far over its new capacity, so the command pushes supply on.
        var session = Session(aSupply: 200, bSupply: 10);
        var model = Transfer(session);
        model.StageUnitToPartner(0);
        var caption = model.SupplyCaption();
        var line = model.ComposeOk()!;

        var real = RealSupplies(Session(aSupply: 200, bSupply: 10), line);

        Assert.NotEqual((190, 20), real); // the rebalancing changed the plain net: the case is not vacuous
        Assert.Equal(real.A, model.SelectedSupplyAfter);
        Assert.Equal(real.B, model.PartnerSupplyAfter);
        Assert.Contains($"{ArmyAId} 200 → {real.A} t", caption);
        Assert.Contains($"{ArmyBId} 10 → {real.B} t", caption);
    }

    [Fact]
    public void Transfer_caption_after_matches_the_real_command_for_many_moves()
    {
        var cases = new (int[] To, int[] Back, int Net)[]
        {
            (new[] { 0 }, System.Array.Empty<int>(), 0),
            (new[] { 1 }, System.Array.Empty<int>(), 30),
            (new[] { 0, 1 }, System.Array.Empty<int>(), 0),
            (System.Array.Empty<int>(), new[] { 0 }, 0),
            (System.Array.Empty<int>(), new[] { 0 }, -10),
            (new[] { 0 }, new[] { 0 }, 50),
            (new[] { 1 }, new[] { 0 }, -10),
            (System.Array.Empty<int>(), System.Array.Empty<int>(), 100),
        };

        foreach (var (to, back, net) in cases)
        {
            var model = Transfer(Session());
            foreach (var i in to) { model.StageUnitToPartner(i); }
            foreach (var i in back) { model.StageUnitBack(i); }
            model.AdjustSupply(net);
            var line = model.ComposeOk()!;

            var real = RealSupplies(Session(), line);

            var label = $"{line} (A {model.SelectedSupplyAfter}, B {model.PartnerSupplyAfter})";
            Assert.True(real.A == model.SelectedSupplyAfter, label + $" real A {real.A}");
            Assert.True(real.B == model.PartnerSupplyAfter, label + $" real B {real.B}");
            Assert.Contains($"{ArmyAId} 200 → {real.A} t", model.SupplyCaption());
        }
    }
}
