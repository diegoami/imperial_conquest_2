using IC2.Engine.Battle.Tactical;
using IC2.Engine.Battle.Tactical.General;
using static IC2.Engine.Tests.Battle.Tactical.TacticalTestbed;

namespace IC2.Engine.Tests.Battle.Tactical.General;

/// <summary>Builders for the computer general's tests, over T123's <see cref="TacticalTestbed"/>.</summary>
public static class GeneralTestbed
{
    /// <summary>The steps of a unit's path on a trace, as cells.</summary>
    public static (int X, int Y)[] Path(TacticalBattleState state, int slot) =>
        state.Log.OfType<TacticalMovedEvent>().Where(m => m.Slot == slot).Select(m => (m.ToX, m.ToY)).ToArray();

    /// <summary>The shots on a trace, as (shooter, target) pairs.</summary>
    public static (int Shooter, int Target)[] Shots(TacticalBattleState state) =>
        state.Log.OfType<TacticalShotEvent>().Select(s => (s.Shooter, s.Target)).ToArray();

    /// <summary>
    /// A fresh battle as copy-in leaves it: each listed army unit in its copy-in cell (attacker
    /// <c>(s mod 13, s div 13)</c>, defender <c>(s mod 13, 11 − s div 13)</c>), the defender to place,
    /// the counter 0, then the first half-round set up (T123's <see cref="TacticalHalfRound.Setup"/>).
    /// </summary>
    public static TacticalBattleState FreshBattle(
        IReadOnlyList<TacticalSlot> attacker,
        IReadOnlyList<TacticalSlot> defender,
        bool attackerComputer = true,
        bool defenderComputer = true)
    {
        var columns = Rules.CopyInColumns;
        var units = new List<(int, TacticalSlot)>();
        for (var s = 0; s < attacker.Count; s++)
        {
            units.Add((s, attacker[s] with { X = s % columns, Y = s / columns }));
        }

        for (var s = 0; s < defender.Count; s++)
        {
            units.Add((D0 + s, defender[s] with { X = s % columns, Y = Rules.BoardHeight - 1 - (s / columns) }));
        }

        var arena = Arena(
            units,
            sideToMove: TacticalBattleState.DefenderSide,
            placed: false,
            counter: 0,
            attackerComputer: attackerComputer,
            defenderComputer: defenderComputer);
        return TacticalHalfRound.Setup(arena, Context);
    }

    /// <summary>
    /// The driver's loop (<see cref="TacticalDriver.Run"/>: the general plays a computer half-round, then
    /// the half-round ends), bounded at <paramref name="maxHalfRounds"/>, so a test cannot hang.
    /// </summary>
    public static (TacticalBattleState State, int HalfRounds) RunBounded(
        TacticalBattleState state, IBattleDraws draws, ITacticalGeneral general, int maxHalfRounds)
    {
        var halfRounds = 0;
        while (!state.IsOver && state.IsComputerDriven(state.SideToMove) && halfRounds < maxHalfRounds)
        {
            state = state.Placed ? general.Move(state, Context, draws) : general.Place(state, Context, draws);
            state = TacticalHalfRound.End(state, Context, draws);
            halfRounds++;
        }

        return (state, halfRounds);
    }
}

/// <summary>A draw seam that always returns 0 (a shot's loss 0, so no morale loss and no rout draw), recording every bound.</summary>
public sealed class ZeroDraws : IBattleDraws
{
    private readonly List<int> _bounds = new();

    /// <summary>The bounds of every call so far.</summary>
    public IReadOnlyList<int> Bounds => _bounds;

    public int Random(int n)
    {
        _bounds.Add(n);
        return 0;
    }
}
