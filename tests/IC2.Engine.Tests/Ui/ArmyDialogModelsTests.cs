using IC2.Engine.Model;
using IC2.Engine.Armies;
using IC2.Engine.Presentation;
using IC2.Engine.Serialization;
using IC2.Engine.Strength;
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
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void T225_Partial_working_copy_matches_OK_and_cancel_changes_no_live_state(bool back)
    {
        var original = Session(aSupply: 0, bSupply: 0);
        var donor = original.State.ArmyById(ArmyAId)! with { Units = ValueList.From(Enumerable.Range(0, 3)
            .Select(i => new UnitSlot(0, "light_infantry", 100, 6, $"D{i}"))) };
        var receiver = original.State.ArmyById(ArmyBId)! with { Units = ValueList.From(Enumerable.Range(0, 19)
            .Select(i => new UnitSlot(0, "light_infantry", 100, 6, $"R{i}"))) };
        var state = original.State with { Armies = ValueList.Of(donor, receiver) };
        var save = new SaveGame(state.SchemaVersion, "partial-model", "partial-model", state.ScenarioId, state.WorldId, state.RulesetId, state);
        var session = new GameSession(original.World, original.Ruleset, original.Scenario, save);
        var model = ArmyTransferModel.ForArmies(back ? receiver : donor, back ? donor : receiver,
            session.Ruleset, state, session.World);
        model.StageTransfers(new[] { new ArmyTransferUnitStep(!back, 1, !back), new ArmyTransferUnitStep(!back, 0, !back) });
        Assert.Equal(20, (back ? model.SelectedWorkingUnits : model.PartnerWorkingUnits).Count);
        Assert.Equal("D1", (back ? model.SelectedWorkingUnits : model.PartnerWorkingUnits)[^1].Unit.Name);
        Assert.Single(model.LastUnitMessages);
        Assert.Equal(state, session.State);
        Assert.Null(model.Cancel());
        var output = session.Submit(model.ComposeOk()!);
        Assert.DoesNotContain(output.Lines, l => l.Contains("rejected", StringComparison.Ordinal));
        Assert.Equal(new[] { "D0", "D2" }, session.State.ArmyById(donor.Id)!.Units.Select(u => u.Name));
        Assert.Equal(20, session.State.ArmyById(receiver.Id)!.Units.Count);
    }

    [Fact]
    public void T225_Separate_forward_and_return_arrows_keep_append_order_and_their_movement_effects()
    {
        var session = Session();
        var before = session.State;
        var model = Transfer(session);
        model.StageUnitToPartner(0);
        model.UnstageUnitToPartner(0);
        Assert.Equal(before.ArmyById(ArmyAId)!.Units.Reverse(), model.SelectedWorkingUnits.Select(u => u.Unit));
        Assert.Equal(before, session.State);
        var output = session.Submit(model.ComposeOk()!);
        Assert.DoesNotContain(output.Lines, l => l.Contains("rejected", StringComparison.Ordinal));
        Assert.Equal(model.SelectedWorkingUnits.Select(u => u.Unit), session.State.ArmyById(ArmyAId)!.Units);
        Assert.Equal(0, session.State.ArmyById(ArmyAId)!.Moves);
        Assert.Equal(0, session.State.ArmyById(ArmyBId)!.Moves);
    }

    [Theory]
    [InlineData(100, 900, 100, 0, 1000)]
    [InlineData(100, 1000, 100, 100, 1000)]
    [InlineData(100, 1100, 100, 200, 1000)]
    [InlineData(900, 100, -100, 1000, 0)]
    [InlineData(1000, 100, -100, 1000, 100)]
    [InlineData(1100, 100, -100, 1000, 200)]
    public void T222_Money_arrows_operate_on_working_purses_including_signed_pullback(
        int a, int b, int delta, int expectedA, int expectedB)
    {
        var session = Session(aMoney: a, bMoney: b);
        var before = session.State;
        var model = Transfer(session);
        model.AdjustMoney(delta);
        Assert.Equal(expectedA, model.SelectedMoneyAfter);
        Assert.Equal(expectedB, model.PartnerMoneyAfter);
        model.AdjustMoney(delta);
        Assert.Equal(expectedA, model.SelectedMoneyAfter);
        Assert.Equal(expectedB, model.PartnerMoneyAfter);
        Assert.Equal(before, session.State);
        Assert.Null(model.Cancel());
        var output = session.Submit(model.ComposeOk()!);
        Assert.DoesNotContain(output.Lines, l => l.StartsWith("Refused:", StringComparison.Ordinal));
        Assert.Equal(expectedA, session.State.ArmyById(ArmyAId)!.Money);
        Assert.Equal(expectedB, session.State.ArmyById(ArmyBId)!.Money);
        Assert.Equal(before.Nations, session.State.Nations);
    }

    [Fact]
    public void T222_Split_money_arrow_can_pull_back_from_an_overcap_parent_with_empty_new_purse()
    {
        var session = Session(aMoney: 1100);
        var before = session.State;
        var model = SplitArmyModel.ForArmy(before, before.ArmyById(ArmyAId)!, session.Ruleset, NewArmyId);
        model.AdjustMoney(-100);
        Assert.Equal(100, model.Money);
        model.AdjustMoney(-100);
        Assert.Equal(100, model.Money);
        Assert.Null(model.Cancel());
        Assert.Equal(before, session.State);
        model.StageUnit(0);
        session.Submit(model.ComposeOk()!);
        Assert.Equal(1000, session.State.ArmyById(ArmyAId)!.Money);
        Assert.Equal(100, session.State.ArmyById(NewArmyId)!.Money);
    }

    private const string NationId = "rome";
    private const string ArmyAId = "t111-a";
    private const string ArmyBId = "t111-b";
    private const string OneUnitArmyId = "t111-lonely";
    private const string NewArmyId = "t111-a-split";
    private const string ThreeUnitArmyId = "t111-three";
    private const string JoinFourArmyId = "t164-join-four";
    private const string ThreeUnitNewArmyId = "t111-three-split";

    [Theory]
    [InlineData(0)]
    [InlineData(200)]
    [InlineData(900)]
    public void Split_live_balances_update_with_edits_and_match_committed_command(int supply)
    {
        var session = Session(aSupply: supply);
        var before = session.State;
        var source = before.ArmyById(ArmyAId)!;
        var model = SplitArmyModel.ForArmy(before, source, session.Ruleset, NewArmyId);
        Assert.Equal((supply, 0, source.Money, 0), model.PreviewBalances(before, session.World));
        model.StageUnit(0);
        model.AdjustSupply(10);
        model.AdjustMoney(50);
        var first = model.PreviewBalances(before, session.World);
        Assert.Equal(source.Money - 50, first.ParentMoney);
        Assert.Equal(50, first.NewMoney);
        Assert.Equal(supply, first.ParentSupply + first.NewSupply);
        model.AdjustMoney(10);
        var updated = model.PreviewBalances(before, session.World);
        Assert.Equal(first.ParentMoney - 10, updated.ParentMoney);
        Assert.Equal(first.NewMoney + 10, updated.NewMoney);
        Assert.Equal(before, session.State);
        Assert.Null(model.Cancel());
        session.Submit(model.ComposeOk()!);
        var parent = session.State.ArmyById(ArmyAId)!;
        var child = session.State.ArmyById(NewArmyId)!;
        Assert.Equal((parent.SupplyTons, child.SupplyTons, parent.Money, child.Money), updated);
    }

    [Fact]
    public void New_army_name_uses_first_free_number_and_never_parent_suffix()
    {
        var state = Session().State;
        var source = state.Armies[0];
        state = state with { Armies = ValueList.Of(source with { Id = "army-1" }, source with { Id = "army-3" }) };
        Assert.Equal("army-2", ArmyDialogModels.NextArmyId(state));
        state = state with { Armies = ValueList.From(state.Armies.Append(source with { Id = "army-2" })) };
        Assert.Equal("army-4", ArmyDialogModels.NextArmyId(state));
    }

    [Fact]
    public void Quick_split_resource_proposal_is_editable_and_only_commits_on_OK()
    {
        var session = Session();
        var source = session.State.ArmyById(ThreeUnitArmyId)!;
        var model = SplitArmyModel.ForArmy(session.State, source, session.Ruleset, ThreeUnitNewArmyId);
        Assert.True(model.SuggestBalancedSplit());
        Assert.True(model.Money > 0);
        Assert.True(model.Supply > 0);
        var proposedMoney = model.Money;
        model.AdjustMoney(-1);
        Assert.Equal(proposedMoney - 1, model.Money);
        Assert.Null(model.Cancel());
        Assert.Equal(source, session.State.ArmyById(source.Id));
        Assert.Null(session.State.ArmyById(ThreeUnitNewArmyId));
        session.Submit(model.ComposeOk()!);
        var parent = session.State.ArmyById(source.Id)!;
        var child = session.State.ArmyById(ThreeUnitNewArmyId)!;
        Assert.Equal(model.Money, child.Money);
        Assert.Equal(source.Money, parent.Money + child.Money);
        Assert.Equal(source.SupplyTons, parent.SupplyTons + child.SupplyTons);
        Assert.Equal(source.TotalTroops, parent.TotalTroops + child.TotalTroops);
    }

    [Theory]
    [InlineData(100, 100, 300, 200, 200)]
    [InlineData(100, 100, 30, 130, 0)]
    [InlineData(100, 990, 300, 1000, 290)]
    [InlineData(-50, 100, 300, 50, 350)]
    public void Supply_money_from_selected_army_uses_its_purse_and_leaves_treasury(
        int amount, int receiver, int donor, int expectedReceiver, int expectedDonor)
    {
        var session = Session(aMoney: receiver, bMoney: donor);
        var before = session.State;
        var model = SupplyDialogModel.ForArmy(before, before.ArmyById(ArmyAId)!, session.Ruleset);
        var index = model.Providers.ToList().FindIndex(p => p.Id == ArmyBId);
        Assert.True(index >= 0);
        model.SelectProvider(index);
        var command = model.PressSelectedMoney(amount);
        Assert.NotNull(command);
        session.Submit(command!);
        Assert.Equal(expectedReceiver, session.State.ArmyById(ArmyAId)!.Money);
        Assert.Equal(expectedDonor, session.State.ArmyById(ArmyBId)!.Money);
        Assert.Equal(before.Nations, session.State.Nations);
    }

    [Fact]
    public void Transfer_displays_pending_money_and_supply_before_confirmation()
    {
        var session = Session();
        var before = session.State;
        var model = ArmyTransferModel.ForArmies(before.ArmyById(ArmyAId)!, before.ArmyById(ArmyBId)!,
            session.Ruleset, before, session.World);
        model.AdjustMoney(50);
        model.AdjustSupply(10);
        Assert.Equal("50 talents → right", model.PendingMoneyText);
        Assert.Equal("10 t → right", model.PendingSupplyText);
        Assert.Equal(150, model.SelectedMoneyAfter);
        Assert.Equal(150, model.PartnerMoneyAfter);
        Assert.Equal(before, session.State);
        Assert.Null(model.Cancel());
        session.Submit(model.ComposeOk()!);
        Assert.Equal(model.SelectedMoneyAfter, session.State.ArmyById(ArmyAId)!.Money);
        Assert.Equal(model.PartnerMoneyAfter, session.State.ArmyById(ArmyBId)!.Money);
        Assert.Equal(model.SelectedSupplyAfter, session.State.ArmyById(ArmyAId)!.SupplyTons);
        Assert.Equal(model.PartnerSupplyAfter, session.State.ArmyById(ArmyBId)!.SupplyTons);
    }

    [Fact]
    public void Quick_split_unequal_troops_propose_proportional_money_without_a_binding_cap()
    {
        var session = Session();
        var source = session.State.ArmyById(ThreeUnitArmyId)! with
        {
            Money = 601, SupplyTons = 30,
            Units = ValueList.Of(new UnitSlot(0, "light_infantry", 1_000, 6, "Small"),
                new UnitSlot(0, "light_infantry", 2_000, 6, "Large")),
        };
        var ruleset = session.Ruleset with
        {
            ArmyManagement = session.Ruleset.ArmyManagement with { NewArmyMorale = source.Morale },
        };
        var model = SplitArmyModel.ForArmy(session.State, source, ruleset, NewArmyId);
        Assert.True(model.SuggestBalancedSplit());
        Assert.Equal(new[] { 0 }, model.StagedUnits);
        Assert.Equal(200, model.Money);
        Assert.Equal(10, model.Supply);
        Assert.True(model.MaxMoney > model.Money);
        Assert.NotEqual(source.Money / 2, model.Money);
    }

    [Fact]
    public void Quick_split_equal_rounded_strength_prefers_closer_troop_totals_before_slot_order()
    {
        var session = Session();
        // All subsets round to zero power under the real LI weight and divisor.
        // [0] is earlier but leaves 100 vs 250; [0,2] leaves 200 vs 150.
        var source = session.State.ArmyById(ThreeUnitArmyId)! with
        {
            Units = ValueList.Of(new UnitSlot(0, "light_infantry", 100, 6, "First"),
                new UnitSlot(0, "light_infantry", 150, 6, "Middle"),
                new UnitSlot(0, "light_infantry", 100, 6, "Last")),
        };
        var model = SplitArmyModel.ForArmy(session.State, source, session.Ruleset, NewArmyId);
        Assert.True(model.SuggestBalancedSplit());
        Assert.Equal(0, model.ParentPower);
        Assert.Equal(0, model.NewArmyPower);
        Assert.Equal(new[] { 0, 2 }, model.StagedUnits);
    }

    [Theory]
    [InlineData(50)]
    [InlineData(70)]
    [InlineData(90)]
    public void Quick_split_matches_independent_strength_oracle_and_keeps_resources_unstaged(int morale)
    {
        var session = Session();
        var source = session.State.ArmyById(ThreeUnitArmyId)!;
        var army = source with { Morale = morale };
        var model = SplitArmyModel.ForArmy(session.State, army, session.Ruleset, ThreeUnitNewArmyId);
        var candidates = Enumerable.Range(1, (1 << army.Units.Count) - 2).Select(mask =>
        {
            var moved = Enumerable.Range(0, army.Units.Count).Where(i => (mask & (1 << i)) != 0).ToArray();
            var kept = Enumerable.Range(0, army.Units.Count).Where(i => (mask & (1 << i)) == 0).ToArray();
            var parentPower = ArmyPower.Compute(kept.Select(i => army.Units[i]), army.Morale, session.Ruleset);
            var childPower = ArmyPower.Compute(moved.Select(i => army.Units[i]),
                session.Ruleset.ArmyManagement.NewArmyMorale, session.Ruleset);
            var troopDifference = Math.Abs(kept.Sum(i => army.Units[i].Troops) - moved.Sum(i => army.Units[i].Troops));
            return new { moved, parentPower, childPower, troopDifference };
        }).OrderBy(x => Math.Abs((long)x.parentPower - x.childPower))
          .ThenBy(x => x.troopDifference)
          .ThenBy(x => string.Join(',', x.moved.Select(i => i.ToString("D2"))), StringComparer.Ordinal).First();

        Assert.True(model.SuggestBalancedSplit());
        Assert.Equal(candidates.moved, model.StagedUnits);
        Assert.Equal(candidates.parentPower, model.ParentPower);
        Assert.Equal(candidates.childPower, model.NewArmyPower);
        var movedTroops = model.StagedUnits.Sum(i => source.Units[i].Troops);
        Assert.Equal(Math.Min(model.MaxMoney, (int)((long)source.Money * movedTroops / source.TotalTroops)), model.Money);
        Assert.Equal(Math.Min(model.MaxSupply, (int)((long)source.SupplyTons * movedTroops / source.TotalTroops)), model.Supply);
        Assert.Equal(source, session.State.ArmyById(ThreeUnitArmyId));
        Assert.True(model.SuggestBalancedSplit());
        Assert.Equal(candidates.moved, model.StagedUnits);
    }

    [Fact]
    public void Quick_split_refuses_one_unit_and_handles_two_and_twenty_whole_units()
    {
        var session = Session();
        var lonely = session.State.ArmyById(OneUnitArmyId)!;
        Assert.False(SplitArmyModel.ForArmy(session.State, lonely, session.Ruleset, NewArmyId).SuggestBalancedSplit());

        var two = session.State.ArmyById(ThreeUnitArmyId)! with
        {
            Units = ValueList.From(session.State.ArmyById(ThreeUnitArmyId)!.Units.Take(2)),
        };
        var twoModel = SplitArmyModel.ForArmy(session.State, two, session.Ruleset, NewArmyId);
        Assert.True(twoModel.SuggestBalancedSplit());
        Assert.Single(twoModel.StagedUnits);

        var twenty = two with { Units = ValueList.From(Enumerable.Range(0, 20)
            .Select(i => new UnitSlot(0, i % 2 == 0 ? "heavy_infantry" : "archers",
                1_000 + i * 300, 6, $"Unit {i}"))) };
        var fullModel = SplitArmyModel.ForArmy(session.State, twenty, session.Ruleset, NewArmyId);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Assert.True(fullModel.SuggestBalancedSplit());
        clock.Stop();
        Assert.InRange(fullModel.StagedUnits.Count, 1, 19);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"20-unit split took {clock.Elapsed}.");
    }

    [Fact]
    public void Quick_split_equal_power_and_troops_choose_earliest_source_slots()
    {
        var session = Session();
        var source = session.State.ArmyById(ThreeUnitArmyId)!;
        var army = source with { Units = ValueList.From(Enumerable.Range(0, 4)
            .Select(i => new UnitSlot(0, "heavy_infantry", 3_000, 6, $"Guard {i}"))) };
        var ruleset = session.Ruleset with
        {
            ArmyManagement = session.Ruleset.ArmyManagement with { NewArmyMorale = army.Morale },
        };
        var model = SplitArmyModel.ForArmy(session.State, army, ruleset, ThreeUnitNewArmyId);

        Assert.True(model.SuggestBalancedSplit());
        Assert.Equal(new[] { 0, 1 }, model.StagedUnits);
        Assert.Equal(model.ParentPower, model.NewArmyPower);
    }

    private static readonly Lazy<ResolvedScenario> LazyClassical = new(
        () => GameDataRepository.Load(ModelTestPaths.DataRoot).Resolve("classical-mediterranean"));

    private static ResolvedScenario Classical => LazyClassical.Value;

    [Theory]
    [InlineData(4100, "light_infantry", null, null, "4,100 Light infantry")]
    [InlineData(2000, "heavy_infantry", 5, null, "2,000 Heavy infantry (poor)")]
    [InlineData(950, "archers", 6, "1st Archers", "950 Archers (average) — 1st Archers")]
    [InlineData(10, "war_elephants", null, null, "10 war_elephants")]
    public void A_unit_line_uses_the_rulesets_own_type_name(int troops, string type, int? quality, string? name, string expected)
    {
        // #1288 (F5 of #1280): "4100x light_infantry" is internal text; the player reads the ruleset's name.
        Assert.Equal(expected, ArmyDialogModels.UnitLine(Classical.Ruleset, troops, type, quality, name));
    }

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
            "army-transfer t111-a t111-b unit-steps=a0:b,b0:a supply=10 money=70",
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
    public void Transfer_confirmation_composes_an_order_even_without_edits_but_cancel_submits_nothing()
    {
        var session = Session();
        var model = Transfer(session);

        Assert.Equal($"army-transfer {ArmyAId} {ArmyBId}", model.ComposeOk());
        Assert.Null(model.Cancel());
        Assert.Equal($"army-transfer {ArmyAId} {ArmyBId}", model.ComposeOk());
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

    /// <summary>#802: the 198 cap counts every army in play, not only the splitting nation's.</summary>
    [Fact]
    public void Split_army_cap_counts_every_army_in_play()
    {
        var session = Session();
        var army = session.State.ArmyById(ThreeUnitArmyId)!;
        var cap = session.Ruleset.ArmyManagement.MaxArmies;
        var foreign = session.State.Armies.First(a => !string.Equals(a.Nation, army.Nation, StringComparison.Ordinal));
        var filler = Enumerable.Range(0, cap - session.State.Armies.Count).Select(i => foreign with { Id = $"filler-{i}" });
        var full = session.State with { Armies = ValueList.From(session.State.Armies.Concat(filler)) };

        var model = SplitArmyModel.ForArmy(full, army, session.Ruleset, ThreeUnitNewArmyId);

        Assert.Equal(cap, full.Armies.Count);
        Assert.False(model.CanSplit);
        Assert.Equal("You have reached your limit of armies.", model.RefusalMessage);
        Assert.False(model.SuggestBalancedSplit());
        Assert.Empty(model.StagedUnits);
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
    [InlineData(4, "very poor")]
    [InlineData(3, "not ready")]
    [InlineData(0, "not ready")]
    [InlineData(10, "10")]
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
