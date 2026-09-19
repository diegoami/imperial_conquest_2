using IC2.Engine.Model;

namespace IC2.Engine.Recruitment;

/// <summary>
/// What moves a nation's <see cref="NationState.MobilizedPercent"/> — <c>docs/task-catalogue.md</c>
/// "T55 Mobilization: a ready recruit becomes an army unit", Done-when 7, closing
/// <a href="https://github.com/diegoami/imperial_conquest_2/issues/183">#183</a> and research plan
/// item 17.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Mobilizing does not change it. Placing a recruitment order does.</strong>
/// <strong>[confirmed: decompiled-mobilization-and-mercenary-restock.md §5]</strong>, from
/// <c>TArmyRecruits_RecruitUnit</c> (<c>0x00454E78</c>) and its exact mirror
/// <c>TArmyRecruits_DisbandUnits</c> (<c>0x004553B0</c>):
/// </para>
/// <code>
/// order placed   : mobilized = min(100, mobilized + 1 + (troops × 1000) / wealth)
/// order cancelled: mobilized = max(0,   mobilized - 1 - (troops × 1000) / wealth)
/// quarterly      : mobilized = max(0,   mobilized - 3)      // already merged, T35
/// </code>
/// <para>
/// <c>wealth</c> is nation <c>+0x430</c>, <see cref="NationState.Wealth"/>: <c>Σ population × 3000</c>
/// over the nation's cities. So the increment is <c>1 + troops / (3 × totalPopulation)</c> in the
/// game's own units, and "a nation's mobilization rate is its standing army expressed as a fraction of
/// its people, accumulated one order at a time". The AI's <c>FUN_004504f4</c> applies the identical
/// increment inline, which is why this lives behind the command rather than behind either seat.
/// </para>
/// <para>
/// <strong>Every step truncates, and the order of operations is the original's.</strong> The troop term
/// is an integer division computed first and added whole; it is not folded into one expression over
/// rationals. The corpus magnitude check depends on it: Rome at wealth 768,000 ordering 15,000 light
/// infantry gains <c>1 + 15,000,000/768,000 = 1 + 19 = 20</c> points at once, against a <c>−3</c>
/// quarterly decay.
/// </para>
/// <para>
/// <strong>Zero wealth.</strong> The original divides without a guard, so a nation whose cities are all
/// lost would divide by zero there; this engine cannot, and treats the troop term as <c>0</c> so the
/// flat step still applies. <c>[designed]</c> — and this is what was searched: the report's §5 and §8,
/// <c>nation-tax-base-and-city-economy-fields.md</c> (which defines the field and its quarterly
/// rebuild) and <c>city-population-growth.md</c> all describe wealth being rebuilt from cities and none
/// records what the original does at zero. A nation with no cities is
/// <see cref="NationState.Eliminated"/> in this engine and issues no commands, so the branch is
/// unreachable through the command seam; <c>MobilizationRateTests</c> visits it directly.
/// </para>
/// </remarks>
public static class MobilizationRate
{
    /// <summary>The nation's mobilization percentage after it places a recruitment order.</summary>
    /// <param name="mobilizedPercent">The percentage before the order.</param>
    /// <param name="troops">The order's troop count.</param>
    /// <param name="wealth">The nation's <see cref="NationState.Wealth"/>.</param>
    /// <param name="rules">Supplies the step, the wealth scale and the cap.</param>
    public static int AfterOrderPlaced(int mobilizedPercent, int troops, int wealth, RecruitmentRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var raised = mobilizedPercent + rules.MobilizationRateOrderStep + TroopTerm(troops, wealth, rules);
        return Math.Min(rules.MobilizationCapPercent, raised);
    }

    /// <summary>The nation's mobilization percentage after it cancels a recruitment order.</summary>
    /// <remarks>
    /// Exactly symmetric to <see cref="AfterOrderPlaced"/>, floored at <c>0</c> rather than capped —
    /// the original's <c>FUN_00448fd8</c> (<c>max</c>) against <c>0</c>. <strong>No command cancels a
    /// standing recruitment order yet</strong>: the original's cancel path is
    /// <c>TArmyRecruits_DisbandUnits</c>'s recruit-list half, and this engine's
    /// <see cref="Armies.Commands.DisbandArmyCommand"/> disbands an army on the map, which is a
    /// different order. The half of the confirmed rule that has a caller is wired to it; this half
    /// ships beside it, tested, so the cancel command finds the rule already written rather than
    /// re-deriving it.
    /// </remarks>
    /// <param name="mobilizedPercent">The percentage before the cancellation.</param>
    /// <param name="troops">The cancelled order's troop count.</param>
    /// <param name="wealth">The nation's <see cref="NationState.Wealth"/>.</param>
    /// <param name="rules">Supplies the step and the wealth scale.</param>
    public static int AfterOrderCancelled(int mobilizedPercent, int troops, int wealth, RecruitmentRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var lowered = mobilizedPercent - rules.MobilizationRateOrderStep - TroopTerm(troops, wealth, rules);
        return Math.Max(0, lowered);
    }

    /// <summary>
    /// The formula's <c>(troops × scale) / wealth</c> term, truncated, and <c>0</c> at zero or negative
    /// wealth.
    /// </summary>
    private static int TroopTerm(int troops, int wealth, RecruitmentRules rules)
    {
        if (wealth <= 0)
        {
            return 0;
        }

        // Widened before the multiply: troops x 1000 overflows a 32-bit int above 2,147,483 troops,
        // which no army cap allows but no argument here relies on. The quotient is back inside int
        // range because it is at most troops x scale / 1.
        return (int)((long)troops * rules.MobilizationRateWealthScale / wealth);
    }
}
