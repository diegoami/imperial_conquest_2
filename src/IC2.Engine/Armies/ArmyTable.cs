using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Armies;

/// <summary>
/// T240 (#1601): the original's army table under <c>flags.armyTableCompaction = swapRemoveSweep</c>, in one place.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The original</strong> leaves a removed army's record in its slot, marked dead, and appends a new army
/// after every slot, dead or alive. The sweep <c>FUN_0044adb0</c> walks the table from the last index down and
/// <c>FUN_0044abe0</c> swap-removes each dead record: the last record moves into the dead slot and the count drops
/// by one. <c>FUN_00451fdc</c> sweeps at its start, when a human ends the turn, and before each AI seat's
/// <c>FUN_0044fa20</c> <strong>[confirmed: code; 2026-10-30-army-table-order-after-deaths117.md and
/// 2026-10-30-all-ashore-army-order1439.md, research 150d95d]</strong>.
/// </para>
/// <para>
/// <strong>The remake</strong> keeps <see cref="GameState.Armies"/> as the live armies in table order, and the dead
/// slots apart in <see cref="GameState.DeadArmySlots"/>. Nothing that removes or creates an army knows about them:
/// <see cref="Record"/> compares the army list before and after each accepted step (a command through
/// <see cref="CommandDispatcher"/>, a system through <see cref="TurnCoordinator"/>, a tactical battle's write-back)
/// and marks each removed army's table position dead. It is idempotent, so a command an AI system dispatches is
/// recorded once by the dispatcher and again, to no effect, by the system step around it. <see cref="Sweep"/> is
/// run by <see cref="ArmyTableSweepSystem"/> at the start of every seat's pipeline run.
/// </para>
/// <para>
/// Under <c>inOrder</c> both are no-ops: a removal closes the gap at once and the survivors keep their order.
/// </para>
/// </remarks>
public static class ArmyTable
{
    /// <summary>
    /// Test seam: called with a description whenever <see cref="Record"/> meets a step that does not keep the
    /// survivors' relative order or does not append its new armies. Thread-static, so a test that sets it sees only
    /// its own steps.
    /// </summary>
    public static Action<string>? OrderViolationObserver
    {
        get => _orderViolationObserver;
        set => _orderViolationObserver = value;
    }

    [ThreadStatic]
    private static Action<string>? _orderViolationObserver;

    /// <summary>Test seam: called with the number of dead slots each sweep that had any closed. Thread-static.</summary>
    public static Action<int>? SweepObserver
    {
        get => _sweepObserver;
        set => _sweepObserver = value;
    }

    [ThreadStatic]
    private static Action<int>? _sweepObserver;

    /// <summary>Whether <paramref name="ruleset"/> keeps dead slots until the sweep.</summary>
    public static bool KeepsDeadSlots(Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(ruleset);
        return ruleset.Flags.ArmyTableCompaction == ArmyTableCompactionPolicy.SwapRemoveSweep;
    }

    /// <summary>
    /// The army table: <see cref="GameState.Armies"/> with the dead slots interleaved, a <see langword="null"/> entry
    /// for each dead slot.
    /// </summary>
    public static IReadOnlyList<ArmyState?> Slots(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var dead = state.DeadArmySlots;
        var table = new List<ArmyState?>(state.Armies.Count + (dead?.Count ?? 0));
        var live = 0;
        var d = 0;
        while (live < state.Armies.Count || (dead is not null && d < dead.Count))
        {
            if (dead is not null && d < dead.Count && dead[d] == table.Count)
            {
                table.Add(null);
                d++;
                continue;
            }

            if (live >= state.Armies.Count)
            {
                throw new InvalidOperationException(
                    $"The army table's dead slot {dead![d]} lies beyond its last position {table.Count - 1}.");
            }

            table.Add(state.Armies[live++]);
        }

        return table;
    }

    /// <summary>
    /// Records the dead slots one accepted step left: every army in <paramref name="before"/> that is no longer in
    /// <paramref name="after"/> leaves a dead slot at its table position. Survivors keep their positions and a new
    /// army takes the next position after every slot, dead or alive. Returns <paramref name="after"/> itself under
    /// <c>inOrder</c> or when nothing changed.
    /// </summary>
    /// <remarks>
    /// A step that reorders surviving armies, or that places a new army anywhere but after every survivor, does not
    /// fit the table. A new army placed between survivors takes a dead slot between them when there is one (the
    /// all-ashore embark's receiver, which replaces its source in place). Otherwise the dead slots are dropped and
    /// the table becomes the live list as it stands. Either case is reported to <see cref="OrderViolationObserver"/>.
    /// </remarks>
    public static GameState Record(GameState before, GameState after, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        if (!KeepsDeadSlots(ruleset)
            || (ReferenceEquals(before.Armies, after.Armies) && ReferenceEquals(before.DeadArmySlots, after.DeadArmySlots)))
        {
            return after;
        }

        // The positions every army of `before` holds in its table.
        var beforeTable = Slots(before);
        var position = new Dictionary<string, int>(before.Armies.Count, StringComparer.Ordinal);
        for (var i = 0; i < beforeTable.Count; i++)
        {
            if (beforeTable[i] is { } army)
            {
                position[army.Id] = i;
            }
        }

        // The dead slots: those `before` had, those a nested step already recorded into `after`, and the positions
        // of the armies this step removed.
        var dead = new SortedSet<int>();
        AddAll(dead, before.DeadArmySlots);
        AddAll(dead, after.DeadArmySlots);
        var afterIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var army in after.Armies)
        {
            afterIds.Add(army.Id);
        }

        foreach (var army in before.Armies)
        {
            if (!afterIds.Contains(army.Id))
            {
                dead.Add(position[army.Id]);
            }
        }

        // Place the live armies of `after`: a survivor at its position, a new army after every slot.
        var nextSurvivor = new int[after.Armies.Count];
        var upcoming = int.MaxValue;
        for (var k = after.Armies.Count - 1; k >= 0; k--)
        {
            nextSurvivor[k] = upcoming;
            if (position.TryGetValue(after.Armies[k].Id, out var p))
            {
                upcoming = p;
            }
        }

        var occupied = new SortedSet<int>();
        var last = -1;
        string? violation = null;
        for (var k = 0; k < after.Armies.Count && violation is null; k++)
        {
            var army = after.Armies[k];
            int at;
            if (position.TryGetValue(army.Id, out var p))
            {
                if (p <= last)
                {
                    violation = $"army '{army.Id}' moved before an army that preceded it in the table";
                    break;
                }

                at = p;
            }
            else if (nextSurvivor[k] != int.MaxValue)
            {
                // A new army between survivors: it can only stand in a dead slot between them.
                var reused = dead.GetViewBetween(last + 1, nextSurvivor[k] - 1);
                if (reused.Count == 0)
                {
                    violation = $"new army '{army.Id}' was placed between surviving armies";
                    break;
                }

                at = reused.Min;
                dead.Remove(at);
                OrderViolationObserver?.Invoke(
                    $"new army '{army.Id}' was placed between surviving armies; it takes dead slot {at}");
            }
            else
            {
                at = Math.Max(last + 1, beforeTable.Count);
                while (dead.Contains(at))
                {
                    at++;
                }
            }

            occupied.Add(at);
            last = at;
        }

        if (violation is null)
        {
            // Every position below the table's end must be either live or dead.
            var end = Math.Max(occupied.Count == 0 ? 0 : occupied.Max + 1, dead.Count == 0 ? 0 : dead.Max + 1);
            if (occupied.Count + dead.Count != end)
            {
                violation = "the step left a gap in the army table";
            }
        }

        if (violation is not null)
        {
            OrderViolationObserver?.Invoke(violation + "; the dead slots are dropped");
            return after.DeadArmySlots is null ? after : after with { DeadArmySlots = null };
        }

        var slots = dead.Count == 0 ? null : ValueList.From(dead);
        return Equals(slots, after.DeadArmySlots) ? after : after with { DeadArmySlots = slots };
    }

    /// <summary>
    /// The sweep, <c>FUN_0044adb0</c>/<c>FUN_0044abe0</c>: from the last slot down, each dead slot takes the record
    /// currently in the last slot, and the table shrinks by one. Returns <paramref name="state"/> itself when it
    /// has no dead slot.
    /// </summary>
    public static GameState Sweep(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.DeadArmySlots is not { Count: > 0 })
        {
            return state.DeadArmySlots is null ? state : state with { DeadArmySlots = null };
        }

        SweepObserver?.Invoke(state.DeadArmySlots.Count);
        var table = new List<ArmyState?>(Slots(state));
        for (var i = table.Count - 1; i >= 0; i--)
        {
            if (i < table.Count && table[i] is null)
            {
                table[i] = table[^1];
                table.RemoveAt(table.Count - 1);
            }
        }

        return state with { Armies = ValueList.From(table.Select(a => a!)), DeadArmySlots = null };
    }

    private static void AddAll(SortedSet<int> set, ValueList<int>? values)
    {
        if (values is null)
        {
            return;
        }

        foreach (var value in values)
        {
            set.Add(value);
        }
    }
}

/// <summary>
/// T240: the army-table sweep (<see cref="ArmyTable.Sweep"/>) at the start of every seat's pipeline run, which is
/// when a human seat ends its turn and before an AI seat acts, as <c>FUN_00451fdc</c> sweeps. Runs first in
/// <see cref="TurnPhase.SeatStart"/>; a no-op under <c>inOrder</c>, where there is never a dead slot.
/// </summary>
[GameSystem(TurnPhase.SeatStart, "armies.table-sweep", Order = int.MinValue)]
public sealed class ArmyTableSweepSystem : IGameSystem
{
    /// <inheritdoc/>
    public GameState Execute(SystemContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return ArmyTable.KeepsDeadSlots(context.Ruleset) ? ArmyTable.Sweep(context.State) : context.State;
    }
}
