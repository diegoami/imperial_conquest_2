using IC2.Engine.Core;

namespace IC2.Engine.Naval.Commands;

/// <summary>
/// Transfers ships, supplies and money from one of the issuing nation's own, co-located fleets to
/// another — <c>docs/task-catalogue.md</c> "T14 Naval" DoD 16: <c>TUnitMap_FleetToFleetTransfer</c>
/// (<c>0x004479F4</c>) opens <c>TFleetToFleet</c>, the naval twin of <c>TArmyToArmy</c>, a reciprocal
/// ships/supplies/money transfer committed on OK. Each amount is a request, clamped to what
/// <see cref="SourceFleetId"/> actually holds — never a rejection for exceeding it, the same
/// clamp-not-reject convention T38 established for the supply dialog. A source left at 0 ships after the
/// transfer is deleted, per the report's own note.
/// </summary>
/// <param name="Ships">Ships requested to move from source to target.</param>
/// <param name="SupplyTons">Supply tons requested to move from source to target.</param>
/// <param name="Money">Talents requested to move from source to target.</param>
public sealed record FleetToFleetTransferCommand(
    string IssuingNationId, string SourceFleetId, string TargetFleetId, int Ships, int SupplyTons, int Money) : ICommand
{
    /// <inheritdoc/>
    public string Kind => "naval.fleet-to-fleet-transfer";
}

/// <summary>Rejection codes this command's handler declares, in its own directory — see <see cref="RejectionCode"/>.</summary>
public static class FleetToFleetTransferRejections
{
    /// <summary>Either named fleet id does not exist.</summary>
    public static readonly RejectionCode UnknownFleet = new("naval.unknown-fleet");

    /// <summary>Either named fleet belongs to a nation other than the one issuing the command.</summary>
    public static readonly RejectionCode NotYourFleet = new("naval.not-your-fleet");

    /// <summary>The two fleets are not on the same tile.</summary>
    public static readonly RejectionCode NotCoLocated = new("naval.not-co-located");

    /// <summary>The two named fleet ids are the same fleet.</summary>
    public static readonly RejectionCode SameFleet = new("naval.same-fleet");

    /// <summary>Either fleet is still under construction.</summary>
    public static readonly RejectionCode UnderConstruction = new("naval.under-construction");

    /// <summary>Every requested amount is zero or negative -- nothing to transfer.</summary>
    public static readonly RejectionCode NothingRequested = new("naval.nothing-requested");
}
