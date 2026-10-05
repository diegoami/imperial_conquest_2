using IC2.Engine.Model;

namespace IC2.Engine.Economy;

/// <summary>
/// The read-only quarterly budget projection behind <see cref="Presentation.GameSession"/>'s
/// <c>balance</c> verb — <c>docs/tasks/T104.md</c>, from the UI command audit's Balance sheet row
/// (<c>docs/investigations/original-ui-command-audit.md</c> §1.3).
/// </summary>
/// <remarks>
/// <para>
/// The original's <c>TBalanceSheet</c> is a read-only dialog whose terms are exactly the quarterly
/// treasury credit (<see cref="NationTreasuryCredit"/>) and the upkeep bill
/// (<see cref="QuarterlyEconomySystem"/>, <see cref="ShipUpkeep"/>, <see cref="GarrisonUpkeep"/>,
/// <see cref="ArmyUpkeep"/>), plus the treasury and the debt line
/// <strong>[confirmed: decompiled-quarterly-billing-and-economy.md, upkeep-payment-and-desertion.md]</strong>.
/// Every field below is produced by that same shared function, never a second copy of a formula.
/// </para>
/// <para>
/// <strong>Mercenaries' pay is shown but is not part of <see cref="ExpenditureTotal"/>.</strong>
/// <see cref="ArmyUpkeep.ComputeForNation"/> reports it so the line exists, but the quarterly billing
/// charges mercenary pay to the army's own purse, not to the nation's treasury
/// (<see cref="MercenaryDesertion.BillArmy"/>), so it never moves the treasury. <see cref="ExpenditureTotal"/>
/// is therefore the treasury outflow — the sum that reconciles with a quarter's treasury change — while
/// <see cref="MercenariesPay"/> is listed for the player's information. This is why
/// <c>docs/tasks/T104.md</c>'s Done-when 2 identity holds even for a nation with mercenaries.
/// </para>
/// </remarks>
/// <param name="TaxIncome">The credit's tax-income term (<see cref="TaxIncome.Compute"/>).</param>
/// <param name="TaxBaseQuarterShare">The credit's tax-base quarter-share term.</param>
/// <param name="TradeIncome">The credit's trade term.</param>
/// <param name="CityAndWealthUpkeep">
/// The credit's two netted deduction terms, as one line — also part of
/// <see cref="ExpenditureTotal"/>, matching the original dialog's combined "cities and wealth" row.
/// </param>
/// <param name="ShipUpkeep">The nation's launched fleets' quarterly upkeep.</param>
/// <param name="RecruitmentSlotUpkeep">The nation's recruitment slots' quarterly upkeep.</param>
/// <param name="RegularsUpkeep">
/// The nation's deployed regulars' quarterly upkeep (treasury-charged) — the sum of
/// <see cref="MercenaryDesertion.BillArmy"/>'s <see cref="MercenaryDesertion.Result.RegularUpkeepCharged"/>
/// over the nation's armies, the same charge the quarterly system applies, so a regular skipped by an
/// unfunded mercenary's swap-remove is skipped here too.
/// </param>
/// <param name="MercenariesPay">The nation's deployed mercenaries' quarterly pay (army-purse-charged).</param>
/// <param name="Treasury">The nation's treasury at projection time.</param>
/// <param name="DebtLimit">The magnitude of the wealth-based debt line (<see cref="Deposition.DebtLimit"/>).</param>
public sealed record BalanceSheet(
    int TaxIncome,
    int TaxBaseQuarterShare,
    int TradeIncome,
    int CityAndWealthUpkeep,
    int ShipUpkeep,
    int RecruitmentSlotUpkeep,
    int RegularsUpkeep,
    int MercenariesPay,
    int Treasury,
    int DebtLimit)
{
    /// <summary>The three income lines' total.</summary>
    public int IncomeTotal => TaxIncome + TaxBaseQuarterShare + TradeIncome;

    /// <summary>
    /// The treasury-charged expenditure lines' total — the figure that reconciles with the quarter's
    /// treasury change. <see cref="MercenariesPay"/> is deliberately excluded; see this type's remarks.
    /// </summary>
    public int ExpenditureTotal =>
        CityAndWealthUpkeep + ShipUpkeep + RecruitmentSlotUpkeep + RegularsUpkeep;

    /// <summary>
    /// Projects one nation's budget for the quarter from the state as it stands, with no mutation. Every
    /// figure comes from the same function the quarterly systems call.
    /// </summary>
    /// <param name="state">The state to project from.</param>
    /// <param name="nationId">The nation whose sheet is wanted.</param>
    /// <param name="ruleset">Supplies every constant — never a C# literal.</param>
    /// <exception cref="ArgumentException"><paramref name="nationId"/> is not one of <paramref name="state"/>'s nations.</exception>
    public static BalanceSheet For(GameState state, string nationId, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(nationId);
        ArgumentNullException.ThrowIfNull(ruleset);

        var nation = state.NationById(nationId)
            ?? throw new ArgumentException($"Nation '{nationId}' is not in the state.", nameof(nationId));

        var (regularsUpkeep, mercenariesPay) = ArmyUpkeep.ComputeForNation(state, nationId, ruleset);

        return new BalanceSheet(
            TaxIncome: IC2.Engine.Economy.TaxIncome.Compute(nation.TaxBase, nation.TaxRatePercent, ruleset),
            TaxBaseQuarterShare: NationTreasuryCredit.TaxBaseQuarterShare(nation, ruleset),
            TradeIncome: NationTreasuryCredit.TradeIncome(nation, state, ruleset),
            CityAndWealthUpkeep: NationTreasuryCredit.CityAndWealthUpkeep(nation, state, ruleset),
            ShipUpkeep: IC2.Engine.Economy.ShipUpkeep.ComputeForNation(state, nationId, ruleset),
            RecruitmentSlotUpkeep: GarrisonUpkeep.Compute(nation.RecruitmentSlots, ruleset),
            RegularsUpkeep: regularsUpkeep,
            MercenariesPay: mercenariesPay,
            Treasury: nation.Treasury,
            DebtLimit: Deposition.DebtLimit(nation, ruleset));
    }
}
