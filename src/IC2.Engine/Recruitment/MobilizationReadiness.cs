using IC2.Engine.Model;

namespace IC2.Engine.Recruitment;

/// <summary>
/// The readiness half of mobilization — when a standing-recruitment slot may be collected, and what
/// permanent quality the unit it becomes is born with. <c>docs/task-catalogue.md</c> "T55 Mobilization:
/// a ready recruit becomes an army unit", Done-when 4.
/// </summary>
/// <remarks>
/// <para>
/// <strong>[confirmed: decompiled-mobilization-and-mercenary-restock.md §1, §2, §4]</strong>. Three
/// facts, together:
/// </para>
/// <list type="number">
/// <item><description>
/// <c>FUN_0044a4e0</c> sets the new unit's quality to <c>state / 4</c>, truncating toward zero
/// (<c>if (v &lt; 0) v += 3; v &gt;&gt; 2</c> — C's own division, written as a shift).
/// </description></item>
/// <item><description>
/// <c>TArmyRecruits_SetCityRecruits</c> renders each pending slot's status column with the identical
/// expression, indexing the DAT's quality-name table at <c>0x1F6CA</c> (11-byte stride).
/// </description></item>
/// <item><description>
/// That table reads <c>not ready</c> at indices <strong>0, 1, 2 and 3</strong>, then <c>very poor</c>,
/// <c>poor</c>, <c>average</c>, <c>good</c>, <c>very good</c>, <c>elite</c>.
/// </description></item>
/// </list>
/// <para>
/// So the dialog's <c>state &gt; 15</c> gate is exactly <c>quality &gt;= 4</c>, which is exactly "the
/// slot no longer reads <em>not ready</em>". A slot starts at state <c>0</c>
/// (<see cref="Commands.RecruitStandingUnitCommandHandler"/>), climbs <c>+2</c> a week through
/// <see cref="Calendar.CityUnitStateCode.Advance"/> and holds at 24, so the ladder is: weeks 0–7
/// <c>not ready</c>; week 8 <c>very poor</c>; week 10 <c>poor</c>; week 12 and after <c>average</c>.
/// <strong>A unit mobilized early is permanently worse</strong> — the quality written here is never
/// revisited.
/// </para>
/// <para>
/// <strong>The two seats do not share a threshold.</strong> The player's dialog
/// (<c>TArmyRecruits_MobilizeUnits</c>) gates on <c>0xf &lt; state</c>; the AI's
/// <c>FUN_004504f4</c> gates on <c>state == 0x18</c> in both of its passes. That is not one rule read
/// twice — it is an AI self-restriction to fully-ready recruits, and
/// <c>MobilizationReadinessTests</c> pins both sides at their boundaries.
/// </para>
/// </remarks>
public static class MobilizationReadiness
{
    /// <summary>
    /// The permanent <see cref="UnitSlot.Quality"/> a slot at <paramref name="stateCode"/> mobilizes at:
    /// <c>stateCode / MobilizationQualityDivisor</c>, truncating toward zero.
    /// </summary>
    /// <param name="stateCode">The slot's <see cref="RecruitmentSlot.StateCode"/>.</param>
    /// <param name="rules">Supplies <see cref="RecruitmentRules.MobilizationQualityDivisor"/>.</param>
    public static int QualityFor(int stateCode, RecruitmentRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        // C# integer division truncates toward zero, which is what the original's "add 3 when negative,
        // then shift right 2" idiom reproduces for a signed value -- so this is the same function over
        // the whole signed range, not only over the non-negative states this engine can produce.
        // MobilizationReadinessTests.Quality_truncates_toward_zero_for_a_negative_state_code visits the
        // negative side, which no engine path reaches today (the report's own §8 leaves "whether state
        // is ever negative" unresolved, and RecruitmentSlotReadinessSystem never writes one).
        return stateCode / rules.MobilizationQualityDivisor;
    }

    /// <summary>
    /// The lowest <see cref="RecruitmentSlot.StateCode"/> <paramref name="control"/> may mobilize at,
    /// under <paramref name="model"/>.
    /// </summary>
    /// <param name="control">The mobilizing nation's seat.</param>
    /// <param name="rules">Supplies both seats' thresholds.</param>
    /// <param name="model">
    /// The ruleset's <see cref="RulesetFlags.SeatAsymmetry"/> setting. Under
    /// <see cref="SeatAsymmetryModel.Faithful"/> each seat gets its own confirmed threshold; under
    /// <see cref="SeatAsymmetryModel.Normalized"/> every seat gets the human one.
    /// </param>
    /// <remarks>
    /// <strong>Why <see cref="SeatAsymmetryModel.Normalized"/> takes the <em>human</em> value</strong>
    /// — <c>[designed]</c>, and this is what was searched: no report says what a normalized variant
    /// should do, because the original has no such variant;
    /// <c>decompiled-mobilization-and-mercenary-restock.md</c>, <c>docs/design-audit.md</c> Q6 and
    /// <c>docs/game-design.md</c>'s "improved" column were all read and none names mobilization. The
    /// enum's own definition — "apply the same rule to every seat, human or AI" — is the whole
    /// specification, so the choice is which of the two rules to keep. The AI's <c>== 24</c> is a
    /// <em>self-restriction</em> (it waits for a fully-ready recruit where the player need not), so
    /// generalising it would make "improved" strictly harsher than the original for every seat, which
    /// is the opposite of what that preset is for. The human threshold is therefore the shared one.
    /// <c>MobilizationReadinessTests</c> asserts it for an AI seat.
    /// </remarks>
    public static int MinStateCode(SeatControl control, RecruitmentRules rules, SeatAsymmetryModel model)
    {
        ArgumentNullException.ThrowIfNull(rules);

        return model == SeatAsymmetryModel.Faithful && control == SeatControl.Ai
            ? rules.MobilizationMinStateCodeAiSeat
            : rules.MobilizationMinStateCodeHumanSeat;
    }

    /// <summary>Whether a slot at <paramref name="stateCode"/> may be mobilized by <paramref name="control"/>.</summary>
    /// <param name="stateCode">The slot's <see cref="RecruitmentSlot.StateCode"/>.</param>
    /// <param name="control">The mobilizing nation's seat.</param>
    /// <param name="rules">Supplies both seats' thresholds.</param>
    /// <param name="model">The ruleset's <see cref="RulesetFlags.SeatAsymmetry"/> setting.</param>
    public static bool IsReady(int stateCode, SeatControl control, RecruitmentRules rules, SeatAsymmetryModel model) =>
        stateCode >= MinStateCode(control, rules, model);
}
