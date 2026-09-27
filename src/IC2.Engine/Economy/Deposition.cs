using IC2.Engine.Model;

namespace IC2.Engine.Economy;

/// <summary>
/// The debt test and the deposition effects (<c>FUN_0044c8f0</c>) shared by both paths that trigger it —
/// <see cref="AiDepositionHandler"/> (the quarterly, 1-in-9 AI check) and
/// <see cref="HumanDepositionSystem"/> (the deterministic, start-of-turn human check) —
/// <c>docs/task-catalogue.md</c> "T39 Quarterly upkeep: who pays, mercenary desertion, and deposition for
/// debt", Done-when 6.
/// </summary>
/// <remarks>
/// <strong>[confirmed: upkeep-payment-and-desertion.md]</strong> throughout, except the relation reset,
/// which is <c>[derived]</c> (code-only; neither sampled deposition held such a relation). The rename of
/// the deposed leader ("a different random name from the nation's 12-name table") is <em>not</em>
/// implemented here — Done-when 7 leaves it a known-open data gap: the name pool lives only in the DAT,
/// and <see cref="Model.NationState.LeaderName"/> is left unchanged.
/// <para>
/// <strong>The new-leader step stays <c>[open]</c> — the user's own decision of 2026-09-27, on #389,
/// after T87's implementation (plan PR #438).</strong> T87 re-checked this gap and it still stands: the
/// 16 × 12 name pool is in the DAT at <c>0x2089A</c>, but neither <c>World.cs</c>'s own
/// <see cref="Model.NationDefinition"/> nor any research report gives the twelve names any nation actually
/// draws from — <c>upkeep-payment-and-desertion.md</c> and <c>decompiled-quarterly-rebellion.md</c> both
/// state the mechanic ("a different random name from the nation's 12-name table" / "one <c>Random(12)</c>
/// from the nation's 12 names") without transcribing the table itself, which is exactly the "lives only in
/// the DAT" gap this remark already named. <see cref="Model.World"/> is outside this task's Owns list in
/// any case (T87's Scope: <c>HumanDepositionSystem.cs</c>/<c>AiDepositionHandler.cs</c>/this file, never
/// <c>Model/World.cs</c>), so even with the data in hand this would need a separate, later export task.
/// Reported rather than invented: no placeholder name pool is added here, and both the fall
/// (<see cref="ApplyEffects"/>) and rebirth (<see cref="Rebirth"/>) leave <see cref="Model.NationState.LeaderName"/>
/// exactly as it was.
/// </para>
/// </remarks>
public static class Deposition
{
    /// <summary>
    /// Whether a nation is in debt: <c>treasury &lt; −(wealth / DebtWealthDivisor)</c>, or
    /// <c>treasury &lt; DebtTreasuryFloor</c>, or <c>unity &lt; DebtUnityThreshold</c>.
    /// </summary>
    /// <param name="ruleset">
    /// Supplies <see cref="EconomyRules.DebtWealthDivisor"/>, <see cref="EconomyRules.DebtTreasuryFloor"/>
    /// and <see cref="EconomyRules.DebtUnityThreshold"/> — never a C# literal.
    /// </param>
    public static bool InDebt(NationState nation, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(nation);
        ArgumentNullException.ThrowIfNull(ruleset);

        var economy = ruleset.Economy;
        return nation.Treasury < -(nation.Wealth / economy.DebtWealthDivisor)
               || nation.Treasury < economy.DebtTreasuryFloor
               || nation.Unity < economy.DebtUnityThreshold;
    }

    /// <summary>
    /// Applies the deposition's unity and treasury effects to one nation, shared by the AI and human
    /// paths alike. Does not touch <see cref="NationState.Control"/> or
    /// <see cref="NationState.LeaderName"/> — those are each caller's own concern (see this type's
    /// remarks on the leader rename).
    /// </summary>
    /// <param name="ruleset">
    /// Supplies <see cref="EconomyRules.DepositionUnityCeiling"/>, <see cref="EconomyRules.DepositionUnityGainAmount"/>
    /// and <see cref="EconomyRules.DepositionTreasuryCredit"/> — never a C# literal.
    /// </param>
    public static NationState ApplyEffects(NationState nation, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(nation);
        ArgumentNullException.ThrowIfNull(ruleset);

        var economy = ruleset.Economy;
        var newUnity = Math.Max(nation.Unity, Math.Min(economy.DepositionUnityCeiling, nation.Unity + economy.DepositionUnityGainAmount));
        var newTreasury = nation.Treasury < 0 ? 0 : nation.Treasury + economy.DepositionTreasuryCredit;

        return nation with { Unity = newUnity, Treasury = newTreasury };
    }

    /// <summary>
    /// Whether a <em>human</em> nation's leader falls at the true start of its own turn — <c>FUN_00452034</c>
    /// (:55024–55032), human-only; <see cref="HumanDepositionSystem"/>'s own trigger, wider than
    /// <see cref="InDebt"/> alone. Folds <see cref="InDebt"/>'s own three OR-terms with the two more the
    /// original ORs into the very same test: the hard end year and total conquest (T87, bug #380 and
    /// follow-up #374's own task, docs/tasks/T87.md's Scope: "The triggers are 250 BC, 334 cities, unity
    /// below 400, or debt").
    /// </summary>
    /// <remarks>
    /// <strong>[confirmed: decompiled-elimination-cleanup.md §3]</strong>, "Callers": "<c>FUN_00452034</c>
    /// (every human turn start: 250 BC, 334 cities, unity &lt; 400, or in debt)". Read directly from the
    /// dump (<c>%LOCALAPPDATA%\ReTools\all_app_functions.txt</c> :55024–55032) to confirm the exact
    /// four-way OR and its two thresholds:
    /// <c>(DAT_004a0332==0xfa) || (0x14d &lt; cityCount) || (unity&lt;400) || (treasury&lt;wealth/-500 ||
    /// treasury&lt;-20000)</c> — year 250, city count above 333 (334, the shipped map's own total), unity
    /// below 400, or the same debt formula <see cref="InDebt"/> already implements (which itself already
    /// carries the unity-below-400 term, so this method does not re-test unity separately; the OR is
    /// idempotent either way).
    /// </remarks>
    /// <param name="nation">The active nation to test — must be the human seat this turn belongs to.</param>
    /// <param name="state">
    /// Supplies <see cref="GameState.Calendar"/>'s <see cref="CalendarState.YearBc"/> and
    /// <see cref="GameState.Cities"/>'s own live count and ownership — never a snapshot.
    /// </param>
    /// <param name="ruleset">
    /// Supplies <see cref="VictoryRules.HardEndYearBc"/> — the <em>same</em> ruleset field
    /// <see cref="Victory.VictoryEvaluator.EvaluateTotalConquest"/> already reads for its own, independent
    /// "250 BC" check (reused, not duplicated: that evaluator's own remarks say "a second, independent
    /// start-of-turn check can be added later without touching this file" — this is that check). The
    /// "334 cities" term is <see cref="GameState.Cities"/>'s own count, generalized the same way
    /// <see cref="Victory.VictoryEvaluator"/> already generalizes it ("never a literal 334... this works
    /// for any world, toy or shipped") — never a C# literal, and no new <see cref="Ruleset"/> key.
    /// </param>
    public static bool ShouldFallAtHumanTurnStart(NationState nation, GameState state, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(nation);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ruleset);

        if (state.Calendar.YearBc <= ruleset.Victory.HardEndYearBc)
        {
            return true;
        }

        var totalCities = state.Cities.Count;
        if (totalCities > 0 && CountCitiesOwnedBy(state, nation.Id) >= totalCities)
        {
            return true;
        }

        return InDebt(nation, ruleset);
    }

    private static int CountCitiesOwnedBy(GameState state, string nationId)
    {
        var count = 0;
        foreach (var city in state.Cities)
        {
            if (string.Equals(city.Owner, nationId, StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Resets every relation between <paramref name="nationId"/> and another nation that falls in
    /// <c>[DepositionRelationResetThreshold, 0)</c> back to zero — a deposed leader wipes the nation's
    /// most recent cooldowns clean. <c>[derived]</c>: code-only, never observed in either sampled
    /// deposition.
    /// </summary>
    /// <param name="ruleset">Supplies <see cref="EconomyRules.DepositionRelationResetThreshold"/> — never a C# literal.</param>
    public static DiplomaticRelations ResetRelations(DiplomaticRelations relations, string nationId, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(relations);
        ArgumentNullException.ThrowIfNull(nationId);
        ArgumentNullException.ThrowIfNull(ruleset);

        var threshold = ruleset.Economy.DepositionRelationResetThreshold;

        foreach (var otherNationId in relations.NationIds)
        {
            if (string.Equals(otherNationId, nationId, StringComparison.Ordinal))
            {
                continue;
            }

            var value = relations.Get(nationId, otherNationId);
            if (value < 0 && value >= threshold)
            {
                relations = relations.WithRelation(nationId, otherNationId, 0);
            }
        }

        return relations;
    }
}
