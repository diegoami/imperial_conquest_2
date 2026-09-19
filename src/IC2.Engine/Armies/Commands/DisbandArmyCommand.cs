using IC2.Engine.Core;

namespace IC2.Engine.Armies.Commands;

/// <summary>
/// Disbands an army at one of the issuing nation's own cities — <c>docs/task-catalogue.md</c> "T15 Army
/// and unit management", Done-when 3: refused away from an owned city; the army's money moves to the
/// national treasury and its supplies to the city's stock, both conserved exactly.
/// </summary>
/// <remarks>
/// <para>
/// <c>TUnitMap_DisbandArmy</c> (<c>0x004476AC</c>) <strong>[confirmed:
/// decompiled-unit-map-orders-and-record-fields.md]</strong>: <em>"An army must be near its own
/// city to disband."</em> Confirmation prompt. The army's money goes to the national treasury and its
/// supplies to the nearby city's stock.
/// </para>
/// <para>
/// <strong>"Near" is an adjoining tile — widened from co-location by T54, on evidence that did not exist
/// when T15 chose the narrower reading</strong> (<c>docs/task-catalogue.md</c> T54 Done-when 4,
/// <see href="https://github.com/diegoami/imperial_conquest_2/issues/215">issue #215</see>). T15 read
/// "near" as exact co-location: the report states the rule but decompiles no distance check, so the
/// narrowest defensible reading was the honest one, it was tagged <c>[derived]</c>, and its reviewer
/// judged it sound. It was also, as it turned out, <strong>unreachable</strong> — an army can never stand
/// on a city's tile in this game, so no army could ever satisfy it. A city is map code <c>20..99</c> and
/// the original's movement walk steps only codes <c>2..11</c>: "codes ≥ 12 are not steppable at all by
/// this walk" <strong>[confirmed: terrain-move-cost-table-in-dat.md]</strong>, which is exactly why
/// <c>MoveArmyCommandHandler</c> blocks every city cell. The user settled what "near" therefore has to
/// mean, from play: <em>"first you select the army, then the town, and if they are adjacent there is a
/// siege action. Same pattern for an army attacking an army, or a fleet attacking a fleet."</em>
/// <strong>[confirmed: attack-and-siege-are-adjacency-orders.md — direct user observation of the original game, 2026-09-19]</strong> — a city is
/// interacted with from an adjoining tile, never from its own.
/// </para>
/// <para>
/// <strong>No new constant, and deliberately not a reused one.</strong> Adjacency here is inherent to the
/// order rather than a tunable radius — it is the same "standing next to it" the attack and siege gates
/// in <c>Battle/Commands/AttackLegality</c> apply, and the same literal one-tile bound the merged supply
/// and disembark gates already carry from <c>TAFSupply_FindProviders</c>'s confirmed "within one tile"
/// provider radius — so no <see cref="Model.Ruleset"/> field is added. In particular it does <em>not</em>
/// reuse <see cref="Model.EconomyRules.ThreatenedCityAdjacencyRadius"/>, which is T35's
/// hostile-army-adjacency rule: numerically similar, conceptually a different rule, and conflating the
/// two is the mistake this project's own conventions warn against (see
/// <see cref="Model.EconomyRules.CitySupplyMobilizationDivisor"/>'s remarks on the same point).
/// </para>
/// <para>
/// <strong>Embarked armies are refused defensively</strong>, the same reasoning as
/// <c>SplitArmyCommand</c>'s: an embarked army's <c>(X, Y)</c> are the carrying fleet's own snapped
/// coordinates, not a real map cell, so a fleet moored at one of the nation's own coastal cities could
/// otherwise satisfy the co-location check while still being referenced by that fleet's
/// <see cref="Model.FleetState.CarriedArmyId"/> — deleting the army out from under the fleet without
/// updating it, exactly the dangling reference <c>docs/build-process.md</c> §4.2 gate 5 looks for.
/// <c>[designed, no confirmed evidence either way]</c>.
/// </para>
/// </remarks>
public sealed record DisbandArmyCommand(string IssuingNationId, string ArmyId) : ICommand
{
    /// <inheritdoc/>
    public string Kind => "armies.disband-army";
}

/// <summary>Rejection codes this command's handler declares, in its own directory — see <see cref="RejectionCode"/>.</summary>
public static class DisbandArmyRejections
{
    /// <summary>The command names an army id that does not exist.</summary>
    public static readonly RejectionCode UnknownArmy = new("armies.unknown-army");

    /// <summary>The named army belongs to a nation other than the one issuing the command.</summary>
    public static readonly RejectionCode NotYourArmy = new("armies.not-your-army");

    /// <summary>The army is aboard a fleet — see <see cref="DisbandArmyCommand"/>'s remarks.</summary>
    public static readonly RejectionCode ArmyEmbarked = new("armies.army-embarked");

    /// <summary><em>"An army must be near its own city to disband."</em></summary>
    public static readonly RejectionCode NotNearOwnCity = new("armies.not-near-own-city");
}
