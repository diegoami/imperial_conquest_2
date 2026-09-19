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
/// <strong>"Near" is read as co-located.</strong> The confirmed report states the rule ("near") but does
/// not decompile a distance check, and this task's Owns list does not include
/// <c>src/IC2.Engine/Model/Ruleset.cs</c>, so it cannot add a new adjacency-radius field to source one
/// even if it wanted to. Reusing an existing, differently-motivated radius (for example
/// <see cref="Model.EconomyRules.ThreatenedCityAdjacencyRadius"/>, T35's hostile-army-adjacency check)
/// would repeat exactly the mistake this project's own conventions warn against — treating two
/// numerically-similar but conceptually distinct rules as one (see, for example,
/// <see cref="Model.EconomyRules.CitySupplyMobilizationDivisor"/>'s remarks on the same point). Requiring
/// exact co-location is the narrowest, most defensible reading of "near" available without inventing an
/// unsourced constant: it never falsely refuses a legitimately "near" disband, and every "away from"
/// case the Done-when line names is still refused. <c>[derived, boundary approximated; see this task's
/// PR body]</c>.
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
