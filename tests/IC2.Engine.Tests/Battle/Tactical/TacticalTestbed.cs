using IC2.Engine.Battle.Tactical;
using IC2.Engine.Model;
using IC2.Engine.Tests.Battle;
using Xunit;

namespace IC2.Engine.Tests.Battle.Tactical;

/// <summary>
/// A scripted draw seam: returns the scripted values in order and records every call's bound, so a test
/// asserts both what was drawn and in what order (<c>docs/game-design.md</c>, "Every random draw, in
/// order"). <c>Random(0)</c> consumes one scripted value and returns 0, as the original's does.
/// </summary>
public sealed class ScriptedDraws : IBattleDraws
{
    private readonly Queue<int> _values;
    private readonly List<(int Bound, int Value)> _calls = new();

    public ScriptedDraws(params int[] values) => _values = new Queue<int>(values);

    /// <summary>Every call so far: its bound and the value returned.</summary>
    public IReadOnlyList<(int Bound, int Value)> Calls => _calls;

    /// <summary>The bounds of every call so far.</summary>
    public IReadOnlyList<int> Bounds => _calls.Select(c => c.Bound).ToList();

    /// <summary>How many scripted values are left.</summary>
    public int Remaining => _values.Count;

    public int Random(int n)
    {
        if (_values.Count == 0)
        {
            throw new InvalidOperationException($"No scripted value left for Random({n}); {_calls.Count} drawn.");
        }

        var scripted = _values.Dequeue();
        var value = n == 0 ? 0 : scripted;
        if (n > 0 && (value < 0 || value >= n))
        {
            throw new InvalidOperationException($"Scripted value {value} is outside Random({n})'s [0, {n}).");
        }

        _calls.Add((n, value));
        return value;
    }
}

/// <summary>A draw seam that fails the test if anything draws.</summary>
public sealed class NoDraws : IBattleDraws
{
    public static NoDraws Instance { get; } = new();

    public int Random(int n) => throw new Xunit.Sdk.XunitException($"Unexpected draw: Random({n}).");
}

/// <summary>
/// Builders for the tactical battle's tests: the toy ruleset's context (its <c>combat.tactical</c>,
/// matrix and unit types are <c>classical-faithful</c>'s, T122), the type indices, and battle states
/// built slot by slot with their grid painted by the icon rule.
/// </summary>
/// <remarks>
/// Every gameplay number a test asserts is either read from the ruleset here or derived by hand in the
/// test's comment from the ruleset's values; what the builders write as literals is fixture data
/// (troops, positions, morale), never a rule.
/// </remarks>
public static class TacticalTestbed
{
    public const int LI = 0;
    public const int HI = 1;
    public const int AR = 2;
    public const int LC = 3;
    public const int HC = 4;

    /// <summary>The toy ruleset.</summary>
    public static Ruleset ToyRuleset => BattleTestbed.Destroyed;

    /// <summary>The toy ruleset's tactical context.</summary>
    public static TacticalContext Context { get; } = TacticalContext.From(BattleTestbed.Destroyed);

    public static TacticalBattleRules Rules => Context.Rules;

    public static int Slots => 2 * Context.SlotsPerSide;

    /// <summary>The first defender slot (20).</summary>
    public static int D0 => Context.SlotsPerSide;

    /// <summary>A unit in a slot. Moves default to the type's stat, shots to the type's stat.</summary>
    public static TacticalSlot Unit(
        int type,
        int x,
        int y,
        int troops,
        int quality = 5,
        int morale = 70,
        int? moves = null,
        int? shots = null,
        int target = TacticalSlot.NoTarget,
        string name = "unit") =>
        new(x, y, 0, type, troops, quality, morale, moves ?? Context.MovesOf(type), shots ?? Context.ShotsOf(type), target, name);

    /// <summary>
    /// A battle with the given slots (every other slot empty), its grid painted from the live slots in
    /// slot order, after placement, attacker to move, both sides human unless stated.
    /// </summary>
    public static TacticalBattleState Arena(
        IEnumerable<(int Slot, TacticalSlot Unit)> units,
        int sideToMove = TacticalBattleState.AttackerSide,
        bool placed = true,
        int counter = 3,
        bool attackerComputer = false,
        bool defenderComputer = false,
        bool attackerGeneral = false,
        bool defenderGeneral = false)
    {
        var slots = Enumerable.Repeat(TacticalSlot.Empty, Slots).ToArray();
        foreach (var (slot, unit) in units)
        {
            slots[slot] = unit;
        }

        var grid = Enumerable.Repeat(TacticalIcon.Empty, Rules.BoardWidth * Rules.BoardHeight).ToArray();
        for (var s = 0; s < slots.Length; s++)
        {
            if (slots[s].IsLive)
            {
                grid[Cell(slots[s].X, slots[s].Y)] = IconOf(s, slots[s]);
            }
        }

        return new TacticalBattleState(
            "att",
            "def",
            sideToMove,
            placed,
            counter,
            IsOver: false,
            ValueList.From(slots),
            ValueList.From(grid),
            ValueList.From(new[] { attackerComputer, defenderComputer }),
            ValueList.From(new[] { attackerGeneral, defenderGeneral }),
            ValueList.From(new int[Slots]),
            Selected: -1,
            ValueList<TacticalEvent>.Empty);
    }

    public static TacticalBattleState Arena(params (int Slot, TacticalSlot Unit)[] units) => Arena(units.AsEnumerable());

    public static int Cell(int x, int y) => (x * Rules.BoardHeight) + y;

    public static int GridAt(TacticalBattleState state, int x, int y) => state.Grid[Cell(x, y)];

    /// <summary>The icon rule, written out independently of the engine: <c>type × 3 + min(2, troops div (std div 3))</c>, +20 for the defender.</summary>
    public static int IconOf(int slot, TacticalSlot unit)
    {
        var std = Context.StandardOf(unit.Type);
        var size = Math.Min(2, unit.Troops / (std / 3));
        return (unit.Type * 3) + size + (slot >= D0 ? 20 : 0);
    }

    /// <summary>The draw events on a trace, as (bound, value) pairs.</summary>
    public static List<(int Bound, int Value)> DrawsOn(TacticalBattleState state) =>
        state.Log.OfType<TacticalDrawEvent>().Select(d => (d.Bound, d.Value)).ToList();

    /// <summary>Asserts that every live slot's cell holds its icon code.</summary>
    public static void AssertGridMatchesSlots(TacticalBattleState state)
    {
        for (var s = 0; s < state.Slots.Count; s++)
        {
            var unit = state.Slots[s];
            if (unit.IsLive)
            {
                Assert.Equal(IconOf(s, unit), GridAt(state, unit.X, unit.Y));
            }
        }
    }
}
