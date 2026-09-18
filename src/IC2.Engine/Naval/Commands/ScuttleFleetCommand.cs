using IC2.Engine.Core;

namespace IC2.Engine.Naval.Commands;

/// <summary>
/// Scuttles a fleet near one of its own nation's cities — <c>docs/task-catalogue.md</c> "T14 Naval",
/// Done-when 5 and 7: money to the treasury, supplies to the city, refused while carrying an army.
/// </summary>
public sealed record ScuttleFleetCommand(string IssuingNationId, string FleetId) : ICommand
{
    /// <inheritdoc/>
    public string Kind => "naval.scuttle-fleet";
}

/// <summary>Rejection codes this command's handler declares, in its own directory — see <see cref="RejectionCode"/>.</summary>
public static class ScuttleFleetRejections
{
    /// <summary>The command names a fleet id that does not exist.</summary>
    public static readonly RejectionCode UnknownFleet = new("naval.unknown-fleet");

    /// <summary>The named fleet belongs to a nation other than the one issuing the command.</summary>
    public static readonly RejectionCode NotYourFleet = new("naval.not-your-fleet");

    /// <summary><em>"Must be near one of your own cities."</em></summary>
    public static readonly RejectionCode NotNearOwnedCity = new("naval.not-near-owned-city");

    /// <summary>
    /// A fleet carrying an army refuses repair, scuttle, split and join —
    /// <c>docs/task-catalogue.md</c> "T14 Naval" Done-when 5.
    /// </summary>
    public static readonly RejectionCode CarryingArmy = new("naval.carrying-army");

    /// <summary>The fleet is under construction and cannot be scuttled.</summary>
    public static readonly RejectionCode UnderConstruction = new("naval.under-construction");
}
