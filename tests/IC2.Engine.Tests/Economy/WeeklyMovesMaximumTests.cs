using IC2.Engine.Core;
using IC2.Engine.Economy;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Economy;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T45 Pin the weekly moves maximum with the tests it never got".
/// The weekly moves budget is <em>already</em> implemented and wired into <see cref="TurnPhase.ArmyTick"/>
/// by already-merged T08 code (<see cref="SupplyMoraleRule.BaseMoves"/>, <see cref="SupplyMoraleRule.ApplyToMorale"/>,
/// <see cref="ArmySupplyAndMoraleSystem"/>). This task adds no production code, no ruleset field and no
/// system — it is the boundary coverage that implementation was merged without: the five troop-size steps
/// at their edges, the starving threshold's exact expression, and the mid-week non-refresh clause the
/// corpus already caught once by accident.
/// </summary>
/// <remarks>
/// The formula is confirmed twice, independently: at instruction level in
/// <see href="https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/army-moves-field-signed-and-the-ffff-underflow.md">
/// army-moves-field-signed-and-the-ffff-underflow.md</see> §4 (<c>0x00451633</c>–<c>0x004516D7</c>,
/// <c>FUN_004514EC</c>, re-derived independently by <c>FUN_0044AAB4</c>), and separately by
/// <c>investigations/thracia-supply-morale.md</c>, which is what T08 actually shipped from. The two
/// derivations agree, which is the strongest confirmation this project has for the rule — this task cites
/// both rather than re-deriving either.
/// </remarks>
public sealed class WeeklyMovesMaximumTests
{
    // ---------------------------------------------------------------------------------------------
    // Done-when 1: the five size steps, at their boundaries. Every expected value below is derived
    // from the ruleset's own BaseMovesMax/MovesReductionCap/MovesTroopDivisor fields, never a literal
    // 10, 5 or 20000 -- only the small, structural "one step further" offsets are literals.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void BaseMoves_OneTroopBelowTheFirstStep_NoReductionYet()
    {
        var ruleset = EconomyTestbed.Ruleset;
        var rules = ruleset.Economy.SupplyMorale;
        var troops = rules.MovesTroopDivisor - 1; // 19,999: integer division still truncates to 0 steps.

        Assert.Equal(rules.BaseMovesMax, SupplyMoraleRule.BaseMoves(troops, ruleset));
    }

    [Fact]
    public void BaseMoves_AtTheFirstStep_ReducesByOne()
    {
        var ruleset = EconomyTestbed.Ruleset;
        var rules = ruleset.Economy.SupplyMorale;
        var troops = rules.MovesTroopDivisor; // 20,000: the first 20,000-troop step engages.

        Assert.Equal(rules.BaseMovesMax - 1, SupplyMoraleRule.BaseMoves(troops, ruleset));
    }

    [Fact]
    public void BaseMoves_OneTroopBelowTheSecondStep_StaysAtTheFirstStepsReduction()
    {
        var ruleset = EconomyTestbed.Ruleset;
        var rules = ruleset.Economy.SupplyMorale;
        var troops = (2 * rules.MovesTroopDivisor) - 1; // 39,999: still only 1 step (integer division).

        Assert.Equal(rules.BaseMovesMax - 1, SupplyMoraleRule.BaseMoves(troops, ruleset));
    }

    [Fact]
    public void BaseMoves_AtTheSecondStep_ReducesByTwo()
    {
        var ruleset = EconomyTestbed.Ruleset;
        var rules = ruleset.Economy.SupplyMorale;
        var troops = 2 * rules.MovesTroopDivisor; // 40,000: the second step engages.

        Assert.Equal(rules.BaseMovesMax - 2, SupplyMoraleRule.BaseMoves(troops, ruleset));
    }

    [Fact]
    public void BaseMoves_AtAndWellBeyondTheFloor_PinsAtTheReductionCap()
    {
        var ruleset = EconomyTestbed.Ruleset;
        var rules = ruleset.Economy.SupplyMorale;
        var atFloor = rules.MovesReductionCap * rules.MovesTroopDivisor; // 100,000: exactly five steps.
        var wellBeyond = (rules.MovesReductionCap + 3) * rules.MovesTroopDivisor; // a much larger army.

        var floored = rules.BaseMovesMax - rules.MovesReductionCap;
        Assert.Equal(floored, SupplyMoraleRule.BaseMoves(atFloor, ruleset));
        Assert.Equal(floored, SupplyMoraleRule.BaseMoves(wellBeyond, ruleset)); // min(5, ...) never goes lower.
    }

    // ---------------------------------------------------------------------------------------------
    // Done-when 2: the starving case, either side of its threshold, proved to be the tick's own
    // `supplies * 10000 / troops` expression -- not FUN_0044AAB4's `supplies * troops / 10000`, which
    // the report's controlled-save recipe B chose numbers specifically to disagree with.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void StarvingPenalty_JustBelowThePercentThreshold_Fires()
    {
        var ruleset = EconomyTestbed.Ruleset;
        var rules = ruleset.Economy.SupplyMorale;
        const int troops = 50_000;
        const int supplyTons = 10; // 10 * 10000 / 50000 = 2, one below the 10% threshold.

        var pct = SupplyCapacity.PercentFull(supplyTons, troops, ruleset);
        var (_, penalty) = SupplyMoraleRule.ApplyToMorale(currentMorale: 60, pct, ruleset);

        Assert.True(pct < rules.DecayThresholdPercent);
        Assert.Equal(rules.MovesPenaltyOnDecay, penalty);
    }

    [Fact]
    public void StarvingPenalty_JustAtThePercentThreshold_DoesNotFire()
    {
        var ruleset = EconomyTestbed.Ruleset;
        var rules = ruleset.Economy.SupplyMorale;
        const int troops = 50_000;
        const int supplyTons = 50; // 50 * 10000 / 50000 = 10, exactly the threshold -- not below it.

        var pct = SupplyCapacity.PercentFull(supplyTons, troops, ruleset);
        var (_, penalty) = SupplyMoraleRule.ApplyToMorale(currentMorale: 60, pct, ruleset);

        Assert.Equal(rules.DecayThresholdPercent, pct);
        Assert.Equal(0, penalty);
    }

    /// <summary>
    /// The report's controlled-save recipe B (<c>army-moves-field-signed-and-the-ffff-underflow.md</c>,
    /// "Controlled-save recipes" B): a 50,000-troop army on 10 tons is chosen precisely because the
    /// tick's expression and <c>FUN_0044AAB4</c>'s disagree loudly -- 2% versus 50%. This pins down which
    /// one the engine actually implements, rather than trusting that the two agree everywhere (they only
    /// agree near 10,000 troops).
    /// </summary>
    [Fact]
    public void StarvingPenalty_UsesTheTicksExpression_NotTheEndTurnHelpersVariant()
    {
        var ruleset = EconomyTestbed.Ruleset;
        var numerator = ruleset.Economy.SupplyPercentNumerator;
        const int troops = 50_000;
        const int supplyTons = 10;

        var ticksExpression = supplyTons * numerator / troops; // FUN_004514EC: 2.
        var endTurnHelpersExpression = supplyTons * troops / numerator; // FUN_0044AAB4: 50.
        Assert.NotEqual(ticksExpression, endTurnHelpersExpression); // the two really do disagree here.

        var pct = SupplyCapacity.PercentFull(supplyTons, troops, ruleset);

        Assert.Equal(ticksExpression, pct);
        Assert.NotEqual(endTurnHelpersExpression, pct);
    }

    /// <summary>
    /// T39's folded follow-up (docs/task-catalogue.md Done-when 10): the three <c>StarvingPenalty_*</c>
    /// tests above all call <see cref="SupplyMoraleRule.ApplyToMorale"/> directly and pin the penalty's
    /// *value* -- but none of them ever calls <see cref="SupplyMoraleRule.ApplyTurn"/> and checks that
    /// its returned <c>Moves</c> actually reflects the subtraction. Deleting <c>baseMoves - movesPenalty</c>
    /// from <c>ApplyTurn</c> (leaving <c>Moves = baseMoves</c> always) would leave all nine of this file's
    /// other tests green. This one closes that gap by going through <c>ApplyTurn</c> itself.
    /// </summary>
    [Fact]
    public void StarvingPenalty_AppliedThroughApplyTurn_ActuallySubtractsFromBaseMoves()
    {
        var ruleset = EconomyTestbed.Ruleset;
        const int troops = 50_000;
        const int supplyTons = 10; // consumption alone (well over 10 in Spring) drives this to 0 -- starving.

        var outcome = SupplyMoraleRule.ApplyTurn(
            troops: troops,
            currentSupplyTons: supplyTons,
            currentMorale: 60,
            isEmbarked: false,
            seasonIndex: 0, // Spring.
            ruleset: ruleset);

        var baseMoves = SupplyMoraleRule.BaseMoves(troops, ruleset);
        Assert.True(outcome.SupplyPercent < ruleset.Economy.SupplyMorale.DecayThresholdPercent); // confirms the branch.
        Assert.Equal(baseMoves - ruleset.Economy.SupplyMorale.MovesPenaltyOnDecay, outcome.Moves);
        Assert.NotEqual(baseMoves, outcome.Moves); // the subtraction actually happened, not just the value.
    }

    // ---------------------------------------------------------------------------------------------
    // Done-when 3: the maximum is not refreshed when an army's size changes mid-week. Confirmed
    // against the corpus, not designed: Rome's army at (100, 42) in 12_mac.sav kept moves 8 -- right
    // for its pre-transfer 48,173 troops -- after growing to 63,173 mid-week. Reproduced here with the
    // real ArmySupplyAndMoraleSystem, registered in the real TurnPhase.ArmyTick, run through the real
    // TurnCoordinator -- narrowed to just that one system, the same pattern ThraciaSupplyMoraleReplayTests
    // uses, so this test is not incidentally exercising quarterly billing or weather.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void MidWeekGrowth_MovesUnchangedUntilNextTick_ThenRecomputedForTheNewTroopTotal()
    {
        var coordinator = EconomyTestbed.CoordinatorOnly(sink: null, typeof(ArmySupplyAndMoraleSystem));
        var ruleset = EconomyTestbed.Ruleset;

        const string armyId = "mid-week-growth-army";
        const int preTransferTroops = 48_173; // the corpus record's own pre-transfer troop total.
        const int postTransferTroops = 63_173; // ...and its post-transfer total, same record.

        var state = EconomyTestbed.InitialState();
        var nationId = state.Nations[0].Id;

        // Supply is set comfortably above any plausible capacity so this test's own concern -- whether
        // Moves recomputes, and when -- stays isolated from the consumption formula's exact ratios; the
        // starving branch is deliberately kept out of the way here (SupplyConsumptionTests and
        // SupplyMoraleClampTests already cover it on their own). Deliberately *not* 10,000,000: T39's
        // folded follow-up (docs/task-catalogue.md Done-when 10) found that value ran
        // SupplyCapacity.PercentFull's `supplyTons * SupplyPercentNumerator` through a signed 32-bit
        // overflow (9,999,904 x 10,000 wraps to 1,214,792,192) that happened to land in the same regen
        // branch as the correct, unwrapped result by luck -- roughly 93,300 tons higher would have wrapped
        // negative instead and failed this test with a baffling message, for a reason with nothing to do
        // with the behaviour under test. 100,000 stays far above the pre-transfer army's capacity (482
        // tons) with no risk of the product overflowing (100,000 x 10,000 = 1,000,000,000, comfortably
        // inside int range).
        var army = new ArmyState(
            Id: armyId,
            Nation: nationId,
            X: 3,
            Y: 2,
            Moves: 0,
            Morale: 65,
            Money: 0,
            SupplyTons: 100_000,
            CoveredTileCode: null,
            AboardFleetId: null,
            Units: ValueList.Of(new UnitSlot(0, "light_infantry", preTransferTroops, 6, "Pre-transfer Battalion")));

        state = state with { Armies = ValueList.Of(army) };

        // Tick 1: the maximum for the pre-transfer troop total.
        var afterTick1 = coordinator.RunRoundTick(state).State;
        var expectedPreTransferMoves = SupplyMoraleRule.BaseMoves(preTransferTroops, ruleset);
        Assert.Equal(expectedPreTransferMoves, afterTick1.ArmyById(armyId)!.Moves);

        // Mid-week: an army-to-army transfer grows the army, but no tick runs -- exactly recipe A's
        // "before ending the next turn" step.
        var grownArmy = afterTick1.ArmyById(armyId)! with
        {
            Units = ValueList.Of(new UnitSlot(0, "light_infantry", postTransferTroops, 6, "Post-transfer Battalion")),
        };
        var midWeekState = afterTick1 with { Armies = ValueList.Of(grownArmy) };

        // No assertion on midWeekState.Moves here: it would only pin C# record-copy semantics (grownArmy
        // copies Moves unchanged because nothing set it), not engine behaviour -- deleting such a line
        // changes nothing a mutation could catch (T39's folded follow-up, docs/task-catalogue.md
        // Done-when 10). The real claim -- that nothing except the ArmyTick pass recomputes Moves -- is
        // carried entirely by the tick1/tick2 assertions below: no system runs between them, so if Moves
        // had refreshed mid-week there would be no code path here that could have done it, and if the
        // tick itself failed to recompute, tick2's assertion would catch that directly.

        // Tick 2: only the next tick recomputes it, to the new troop total's value -- one step lower.
        var afterTick2 = coordinator.RunRoundTick(midWeekState).State;
        var expectedPostTransferMoves = SupplyMoraleRule.BaseMoves(postTransferTroops, ruleset);
        Assert.Equal(expectedPostTransferMoves, afterTick2.ArmyById(armyId)!.Moves);
        Assert.NotEqual(expectedPreTransferMoves, expectedPostTransferMoves); // the step really does move.
    }
}
