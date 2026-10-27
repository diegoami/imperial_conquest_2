using IC2.Engine.Core;

namespace IC2.Engine.Naval.Commands;

/// <summary>
/// Embarks an army aboard a co-located fleet — <c>docs/task-catalogue.md</c> "T14 Naval", Done-when 3
/// (rewritten 2026-09-18, commit <c>87bec66</c>: the original DoD 3 said "refused ... under both
/// rulesets", which contradicted <c>design-audit.md</c> Q6 — already answered by the user — and
/// <c>build-process.md</c> §9 Q-D, which names this task for the <c>seatAsymmetry</c> flag. The answered
/// governance question wins over the DoD line that contradicted it).
/// </summary>
/// <remarks>
/// <para>
/// <c>TUnitMap_SelectUnit</c>'s own embark check (<c>decompiled-unit-map-orders-and-record-fields.md</c>):
/// <c>if (fleet.ships &lt; troops / 500) reject "The army is too large for this fleet ?" else embark</c>
/// — <strong>this is the human UI path</strong>: a player selects an army, then clicks a friendly fleet,
/// and the dialog simply refuses an over-capacity request. Embarking itself calls <c>FUN_0044B79C</c>,
/// whose AI-only over-capacity branch calls <c>FUN_0044F8FC</c> to land whole overflow slots
/// (2026-10-24-strategic-economy-diplomacy-fleet-continuation.md, Research PR74). The two are the same function's two branches, not
/// two competing readings: a human seat never reaches the trim because the UI check above refuses first;
/// an AI nation's order presumably bypasses that dialog check and reaches <c>FUN_0044B79C</c> directly,
/// where the trim applies.
/// </para>
/// <para>
/// Exactly <c>ships × 500</c> troops is always accepted unchanged, in every case. Above it:
/// <list type="bullet">
/// <item><description>
/// <c>classical-faithful</c> (<see cref="Model.SeatAsymmetryModel.Faithful"/>): a <strong>human</strong>
/// seat compares <c>troops div transportTroopsPerShip</c> with ships (Research83/T221), so ten ships
/// admit up to 5,499 and refuse 5,500 with <see cref="EmbarkArmyRejections.ArmyTooLarge"/>. An <strong>AI</strong> seat
/// lands every whole unit from the first slot that would exceed capacity.
/// A receiver that cannot be made safely refuses atomically under T216's owner-approved interim policy.
/// </description></item>
/// <item><description>
/// <c>improved</c> (<see cref="Model.SeatAsymmetryModel.Normalized"/>): every seat is refused with the
/// same rejection — silently destroying a player's troops is not a rule worth generalising to human
/// seats, so this preset normalises toward refusal rather than toward the trim. This is the
/// <strong>opposite</strong> direction from T09's own <c>seatAsymmetry</c> case (there, <c>improved</c>
/// generalises the AI's move-zeroing to every seat); the two are decided independently, on their own
/// merits, not by a blanket "improved always does X" rule.
/// </description></item>
/// </list>
/// The original F8FC was unavailable to the earlier report; Research PR74 now confirms its
/// whole-slot transfer on the measured free-receiver branch. It does not claim the original's
/// unmeasured receiver-failure outcome, for which this remake deliberately refuses before mutation.
/// </para>
/// </remarks>
public sealed record EmbarkArmyCommand(string IssuingNationId, string ArmyId, string FleetId) : ICommand
{
    /// <inheritdoc/>
    public string Kind => "naval.embark-army";
}

/// <summary>Rejection codes this command's handler declares, in its own directory — see <see cref="RejectionCode"/>.</summary>
public static class EmbarkArmyRejections
{
    /// <summary>The command names an army id that does not exist.</summary>
    public static readonly RejectionCode UnknownArmy = new("naval.unknown-army");

    /// <summary>The command names a fleet id that does not exist.</summary>
    public static readonly RejectionCode UnknownFleet = new("naval.unknown-fleet");

    /// <summary>Either the army or the fleet belongs to a nation other than the one issuing the command.</summary>
    public static readonly RejectionCode NotYours = new("naval.not-yours");

    /// <summary>The army is not adjacent to the fleet (within <c>Economy.CommandAdjacencyRadiusTiles</c>, per T93).</summary>
    public static readonly RejectionCode NotAdjacent = new("naval.not-adjacent");

    /// <summary>The fleet is still under construction.</summary>
    public static readonly RejectionCode UnderConstruction = new("naval.under-construction");

    /// <summary>The fleet is already carrying a different army.</summary>
    public static readonly RejectionCode FleetAlreadyCarrying = new("naval.fleet-already-carrying");

    /// <summary>The army is already embarked.</summary>
    public static readonly RejectionCode ArmyAlreadyEmbarked = new("naval.army-already-embarked");

    /// <summary><em>"The army is too large for this fleet."</em></summary>
    public static readonly RejectionCode ArmyTooLarge = new("naval.army-too-large");

    /// <summary>An AI over-capacity embark cannot make a validated land receiver; refuse without troop loss.</summary>
    public static readonly RejectionCode NoSafeOverflowReceiver = new("naval.no-safe-overflow-receiver");
}
