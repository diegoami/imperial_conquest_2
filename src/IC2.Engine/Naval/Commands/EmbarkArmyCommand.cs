using IC2.Engine.Core;

namespace IC2.Engine.Naval.Commands;

/// <summary>
/// Embarks an army aboard a co-located fleet — <c>docs/task-catalogue.md</c> "T14 Naval", Done-when 3:
/// an army of more than <c>ships × 500</c> troops is refused with a typed rejection under both rulesets;
/// exactly <c>ships × 500</c> is accepted. <c>TUnitMap_SelectUnit</c>'s own embark check
/// (<c>decompiled-unit-map-orders-and-record-fields.md</c>): <c>if (fleet.ships &lt; troops / 500) reject
/// else embark</c> — this handler reproduces exactly that refusal, for every seat. The report also notes
/// an AI-only troop trim inside <c>FUN_0044F8FC</c> past this same check, but that function was never
/// decompiled and neither report the T14 entry names for this area
/// (<c>mobilization-movement-and-city-capture-modes.md</c>) describes fleet embarkation trimming at
/// all — searched and came up empty — so per the entry's own instruction this handler implements
/// outright refusal, the evidence this task <em>does</em> have, for both seat types, rather than
/// guessing at an unconfirmed trim.
/// </summary>
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

    /// <summary>The army and the fleet are not on the same tile.</summary>
    public static readonly RejectionCode NotCoLocated = new("naval.not-co-located");

    /// <summary>The fleet is still under construction.</summary>
    public static readonly RejectionCode UnderConstruction = new("naval.under-construction");

    /// <summary>The fleet is already carrying a different army.</summary>
    public static readonly RejectionCode FleetAlreadyCarrying = new("naval.fleet-already-carrying");

    /// <summary>The army is already embarked.</summary>
    public static readonly RejectionCode ArmyAlreadyEmbarked = new("naval.army-already-embarked");

    /// <summary><em>"The army is too large for this fleet."</em></summary>
    public static readonly RejectionCode ArmyTooLarge = new("naval.army-too-large");
}
