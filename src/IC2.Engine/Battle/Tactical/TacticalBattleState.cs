using IC2.Engine.Model;

namespace IC2.Engine.Battle.Tactical;

/// <summary>
/// One of the battle's unit slots: the report's 11 fields (<c>2026-10-04-decompiled-tactical-battle-rules.md</c>
/// §1, "Unit slot", 22 words; <c>docs/game-design.md</c>, "The board and the battle state").
/// </summary>
/// <param name="X">Column, <c>0 … boardWidth − 1</c> (word 0).</param>
/// <param name="Y">Row, <c>0 … boardHeight − 1</c> (word 1).</param>
/// <param name="Origin">
/// The origin label (word 2): 0 for a national unit, non-zero for a mercenary; display only, written back
/// (the army unit's <see cref="UnitSlot.MercenaryLabel"/>).
/// </param>
/// <param name="Type">The unit type (word 3), as the index into <see cref="Ruleset.UnitTypes"/>: 0 LI, 1 HI, 2 archers, 3 LC, 4 HC.</param>
/// <param name="Troops">Troops (word 4); 0 means the slot is out of the battle.</param>
/// <param name="Quality">Quality (word 5), constant during the battle.</param>
/// <param name="Morale">The battle morale (word 6), the per-unit tactical morale, not the army's.</param>
/// <param name="Moves">Moves left this half-round (word 7).</param>
/// <param name="Shots">Shots left for the battle (word 8). Signed: the computer general's pass 2 can drive it below 0.</param>
/// <param name="Target">The melee target slot (word 9), <c>−1</c> for none.</param>
/// <param name="Name">The unit's name (words 10–21).</param>
public sealed record TacticalSlot(
    int X,
    int Y,
    int Origin,
    int Type,
    int Troops,
    int Quality,
    int Morale,
    int Moves,
    int Shots,
    int Target,
    string Name)
{
    /// <summary>The <see cref="Target"/> value for "no target", the original's <c>−1</c>.</summary>
    public const int NoTarget = -1;

    /// <summary>An empty slot: out of the battle, no target.</summary>
    public static TacticalSlot Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, NoTarget, string.Empty);

    /// <summary>Whether the slot holds a unit in the battle (<c>troops &gt; 0</c>).</summary>
    public bool IsLive => Troops > 0;
}

/// <summary>
/// The immutable state of one tactical battle: the original's battle block plus who plays each side,
/// and the trace of everything that happened.
/// </summary>
/// <remarks>
/// <para>
/// <strong>[confirmed: code, static, <c>2026-10-04-decompiled-tactical-battle-rules.md</c> §1]</strong>
/// (<c>docs/game-design.md</c>, "The board and the battle state"): the attacker and defender army, the
/// side to move (0 attacker, 1 defender), whether placement has finished, the half-round counter, the 40
/// slots (0–19 the attacker's, 20–39 the defender's) and the 14 × 12 icon grid, <c>cell(x, y) = [x ×
/// boardHeight + y]</c>. Which sides are computer-controlled, and which have <em>Computer general</em>
/// on, are the battle's inputs from the nation records and the toolbar.
/// </para>
/// <para>
/// The grid holds icon codes, not slots (<see cref="TacticalIcon"/>): it is the occupancy map the
/// movement and placement rules test, and the game never repairs it from the slots (sweep B3).
/// </para>
/// </remarks>
/// <param name="AttackerArmyId">The attacking army (side 0).</param>
/// <param name="DefenderArmyId">The defending army (side 1).</param>
/// <param name="SideToMove">0 attacker, 1 defender.</param>
/// <param name="Placed">Whether the placement half-rounds are over.</param>
/// <param name="Counter">The half-round counter, incremented at the end of every half-round setup.</param>
/// <param name="IsOver">Whether the battle has ended (a side emptied by a rout, or a surrender).</param>
/// <param name="Slots">The 40 slots.</param>
/// <param name="Grid">The icon codes, <c>boardWidth × boardHeight</c> of them, indexed <c>x × boardHeight + y</c>.</param>
/// <param name="ComputerControlled">Per side, whether its nation is computer-controlled (nation <c>+0x490 = 0</c>).</param>
/// <param name="ComputerGeneral">Per side, whether <em>Computer general</em> is on.</param>
/// <param name="GeneralClaims">The computer general's 40 target claims, cleared at every half-round setup (T124 writes them).</param>
/// <param name="Selected">The slot a human has selected, or <c>−1</c>.</param>
/// <param name="Log">Every placement, move, exchange, rout and draw, in order.</param>
public sealed record TacticalBattleState(
    string AttackerArmyId,
    string DefenderArmyId,
    int SideToMove,
    bool Placed,
    int Counter,
    bool IsOver,
    ValueList<TacticalSlot> Slots,
    ValueList<int> Grid,
    ValueList<bool> ComputerControlled,
    ValueList<bool> ComputerGeneral,
    ValueList<int> GeneralClaims,
    int Selected,
    ValueList<TacticalEvent> Log)
{
    /// <summary>Side 0, the attacker.</summary>
    public const int AttackerSide = 0;

    /// <summary>Side 1, the defender.</summary>
    public const int DefenderSide = 1;

    /// <summary>Slots per side: the army's unit capacity (20).</summary>
    public int SlotsPerSide => Slots.Count / 2;

    /// <summary>The side slot <paramref name="slot"/> belongs to.</summary>
    public int SideOf(int slot) => slot < SlotsPerSide ? AttackerSide : DefenderSide;

    /// <summary>Whether <paramref name="side"/> has any live unit.</summary>
    public bool HasLiveUnit(int side)
    {
        var start = side * SlotsPerSide;
        for (var s = start; s < start + SlotsPerSide; s++)
        {
            if (Slots[s].IsLive)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether the side to move is played by the computer (its nation, or <em>Computer general</em>).</summary>
    public bool IsComputerDriven(int side) => ComputerControlled[side] || ComputerGeneral[side];
}
