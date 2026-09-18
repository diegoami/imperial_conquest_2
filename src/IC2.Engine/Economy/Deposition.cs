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
