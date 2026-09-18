using IC2.Engine.Model;

namespace IC2.Engine.Economy;

/// <summary>
/// The quarterly treasury credit — <c>FUN_00451b40</c>'s nation loop
/// <strong>[confirmed: nation-tax-base-and-city-economy-fields.md, upkeep-payment-and-desertion.md]</strong>:
/// <c>taxBase × taxRate / 100 + taxBase / 4 − cityCount × 7 − wealth / 20000 + trade income</c>.
/// </summary>
/// <remarks>
/// <para>
/// Reads <paramref name="nation"/>'s <see cref="NationState.TaxBase"/> and <see cref="NationState.Wealth"/>
/// as <see cref="NationTaxBaseRebuild"/> just rebuilt them this quarter, not the value stored before the
/// tick <strong>[confirmed: city-population-growth.md's step order]</strong>. The trade term is
/// <c>Σ</c> over nations at <see cref="RelationStateCodes.Trade"/> or <see cref="RelationStateCodes.Alliance"/>
/// with <paramref name="nation"/> of that partner's own just-rebuilt tax base, divided by
/// <see cref="EconomyRules.TradeIncomeTaxBaseDivisor"/> — with it, the human nation's whole-quarter
/// treasury change matched in 6 of 6 sampled quarter pairs.
/// </para>
/// <para>
/// Does not itself apply the credit to <see cref="NationState.Treasury"/>: that, and the "for every
/// nation with unity &gt; 0" guard around the whole nation loop, belong to the wired
/// <see cref="QuarterlyNationEconomySystem"/>.
/// </para>
/// </remarks>
public static class NationTreasuryCredit
{
    /// <summary>Computes one quarter's treasury credit for a nation (may be negative).</summary>
    /// <param name="nation">The nation being credited, with its tax base and wealth already rebuilt this quarter.</param>
    /// <param name="state">The state, for the live city count and the other nations' just-rebuilt tax bases.</param>
    /// <param name="ruleset">Supplies every divisor and multiplier — never a C# literal.</param>
    public static int Compute(NationState nation, GameState state, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(nation);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ruleset);

        var economy = ruleset.Economy;
        var cityCount = state.CountCitiesOwnedBy(nation.Id);
        var tradeIncome = TradeIncome(nation, state, ruleset);

        return TaxIncome.Compute(nation.TaxBase, nation.TaxRatePercent, ruleset)
               + (nation.TaxBase / economy.TreasuryCreditTaxBaseQuarterShareDivisor)
               - (cityCount * economy.TreasuryCreditPerCityUpkeep)
               - (nation.Wealth / economy.TreasuryCreditWealthDivisor)
               + tradeIncome;
    }

    private static int TradeIncome(NationState nation, GameState state, Ruleset ruleset)
    {
        var stateCodes = ruleset.Diplomacy.StateCodes;
        var divisor = ruleset.Economy.TradeIncomeTaxBaseDivisor;
        var total = 0;

        foreach (var partner in state.Nations)
        {
            if (string.Equals(partner.Id, nation.Id, StringComparison.Ordinal))
            {
                continue;
            }

            var relation = state.Relations.Get(nation.Id, partner.Id);
            if (relation == stateCodes.Trade || relation == stateCodes.Alliance)
            {
                total += partner.TaxBase / divisor;
            }
        }

        return total;
    }
}
