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
/// state) plus a registry that scans the real <c>IC2.Engine</c> assembly -- so this task's own
/// <c>CalendarSystem</c> and <c>SeatRotationSystem</c> are found by the same attribute scan a shipped
/// build uses -- alongside this test project's own group-scoped fixtures (the attrition-phase probes).
/// </summary>
public static class CalendarTestbed
{
    /// <summary>The shipped toy scenario with its world and ruleset resolved.</summary>
    public static ResolvedScenario Toy => CoreTestbed.Toy;

    /// <summary>The toy scenario's starting state.</summary>
    public static GameState InitialState() => CoreTestbed.InitialState();

    /// <summary>
    /// A registry over the real engine assembly (so <c>CalendarSystem</c>/<c>SeatRotationSystem</c> are
    /// registered exactly as a shipped build would find them) plus this test project's fixtures tagged
    /// with <paramref name="group"/>.
    /// </summary>
    public static SystemRegistry RegistryFor(string group)
    {
        var engineAssembly = typeof(CalendarSystem).Assembly;
        var testAssembly = typeof(CalendarTestbed).Assembly;

        return SystemRegistry.FromAssemblies(
            new[] { engineAssembly, testAssembly },
            type => type.Assembly == engineAssembly || IsInGroup(type, group));
    }

    /// <summary>
    /// A coordinator over one fixture group plus the engine's own Calendar systems, publishing to
    /// <paramref name="sink"/>, with no dispatcher.
    /// </summary>
    public static TurnCoordinator CoordinatorFor(string group, IEventSink? sink = null) =>
        new(RegistryFor(group), Toy.Ruleset, Toy.World, sink ?? NullEventSink.Instance);

    private static bool IsInGroup(Type type, string group) =>
        string.Equals(
            type.GetCustomAttribute<TestFixtureGroupAttribute>(inherit: false)?.Group,
            group,
            StringComparison.Ordinal);
}
