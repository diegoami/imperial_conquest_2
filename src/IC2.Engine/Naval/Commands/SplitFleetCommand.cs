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
/// this area — is silent on fleet splitting entirely, so the two allocation fields default to 0
/// <c>[designed, no confirmed field-init evidence for a fleet split's money/supply]</c>: what was
/// searched is exactly those two reports, and neither states what the original initialises. The original's
/// Split fleet dialog is a two-column table of the first and the second fleet's ships, supply and money,
/// with 1s/10s arrows for ships and 10s/100s for supply and money
/// <strong>[Wine candidate: <c>2026-10-02-fleet-orders-live.md</c> (research <c>38b4c01</c>),
/// <c>T_SPLIT_FLEET.SAV</c>; <c>TFleetToFleet</c> is opened by Split fleet]</strong>, so
/// <see cref="SupplyTonsToNewFleet"/> and <see cref="MoneyToNewFleet"/> model the spinners' committed
/// values, each between 0 and what the parent holds. There is no capacity rebalance and no purse bound
/// <c>[designed: <c>TFleetToFleet_OK</c> is unread, and <c>fleet-transfer</c> applies neither]</c>. The new
/// fleet stands one tile from the parent (bugs #584 and #596) so it can rejoin immediately.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Where the new fleet stands.</strong> The army creator <c>FUN_00449F08</c> places its new
/// unit with <c>FUN_004492C0</c>'s 3×3 last-qualifying-cell scan
/// (<c>decompiled-mobilization-and-mercenary-restock.md</c> §3, confirmed for mobilisation, and reached
/// by the army split by the same form). Which routine places a split fleet is unread, so this uses the
/// same scan restricted to <see cref="Model.TileType.PassableByFleets"/> water — <c>[designed]</c> by
/// analogy. It fits the one observation: fleet 2 at <c>(101,46)</c> splits to fleet 5 at
/// <c>(101,47)</c>, the last sea cell of the block, where <c>(+1, +1)</c> is land. When no cell
/// qualifies the split is refused with <see cref="SplitFleetRejections.NoFreeAdjacentTile"/>
/// <c>[designed]</c>; what the original's split shows then is unread. Wine candidates:
/// <c>2026-10-02-unit-map-mouse-orders-and-tax-range.md</c> observation (e) and its review, and
/// <c>2026-10-02-fleet-orders-live.md</c> (<c>T_SPLIT_FLEET.SAV</c>).
/// </para>
/// <para>
/// <c>[open]</c> (first review, N8): the confirmed report also states split "can fail with 'You can not
/// make any more fleets at this time.' (fleet-table cap)" — a global cap on the number of live fleet
/// records, distinct from <see cref="Model.NavalRules.SplitMinShips"/>. No report gives the cap's actual
/// value (the army table's analogous 198-army cap is confirmed and is T15's, not transferable here
/// without evidence), so this handler does not invent one. Flagged rather than implemented.
/// </para>
/// </remarks>
/// <param name="NewFleetId">The new fleet's id — see <see cref="OrderFleetCommand.NewFleetId"/>'s remarks.</param>
/// <param name="ShipsToNewFleet">How many ships move to the new fleet; the rest stay with <see cref="FleetId"/>.</param>
/// <param name="SupplyTonsToNewFleet">
/// Supply tons moved from the parent fleet's stock to the new fleet's, between 0 and what the parent holds
/// (<see cref="SplitFleetRejections.InvalidSupplyAllocation"/>). Defaults to 0; no rebalance applies.
/// </param>
/// <param name="MoneyToNewFleet">
/// Talents moved from the parent fleet's purse to the new fleet's, between 0 and what the parent holds
/// (<see cref="SplitFleetRejections.InvalidMoneyAllocation"/>). Defaults to 0; no purse bound applies.
/// </param>
public sealed record SplitFleetCommand(
    string IssuingNationId,
    string FleetId,
    string NewFleetId,
    int ShipsToNewFleet,
    int SupplyTonsToNewFleet = 0,
    int MoneyToNewFleet = 0) : ICommand
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

    /// <summary>
    /// <see cref="SplitFleetCommand.SupplyTonsToNewFleet"/> is negative or more than the parent fleet
    /// holds — the dialog's supply spinner bound.
    /// </summary>
    public static readonly RejectionCode InvalidSupplyAllocation = new("naval.invalid-supply-allocation");

    /// <summary>
    /// <see cref="SplitFleetCommand.MoneyToNewFleet"/> is negative or more than the parent fleet holds —
    /// the dialog's money spinner bound.
    /// </summary>
    public static readonly RejectionCode InvalidMoneyAllocation = new("naval.invalid-money-allocation");

    /// <summary>The requested new fleet id already names an existing fleet.</summary>
    public static readonly RejectionCode DuplicateFleetId = new("naval.duplicate-fleet-id");

    /// <summary>The fleet is under construction and cannot be split.</summary>
    public static readonly RejectionCode UnderConstruction = new("naval.under-construction");

    /// <summary>
    /// No cell in the parent fleet's 3×3 block is free water for the new fleet — the placement scan
    /// finding nothing. See <see cref="SplitFleetCommand"/>'s remarks.
    /// </summary>
    public static readonly RejectionCode NoFreeAdjacentTile = new("naval.no-free-adjacent-tile");
}
