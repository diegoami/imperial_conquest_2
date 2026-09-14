using IC2.Engine.Core;

namespace IC2.Engine.Movement.Commands;

/// <summary>
/// Orders an army to walk toward <c>(<see cref="X"/>, <see cref="Y"/>)</c> — <c>docs/task-catalogue.md</c>
/// "T41 Thin CLI demo on the toy world", the <c>move &lt;army&gt; &lt;x&gt; &lt;y&gt;</c> command.
/// </summary>
/// <remarks>
/// The rule lives entirely in <see cref="MoveArmyCommandHandler"/>, and entirely in terms of T09's
/// <see cref="MovementWalker"/>/<see cref="TerrainCostLookup"/> — this record carries only the request.
/// </remarks>
public sealed record MoveArmyCommand(string IssuingNationId, string ArmyId, int X, int Y) : ICommand
{
    /// <inheritdoc/>
    public string Kind => "movement.move-army";
}

/// <summary>Rejection codes this command's handler declares, in its own directory — see <see cref="RejectionCode"/>.</summary>
public static class MoveArmyRejections
{
    /// <summary>The command names an army id that does not exist.</summary>
    public static readonly RejectionCode UnknownArmy = new("movement.unknown-army");

    /// <summary>The named army belongs to a nation other than the one issuing the command.</summary>
    public static readonly RejectionCode NotYourArmy = new("movement.not-your-army");

    /// <summary>The army has no moves left this turn.</summary>
    public static readonly RejectionCode NoMovesLeft = new("movement.no-moves-left");
}
