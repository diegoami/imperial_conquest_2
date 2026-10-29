using IC2.Data;
using IC2.Engine.Economy;
using IC2.Engine.Import;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Import;
using IC2.Slice.UI;
using Xunit;

namespace IC2.Engine.Tests.Ui;

/// <summary>
/// Issue #1549 (T231 follow-up, notes N1, N2 and N4 of the v0.8.3 candidate-2 review).
/// </summary>
/// <remarks>
/// <para>
/// <strong>N1 is characterised, not corrected.</strong> The tests named <c>N1_Characterisation_*</c> pin
/// today's behaviour of an emptied-army merge whose summed purse exceeds the 32,767 field, and of what
/// later happens to that army. They are not a policy: the owner has not chosen one, and when a policy is
/// chosen these tests are expected to change with it.
/// </para>
/// <para>
/// <strong>N2</strong>: with the purse cap at the field maximum, the flexible arrow and the original
/// signed arrow differ only when the receiver already holds more than 32,767, which N1 makes reachable.
/// These tests pin T231's no-reversal floor there, in both directions and for Split.
/// </para>
/// <para>
/// <strong>N4</strong>: a Supply-dialog money arrow against another army that moves nothing (empty giver,
/// full receiver) must submit nothing, so the dialog shows its no-money feedback instead of running an
/// unchanged <c>army-transfer</c> and its supply rebalance.
/// </para>
/// </remarks>
public sealed partial class ArmyDialogModelsTests
{
    private const string OverFieldPartnerId = "fix1549-c";

    /// <summary>
    /// N1's reachable state: A and B at 30,000 each, every A unit transferred to B, OK. B ends at 60,000
    /// and A is disbanded. Returns the session after that commit.
    /// </summary>
    private static GameSession MergedAboveTheField(bool intoPartner = true)
    {
        var session = Session(aMoney: 30_000, bMoney: 30_000);
        var model = Transfer(session);
        if (intoPartner)
        {
            model.StageUnitToPartner(1);
            model.StageUnitToPartner(0);
        }
        else
        {
            model.StageUnitBack(0);
        }

        var output = session.Submit(model.ComposeOk()!);
        Assert.DoesNotContain(output.Lines, l => l.Contains("rejected", StringComparison.OrdinalIgnoreCase));
        return session;
    }

    /// <summary>The merged session plus a one-unit army C (100 talents) on A's freed cell, next to B.</summary>
    private static GameSession WithPartnerNextToOverFieldArmy(GameSession merged)
    {
        var b = merged.State.ArmyById(ArmyBId)!;
        var c = b with
        {
            Id = OverFieldPartnerId, X = 100, Y = 100, Money = 100, SupplyTons = 0,
            Units = ValueList.Of(new UnitSlot(0, "light_infantry", 1_000, 6, "Fix1549 C")),
        };
        var state = merged.State with { Armies = ValueList.From(merged.State.Armies.Append(c)) };
        var save = new SaveGame(state.SchemaVersion, "fix1549", "fix1549", state.ScenarioId, state.WorldId,
            state.RulesetId, state);
        return new GameSession(merged.World, merged.Ruleset, merged.Scenario, save);
    }

    // ---- N1: characterisation of today's behaviour (no policy asserted) ----

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void N1_Characterisation_emptied_army_merge_previews_and_commits_a_purse_above_the_field(bool intoPartner)
    {
        var session = Session(aMoney: 30_000, bMoney: 30_000);
        var before = session.State;
        var model = Transfer(session);
        if (intoPartner)
        {
            model.StageUnitToPartner(1);
            model.StageUnitToPartner(0);
        }
        else
        {
            model.StageUnitBack(0);
        }

        // Today: no preview rejection, and the preview predicts the raw sum.
        Assert.Null(model.PreviewRejection);
        Assert.Equal(intoPartner ? 0 : 60_000, model.SelectedMoneyAfter);
        Assert.Equal(intoPartner ? 60_000 : 0, model.PartnerMoneyAfter);
        Assert.Equal(before, session.State);

        var output = session.Submit(model.ComposeOk()!);
        Assert.DoesNotContain(output.Lines, l => l.Contains("rejected", StringComparison.OrdinalIgnoreCase));
        var survivor = session.State.ArmyById(intoPartner ? ArmyBId : ArmyAId)!;
        Assert.Null(session.State.ArmyById(intoPartner ? ArmyAId : ArmyBId));
        Assert.Equal(60_000, survivor.Money);
        Assert.True(survivor.Money > PurseAccounting.PurseFieldMax);
        Assert.Equal(before.Nations, session.State.Nations);
    }

    [Fact]
    public void N1_Characterisation_join_armies_clamps_the_same_sum_at_the_field_and_discards_the_excess()
    {
        // The comparison the note asks for: Join armies routes the same sum through PurseAccounting.Credit.
        var session = Session(aMoney: 30_000, bMoney: 30_000);
        var output = session.Submit($"join-armies {ArmyBId} {ArmyAId}");
        Assert.DoesNotContain(output.Lines, l => l.Contains("rejected", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(PurseAccounting.PurseFieldMax, session.State.ArmyById(ArmyBId)!.Money);
        Assert.Null(session.State.ArmyById(ArmyAId));
    }

    [Fact]
    public void N1_Characterisation_original_save_export_refuses_the_merged_purse_as_unrepresentable()
    {
        var session = MergedAboveTheField();
        var index = session.State.Armies.ToList().FindIndex(a => a.Id == ArmyBId);

        var ex = Assert.Throws<OriginalSaveExportException>(() =>
            OriginalSaveExporter.ToDocument(session, OriginalSaveExportTests.Ui));
        var refusal = Assert.Single(ex.Refusals);
        Assert.Equal(OriginalSaveExportRefusal.Unrepresentable, refusal.Code);
        Assert.Equal($"army[{index}].money", refusal.Field);

        // Control: the same state with the purse at the field maximum exports, so the money field alone
        // is what refuses (refused, not clamped or wrapped).
        var b = session.State.ArmyById(ArmyBId)!;
        var atField = session.State with
        {
            Armies = session.State.Armies.Replace(index, b with { Money = PurseAccounting.PurseFieldMax }),
        };
        var save = new SaveGame(atField.SchemaVersion, "fix1549", "fix1549", atField.ScenarioId, atField.WorldId,
            atField.RulesetId, atField);
        var document = OriginalSaveExporter.ToDocument(save, session.World, session.Ruleset, OriginalSaveExportTests.Ui).Document;
        Assert.Equal(PurseAccounting.PurseFieldMax, document.Armies[index].Money);
    }

    [Fact]
    public void N1_Characterisation_the_native_save_round_trips_the_merged_purse_unchanged()
    {
        var session = MergedAboveTheField();
        var save = new SaveGame(GameDataSchema.CurrentVersion, "fix1549", "fix1549", session.State.ScenarioId,
            session.State.WorldId, session.State.RulesetId, session.State);
        var loaded = GameDataLoader.Load<SaveGame>("fix1549.json", GameJson.Serialize(save));
        var reopened = new GameSession(session.World, session.Ruleset, session.Scenario, loaded);
        Assert.Equal(60_000, reopened.State.ArmyById(ArmyBId)!.Money);
    }

    [Fact]
    public void N1_Characterisation_a_later_join_credits_through_the_field_clamp_and_loses_the_excess()
    {
        var session = WithPartnerNextToOverFieldArmy(MergedAboveTheField());
        var treasury = session.State.NationById(NationId)!.Treasury;

        var output = session.Submit($"join-armies {ArmyBId} {OverFieldPartnerId}");

        Assert.DoesNotContain(output.Lines, l => l.Contains("rejected", StringComparison.OrdinalIgnoreCase));
        // 60,000 + 100 lands at 32,767: 27,333 talents leave the game, none reach the treasury.
        Assert.Equal(PurseAccounting.PurseFieldMax, session.State.ArmyById(ArmyBId)!.Money);
        Assert.Null(session.State.ArmyById(OverFieldPartnerId));
        Assert.Equal(treasury, session.State.NationById(NationId)!.Treasury);
    }

    [Theory]
    [InlineData(-100, 27_233)]
    [InlineData(100, 59_000)]
    public void N1_Characterisation_a_later_treasury_money_arrow_pulls_the_purse_into_range(int request, int treasuryGain)
    {
        // Faithful per-unit purses: a withdrawal of 100 lands at the field through PurseAccounting.Credit, and
        // a deposit meets the signed 1,000 arrow cap; either way the treasury receives the difference.
        var session = MergedAboveTheField();
        var treasury = session.State.NationById(NationId)!.Treasury;

        session.Submit($"transfer-money {ArmyBId} {request}");

        var purse = session.State.ArmyById(ArmyBId)!.Money;
        Assert.Equal(60_000 - treasuryGain, purse);
        Assert.Equal(treasury + treasuryGain, session.State.NationById(NationId)!.Treasury);
    }

    // ---- N2: T231's no-reversal floor, reachable only above the field ----

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void N2_Transfer_arrow_toward_a_receiver_above_the_field_moves_nothing_and_never_reverses(bool overFieldSelected)
    {
        var session = WithPartnerNextToOverFieldArmy(MergedAboveTheField());
        var before = session.State;
        var b = before.ArmyById(ArmyBId)!;
        var c = before.ArmyById(OverFieldPartnerId)!;
        var model = overFieldSelected
            ? ArmyTransferModel.ForArmies(b, c, session.Ruleset, before, session.World)
            : ArmyTransferModel.ForArmies(c, b, session.Ruleset, before, session.World);
        var towardOverField = overFieldSelected ? -100 : 100;

        model.AdjustMoney(towardOverField);
        model.AdjustMoney(towardOverField);

        Assert.Equal(0, model.MoneyNet);
        Assert.Null(model.PreviewRejection);
        Assert.Equal(overFieldSelected ? 60_000 : 100, model.SelectedMoneyAfter);
        Assert.Equal(overFieldSelected ? 100 : 60_000, model.PartnerMoneyAfter);
        var output = session.Submit(model.ComposeOk()!);
        Assert.DoesNotContain(output.Lines, l => l.Contains("rejected", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(60_000, session.State.ArmyById(ArmyBId)!.Money);
        Assert.Equal(100, session.State.ArmyById(OverFieldPartnerId)!.Money);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void N2_Transfer_arrow_away_from_a_purse_above_the_field_still_moves_the_requested_amount(bool overFieldSelected)
    {
        var session = WithPartnerNextToOverFieldArmy(MergedAboveTheField());
        var before = session.State;
        var b = before.ArmyById(ArmyBId)!;
        var c = before.ArmyById(OverFieldPartnerId)!;
        var model = overFieldSelected
            ? ArmyTransferModel.ForArmies(b, c, session.Ruleset, before, session.World)
            : ArmyTransferModel.ForArmies(c, b, session.Ruleset, before, session.World);

        model.AdjustMoney(overFieldSelected ? 100 : -100);

        Assert.Equal(overFieldSelected ? 100 : -100, model.MoneyNet);
        session.Submit(model.ComposeOk()!);
        Assert.Equal(59_900, session.State.ArmyById(ArmyBId)!.Money);
        Assert.Equal(200, session.State.ArmyById(OverFieldPartnerId)!.Money);
    }

    [Fact]
    public void N2_Split_money_pullback_into_a_parent_above_the_field_moves_nothing_and_never_reverses()
    {
        var session = MergedAboveTheField();
        var before = session.State;
        var parent = before.ArmyById(ArmyBId)!;
        var model = SplitArmyModel.ForArmy(before, parent, session.Ruleset, NewArmyId);

        model.AdjustMoney(100);
        Assert.Equal(100, model.Money);
        model.AdjustMoney(-100);
        model.AdjustMoney(-100);

        Assert.Equal(100, model.Money);
        model.StageUnit(0);
        var output = session.Submit(model.ComposeOk()!);
        Assert.DoesNotContain(output.Lines, l => l.Contains("rejected", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(59_900, session.State.ArmyById(ArmyBId)!.Money);
        Assert.Equal(100, session.State.ArmyById(NewArmyId)!.Money);
    }

    // ---- N4: Supply-dialog money arrow against another army that moves nothing ----

    /// <summary>
    /// Presses a money arrow exactly as <c>SupplyDialog.PressMoney</c> does: a null line shows the
    /// no-money feedback and submits nothing; any line is submitted to the production session.
    /// </summary>
    private static (int Commands, string? Feedback) PressSupplyMoney(GameSession session, string armyId, string sourceId, int amount)
    {
        var model = SupplyDialogModel.ForArmy(session.State, session.State.ArmyById(armyId)!, session.Ruleset);
        var index = model.MoneyViaChoices.ToList().FindIndex(choice => choice.ArmyId == sourceId);
        Assert.True(index >= 0, $"{sourceId} is not offered as a money source.");
        model.SelectMoneySource(index);
        var line = model.PressSelectedMoney(amount);
        if (line is null)
        {
            return (0, "No money moved: source empty or receiving purse full.");
        }

        session.Submit(line);
        return (1, null);
    }

    [Theory]
    [InlineData(200, 0, 100)] // empty giver: B has nothing to send A
    [InlineData(0, 100, -100)] // empty giver: A has nothing to send B
    [InlineData(32_767, 100, 100)] // full receiver: A at the field
    [InlineData(100, 32_767, -100)] // full receiver: B at the field
    public void N4_Supply_army_money_arrow_that_moves_nothing_submits_nothing_and_changes_no_stock(
        int aMoney, int bMoney, int amount)
    {
        // A holds 200 t for an 80 t army, so an unchanged army-transfer would rebalance supply.
        var session = Session(aSupply: 200, bSupply: 10, aMoney: aMoney, bMoney: bMoney);
        var before = session.State;

        var (commands, feedback) = PressSupplyMoney(session, ArmyAId, ArmyBId, amount);

        Assert.Equal(0, commands);
        Assert.Equal("No money moved: source empty or receiving purse full.", feedback);
        Assert.Equal(before, session.State);
        Assert.Equal(200, session.State.ArmyById(ArmyAId)!.SupplyTons);
        Assert.Equal(10, session.State.ArmyById(ArmyBId)!.SupplyTons);
        Assert.Equal(aMoney, session.State.ArmyById(ArmyAId)!.Money);
        Assert.Equal(bMoney, session.State.ArmyById(ArmyBId)!.Money);
        Assert.Equal(before.RandomSeed, session.State.RandomSeed);
    }

    [Fact]
    public void N4_Supply_army_money_arrow_that_moves_money_still_submits_one_army_transfer()
    {
        var session = Session(aSupply: 200, bSupply: 10, aMoney: 200, bMoney: 300);
        var before = session.State;

        var (commands, feedback) = PressSupplyMoney(session, ArmyAId, ArmyBId, 100);

        Assert.Equal(1, commands);
        Assert.Null(feedback);
        Assert.Equal(300, session.State.ArmyById(ArmyAId)!.Money);
        Assert.Equal(200, session.State.ArmyById(ArmyBId)!.Money);
        // The submitted army-transfer runs OK's rebalance (200/10 → 170/40) and advances the session's
        // random seed: the cost the no-movement arrow above must not pay.
        Assert.Equal(170, session.State.ArmyById(ArmyAId)!.SupplyTons);
        Assert.Equal(40, session.State.ArmyById(ArmyBId)!.SupplyTons);
        Assert.NotEqual(before.RandomSeed, session.State.RandomSeed);
    }
}
