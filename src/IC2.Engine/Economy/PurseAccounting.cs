using IC2.Engine.Model;

namespace IC2.Engine.Economy;

/// <summary>
/// The one place an army's or fleet's own money purse is credited — <c>docs/task-catalogue.md</c>
/// "T08 Economy, supply, and purses", Done-when 6: "the purse cap of 1,000 is enforced on every path that
/// credits a purse."
/// </summary>
/// <remarks>
/// <strong>[confirmed: decompiled-unit-map-orders-and-record-fields.md]</strong> — <c>TAFSupply_ChangeMoney</c>
/// caps an army's or fleet's own purse at 1,000 talents (<see cref="EconomyRules.PurseCapPerUnit"/>). The
/// national treasury has no such cap — it is observed negative in the fixtures corpus
/// (<c>reparation.ptolemaicTreasuryAfter</c>: −1,270) — so this clamp applies only to
/// <see cref="Model.ArmyState.Money"/> and <see cref="Model.FleetState.Money"/>, never to
/// <see cref="Model.NationState.Treasury"/>.
/// </remarks>
public static class PurseAccounting
{
    /// <summary>
    /// Credits a purse by <paramref name="amount"/>, clamped so the result never exceeds
    /// <see cref="EconomyRules.PurseCapPerUnit"/>. A negative <paramref name="amount"/> is a debit and is
    /// not clamped by this method — <see cref="EconomyRules.PurseCapPerUnit"/> is a ceiling, not a floor;
    /// the original lets a purse go to (but the confirmed mechanics in this task never take it below) zero.
    /// </summary>
    /// <param name="currentMoney">The purse's current balance.</param>
    /// <param name="amount">The amount to credit (or, if negative, debit).</param>
    /// <param name="ruleset">Supplies <see cref="EconomyRules.PurseCapPerUnit"/> — never a C# literal.</param>
    public static int Credit(int currentMoney, int amount, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(ruleset);
        return Math.Min(ruleset.Economy.PurseCapPerUnit, currentMoney + amount);
    }
}
