using IC2.Engine.Core;

namespace IC2.Engine.Naval.Commands;

/// <summary>
/// <c>TUnitMap_FleetToFleetTransfer</c> (<c>0x004479F4</c>) → <c>TFleetToFleet</c>: moves ships, supply
/// tons and money from one of the issuing nation's own, co-located fleets to another, refusing while
/// either carries an army — <c>docs/task-catalogue.md</c> "T46 Fleet-to-fleet transfer, and the supply
/// path that keeps fleets alive" (issue #148), Done-when 1-5.
/// </summary>
/// <remarks>
/// <para>
/// <strong>"Reciprocal", per the decompiled description</strong>
/// [confirmed: decompiled-unit-map-orders-and-record-fields.md line 81, "reciprocal ships/supply/money
/// transfer, the naval twin of <c>TArmyToArmy</c>"] <strong>and its own naval-twin pointer to
/// <c>TArmyToArmy</c></strong> [confirmed: army-to-army-transfer-confirmed.md]: the dialog's stepper
/// controls let a player move ships/supply/money either direction inside one screen. This command exposes
/// a single direction per call — <see cref="FromFleetId"/> to <see cref="ToFleetId"/> — and a caller
/// wanting the opposite direction issues the command with the two ids swapped; the net effect on both
/// fleets' books is identical to the dialog's own <c>+n</c>/<c>-n</c> reciprocal pair.
/// </para>
/// <para>
/// <strong>DoD 2, the carrying-army guard.</strong> <c>TArmyToArmy</c>'s own report does not exercise the
/// aboard-a-fleet case (armies do not carry other armies), but every one of <c>TFleetToFleet</c>'s naval
/// siblings — <c>JoinFleets</c>, <c>SplitFleet</c>, <c>RepairFleet</c>, <c>ScuttleFleet</c> — refuses a
/// carrying fleet, and T14's round-2 review
/// (<see href="https://github.com/diegoami/imperial_conquest_2/pull/144#issuecomment-5730028918">PR #144
/// comment 5730028918</see>, finding B6) proved that not refusing here corrupts the save: transferring a
/// carrying fleet's last ship deleted the carrier out from under its embarked army, leaving
/// <c>army.AboardFleetId</c> pointing at nothing — a state <c>GameDataValidation</c> itself rejects with
/// <c>UnresolvedReferenceException</c>, i.e. an unsaveable, unloadable game with the army permanently
/// stranded. This handler refuses outright whenever either fleet carries an army, matching the sibling
/// orders exactly rather than trying to guess whether the army should travel with the ships.
/// </para>
/// <para>
/// <strong>DoD 3, conservation on disband.</strong> <c>army-to-army-transfer-confirmed.md</c>'s own
/// OK-commit description: "checks whether either army's unit count reads zero after the transfer, and if
/// so, merges that now-empty army's supply and money into the other and disbands it" — the confirmed twin
/// mechanism this command matches exactly at the ship-count level (a fleet's "unit count" is its ships):
/// when <see cref="FromFleetId"/>'s ships reach zero, its <em>full remaining</em> supply and money — not
/// just the requested <see cref="SupplyTons"/>/<see cref="Money"/> — pool into <see cref="ToFleetId"/>,
/// and the source fleet is removed. This is the same choice <c>JoinFleetsCommandHandler</c> makes (pool
/// into the survivor), not <c>ScuttleFleetCommandHandler</c>'s city/treasury split, because there is no
/// "near an owned city" fact available at a mid-ocean transfer, and because the confirmed army twin pools
/// into the other party to the transfer, never into a city. T14's round-2 review (finding B7) proved the
/// alternative — dropping the remainder — annihilates it: a source with 100 tons and 50 talents, emptied
/// of ships, vanished with both intact.
/// </para>
/// <para>
/// <strong>DoD 5, the 100-ship cap [designed].</strong> No report states whether <c>TFleetToFleet</c>
/// enforces <c>JoinFleets</c>' confirmed 100-ship cap (T14 round-2 review, non-blocking finding N11: two
/// 60-ship fleets transferring 50 would leave 110, uncapped by the first attempt). This command caps the
/// resulting combined ship count at the same <see cref="Model.NavalRules.JoinMaxShips"/> value
/// <c>JoinFleetsCommandHandler</c> already enforces, rather than a new literal or a new ruleset field:
/// allowing a transfer to reach a combined ship count <c>JoinFleets</c> is confirmed to refuse would make
/// the 100-ship cap enforceable only by picking the right order (join vs. transfer) for the exact same
/// outcome, which defeats the point of having a cap at all. What was searched and came up empty: the order
/// table's own transfer row (line 81) gives no numeric limit of its own, unlike its adjacent join row
/// (line 79) — the asymmetry this note records rather than resolves.
/// </para>
/// <para>
/// Moves are left untouched on both fleets: unlike <c>JoinFleetsCommandHandler</c> ("the survivor's moves
/// are zeroed", confirmed for join specifically), no report states that a partial ships/supply/money
/// transfer zeroes either side's moves, so none is invented here.
/// </para>
/// </remarks>
/// <param name="IssuingNationId">The nation issuing the order; both fleets must be its own.</param>
/// <param name="FromFleetId">The fleet giving up ships, supply and money.</param>
/// <param name="ToFleetId">The fleet receiving them.</param>
/// <param name="Ships">Ships to move — must be between 0 and <see cref="FromFleetId"/>'s own ship count.</param>
/// <param name="SupplyTons">Supply tons to move — must be between 0 and the source's own stock.</param>
/// <param name="Money">Money to move — must be between 0 and the source's own purse.</param>
public sealed record FleetToFleetTransferCommand(
    string IssuingNationId,
    string FromFleetId,
    string ToFleetId,
    int Ships,
    int SupplyTons,
    int Money) : ICommand
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

    /// <summary>The two named fleet ids are the same fleet.</summary>
    public static readonly RejectionCode SameFleet = new("naval.same-fleet");

    /// <summary>Either fleet is still under construction.</summary>
    public static readonly RejectionCode UnderConstruction = new("naval.under-construction");

    /// <summary>The two fleets are not on the same tile.</summary>
    public static readonly RejectionCode NotCoLocated = new("naval.not-co-located");

    /// <summary>
    /// A fleet carrying an army refuses transfer, like every sibling order — <c>docs/task-catalogue.md</c>
    /// "T46" Done-when 2.
    /// </summary>
    public static readonly RejectionCode CarryingArmy = new("naval.carrying-army");

    /// <summary>A requested amount is negative, or all three requested amounts are zero.</summary>
    public static readonly RejectionCode InvalidAmount = new("naval.invalid-amount");

    /// <summary>The source fleet does not have enough ships to move the requested amount.</summary>
    public static readonly RejectionCode InsufficientShips = new("naval.insufficient-ships");

    /// <summary>The source fleet does not have enough supply to move the requested amount.</summary>
    public static readonly RejectionCode InsufficientSupply = new("naval.insufficient-supply");

    /// <summary>The source fleet does not have enough money to move the requested amount.</summary>
    public static readonly RejectionCode InsufficientMoney = new("naval.insufficient-money");

    /// <summary>
    /// The resulting combined ship count would exceed <see cref="Model.NavalRules.JoinMaxShips"/> —
    /// Done-when 5's <c>[designed]</c> cap; see this command's remarks.
    /// </summary>
    public static readonly RejectionCode CombinedShipsTooLarge = new("naval.combined-ships-too-large");
}
