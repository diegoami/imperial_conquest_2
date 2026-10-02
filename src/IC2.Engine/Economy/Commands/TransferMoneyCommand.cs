using IC2.Engine.Core;

namespace IC2.Engine.Economy.Commands;

/// <summary>
/// Moves talents between the national treasury — or a co-located fleet — and one army's or fleet's own
/// purse — <c>docs/tasks/T105.md</c> "A command moves money between the treasury and a purse". The
/// original's <c>TAFSupply_ChangeMoney</c> panel moves money "between the treasury, or a co-located fleet,
/// and the army, capped at 1,000" <strong>[confirmed:
/// <c>docs/investigations/decompiled-unit-map-orders-and-record-fields.md</c>;
/// <c>docs/investigations/original-ui-command-audit.md</c> §1.6]</strong>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The sign of <see cref="Amount"/> picks the direction.</strong> A positive amount moves talents
/// <em>into</em> the named unit's purse (from the treasury, or from the <c>via</c> fleet); a negative one
/// moves that many <em>out</em> of it (back to the treasury, or to the <c>via</c> fleet). The wrapped
/// rule — the per-move 1,000 ceiling, the receiving purse's cap and the source-balance clamp — lives in
/// <see cref="TreasuryPurseTransfer"/>, never re-implemented here.
/// </para>
/// <para>
/// <strong>The per-move ceiling reuses <c>PurseCapPerUnit</c>, never a literal.</strong> The one
/// "capped at 1,000 money per army or fleet" sentence names both this move's ceiling and the purse cap,
/// which the ruleset already holds once as
/// <see cref="Model.EconomyRules.PurseCapPerUnit"/>. T105's own first Hazard keeps both, so the handler
/// reads the per-move ceiling from that same field rather than spelling a second 1,000.
/// </para>
/// <para>
/// <strong>The co-located fleet</strong> is the same dialog's own provider radius
/// (<c>TAFSupply_FindProviders</c>, audit §1.6): an own fleet within
/// <see cref="Model.EconomyRules.CommandAdjacencyRadiusTiles"/> of the named unit, or the fleet carrying
/// the named army. It is never the named unit itself.
/// </para>
/// </remarks>
/// <param name="IssuingNationId">The nation issuing the command; both sides must be its own.</param>
/// <param name="UnitId">The army or fleet whose purse receives a positive amount (and funds a negative one).</param>
/// <param name="Amount">
/// The signed move in talents. Positive is into <see cref="UnitId"/>'s purse; negative is out of it.
/// Zero is rejected. Magnitude is at most <see cref="Model.EconomyRules.PurseCapPerUnit"/>.
/// </param>
/// <param name="ViaFleetId">
/// The own fleet whose purse stands in for the national treasury as the other side, or <see langword="null"/>
/// for the treasury itself. Must be co-located with (or carrying) <see cref="UnitId"/>.
/// </param>
public sealed record TransferMoneyCommand(
    string IssuingNationId,
    string UnitId,
    int Amount,
    string? ViaFleetId = null) : ICommand
{
    /// <inheritdoc/>
    public string Kind => "economy.transfer-money";
}

/// <summary>Rejection codes this command's handler declares, in its own directory — see <see cref="RejectionCode"/>.</summary>
public static class TransferMoneyRejections
{
    /// <summary>The named unit id is neither a known army nor a known fleet.</summary>
    public static readonly RejectionCode UnknownUnit = new("economy.unknown-unit");

    /// <summary>The named unit exists but belongs to a nation other than the issuing one.</summary>
    public static readonly RejectionCode NotYourUnit = new("economy.not-your-unit");

    /// <summary>The requested move is zero.</summary>
    public static readonly RejectionCode InvalidAmount = new("economy.invalid-transfer-amount");

    /// <summary>
    /// The requested move's magnitude is past the per-move ceiling
    /// (<see cref="Model.EconomyRules.PurseCapPerUnit"/>, reused — see <see cref="TransferMoneyCommand"/>
    /// and its first Hazard). A move inside the ceiling is <em>accepted</em>, at whatever the source and
    /// receiving purse clamp it to.
    /// </summary>
    public static readonly RejectionCode AmountExceedsMoveLimit = new("economy.transfer-amount-too-large");

    /// <summary>The command names a <c>via</c> fleet id that is not a known fleet.</summary>
    public static readonly RejectionCode UnknownViaFleet = new("economy.unknown-via-fleet");

    /// <summary>The named <c>via</c> fleet belongs to a nation other than the issuing one.</summary>
    public static readonly RejectionCode ViaFleetNotYours = new("economy.via-fleet-not-yours");

    /// <summary>The named <c>via</c> fleet is the named unit itself.</summary>
    public static readonly RejectionCode ViaFleetIsTheUnit = new("economy.via-fleet-is-the-unit");

    /// <summary>The named <c>via</c> fleet is neither within the provider radius nor carrying the named unit.</summary>
    public static readonly RejectionCode ViaFleetNotWithinRange = new("economy.via-fleet-not-within-range");
}
