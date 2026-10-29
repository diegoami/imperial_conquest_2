using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Armies;

/// <summary>
/// T240 (#1601) and T244 (#1606): the original's army and fleet tables under
/// <c>flags.unitTableCompaction = swapRemoveSweep</c>, in one place.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The original</strong> leaves a removed army's record in its slot, marked dead, and appends a new army
/// after every slot, dead or alive. The sweep <c>FUN_0044adb0</c> walks the table from the last index down and
/// <c>FUN_0044abe0</c> swap-removes each dead record: the last record moves into the dead slot and the count drops
/// by one. <c>FUN_00451fdc</c> sweeps at its start, when a human ends the turn, and before each AI seat's
/// <c>FUN_0044fa20</c> <strong>[confirmed: code; 2026-10-30-army-table-order-after-deaths117.md and
/// 2026-10-30-all-ashore-army-order1439.md, research 150d95d]</strong>. The same call then sweeps the fleet table the
/// same way, through <c>FUN_0044ad7c</c> <strong>[confirmed: code and 31 of 31 fleet-loss End turns; the same
/// report's addendum 4, research 325c94c]</strong>.
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
/// T244 treats <see cref="GameState.Fleets"/> and <see cref="GameState.DeadFleetSlots"/> exactly the same way, in the
/// same <see cref="Record"/> and <see cref="Sweep"/>, armies first. A fleet's <see cref="FleetState.CarriedArmyId"/>
/// and an army's <see cref="ArmyState.AboardFleetId"/> are ids, not table indexes, so moving a record repoints
/// nothing.
/// </para>
/// <para>
/// Under <c>inOrder</c> both are no-ops: a removal closes the gap at once and the survivors keep their order.
/// </para>
/// </remarks>
public static class ArmyTable
{
    /// <summary>
    /// Test seam: called with a description whenever <see cref="Record"/> meets a step that does not keep the
    /// survivors' relative order or does not append its new armies or fleets. Thread-static, so a test that sets it
    /// sees only its own steps.
    /// </summary>
    public static Action<string>? OrderViolationObserver
    {
        get => _orderViolationObserver;
        set => _orderViolationObserver = value;
    }

    [ThreadStatic]
    private static Action<string>? _orderViolationObserver;

    /// <summary>Test seam: called with the number of dead army slots each sweep that had any closed. Thread-static.</summary>
    public static Action<int>? SweepObserver
    {
        get => _sweepObserver;
        set => _sweepObserver = value;
    }

    [ThreadStatic]
    private static Action<int>? _sweepObserver;

    /// <summary>T244: as <see cref="SweepObserver"/>, for the fleet table's dead slots. Thread-static.</summary>
    public static Action<int>? FleetSweepObserver
    {
        get => _fleetSweepObserver;
        set => _fleetSweepObserver = value;
    }

    [ThreadStatic]
    private static Action<int>? _fleetSweepObserver;

    private sealed record Kind(string Noun, string WithArticle, string Plural);

    private static readonly Kind ArmyKind = new("army", "an army", "armies");

    private static readonly Kind FleetKind = new("fleet", "a fleet", "fleets");

    /// <summary>Whether <paramref name="ruleset"/> keeps dead slots until the sweep.</summary>
    public static bool KeepsDeadSlots(Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(ruleset);
        return ruleset.Flags.UnitTableCompaction == UnitTableCompactionPolicy.SwapRemoveSweep;
    }

    /// <summary>
    /// The army table: <see cref="GameState.Armies"/> with the dead slots interleaved, a <see langword="null"/> entry
    /// for each dead slot.
    /// </summary>
    public static IReadOnlyList<ArmyState?> Slots(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return Interleave(state.Armies, state.DeadArmySlots, ArmyKind);
    }

    /// <summary>
    /// T244: the fleet table, <see cref="GameState.Fleets"/> with the dead slots interleaved, a <see langword="null"/>
    /// entry for each dead slot.
    /// </summary>
    public static IReadOnlyList<FleetState?> FleetSlots(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return Interleave(state.Fleets, state.DeadFleetSlots, FleetKind);
    }

    /// <summary>
    /// T244: the number of records in the fleet table, the live fleets plus the dead slots not yet swept — the
    /// original's whole-table count <c>DAT_004a0326</c>.
    /// </summary>
    public static int FleetTableCount(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.Fleets.Count + (state.DeadFleetSlots?.Count ?? 0);
    }

    /// <summary>
    /// Records the dead slots one accepted step left: every army or fleet in <paramref name="before"/> that is no
    /// longer in <paramref name="after"/> leaves a dead slot at its table position. Survivors keep their positions and
    /// a new army or fleet takes the next position after every slot of its table, dead or alive. Returns
    /// <paramref name="after"/> itself under <c>inOrder</c> or when nothing changed.
    /// </summary>
    /// <remarks>
    /// A step that reorders surviving records, or that places a new record anywhere but after every survivor, does not
    /// fit the table. A new record placed between survivors takes a dead slot between them when there is one (the
    /// all-ashore embark's receiver, which replaces its source in place). Otherwise that table's dead slots are dropped
    /// and the table becomes the live list as it stands. Either case is reported to <see cref="OrderViolationObserver"/>.
    /// </remarks>
    public static GameState Record(GameState before, GameState after, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        if (!KeepsDeadSlots(ruleset))
        {
            return after;
        }

        var result = after;
        if (!ReferenceEquals(before.Armies, after.Armies) || !ReferenceEquals(before.DeadArmySlots, after.DeadArmySlots))
        {
            var slots = RecordTable(
                before.Armies, before.DeadArmySlots, after.Armies, after.DeadArmySlots, a => a.Id, ArmyKind);
            if (!Equals(slots, after.DeadArmySlots))
            {
                result = result with { DeadArmySlots = slots };
            }
        }

        // T244: the fleet table, the same way.
        if (!ReferenceEquals(before.Fleets, after.Fleets) || !ReferenceEquals(before.DeadFleetSlots, after.DeadFleetSlots))
        {
            var slots = RecordTable(
                before.Fleets, before.DeadFleetSlots, after.Fleets, after.DeadFleetSlots, f => f.Id, FleetKind);
            if (!Equals(slots, after.DeadFleetSlots))
            {
                result = result with { DeadFleetSlots = slots };
            }
        }

        return result;
    }

    /// <summary>
    /// The sweep, <c>FUN_0044adb0</c>: the army table through <c>FUN_0044abe0</c>, then the fleet table through
    /// <c>FUN_0044ad7c</c>. In each, from the last slot down, each dead slot takes the record currently in the last
    /// slot, and the table shrinks by one. Returns <paramref name="state"/> itself when it has no dead slot.
    /// </summary>
    public static GameState Sweep(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var result = state;
        if (state.DeadArmySlots is { Count: > 0 })
        {
            SweepObserver?.Invoke(state.DeadArmySlots.Count);
            result = result with
            {
                Armies = SweepTable(Interleave(state.Armies, state.DeadArmySlots, ArmyKind)),
                DeadArmySlots = null,
            };
        }
        else if (state.DeadArmySlots is not null)
        {
            result = result with { DeadArmySlots = null };
        }

        if (state.DeadFleetSlots is { Count: > 0 })
        {
            FleetSweepObserver?.Invoke(state.DeadFleetSlots.Count);
            result = result with
            {
                Fleets = SweepTable(Interleave(state.Fleets, state.DeadFleetSlots, FleetKind)),
                DeadFleetSlots = null,
            };
        }
        else if (state.DeadFleetSlots is not null)
        {
            result = result with { DeadFleetSlots = null };
        }

        return result;
    }

    private static List<T?> Interleave<T>(ValueList<T> live, ValueList<int>? dead, Kind kind)
        where T : class
    {
        var table = new List<T?>(live.Count + (dead?.Count ?? 0));
        var next = 0;
        var d = 0;
        while (next < live.Count || (dead is not null && d < dead.Count))
        {
            if (dead is not null && d < dead.Count && dead[d] == table.Count)
            {
                table.Add(null);
                d++;
                continue;
            }

            if (next >= live.Count)
            {
                throw new InvalidOperationException(
                    $"The {kind.Noun} table's dead slot {dead![d]} lies beyond its last position {table.Count - 1}.");
            }

            table.Add(live[next++]);
        }

        return table;
    }

    /// <summary>
    /// <see cref="Record"/> for one table: the dead slots the table holds after the step, or <see langword="null"/>
    /// for none or when the step does not fit the table.
    /// </summary>
    private static ValueList<int>? RecordTable<T>(
        ValueList<T> beforeLive,
        ValueList<int>? beforeDead,
        ValueList<T> afterLive,
        ValueList<int>? afterDead,
        Func<T, string> idOf,
        Kind kind)
        where T : class
    {
        // The positions every record of `before` holds in its table.
        var beforeTable = Interleave(beforeLive, beforeDead, kind);
        var position = new Dictionary<string, int>(beforeLive.Count, StringComparer.Ordinal);
        for (var i = 0; i < beforeTable.Count; i++)
        {
            if (beforeTable[i] is { } record)
            {
                position[idOf(record)] = i;
            }
        }

        // The dead slots: those `before` had, those a nested step already recorded into `after`, and the positions
        // of the records this step removed.
        var dead = new SortedSet<int>();
        AddAll(dead, beforeDead);
        AddAll(dead, afterDead);
        var afterIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var record in afterLive)
        {
            afterIds.Add(idOf(record));
        }

        foreach (var record in beforeLive)
        {
            if (!afterIds.Contains(idOf(record)))
            {
                dead.Add(position[idOf(record)]);
            }
        }

        // Place the live records of `after`: a survivor at its position, a new record after every slot.
        var nextSurvivor = new int[afterLive.Count];
        var upcoming = int.MaxValue;
        for (var k = afterLive.Count - 1; k >= 0; k--)
        {
            nextSurvivor[k] = upcoming;
            if (position.TryGetValue(idOf(afterLive[k]), out var p))
            {
                upcoming = p;
            }
        }

        var occupied = new SortedSet<int>();
        var last = -1;
        string? violation = null;
        for (var k = 0; k < afterLive.Count && violation is null; k++)
        {
            var id = idOf(afterLive[k]);
            int at;
            if (position.TryGetValue(id, out var p))
            {
                if (p <= last)
                {
                    violation = $"{kind.Noun} '{id}' moved before {kind.WithArticle} that preceded it in the table";
                    break;
                }

                at = p;
            }
            else if (nextSurvivor[k] != int.MaxValue)
            {
                // A new record between survivors: it can only stand in a dead slot between them.
                var reused = dead.GetViewBetween(last + 1, nextSurvivor[k] - 1);
                if (reused.Count == 0)
                {
                    violation = $"new {kind.Noun} '{id}' was placed between surviving {kind.Plural}";
                    break;
                }

                at = reused.Min;
                dead.Remove(at);
                OrderViolationObserver?.Invoke(
                    $"new {kind.Noun} '{id}' was placed between surviving {kind.Plural}; it takes dead slot {at}");
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
                violation = $"the step left a gap in the {kind.Noun} table";
            }
        }

        if (violation is not null)
        {
            OrderViolationObserver?.Invoke(violation + "; the dead slots are dropped");
            return null;
        }

        return dead.Count == 0 ? null : ValueList.From(dead);
    }

    private static ValueList<T> SweepTable<T>(List<T?> table)
        where T : class
    {
        for (var i = table.Count - 1; i >= 0; i--)
        {
            if (i < table.Count && table[i] is null)
            {
                table[i] = table[^1];
                table.RemoveAt(table.Count - 1);
            }
        }

        return ValueList.From(table.Select(r => r!));
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
/// T240/T244: the army- and fleet-table sweep (<see cref="ArmyTable.Sweep"/>) at the start of every seat's pipeline
/// run, which is when a human seat ends its turn and before an AI seat acts, as <c>FUN_00451fdc</c> sweeps. Runs first
/// in <see cref="TurnPhase.SeatStart"/>; a no-op under <c>inOrder</c>, where there is never a dead slot.
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
