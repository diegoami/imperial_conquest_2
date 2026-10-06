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
/// implemented here, because it belongs to each caller's own draw: T146 exports the 16 × 12 name pool
/// (<see cref="Model.NationDefinition.LeaderNames"/>, DAT <c>0x2089A</c>) and draws it in
/// <see cref="LeaderSuccession"/> — the human fall's two draws at
/// <see cref="Presentation.GameSession"/>'s fall sites, the computer deposition's retry loop in
/// <see cref="AiDepositionHandler"/>, and rebirth's one draw in <see cref="Rebirth"/>. This type still
/// leaves <see cref="Model.NationState.LeaderName"/> untouched; <see cref="ApplyEffects"/> only computes
/// the unity and treasury halves (the latter through <see cref="FallTreasury"/>).
/// <para>
/// <strong>The new-leader step is no longer <c>[open]</c>.</strong> T87 (the user's decision of
/// 2026-09-27, on #389) left the name a known-open data gap because the 16 × 12 pool lived only in the
/// DAT; T146's export adds it to the world and draws it as the original does.
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
        return nation.Treasury < -DebtLimit(nation, ruleset)
               || nation.Unity < economy.DebtUnityThreshold;
    }

    /// <summary>
    /// The magnitude of the wealth-based debt line — <c>min(wealth / DebtWealthDivisor, 20000)</c>, where
    /// the <c>20000</c> is the magnitude of <see cref="EconomyRules.DebtTreasuryFloor"/>. A nation is over
    /// this line when <c>treasury &lt; −DebtLimit</c>; the same figure is the balance sheet's "Debt limit"
    /// line (<c>docs/tasks/T104.md</c>), so the projection and the deposition test can never disagree.
    /// </summary>
    /// <remarks>
    /// <strong>[confirmed: upkeep-payment-and-desertion.md]</strong>: the original ORs
    /// <c>treasury &lt; −(wealth / DebtWealthDivisor)</c> with <c>treasury &lt; DebtTreasuryFloor</c>, which
    /// is exactly <c>treasury &lt; −min(wealth / DebtWealthDivisor, −DebtTreasuryFloor)</c> in integer
    /// arithmetic — the less-negative of the two thresholds is the one that binds. This method restates
    /// neither term: <see cref="InDebt"/> now calls it, so the debt test and the projection share one
    /// computation.
    /// </remarks>
    /// <param name="nation">The nation whose wealth is read.</param>
    /// <param name="ruleset">
    /// Supplies <see cref="EconomyRules.DebtWealthDivisor"/> and <see cref="EconomyRules.DebtTreasuryFloor"/>
    /// — never a C# literal.
    /// </param>
    public static int DebtLimit(NationState nation, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(nation);
        ArgumentNullException.ThrowIfNull(ruleset);

        var economy = ruleset.Economy;
        return Math.Min(nation.Wealth / economy.DebtWealthDivisor, -economy.DebtTreasuryFloor);
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
        var newTreasury = FallTreasury(nation.Treasury, ruleset);

        return nation with { Unity = newUnity, Treasury = newTreasury };
    }

    /// <summary>
    /// T146: the treasury half of <c>FUN_0044c8f0</c>'s fall write — <c>treasury &lt; 0 ? 0 :
    /// treasury + DepositionTreasuryCredit</c> (<c>decompiled-elimination-cleanup.md</c> §3, the
    /// :50802–50805 write) — shared by <see cref="ApplyEffects"/> (a human's fall or an AI's
    /// deposition) and by the two capture paths that eliminate a <em>human</em> loser
    /// (<see cref="IC2.Engine.Cities.Capture.ConquestCascade"/> step 7 and
    /// <see cref="IC2.Engine.Cities.Capture.NationElimination.ApplyIfLastCityLost"/>), which call the
    /// same routine for a human in the original. A computer loser is never handed to it, so those paths
    /// apply it only when the loser's control was <see cref="SeatControl.Human"/>.
    /// </summary>
    /// <param name="treasury">The treasury before the credit.</param>
    /// <param name="ruleset">Supplies <see cref="EconomyRules.DepositionTreasuryCredit"/> — never a C# literal.</param>
    public static int FallTreasury(int treasury, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(ruleset);
        return treasury < 0 ? 0 : treasury + ruleset.Economy.DepositionTreasuryCredit;
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
