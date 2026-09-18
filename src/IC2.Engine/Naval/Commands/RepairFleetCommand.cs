using IC2.Engine.Core;

namespace IC2.Engine.Naval.Commands;

/// <summary>
/// Orders a fleet repaired at one of its own nation's cities — <c>docs/task-catalogue.md</c>
/// "T14 Naval", Done-when 4 and 5. Cost <c>ships × points / 5</c>, zeroes moves, refused away from an
/// owned city and while carrying an army.
/// </summary>
/// <remarks>
/// Noted, not fixed (first review, N2, see <see cref="OrderFleetCommand"/>'s remarks for the same
/// point): this handler debits the treasury unconditionally and can drive it negative. No Done-when line
/// requires an affordability check.
/// </remarks>
public sealed record RepairFleetCommand(string IssuingNationId, string FleetId, int Points) : ICommand
{
    /// <inheritdoc/>
    public string Kind => "naval.repair-fleet";
}

/// <summary>Rejection codes this command's handler declares, in its own directory — see <see cref="RejectionCode"/>.</summary>
public static class RepairFleetRejections
{
    /// <summary>The command names a fleet id that does not exist.</summary>
    public static readonly RejectionCode UnknownFleet = new("naval.unknown-fleet");

    /// <summary>The named fleet belongs to a nation other than the one issuing the command.</summary>
    public static readonly RejectionCode NotYourFleet = new("naval.not-your-fleet");

    /// <summary>
    /// <em>"The fleet can only be repaired at one of your cities."</em> — the fleet is not adjacent to
    /// one of its own nation's cities (a launched fleet only ever occupies a sea tile, so "at" means
    /// adjacent, not an exact tile match — see <see cref="IC2.Engine.Naval.CoastalCity"/>'s remarks).
    /// </summary>
    public static readonly RejectionCode NotAtOwnedCity = new("naval.not-at-owned-city");

    /// <summary>
    /// A fleet carrying an army refuses repair, scuttle, split and join —
    /// <c>docs/task-catalogue.md</c> "T14 Naval" Done-when 5.
    /// </summary>
    public static readonly RejectionCode CarryingArmy = new("naval.carrying-army");

    /// <summary>The requested points are not positive.</summary>
    public static readonly RejectionCode InvalidPoints = new("naval.invalid-points");

    /// <summary>The fleet is under construction and cannot be repaired.</summary>
    public static readonly RejectionCode UnderConstruction = new("naval.under-construction");
}
