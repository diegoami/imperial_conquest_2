namespace IC2.Engine.Battle.Tactical;

/// <summary>
/// The computer general: plays a computer-driven side's placement (<c>FUN_004381A4</c>) and movement
/// (<c>FUN_0043A31C</c>) half-rounds. T124 implements it; this task calls it through this seam and tests
/// with <see cref="NullTacticalGeneral"/> or a scripted general.
/// </summary>
/// <remarks>
/// The general plays the half-round only; <see cref="TacticalDriver"/> ends it. It draws through the
/// same <see cref="IBattleDraws"/> the battle uses, and acts through the public routines of this
/// namespace (<see cref="TacticalMovement"/>, <see cref="TacticalShooting"/>, ...), so its draws land on
/// the trace in order.
/// </remarks>
public interface ITacticalGeneral
{
    /// <summary>Places the side to move's units (a placement half-round).</summary>
    TacticalBattleState Place(TacticalBattleState state, TacticalContext context, IBattleDraws draws);

    /// <summary>Plays the side to move's movement half-round.</summary>
    TacticalBattleState Move(TacticalBattleState state, TacticalContext context, IBattleDraws draws);
}

/// <summary>A general that does nothing: the units keep their copy-in positions and never move. For tests.</summary>
public sealed class NullTacticalGeneral : ITacticalGeneral
{
    /// <summary>The one instance.</summary>
    public static NullTacticalGeneral Instance { get; } = new();

    private NullTacticalGeneral()
    {
    }

    /// <inheritdoc />
    public TacticalBattleState Place(TacticalBattleState state, TacticalContext context, IBattleDraws draws) => state;

    /// <inheritdoc />
    public TacticalBattleState Move(TacticalBattleState state, TacticalContext context, IBattleDraws draws) => state;
}

/// <summary>
/// Who plays a half-round, <c>FUN_00439C84</c>: <strong>[confirmed: code, static,
/// <c>2026-10-04-decompiled-tactical-battle-rules.md</c> §3, "Who drives a half-round"]</strong>;
/// <c>docs/game-design.md</c>, "The half-round", "Who plays it".
/// </summary>
/// <remarks>
/// While the side to move's nation is computer-controlled, or <em>Computer general</em> is on for it, and
/// the battle is not over, the computer general plays the half-round (placement or movement) and the
/// half-round ends (<see cref="TacticalHalfRound.End"/>). A human side waits for its orders: the loop
/// returns at the first human half-round, or when the battle is over. Nothing ends a battle but a side
/// emptied, so two computer-driven sides with a general that never fights loop forever, as the
/// original would.
/// </remarks>
public static class TacticalDriver
{
    /// <summary>Runs the computer-driven half-rounds until a human must act or the battle is over.</summary>
    public static TacticalBattleState Run(
        TacticalBattleState state, TacticalContext context, IBattleDraws draws, ITacticalGeneral general)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(draws);
        ArgumentNullException.ThrowIfNull(general);

        while (!state.IsOver && state.IsComputerDriven(state.SideToMove))
        {
            state = state.Placed
                ? general.Move(state, context, draws)
                : general.Place(state, context, draws);
            state = TacticalHalfRound.End(state, context, draws);
        }

        return state;
    }
}
