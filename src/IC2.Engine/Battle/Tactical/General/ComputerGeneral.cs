namespace IC2.Engine.Battle.Tactical.General;

/// <summary>
/// The original's computer general, the <see cref="ITacticalGeneral"/> the tactical battle's driver
/// (<see cref="TacticalDriver"/>) calls for every computer-driven half-round: <strong>[confirmed: code,
/// static, <c>2026-10-04-decompiled-tactical-battle-rules.md</c> §2 "AI placement" and §7]</strong>;
/// <c>docs/game-design.md</c>, Combat, "The tactical battle", "The computer general".
/// </summary>
/// <remarks>
/// <para>
/// <strong>Placement</strong> is <see cref="GeneralPlacement"/> (<c>FUN_004381A4</c>).
/// </para>
/// <para>
/// <strong>Movement</strong> is <c>FUN_0043A31C</c>: the side's units in <c>combat.tactical.typeOrder</c>
/// (heavy infantry, heavy cavalry, light cavalry, light infantry, archers; <c>0x4790A0</c>) and, within a
/// type, the live units in slot order. Pass 1 takes each in turn: the target choice
/// (<see cref="GeneralTargeting"/>, <c>FUN_00439E08</c>), which records a claim, then engage
/// (<see cref="GeneralEngage"/>, <c>FUN_0043A160</c>). Then pass 2 (<see cref="GeneralPassTwo"/>,
/// <c>FUN_0043A544</c>) and pass 3 (<see cref="GeneralPassThree"/>, <c>FUN_0043ABB4</c>: the threat move,
/// pass 2, the flank, pass 2 again), each over all the units in the same order.
/// </para>
/// <para>
/// It is a port, not a better general: the negative shots, the stacking, the wrapped <c>int</c> terms, the
/// selection sort and every tie rule are the original's. It reads only T123's state and acts only through
/// T123's orders (a walk, a shot, a target set), so every rule a unit obeys, the slow advance and the
/// movement's computer-only branches included, is T123's. A unit's liveness is read when its turn comes,
/// and a pass stops at once when the battle ends.
/// </para>
/// </remarks>
public sealed class ComputerGeneral : ITacticalGeneral
{
    private readonly IList<GeneralDecision>? _journal;

    /// <summary>A general that keeps no journal.</summary>
    public ComputerGeneral()
    {
    }

    /// <summary>A general that appends every decision to <paramref name="journal"/>.</summary>
    public ComputerGeneral(IList<GeneralDecision> journal)
    {
        ArgumentNullException.ThrowIfNull(journal);
        _journal = journal;
    }

    /// <inheritdoc />
    public TacticalBattleState Place(TacticalBattleState state, TacticalContext context, IBattleDraws draws) =>
        GeneralPlacement.Place(state, context, draws, _journal);

    /// <inheritdoc />
    public TacticalBattleState Move(TacticalBattleState state, TacticalContext context, IBattleDraws draws)
    {
        var board = TacticalBoard.Thaw(state, context, draws);

        foreach (var slot in GeneralOrders.InOrder(board))
        {
            var target = GeneralTargeting.Apply(board, slot, _journal);
            if (target >= 0)
            {
                GeneralEngage.Apply(board, slot, target, _journal);
            }
        }

        GeneralPassTwo.Apply(board, _journal);
        GeneralPassThree.Apply(board, _journal);
        return board.Freeze();
    }
}
