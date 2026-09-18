using IC2.Engine.Core;

namespace IC2.Engine.Naval.Commands;

/// <summary>
/// Orders a new fleet built at a named coastal city — <c>docs/task-catalogue.md</c> "T14 Naval",
/// Done-when 1 and 2. The rule (10-100 ships, <c>ships × 10</c> cost, a 24-tick countdown, launch at 100%
/// condition with 50 tons and no money, coastal-nations-only) lives in
/// <see cref="OrderFleetCommandHandler"/>, never re-derived by a caller.
/// </summary>
/// <param name="NewFleetId">
/// The new fleet's id. The engine invents no id of its own — <c>docs/game-design.md</c> principle 4
/// ("ship commands + seed, not state") means every value a replay needs to reproduce comes from the
/// command itself, and an id generated from a forbidden source (a counter tied to insertion order, a
/// GUID) would not replay identically if an earlier command in the log were ever replayed differently.
/// The caller — the CLI, the AI, a test — names it, exactly as it already names <see cref="CityId"/>.
/// </param>
/// <remarks>
/// Noted, not fixed (first review, N2): this handler debits the treasury unconditionally and never
/// refuses for insufficient funds, so an order can drive the treasury negative. No Done-when line
/// requires an affordability check, and T38 established a clamp-not-reject convention for the supply
/// dialog's own money handling; neither cited report states whether the original's build dialog gates on
/// affordability. Flagged rather than guessed at.
/// </remarks>
public sealed record OrderFleetCommand(string IssuingNationId, string CityId, int Ships, string NewFleetId) : ICommand
{
    /// <inheritdoc/>
    public string Kind => "naval.order-fleet";
}

/// <summary>Rejection codes this command's handler declares, in its own directory — see <see cref="RejectionCode"/>.</summary>
public static class OrderFleetRejections
{
    /// <summary>The command names a city id that does not exist.</summary>
    public static readonly RejectionCode UnknownCity = new("naval.unknown-city");

    /// <summary>The named city belongs to a nation other than the one issuing the command.</summary>
    public static readonly RejectionCode NotYourCity = new("naval.not-your-city");

    /// <summary>
    /// The city is not coastal — <c>docs/game-design.md</c> §Naval: "only nations with coastal cities
    /// can build."
    /// </summary>
    public static readonly RejectionCode CityNotCoastal = new("naval.city-not-coastal");

    /// <summary>The requested ship count falls outside <c>[OrderMinShips, OrderMaxShips]</c>.</summary>
    public static readonly RejectionCode ShipsOutOfRange = new("naval.ships-out-of-range");

    /// <summary>The requested new fleet id already names an existing fleet.</summary>
    public static readonly RejectionCode DuplicateFleetId = new("naval.duplicate-fleet-id");
}
