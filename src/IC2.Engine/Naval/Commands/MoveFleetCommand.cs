using IC2.Engine.Core;

namespace IC2.Engine.Naval.Commands;

/// <summary>
/// Orders a launched fleet to walk toward <c>(<see cref="X"/>, <see cref="Y"/>)</c> — the naval twin of
/// T09's <c>movement.move-army</c> (<c>docs/task-catalogue.md</c> "T41 Thin CLI demo on the toy world"),
/// entirely in terms of T09's <see cref="Movement.MovementWalker"/>/<see cref="Movement.TerrainCostLookup"/>
/// (<c>docs/task-catalogue.md</c> "T14 Naval" Hazards: never <see cref="Model.Ruleset.MoveCostFor"/>, the
/// only path that raises <see cref="Movement.UnpricedTerrainEncountered"/>).
/// </summary>
public sealed record MoveFleetCommand(string IssuingNationId, string FleetId, int X, int Y) : ICommand
{
    /// <inheritdoc/>
    public string Kind => "naval.move-fleet";
}

/// <summary>Rejection codes this command's handler declares, in its own directory — see <see cref="RejectionCode"/>.</summary>
public static class MoveFleetRejections
{
    /// <summary>The command names a fleet id that does not exist.</summary>
    public static readonly RejectionCode UnknownFleet = new("naval.unknown-fleet");

    /// <summary>The named fleet belongs to a nation other than the one issuing the command.</summary>
    public static readonly RejectionCode NotYourFleet = new("naval.not-your-fleet");

    /// <summary>The fleet is still under construction and has not appeared on the map yet.</summary>
    public static readonly RejectionCode UnderConstruction = new("naval.under-construction");

    /// <summary>The fleet has no moves left this turn.</summary>
    public static readonly RejectionCode NoMovesLeft = new("naval.no-moves-left");

    /// <summary>The requested destination lies outside the world's grid.</summary>
    public static readonly RejectionCode OutOfBounds = new("naval.out-of-bounds");
}
