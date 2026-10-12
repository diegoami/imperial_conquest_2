using IC2.Engine.Model;

namespace IC2.Engine.Economy;

/// <summary>
/// The read-only quarterly budget projection behind <see cref="Presentation.GameSession"/>'s
/// <c>balance</c> verb — <c>docs/tasks/T104.md</c>, from the UI command audit's Balance sheet row
/// (<c>docs/investigations/original-ui-command-audit.md</c> §1.3).
/// </summary>
/// <remarks>
/// <para>
/// The original's <c>TBalanceSheet</c> is a read-only dialog whose terms, under
/// <see cref="BalanceSheetTotalsPolicy.Reconciling"/>, are exactly the quarterly
/// treasury credit (<see cref="NationTreasuryCredit"/>) and the upkeep bill
/// (<see cref="QuarterlyEconomySystem"/>, <see cref="ShipUpkeep"/>, <see cref="GarrisonUpkeep"/>,
/// <see cref="ArmyUpkeep"/>), plus the treasury and the debt line
/// <strong>[confirmed: decompiled-quarterly-billing-and-economy.md, upkeep-payment-and-desertion.md]</strong>.
/// Every field below is produced by that same shared function, never a second copy of a formula. Under
/// <see cref="BalanceSheetTotalsPolicy.Original"/> the sheet instead follows the original's display
/// (g1 G2), and two lines differ from what the tick charges and tests: the Fleet line counts fleets under
/// construction, and the Debt limit is rounded for display.
/// </para>
/// <para>
/// <strong>Under <see cref="BalanceSheetTotalsPolicy.Reconciling"/> (<c>improved</c>, the toy ruleset),
/// mercenaries' pay is shown but is not part of <see cref="ExpenditureTotal"/>.</strong>
/// <see cref="ArmyUpkeep.ComputeForNation"/> reports it so the line exists, but the quarterly billing
/// charges mercenary pay to the army's own purse, not to the nation's treasury
/// (<see cref="MercenaryDesertion.BillArmy"/>), so it never moves the treasury. Under this policy
/// <see cref="ExpenditureTotal"/> is therefore the treasury outflow — the sum that reconciles with a quarter's
/// treasury change — while <see cref="MercenariesPay"/> is listed for the player's information. This is why
/// <c>docs/tasks/T104.md</c>'s Done-when 2 identity holds even for a nation with mercenaries.
/// </para>
/// <para>
/// <strong>T167, <see cref="RulesetFlags.BalanceSheetTotals"/> = <see cref="BalanceSheetTotalsPolicy.Original"/></strong>
/// (<c>classical-faithful</c>) shows the original's figures instead <strong>[confirmed: Wine + code, research
/// 89c37ff, 2026-10-11-g1-original-checks.md G2]</strong>: the Total includes <see cref="MercenariesPay"/>,
/// the Fleet line (<see cref="ShipUpkeep"/>) counts every fleet the nation owns, finished or under
/// construction, at the per-ship rate, and <see cref="DebtLimit"/> is rounded for display. Only the sheet's
/// figures change; <see cref="ShipUpkeep.ComputeForNation"/>'s charge, the quarterly tick and
/// <see cref="Deposition.DebtLimit"/> are untouched, so under this policy the Total no longer reconciles
/// with the quarter's treasury change.
/// </para>
/// </remarks>
/// <param name="TaxIncome">The credit's tax-income term (<see cref="TaxIncome.Compute"/>).</param>
/// <param name="TaxBaseQuarterShare">The credit's tax-base quarter-share term.</param>
/// <param name="TradeIncome">The credit's trade term.</param>
/// <param name="CityAndWealthUpkeep">
/// The credit's two netted deduction terms, as one line — also part of
/// <see cref="ExpenditureTotal"/>, matching the original dialog's combined "cities and wealth" row.
/// </param>
/// <param name="ShipUpkeep">
/// The Fleet line. Under <see cref="BalanceSheetTotalsPolicy.Reconciling"/>, the launched fleets' quarterly
/// upkeep (what the tick charges); under <see cref="BalanceSheetTotalsPolicy.Original"/>, every fleet the
/// nation owns, fleets under construction included, at the per-ship rate (g1 G2).
/// </param>
/// <param name="RecruitmentSlotUpkeep">The nation's recruitment slots' quarterly upkeep.</param>
/// <param name="RegularsUpkeep">
/// The nation's deployed regulars' quarterly upkeep (treasury-charged) — the sum of
/// <see cref="MercenaryDesertion.BillArmy"/>'s <see cref="MercenaryDesertion.Result.RegularUpkeepCharged"/>
/// over the nation's armies, the same charge the quarterly system applies, so a regular skipped by an
/// unfunded mercenary's swap-remove is skipped here too.
/// </param>
/// <param name="MercenariesPay">The nation's deployed mercenaries' quarterly pay (army-purse-charged).</param>
/// <param name="Treasury">The nation's treasury at projection time.</param>
/// <param name="DebtLimit">
/// The magnitude of the wealth-based debt line (<see cref="Deposition.DebtLimit"/>), rounded for display
/// under <see cref="BalanceSheetTotalsPolicy.Original"/>.
/// </param>
/// <param name="TotalIncludesMercenaries">
/// Whether <see cref="ExpenditureTotal"/> includes <see cref="MercenariesPay"/>
/// (<see cref="BalanceSheetTotalsPolicy.Original"/>).
/// </param>
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
    int DebtLimit,
    bool TotalIncludesMercenaries = false)
{
    /// <summary>The three income lines' total.</summary>
    public int IncomeTotal => TaxIncome + TaxBaseQuarterShare + TradeIncome;

    /// <summary>
    /// The expenditure lines' total. Under <see cref="BalanceSheetTotalsPolicy.Reconciling"/> it is the
    /// treasury-charged lines only, the figure that reconciles with the quarter's treasury change, and
    /// <see cref="MercenariesPay"/> is excluded. Under <see cref="BalanceSheetTotalsPolicy.Original"/>
    /// (<see cref="TotalIncludesMercenaries"/>) it includes <see cref="MercenariesPay"/> and the all-fleets
    /// Fleet line, as the original's does, and deliberately does not reconcile with the treasury change
    /// (g1 G2); see this type's remarks.
    /// </summary>
    public int ExpenditureTotal =>
        CityAndWealthUpkeep + ShipUpkeep + RecruitmentSlotUpkeep + RegularsUpkeep
        + (TotalIncludesMercenaries ? MercenariesPay : 0);

    /// <summary>
    /// Projects one nation's budget for the quarter from the state as it stands, with no mutation. Every
    /// figure comes from the same function the quarterly systems call. Under
    /// <see cref="BalanceSheetTotalsPolicy.Original"/> the Fleet line and the Debt limit are then adjusted to
    /// the original's display (g1 G2); the charges themselves are unchanged.
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
        var original = ruleset.Flags.BalanceSheetTotals == BalanceSheetTotalsPolicy.Original;

        return new BalanceSheet(
            TaxIncome: IC2.Engine.Economy.TaxIncome.Compute(nation.TaxBase, nation.TaxRatePercent, ruleset),
            TaxBaseQuarterShare: NationTreasuryCredit.TaxBaseQuarterShare(nation, ruleset),
            TradeIncome: NationTreasuryCredit.TradeIncome(nation, state, ruleset),
            CityAndWealthUpkeep: NationTreasuryCredit.CityAndWealthUpkeep(nation, state, ruleset),
            ShipUpkeep: original
                ? AllFleetsUpkeep(state, nationId, ruleset)
                : IC2.Engine.Economy.ShipUpkeep.ComputeForNation(state, nationId, ruleset),
            RecruitmentSlotUpkeep: GarrisonUpkeep.Compute(nation.RecruitmentSlots, ruleset),
            RegularsUpkeep: regularsUpkeep,
            MercenariesPay: mercenariesPay,
            Treasury: nation.Treasury,
            DebtLimit: original
                ? RoundDebtLimit(Deposition.DebtLimit(nation, ruleset), ruleset.Economy)
                : Deposition.DebtLimit(nation, ruleset),
            TotalIncludesMercenaries: original);
    }

    /// <summary>
    /// The Fleet line under <see cref="BalanceSheetTotalsPolicy.Original"/>: <c>ships × rate</c> over every
    /// fleet the nation owns, with no completion test (<c>TBalanceSheet_PaintBalance</c> 55471-55480). The
    /// quarterly charge (<see cref="IC2.Engine.Economy.ShipUpkeep.ComputeForNation"/>) still skips fleets under
    /// construction.
    /// </summary>
    private static int AllFleetsUpkeep(GameState state, string nationId, Ruleset ruleset)
    {
        var total = 0;
        foreach (var fleet in state.Fleets)
        {
            if (string.Equals(fleet.Nation, nationId, StringComparison.Ordinal))
            {
                total += IC2.Engine.Economy.ShipUpkeep.Compute(fleet.Ships, ruleset);
            }
        }

        return total;
    }

    /// <summary>
    /// Rounds the (already capped) debt limit down for display: to the low step up to the low maximum, to
    /// the mid step up to the mid maximum, and to the high step above (<c>TBalanceSheet_PaintBalance</c>
    /// 55541-55556). All thresholds and steps come from the ruleset.
    /// </summary>
    private static int RoundDebtLimit(int limit, EconomyRules economy)
    {
        var step = limit <= economy.BalanceSheetDebtLimitLowMax ? economy.BalanceSheetDebtLimitLowStep
            : limit <= economy.BalanceSheetDebtLimitMidMax ? economy.BalanceSheetDebtLimitMidStep
            : economy.BalanceSheetDebtLimitHighStep;
        return limit / step * step;
    }
}
