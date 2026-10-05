using IC2.Engine.Model;

namespace IC2.Engine.Battle.Tactical;

/// <summary>A fresh battle: the strategic state after copy-in's writes, and the battle after its first setup.</summary>
/// <param name="Game">
/// The game state with copy-in's persistent writes applied: a computer-controlled side's army has its
/// strategic morale raised and its unit records sorted. A human side's army is unchanged.
/// </param>
/// <param name="Battle">The battle, set up for its first half-round (the defender's placement).</param>
public sealed record TacticalBattleStart(GameState Game, TacticalBattleState Battle);

/// <summary>
/// Copy-in, <c>FUN_00437DE4</c> (a fresh battle only): <strong>[confirmed: code, static,
/// <c>2026-10-04-decompiled-tactical-battle-rules.md</c> §2]</strong>; <c>docs/game-design.md</c>,
/// "Copy-in".
/// </summary>
/// <remarks>
/// <list type="number">
/// <item><description>
/// For each side whose nation is computer-controlled: its strategic army morale goes up by 3, unclamped
/// and persisted; its army's unit records are selection-sorted, descending, in place, by
/// <c>troops × M[type][0] div M[0][type]</c>, with <c>i = 0 … n − 2</c>, <c>j = i + 1 … n − 1</c>, a strict
/// <c>&gt;</c> and whole-record swaps, and the order persists. (Its adoption of the opponent's pause
/// settings is display only; the clone keeps no state for it.) Never for a human side, whatever
/// <em>Computer general</em> says.
/// </description></item>
/// <item><description>Every cell is emptied and every slot's troops set to 0.</description></item>
/// <item><description>
/// For each attacker unit slot <c>s</c> with troops, in order: position <c>(s mod 13, s div 13)</c>;
/// origin, type, troops, quality and name copied; shots from the type's stat; and
/// <c>morale = max(60, min(90, Random(quality × 4) + armyMorale))</c> with the army morale after any
/// <c>+3</c>. <c>Random(0)</c> still consumes a draw.
/// </description></item>
/// <item><description>The same for the defender, at <c>(s mod 13, 11 − s div 13)</c>.</description></item>
/// <item><description>The side to move is the defender, the counter 0, and the first half-round is set up.</description></item>
/// </list>
/// <para>
/// The clone's army holds its live units compacted, so army unit <c>i</c> is the original's record
/// <c>i</c> and the original's trailing empty records (key 0, never moved by a strict <c>&gt;</c>) are
/// simply absent from the sort.
/// </para>
/// </remarks>
public static class TacticalCopyIn
{
    /// <summary>Starts a battle between two armies of <paramref name="game"/>.</summary>
    public static TacticalBattleStart Start(
        GameState game,
        string attackerArmyId,
        string defenderArmyId,
        TacticalContext context,
        IBattleDraws draws)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(draws);

        var attacker = game.ArmyById(attackerArmyId)
                       ?? throw new ArgumentException($"'{attackerArmyId}' is not a known army.", nameof(attackerArmyId));
        var defender = game.ArmyById(defenderArmyId)
                       ?? throw new ArgumentException($"'{defenderArmyId}' is not a known army.", nameof(defenderArmyId));
        if (string.Equals(attacker.Nation, defender.Nation, StringComparison.Ordinal))
        {
            throw new ArgumentException($"'{attacker.Id}' and '{defender.Id}' both belong to '{attacker.Nation}'.", nameof(defenderArmyId));
        }

        var computer = new[] { IsComputer(game, attacker.Nation), IsComputer(game, defender.Nation) };
        var armies = new[] { attacker, defender };
        for (var side = 0; side < armies.Length; side++)
        {
            if (armies[side].Units.Count > context.SlotsPerSide)
            {
                throw new ArgumentException(
                    $"'{armies[side].Id}' has {armies[side].Units.Count} units; a battle side has {context.SlotsPerSide} slots.",
                    nameof(game));
            }

            if (computer[side])
            {
                armies[side] = armies[side] with
                {
                    Morale = unchecked(armies[side].Morale + context.Rules.ComputerArmyMoraleBonus),
                    Units = SortForComputer(armies[side].Units, context),
                };
            }
        }

        var newGame = game with
        {
            Armies = ValueList.From(game.Armies.Select(a =>
                string.Equals(a.Id, attacker.Id, StringComparison.Ordinal) ? armies[0]
                : string.Equals(a.Id, defender.Id, StringComparison.Ordinal) ? armies[1]
                : a)),
        };

        var slotCount = 2 * context.SlotsPerSide;
        var cellCount = context.Rules.BoardWidth * context.Rules.BoardHeight;
        var blank = new TacticalBattleState(
            attacker.Id,
            defender.Id,
            TacticalBattleState.DefenderSide,
            Placed: false,
            Counter: 0,
            IsOver: false,
            ValueList.From(Enumerable.Repeat(TacticalSlot.Empty, slotCount)),
            ValueList.From(Enumerable.Repeat(TacticalIcon.Empty, cellCount)),
            ValueList.From(computer),
            ValueList.From(new[] { false, false }),
            ValueList.From(new int[slotCount]),
            Selected: -1,
            ValueList<TacticalEvent>.Empty);

        var board = TacticalBoard.Thaw(blank, context, draws);
        CopySide(board, armies[0], TacticalBattleState.AttackerSide);
        CopySide(board, armies[1], TacticalBattleState.DefenderSide);
        TacticalHalfRound.ApplySetup(board);

        return new TacticalBattleStart(newGame, board.Freeze());
    }

    /// <summary>
    /// <c>FUN_00437D10</c>: the computer side's in-place selection sort, descending by
    /// <c>troops × M[type][0] div M[0][type]</c>, strict <c>&gt;</c>, whole-record swaps.
    /// </summary>
    public static ValueList<UnitSlot> SortForComputer(ValueList<UnitSlot> units, TacticalContext context)
    {
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(context);

        var records = units.ToArray();
        for (var i = 0; i < records.Length - 1; i++)
        {
            for (var j = i + 1; j < records.Length; j++)
            {
                if (SortKey(records[j], context) > SortKey(records[i], context))
                {
                    (records[i], records[j]) = (records[j], records[i]);
                }
            }
        }

        return ValueList.From(records);
    }

    /// <summary>The sort key, <c>troops × M[type][0] div M[0][type]</c> (LI ×1, HI ×15, archers ×0.5, LC ×5, HC ×6).</summary>
    public static int SortKey(UnitSlot unit, TacticalContext context)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(context);
        var type = RequireType(unit.UnitTypeId, context);
        return unchecked(unit.Troops * context.Matrix(type, 0)) / context.Matrix(0, type);
    }

    private static void CopySide(TacticalBoard board, ArmyState army, int side)
    {
        var rules = board.Rules;
        var context = board.Context;
        var first = board.FirstSlotOf(side);
        for (var s = 0; s < army.Units.Count; s++)
        {
            var unit = army.Units[s];
            if (unit.Troops <= 0)
            {
                continue;
            }

            var type = RequireType(unit.UnitTypeId, context);
            var x = s % rules.CopyInColumns;
            var row = s / rules.CopyInColumns;
            var y = side == TacticalBattleState.AttackerSide ? row : rules.BoardHeight - 1 - row;
            var draw = board.Draw(unchecked(unit.Quality * rules.MoraleQualityFactor));
            var morale = Math.Max(rules.MoraleMin, Math.Min(rules.MoraleMax, unchecked(draw + army.Morale)));

            board.Slots[first + s] = new TacticalSlot(
                x,
                y,
                unit.MercenaryLabel,
                type,
                unit.Troops,
                unit.Quality,
                morale,
                Moves: 0,
                Shots: context.ShotsOf(type),
                Target: TacticalSlot.NoTarget,
                unit.Name);
            board.PaintIcon(first + s);
            board.Log.Add(new TacticalCopiedInEvent(first + s, x, y, morale));
        }
    }

    private static int RequireType(string unitTypeId, TacticalContext context)
    {
        var type = context.TypeIndexOf(unitTypeId);
        return type >= 0
            ? type
            : throw new ArgumentException($"Unit type '{unitTypeId}' is not in ruleset '{context.Ruleset.Id}'.", nameof(unitTypeId));
    }

    private static bool IsComputer(GameState game, string nationId) =>
        (game.NationById(nationId) ?? throw new ArgumentException($"'{nationId}' is not a known nation.", nameof(game)))
        .Control == SeatControl.Ai;
}
