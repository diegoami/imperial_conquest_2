using System.Collections.Concurrent;
using System.Reflection;
using IC2.Engine.Calendar;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Core;

namespace IC2.Engine.Tests.Calendar;

/// <summary>
/// The pieces a Calendar test needs: the shipped toy scenario (reused from
/// <see cref="CoreTestbed"/> rather than re-loaded, so both tasks' tests agree on the same starting
/// state) plus a registry scoped to <em>this task's own</em> systems -- <c>CalendarSystem</c> and
/// <c>SeatRotationSystem</c>, found by namespace rather than "every type in the engine assembly" -- plus
/// this test project's own group-scoped fixtures (the attrition-phase probes and friends).
/// </summary>
/// <remarks>
/// T32 (bug #50): the previous filter, <c>type.Assembly == engineAssembly</c>, pulled in <em>every</em>
/// <c>[GameSystem]</c>-attributed class the real engine assembly happens to contain -- so a Calendar
/// test's outcome silently changed the moment a later task (T08, then T14) registered a real production
/// system into a phase a Calendar test also cares about. Scoping to
/// <c>typeof(CalendarSystem).Namespace</c> keeps exactly the two systems this task's own Owns list
/// (<c>src/IC2.Engine/Calendar/**</c>) declares, so no later task's system can change what a Calendar
/// test sees.
/// </remarks>
public static class CalendarTestbed
{
    private static readonly ConcurrentDictionary<string, Lazy<SystemRegistry>> RegistryCache =
        new(StringComparer.Ordinal);

    private static readonly string? CalendarNamespace = typeof(CalendarSystem).Namespace;

    /// <summary>The shipped toy scenario with its world and ruleset resolved.</summary>
    public static ResolvedScenario Toy => CoreTestbed.Toy;

    /// <summary>The toy scenario's starting state.</summary>
    public static GameState InitialState() => CoreTestbed.InitialState();

    /// <summary>
    /// A registry over this task's own Calendar systems (by namespace, not by "the whole engine
    /// assembly") plus this test project's fixtures tagged with <paramref name="group"/>. Built once per
    /// distinct <paramref name="group"/> and reused on every later call, rather than re-scanning both
    /// assemblies every time a test asks for it (T06 review follow-up #43).
    /// </summary>
    public static SystemRegistry RegistryFor(string group) =>
        RegistryCache.GetOrAdd(group, g => new Lazy<SystemRegistry>(() => BuildRegistry(g))).Value;

    /// <summary>
    /// A coordinator over one fixture group plus the engine's own Calendar systems, publishing to
    /// <paramref name="sink"/>, with no dispatcher.
    /// </summary>
    public static TurnCoordinator CoordinatorFor(string group, IEventSink? sink = null) =>
        new(RegistryFor(group), Toy.Ruleset, Toy.World, sink ?? NullEventSink.Instance);

    private static SystemRegistry BuildRegistry(string group)
    {
        var engineAssembly = typeof(CalendarSystem).Assembly;
        var testAssembly = typeof(CalendarTestbed).Assembly;

        return SystemRegistry.FromAssemblies(
            new[] { engineAssembly, testAssembly },
            type => IsCalendarOwnSystem(type) || IsInGroup(type, group));
    }

    private static bool IsCalendarOwnSystem(Type type) =>
        string.Equals(type.Namespace, CalendarNamespace, StringComparison.Ordinal);

    private static bool IsInGroup(Type type, string group) =>
        string.Equals(
            type.GetCustomAttribute<TestFixtureGroupAttribute>(inherit: false)?.Group,
            group,
            StringComparison.Ordinal);
}
