using IC2.Engine.Calendar;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Core;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Economy;

/// <summary>
/// The pieces an Economy test needs: the shipped toy scenario (reused from <see cref="CoreTestbed"/>, not
/// re-loaded, so this task's tests agree with every other task's on the same starting state), and a
/// coordinator built over the <em>real</em> engine assembly — no test-only stand-ins — so a multi-round
/// simulation test exercises exactly the pipeline a shipped build runs
/// (<c>calendar.advance-week</c>, <c>calendar.seat-rotation</c>, and this task's own
/// <c>economy.supply-consumption-and-morale</c> / <c>economy.weather-events</c> /
/// <c>economy.quarterly-billing</c>).
/// </summary>
public static class EconomyTestbed
{
    /// <summary>The shipped toy scenario with its world and ruleset resolved.</summary>
    public static ResolvedScenario Toy => CoreTestbed.Toy;

    /// <summary>The shipped toy ruleset. Every constant an Economy test needs comes from here.</summary>
    public static Ruleset Ruleset => Toy.Ruleset;

    /// <summary>The toy scenario's starting state.</summary>
    public static GameState InitialState() => CoreTestbed.InitialState();

    /// <summary>
    /// A coordinator over the real, unfiltered engine assembly — every shipped
    /// <c>[GameSystem]</c>/<c>[QuarterBoundaryHandler]</c> registration this build declares, nothing from
    /// the test assembly. Publishing to <paramref name="sink"/> if given, otherwise discarding.
    /// </summary>
    public static TurnCoordinator RealEngineCoordinator(IEventSink? sink = null) =>
        new(SystemRegistry.FromEngineAssembly(), Toy.Ruleset, Toy.World, sink ?? NullEventSink.Instance);

    /// <summary>
    /// A coordinator over the real engine assembly, narrowed to only the declaring types named in
    /// <paramref name="only"/>. Real production classes throughout — never a test stand-in — but scoped so
    /// a test of one mechanism (e.g. the supply→morale replay) is not also, incidentally, exercising
    /// quarterly billing or weather against a hand-built test army the toy world's economy numbers were
    /// never chosen to be neutral against.
    /// </summary>
    public static TurnCoordinator CoordinatorOnly(IEventSink? sink, params Type[] only) =>
        new(
            SystemRegistry.FromAssemblies(new[] { typeof(CalendarSystem).Assembly }, type => only.Contains(type)),
            Toy.Ruleset,
            Toy.World,
            sink ?? NullEventSink.Instance);
}
