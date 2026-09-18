using IC2.Engine.Calendar;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Core;

namespace IC2.Engine.Tests.Naval;

/// <summary>
/// The pieces a Naval test needs: the shipped toy scenario (reused from <see cref="CoreTestbed"/>, not
/// re-loaded, so this task's tests agree with every other task's on the same starting state), and a
/// coordinator/dispatcher built over the <em>real</em> engine assembly — mirrors
/// <c>IC2.Engine.Tests.Economy.EconomyTestbed</c>'s shape exactly, kept as its own file here rather than
/// referencing that one, since <c>tests/IC2.Engine.Tests/Economy/**</c> is T08's Owns list, not this
/// task's.
/// </summary>
public static class NavalTestbed
{
    /// <summary>The shipped toy scenario with its world and ruleset resolved.</summary>
    public static ResolvedScenario Toy => CoreTestbed.Toy;

    /// <summary>The shipped toy ruleset. Every constant a Naval test needs comes from here.</summary>
    public static Ruleset Ruleset => Toy.Ruleset;

    /// <summary>The toy scenario's starting state.</summary>
    public static GameState InitialState() => CoreTestbed.InitialState();

    /// <summary>
    /// A coordinator over the real engine assembly, narrowed to only the declaring types named in
    /// <paramref name="only"/> — real production classes throughout, scoped so a test of one mechanism
    /// is not also, incidentally, exercising quarterly billing, weather or calendar advance against a
    /// hand-built fixture those systems' own numbers were never chosen to be neutral against.
    /// </summary>
    public static TurnCoordinator CoordinatorOnly(IEventSink? sink, params Type[] only) =>
        new(
            SystemRegistry.FromAssemblies(new[] { typeof(CalendarSystem).Assembly }, type => only.Contains(type)),
            Toy.Ruleset,
            Toy.World,
            sink ?? NullEventSink.Instance);

    /// <summary>A dispatcher over the real engine assembly's command handlers, publishing to <paramref name="sink"/>.</summary>
    public static CommandDispatcher RealEngineDispatcher(IEventSink? sink = null) =>
        new(SystemRegistry.FromEngineAssembly(), Toy.Ruleset, Toy.World, sink ?? NullEventSink.Instance);
}
