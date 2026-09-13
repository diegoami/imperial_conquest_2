using System.Reflection;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Serialization;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Core;

/// <summary>
/// Scopes a test fixture's registrations to one named group.
/// </summary>
/// <remarks>
/// <para>
/// The assembly scan is global by design, so every attributed fixture in this test project would
/// otherwise be discovered by every test that builds a registry. A group marker keeps each test's
/// pipeline to its own fixtures <em>without</em> giving up the property under test: a fixture is still
/// registered by attribute alone, still lives only in its own file, and adding another one to a group
/// still means adding a file and editing nothing.
/// </para>
/// <para>
/// This lives in the test project, not the engine: production code has one pipeline and does not need
/// to slice it.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class TestFixtureGroupAttribute : Attribute
{
    /// <summary>Puts a fixture in a group.</summary>
    public TestFixtureGroupAttribute(string group) => Group = group;

    /// <summary>The group name.</summary>
    public string Group { get; }
}

/// <summary>
/// The shipped toy scenario, plus the pieces a Core test needs built on top of it: a registry scoped to
/// one fixture group, a coordinator, and a dispatcher.
/// </summary>
/// <remarks>
/// Every number these tests use comes from the loaded <see cref="Model.Ruleset"/> or from the T04
/// fixtures corpus — nothing in this folder writes a gameplay constant as a C# literal.
/// </remarks>
public static class CoreTestbed
{
    private static readonly Lazy<ResolvedScenario> LazyToy = new(
        () => GameDataRepository.Load(ModelTestPaths.DataRoot).Resolve("toy-3city"));

    /// <summary>The toy scenario with its world and ruleset resolved.</summary>
    public static ResolvedScenario Toy => LazyToy.Value;

    /// <summary>The toy scenario's starting state.</summary>
    public static GameState InitialState() =>
        GameStateFactory.CreateInitial(Toy.World, Toy.Ruleset, Toy.Scenario);

    /// <summary>
    /// A registry built by scanning this test assembly, keeping only fixtures in
    /// <paramref name="group"/>.
    /// </summary>
    public static SystemRegistry RegistryFor(string group) =>
        SystemRegistry.FromAssemblies(
            new[] { typeof(CoreTestbed).Assembly },
            type => string.Equals(
                type.GetCustomAttribute<TestFixtureGroupAttribute>(inherit: false)?.Group,
                group,
                StringComparison.Ordinal));

    /// <summary>
    /// A coordinator over one fixture group, publishing to <paramref name="sink"/>, with no dispatcher —
    /// so a system that tries to issue a command is refused rather than served.
    /// </summary>
    public static TurnCoordinator CoordinatorFor(string group, IEventSink? sink = null) =>
        new(RegistryFor(group), Toy.Ruleset, Toy.World, sink ?? NullEventSink.Instance);

    /// <summary>
    /// A coordinator and a dispatcher over the same registry and the same sink, wired together the way
    /// T23's harness will wire them.
    /// </summary>
    public static TurnCoordinator CoordinatorWithCommandsFor(string group, IEventSink? sink = null)
    {
        var registry = RegistryFor(group);
        var events = sink ?? NullEventSink.Instance;
        var dispatcher = new CommandDispatcher(registry, Toy.Ruleset, Toy.World, events);
        return new TurnCoordinator(registry, Toy.Ruleset, Toy.World, events, dispatcher);
    }

    /// <summary>A dispatcher over one fixture group, publishing to <paramref name="sink"/>.</summary>
    public static CommandDispatcher DispatcherFor(string group, IEventSink? sink = null) =>
        new(RegistryFor(group), Toy.Ruleset, Toy.World, sink ?? NullEventSink.Instance);
}
