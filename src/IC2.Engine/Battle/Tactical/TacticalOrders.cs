namespace IC2.Engine.Battle.Tactical;

/// <summary>The outcome of a human's order: the new state, or the unchanged state and why the order was refused.</summary>
/// <param name="State">The battle after the order (unchanged when refused).</param>
/// <param name="Rejection">Why the order was refused, or <see langword="null"/> when it was carried out.</param>
public sealed record TacticalOrderResult(TacticalBattleState State, string? Rejection)
{
    /// <summary>Whether the order was carried out.</summary>
    public bool Accepted => Rejection is null;
}

/// <summary>
/// A human's orders, <c>OnMapClick</c> and the toolbar: <strong>[confirmed: code, static,
/// <c>2026-10-04-decompiled-tactical-battle-rules.md</c> §2 "Human placement", §3 "Human actions", §10
/// "The toolbar"]</strong>; <c>docs/game-design.md</c>, "Placement" and "A human's orders".
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>A left click on a unit selects it (<see cref="Select"/>).</description></item>
/// <item><description>
/// A left click on an empty cell with a unit selected places it during placement (<see cref="Place"/>:
/// an own unit, an empty cell in its home rows, <c>y &lt; 3</c> for the attacker and <c>y &gt; 8</c> for
/// the defender, as often as it likes) or moves it (<see cref="Move"/>: the target cleared, the walk made
/// twice when the first did not arrive; no draw).
/// </description></item>
/// <item><description>
/// A left click on an adjacent enemy, by a unit with at least 1 move, sets its melee target at no cost
/// (<see cref="SetTarget"/>).
/// </description></item>
/// <item><description>
/// A right click on an enemy, by a unit with shots, moves and the enemy within range, shoots
/// (<see cref="Shoot"/>). A right click on an own unit clears its target (<see cref="ClearTarget"/>).
/// </description></item>
/// <item><description>
/// <em>End turn</em> ends the half-round and hands to the driver (<see cref="EndHalfRound"/>);
/// <em>Computer general</em> turns the computer on for the side to move and hands to the driver
/// (<see cref="ComputerGeneralOn"/>); a click on the information panel turns it off
/// (<see cref="ComputerGeneralOff"/>); <em>Surrender</em> surrenders (<see cref="Surrender"/>).
/// </description></item>
/// </list>
/// Every order but <see cref="ComputerGeneralOff"/> is refused once the battle is over and while the
/// side to move is computer-driven; a unit order is refused for a unit of the other side.
/// </remarks>
public static class TacticalOrders
{
    /// <summary>Selects a live unit (either side).</summary>
    public static TacticalOrderResult Select(TacticalBattleState state, int slot, TacticalContext context)
    {
        if (CommonRefusal(state) is { } refused)
        {
            return Refuse(state, refused);
        }

        if (!InRange(state, slot) || !state.Slots[slot].IsLive)
        {
            return Refuse(state, "There is no unit there.");
        }

        var board = TacticalBoard.Thaw(state, context, draws: null);
        board.Selected = slot;
        board.Log.Add(new TacticalSelectedEvent(slot));
        return Accept(board);
    }

    /// <summary>Places an own unit on an empty home-row cell during placement.</summary>
    public static TacticalOrderResult Place(TacticalBattleState state, int slot, int x, int y, TacticalContext context)
    {
        if (OwnUnitRefusal(state, slot) is { } refused)
        {
            return Refuse(state, refused);
        }

        if (state.Placed)
        {
            return Refuse(state, "Placement is over.");
        }

        var board = TacticalBoard.Thaw(state, context, draws: null);
        if (!board.OnBoard(x, y) || !InHomeRows(board, state.SideToMove, y))
        {
            return Refuse(state, "Units are placed in their own home rows.");
        }

        if (!board.IsEmptyCell(x, y))
        {
            return Refuse(state, "That cell is occupied.");
        }

        var unit = board.Slots[slot];
        board.Grid[board.CellIndex(unit.X, unit.Y)] = TacticalIcon.Empty;
        board.Slots[slot] = unit with { X = x, Y = y };
        board.PaintIcon(slot);
        board.Log.Add(new TacticalPlacedEvent(slot, unit.X, unit.Y, x, y));
        return Accept(board);
    }

    /// <summary>Moves an own unit toward an empty cell (<c>MoveHumanUnit</c>).</summary>
    public static TacticalOrderResult Move(
        TacticalBattleState state, int slot, int x, int y, TacticalContext context, IBattleDraws draws)
    {
        if (OwnUnitRefusal(state, slot) is { } refused)
        {
            return Refuse(state, refused);
        }

        if (!state.Placed)
        {
            return Refuse(state, "Units are placed, not moved, during placement.");
        }

        var board = TacticalBoard.Thaw(state, context, draws);
        if (!board.OnBoard(x, y) || !board.IsEmptyCell(x, y))
        {
            return Refuse(state, "A unit moves to an empty cell.");
        }

        TacticalMovement.ApplyHumanMove(board, slot, x, y);
        return Accept(board);
    }

    /// <summary>Sets an own unit's melee target to an adjacent live enemy, at no cost in moves.</summary>
    public static TacticalOrderResult SetTarget(TacticalBattleState state, int slot, int target, TacticalContext context)
    {
        if (EnemyOrderRefusal(state, slot, target) is { } refused)
        {
            return Refuse(state, refused);
        }

        var board = TacticalBoard.Thaw(state, context, draws: null);
        if (board.Slots[slot].Moves < 1)
        {
            return Refuse(state, "The unit has no moves left.");
        }

        if (board.Distance(slot, target) != 1)
        {
            return Refuse(state, "A unit attacks an adjacent enemy.");
        }

        board.Slots[slot] = board.Slots[slot] with { Target = target };
        board.Log.Add(new TacticalTargetSetEvent(slot, target));
        return Accept(board);
    }

    /// <summary>An own unit with shots and moves shoots a live enemy within its range.</summary>
    public static TacticalOrderResult Shoot(
        TacticalBattleState state, int slot, int target, TacticalContext context, IBattleDraws draws)
    {
        if (EnemyOrderRefusal(state, slot, target) is { } refused)
        {
            return Refuse(state, refused);
        }

        var board = TacticalBoard.Thaw(state, context, draws);
        var unit = board.Slots[slot];
        if (unit.Shots <= 0 || unit.Moves <= 0)
        {
            return Refuse(state, "The unit has no shots or no moves left.");
        }

        if (board.Distance(slot, target) > context.RangeOf(unit.Type))
        {
            return Refuse(state, "The enemy is out of range.");
        }

        TacticalShooting.Apply(board, slot, target);
        return Accept(board);
    }

    /// <summary>Clears an own unit's melee target.</summary>
    public static TacticalOrderResult ClearTarget(TacticalBattleState state, int slot, TacticalContext context)
    {
        if (OwnUnitRefusal(state, slot) is { } refused)
        {
            return Refuse(state, refused);
        }

        var board = TacticalBoard.Thaw(state, context, draws: null);
        board.Slots[slot] = board.Slots[slot] with { Target = TacticalSlot.NoTarget };
        board.Log.Add(new TacticalTargetClearedEvent(slot));
        return Accept(board);
    }

    /// <summary><em>End turn</em>: the half-round ends, then the driver plays any computer half-rounds.</summary>
    public static TacticalOrderResult EndHalfRound(
        TacticalBattleState state, TacticalContext context, IBattleDraws draws, ITacticalGeneral general)
    {
        if (CommonRefusal(state) is { } refused)
        {
            return Refuse(state, refused);
        }

        var ended = TacticalHalfRound.End(state, context, draws);
        return new TacticalOrderResult(TacticalDriver.Run(ended, context, draws, general), null);
    }

    /// <summary><em>Computer general on</em> for the side to move, then the driver.</summary>
    public static TacticalOrderResult ComputerGeneralOn(
        TacticalBattleState state, TacticalContext context, IBattleDraws draws, ITacticalGeneral general)
    {
        if (CommonRefusal(state) is { } refused)
        {
            return Refuse(state, refused);
        }

        var board = TacticalBoard.Thaw(state, context, draws);
        board.ComputerGeneral[board.SideToMove] = true;
        board.Log.Add(new TacticalComputerGeneralEvent(board.SideToMove, On: true));
        return new TacticalOrderResult(TacticalDriver.Run(board.Freeze(), context, draws, general), null);
    }

    /// <summary>The information-panel click: <em>Computer general</em> off for <paramref name="side"/>.</summary>
    public static TacticalOrderResult ComputerGeneralOff(TacticalBattleState state, int side, TacticalContext context)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (side is not (TacticalBattleState.AttackerSide or TacticalBattleState.DefenderSide))
        {
            return Refuse(state, "There is no such side.");
        }

        var board = TacticalBoard.Thaw(state, context, draws: null);
        board.ComputerGeneral[side] = false;
        board.Log.Add(new TacticalComputerGeneralEvent(side, On: false));
        return Accept(board);
    }

    /// <summary><em>Surrender</em>: the side to move surrenders.</summary>
    public static TacticalOrderResult Surrender(TacticalBattleState state, TacticalContext context, IBattleDraws draws)
    {
        if (CommonRefusal(state) is { } refused)
        {
            return Refuse(state, refused);
        }

        return new TacticalOrderResult(TacticalSurrender.Surrender(state, context, draws), null);
    }

    private static bool InHomeRows(TacticalBoard board, int side, int y) =>
        side == TacticalBattleState.AttackerSide
            ? y < board.Rules.HomeRows
            : y > board.Rules.BoardHeight - board.Rules.HomeRows - 1;

    private static bool InRange(TacticalBattleState state, int slot) => slot >= 0 && slot < state.Slots.Count;

    private static string? CommonRefusal(TacticalBattleState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.IsOver)
        {
            return "The battle is over.";
        }

        return state.IsComputerDriven(state.SideToMove) ? "The computer is playing this half-round." : null;
    }

    private static string? OwnUnitRefusal(TacticalBattleState state, int slot)
    {
        if (CommonRefusal(state) is { } refused)
        {
            return refused;
        }

        if (!InRange(state, slot) || !state.Slots[slot].IsLive)
        {
            return "There is no unit there.";
        }

        return state.SideOf(slot) == state.SideToMove ? null : "That unit is not yours to order.";
    }

    private static string? EnemyOrderRefusal(TacticalBattleState state, int slot, int target)
    {
        if (OwnUnitRefusal(state, slot) is { } refused)
        {
            return refused;
        }

        if (!state.Placed)
        {
            return "Units are placed, not ordered, during placement.";
        }

        if (!InRange(state, target) || !state.Slots[target].IsLive || state.SideOf(target) == state.SideToMove)
        {
            return "There is no enemy unit there.";
        }

        return null;
    }

    private static TacticalOrderResult Accept(TacticalBoard board) => new(board.Freeze(), null);

    private static TacticalOrderResult Refuse(TacticalBattleState state, string reason) => new(state, reason);
}
