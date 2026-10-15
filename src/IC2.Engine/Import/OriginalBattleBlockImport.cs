using IC2.Data;
using IC2.Engine.Battle.Tactical;
using IC2.Engine.Model;

namespace IC2.Engine.Import;

/// <summary>
/// Maps an original save's tactical-battle block (<see cref="SaveBattleBlock"/>) onto the clone's
/// <see cref="TacticalBattleState"/> (T129): the 9 header bytes, the 40 slots and the 168 icon codes,
/// field for field, with nothing repaired (the grid is the file's, stale or doubled cells included).
/// </summary>
/// <remarks>
/// The two armies are named by the importer's own convention (<c>"army-{index}"</c>,
/// <see cref="OriginalSaveImporter"/>) and must exist in <paramref name="game"/>. Who is computer
/// controlled is the game's nations' <see cref="SeatControl"/> (nation <c>+0x490</c>), as copy-in reads it;
/// whether <em>Computer general</em> is on for a side is not in the block (it is a toolbar setting), so the
/// caller states it.
/// </remarks>
public static class OriginalBattleBlockImport
{
    /// <summary>The army id the importer gives original army-table index <paramref name="index"/>.</summary>
    public static string ArmyIdOf(int index) => $"army-{index}";

    /// <summary>Imports <paramref name="block"/> as a battle state of <paramref name="game"/>.</summary>
    /// <param name="block">The parsed block.</param>
    /// <param name="game">The game imported from the same save (its armies and nations).</param>
    /// <param name="computerGeneral">Per side (attacker, defender), whether <em>Computer general</em> is on.</param>
    public static TacticalBattleState Import(SaveBattleBlock block, GameState game, bool[] computerGeneral)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(computerGeneral);
        if (computerGeneral.Length != 2)
        {
            throw new ArgumentException("Computer general is stated for two sides.", nameof(computerGeneral));
        }

        var attackerId = ArmyIdOf(block.AttackerArmy);
        var defenderId = ArmyIdOf(block.DefenderArmy);
        var attacker = game.ArmyById(attackerId)
                       ?? throw new ArgumentException($"The block's attacker '{attackerId}' is not in the game.", nameof(block));
        var defender = game.ArmyById(defenderId)
                       ?? throw new ArgumentException($"The block's defender '{defenderId}' is not in the game.", nameof(block));

        var computer = new[] { IsComputer(game, attacker.Nation), IsComputer(game, defender.Nation) };
        var slots = block.Slots.Select(s => new TacticalSlot(
            s.X, s.Y, s.Origin, s.Type, s.Troops, s.Quality, s.Morale, s.Moves, s.Shots, s.Target, s.Name)).ToArray();

        return new TacticalBattleState(
            attackerId,
            defenderId,
            block.Side,
            Placed: block.Placed != 0,
            Counter: block.Counter,
            IsOver: false,
            ValueList.From(slots),
            ValueList.From(block.Grid.Select(c => (int)c)),
            ValueList.From(computer),
            ValueList.From(computerGeneral),
            ValueList.From(new int[slots.Length]),
            Selected: -1,
            ValueList<TacticalEvent>.Empty);
    }

    private static bool IsComputer(GameState game, string nationId) =>
        (game.NationById(nationId) ?? throw new ArgumentException($"'{nationId}' is not a known nation.", nameof(game)))
        .Control == SeatControl.Ai;
}
