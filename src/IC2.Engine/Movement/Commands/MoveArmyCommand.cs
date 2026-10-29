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
/// <param name="Walk">T238: <see cref="MoveWalkPolicy.HumanSecondWalk"/> for a human seat's order; the designed AI's
/// are <see cref="MoveWalkPolicy.Single"/>, and a faithful AI march's <see cref="MoveWalkPolicy.AiContinuation"/> (T239).</param>
/// <param name="TargetX">T239: the march's target column (the city or army the AI chose), which
/// <see cref="MoveWalkPolicy.AiContinuation"/> keeps walking toward; <c>(X, Y)</c> may be a waypoint on the way.</param>
/// <param name="TargetY">T239: the march's target row.</param>
public sealed record MoveArmyCommand(
    string IssuingNationId, string ArmyId, int X, int Y, MoveWalkPolicy Walk = MoveWalkPolicy.Single,
    int? TargetX = null, int? TargetY = null) : ICommand
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

    /// <summary>
    /// The requested destination lies outside the world's grid. Checked before
    /// <see cref="MovementWalker.Walk"/> ever runs: the walker's own contract requires a caller's
    /// <c>tileTypeIdAt</c> lookup to resolve every cell the walk actually reaches, and throws
    /// <see cref="InvalidOperationException"/> if it does not — a caller error for a pure function, not
    /// something it can turn into a polite outcome itself. A destination off the map is exactly that
    /// caller error made from user input, so the handler catches it first and reports it the same way
    /// every other illegal order is reported: a typed rejection, never an exception.
    /// </summary>
    public static readonly RejectionCode OutOfBounds = new("movement.out-of-bounds");

    /// <summary>
    /// T237 (#1587): the destination is a tile armies cannot stand on (<c>TileType.PassableByArmies</c>),
    /// which the original's and the remake's map click ignore.
    /// </summary>
    public static readonly RejectionCode DestinationNotLand = new("movement.destination-not-land");

    /// <summary>T237 (#920 item 3): the army is aboard a fleet, which moves it; disembarking is the fleet's order.</summary>
    public static readonly RejectionCode ArmyEmbarked = new("movement.army-embarked");
}
