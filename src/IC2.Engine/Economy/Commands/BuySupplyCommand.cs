using IC2.Engine.Core;

namespace IC2.Engine.Economy.Commands;

/// <summary>
/// Orders an army to buy supply at a city — <c>docs/task-catalogue.md</c> "T41 Thin CLI demo on the toy
/// world", the <c>buy &lt;army&gt; &lt;city&gt; &lt;tons&gt;</c> command. Wraps
/// <see cref="SupplyPurchase.BuyForArmy"/>'s dialog path; the rule (free at your own city, <c>amount / 5</c>
/// abroad, clamped rather than rejected past a cap — "T38 Supply dialog follow-ups" Done-when 1) lives
/// there, never re-implemented here.
/// </summary>
public sealed record BuySupplyCommand(string IssuingNationId, string ArmyId, string CityId, int Tons) : ICommand
{
    /// <inheritdoc/>
    public string Kind => "economy.buy-supply";
}

/// <summary>Rejection codes this command's handler declares, in its own directory — see <see cref="RejectionCode"/>.</summary>
/// <remarks>
/// T38 (issue #78, Done-when 1) dropped <c>InsufficientCitySupply</c> and <c>InsufficientFunds</c>:
/// <see cref="SupplyPurchase.BuyForArmy"/> no longer throws for either condition, it clamps the transfer
/// instead, so a command that used to be rejected for exceeding a cap is now accepted at whatever the
/// cap allows — down to, and including, zero.
/// </remarks>
public static class BuySupplyRejections
{
    /// <summary>The command names an army id that does not exist.</summary>
    public static readonly RejectionCode UnknownArmy = new("supply.unknown-army");

    /// <summary>The named army belongs to a nation other than the one issuing the command.</summary>
    public static readonly RejectionCode NotYourArmy = new("supply.not-your-army");

    /// <summary>The command names a city id that does not exist.</summary>
    public static readonly RejectionCode UnknownCity = new("supply.unknown-city");

    /// <summary>The requested amount is not positive.</summary>
    public static readonly RejectionCode InvalidAmount = new("supply.invalid-amount");
}
