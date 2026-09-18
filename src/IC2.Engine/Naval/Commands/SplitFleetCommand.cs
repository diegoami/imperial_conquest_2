using IC2.Engine.Core;

namespace IC2.Engine.Naval.Commands;

/// <summary>
/// Splits <paramref name="ShipsToNewFleet"/> ships off an existing fleet into a new one —
/// <c>docs/task-catalogue.md</c> "T14 Naval", Done-when 5 and 6: needs at least
/// <see cref="Model.NavalRules.SplitMinShips"/> (20) ships, refused while carrying an army. Ships
/// conserve exactly, the one fact <c>decompiled-unit-map-orders-and-record-fields.md</c> confirms for
/// <c>TUnitMap_SplitFleet</c> (<c>0x00447CAC</c>); that report gives no field-initialisation detail for
/// the new fleet's money or supply the way the army-side split's real observed example does (see
/// <c>pending-offer-block-army-split-and-naupactus.md</c>, T15's), and
/// <c>mobilization-movement-and-city-capture-modes.md</c> — the other report the T14 entry names for
/// this area — is silent on fleet splitting entirely, so both start at 0
/// <c>[designed, no confirmed field-init evidence for a fleet split's money/supply]</c>: what was
/// searched is exactly those two reports, and neither states what the original initialises.
/// </summary>
/// <remarks>
/// <c>[open]</c> (first review, N8): the confirmed report also states split "can fail with 'You can not
/// make any more fleets at this time.' (fleet-table cap)" — a global cap on the number of live fleet
/// records, distinct from <see cref="Model.NavalRules.SplitMinShips"/>. No report gives the cap's actual
/// value (the army table's analogous 198-army cap is confirmed and is T15's, not transferable here
/// without evidence), so this handler does not invent one. Flagged rather than implemented.
/// </remarks>
/// <param name="NewFleetId">The new fleet's id — see <see cref="OrderFleetCommand.NewFleetId"/>'s remarks.</param>
/// <param name="ShipsToNewFleet">How many ships move to the new fleet; the rest stay with <see cref="FleetId"/>.</param>
public sealed record SplitFleetCommand(string IssuingNationId, string FleetId, string NewFleetId, int ShipsToNewFleet) : ICommand
{
    /// <inheritdoc/>
    public string Kind => "naval.split-fleet";
}

/// <summary>Rejection codes this command's handler declares, in its own directory — see <see cref="RejectionCode"/>.</summary>
public static class SplitFleetRejections
{
    /// <summary>The command names a fleet id that does not exist.</summary>
    public static readonly RejectionCode UnknownFleet = new("naval.unknown-fleet");

    /// <summary>The named fleet belongs to a nation other than the one issuing the command.</summary>
    public static readonly RejectionCode NotYourFleet = new("naval.not-your-fleet");

    /// <summary>The fleet has fewer than <see cref="Model.NavalRules.SplitMinShips"/> ships.</summary>
    public static readonly RejectionCode TooFewShipsToSplit = new("naval.too-few-ships-to-split");

    /// <summary>
    /// A fleet carrying an army refuses repair, scuttle, split and join —
    /// <c>docs/task-catalogue.md</c> "T14 Naval" Done-when 5.
    /// </summary>
    public static readonly RejectionCode CarryingArmy = new("naval.carrying-army");

    /// <summary><paramref name="SplitFleetCommand.ShipsToNewFleet"/> is not between 1 and ships − 1.</summary>
    public static readonly RejectionCode InvalidShipCount = new("naval.invalid-ship-count");

    /// <summary>The requested new fleet id already names an existing fleet.</summary>
    public static readonly RejectionCode DuplicateFleetId = new("naval.duplicate-fleet-id");

    /// <summary>The fleet is under construction and cannot be split.</summary>
    public static readonly RejectionCode UnderConstruction = new("naval.under-construction");
}
