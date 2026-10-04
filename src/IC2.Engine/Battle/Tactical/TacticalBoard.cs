using IC2.Engine.Model;

namespace IC2.Engine.Battle.Tactical;

/// <summary>
/// The working copy every routine mutates: the battle block as arrays, the trace as a list, and the draw
/// seam. A public routine thaws a <see cref="TacticalBattleState"/> into one, runs, and freezes the result,
/// so the state the caller sees stays immutable.
/// </summary>
internal sealed class TacticalBoard
{
    private readonly IBattleDraws? _draws;

    private TacticalBoard(TacticalBattleState state, TacticalContext context, IBattleDraws? draws)
    {
        Context = context;
        _draws = draws;
        AttackerArmyId = state.AttackerArmyId;
        DefenderArmyId = state.DefenderArmyId;
        SideToMove = state.SideToMove;
        Placed = state.Placed;
        Counter = state.Counter;
        IsOver = state.IsOver;
        Slots = state.Slots.ToArray();
        Grid = state.Grid.ToArray();
        ComputerControlled = state.ComputerControlled.ToArray();
        ComputerGeneral = state.ComputerGeneral.ToArray();
        GeneralClaims = state.GeneralClaims.ToArray();
        Selected = state.Selected;
        Log = new List<TacticalEvent>(state.Log);
    }

    public TacticalContext Context { get; }

    public TacticalBattleRules Rules => Context.Rules;

    public string AttackerArmyId { get; }

    public string DefenderArmyId { get; }

    public int SideToMove { get; set; }

    public bool Placed { get; set; }

    public int Counter { get; set; }

    public bool IsOver { get; set; }

    public TacticalSlot[] Slots { get; }

    public int[] Grid { get; }

    public bool[] ComputerControlled { get; }

    public bool[] ComputerGeneral { get; }

    public int[] GeneralClaims { get; }

    public int Selected { get; set; }

    public List<TacticalEvent> Log { get; }

    public int SlotsPerSide => Slots.Length / 2;

    public static TacticalBoard Thaw(TacticalBattleState state, TacticalContext context, IBattleDraws? draws)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(context);
        if (state.Slots.Count != 2 * context.SlotsPerSide)
        {
            throw new ArgumentException(
                $"The battle has {state.Slots.Count} slots; the ruleset's armies have {context.SlotsPerSide} units a side.",
                nameof(state));
        }

        if (state.Grid.Count != context.Rules.BoardWidth * context.Rules.BoardHeight)
        {
            throw new ArgumentException("The battle's grid does not match the ruleset's board.", nameof(state));
        }

        return new TacticalBoard(state, context, draws);
    }

    public TacticalBattleState Freeze() =>
        new(
            AttackerArmyId,
            DefenderArmyId,
            SideToMove,
            Placed,
            Counter,
            IsOver,
            ValueList.From(Slots),
            ValueList.From(Grid),
            ValueList.From(ComputerControlled),
            ValueList.From(ComputerGeneral),
            ValueList.From(GeneralClaims),
            Selected,
            ValueList.From(Log));

    /// <summary>One <c>Random(n)</c> through the seam, recorded on the trace.</summary>
    public int Draw(int n)
    {
        if (_draws is null)
        {
            throw new InvalidOperationException("This routine draws, but no draw seam was supplied.");
        }

        var value = _draws.Random(n);
        Log.Add(new TacticalDrawEvent(n, value));
        return value;
    }

    public int SideOf(int slot) => slot < SlotsPerSide ? TacticalBattleState.AttackerSide : TacticalBattleState.DefenderSide;

    public int FirstSlotOf(int side) => side * SlotsPerSide;

    public static int Other(int side) => 1 - side;

    public bool IsLive(int slot) => Slots[slot].Troops > 0;

    public bool OnBoard(int x, int y) => x >= 0 && x < Rules.BoardWidth && y >= 0 && y < Rules.BoardHeight;

    public int CellIndex(int x, int y) => (x * Rules.BoardHeight) + y;

    /// <summary>Whether the cell's icon code is empty; the grid is the occupancy map (report §1).</summary>
    public bool IsEmptyCell(int x, int y) => Grid[CellIndex(x, y)] == TacticalIcon.Empty;

    /// <summary>Chebyshev distance, <c>max(|dx|, |dy|)</c> (<c>FUN_00449018</c>, report §1).</summary>
    public static int Distance(int x1, int y1, int x2, int y2) => Math.Max(Math.Abs(x1 - x2), Math.Abs(y1 - y2));

    public int Distance(int a, int b) => Distance(Slots[a].X, Slots[a].Y, Slots[b].X, Slots[b].Y);

    /// <summary>
    /// The slot at a cell (<c>FUN_004383BC</c>): the 40 slots scanned for a live unit at that position;
    /// when two share a cell, the last one in the scan wins (report §1). <c>−1</c> for none.
    /// </summary>
    public int SlotAt(int x, int y)
    {
        var found = -1;
        for (var s = 0; s < Slots.Length; s++)
        {
            var slot = Slots[s];
            if (slot.Troops > 0 && slot.X == x && slot.Y == y)
            {
                found = s;
            }
        }

        return found;
    }

    /// <summary>Writes the slot's icon code into its cell (recomputed after every loss, report §10).</summary>
    public void PaintIcon(int slot)
    {
        var unit = Slots[slot];
        Grid[CellIndex(unit.X, unit.Y)] =
            TacticalIcon.Code(SideOf(slot), unit.Type, unit.Troops, Context.StandardOf(unit.Type));
    }

    /// <summary>Takes a unit out of the battle: troops 0, its cell empty (report §6).</summary>
    public void Remove(int slot)
    {
        var unit = Slots[slot];
        Slots[slot] = unit with { Troops = 0 };
        Grid[CellIndex(unit.X, unit.Y)] = TacticalIcon.Empty;
    }

    /// <summary>The winner: the attacker if any attacker slot has troops, else the defender (report §8).</summary>
    public int Winner()
    {
        for (var s = FirstSlotOf(TacticalBattleState.AttackerSide); s < SlotsPerSide; s++)
        {
            if (IsLive(s))
            {
                return TacticalBattleState.AttackerSide;
            }
        }

        return TacticalBattleState.DefenderSide;
    }

    public bool HasLiveUnit(int side)
    {
        var first = FirstSlotOf(side);
        for (var s = first; s < first + SlotsPerSide; s++)
        {
            if (IsLive(s))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Ends the battle once, with its winner on the trace.</summary>
    public void EndBattle()
    {
        if (IsOver)
        {
            return;
        }

        IsOver = true;
        Log.Add(new TacticalBattleOverEvent(Winner()));
    }
}
