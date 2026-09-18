using IC2.Engine.Core;

namespace IC2.Engine.Naval.Commands;

/// <summary>
/// Joins two of the issuing nation's own, co-located fleets — <c>docs/task-catalogue.md</c> "T14 Naval",
/// Done-when 5 and 6: combined ships capped at 100, refused while either carries an army. Ships,
/// supplies and money add; the survivor's moves are zeroed; <paramref name="AbsorbedFleetId"/> is
/// deleted.
/// </summary>
public sealed record JoinFleetsCommand(string IssuingNationId, string SurvivingFleetId, string AbsorbedFleetId) : ICommand
{
    /// <inheritdoc/>
    public string Kind => "naval.join-fleets";
}

/// <summary>Rejection codes this command's handler declares, in its own directory — see <see cref="RejectionCode"/>.</summary>
public static class JoinFleetsRejections
{
    /// <summary>Either named fleet id does not exist.</summary>
    public static readonly RejectionCode UnknownFleet = new("naval.unknown-fleet");

    /// <summary>Either named fleet belongs to a nation other than the one issuing the command.</summary>
    public static readonly RejectionCode NotYourFleet = new("naval.not-your-fleet");

    /// <summary>The two fleets are not on the same tile.</summary>
    public static readonly RejectionCode NotCoLocated = new("naval.not-co-located");

    /// <summary>
    /// A fleet carrying an army refuses repair, scuttle, split and join —
    /// <c>docs/task-catalogue.md</c> "T14 Naval" Done-when 5.
    /// </summary>
    public static readonly RejectionCode CarryingArmy = new("naval.carrying-army");

    /// <summary><em>"There are more than 100 ships in these fleets combined."</em></summary>
    public static readonly RejectionCode CombinedShipsTooLarge = new("naval.combined-ships-too-large");

    /// <summary>The two named fleet ids are the same fleet.</summary>
    public static readonly RejectionCode SameFleet = new("naval.same-fleet");

    /// <summary>Either fleet is still under construction.</summary>
    public static readonly RejectionCode UnderConstruction = new("naval.under-construction");
}
