using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Recruitment;
using IC2.Engine.Tests.Core;

namespace IC2.Engine.Tests.Recruitment;

/// <summary>
/// The pieces a Recruitment test needs: the real shipped toy scenario (reused from
/// <see cref="CoreTestbed"/>, not re-loaded, so this task's tests agree with every other task's on the
/// same starting state) and small helpers for editing one army, nation or the mercenary pool without
/// repeating the same <c>with</c>-expression everywhere.
/// </summary>
public static class RecruitmentTestbed
{
    /// <summary>The shipped toy ruleset. Every constant a Recruitment test needs comes from here.</summary>
    public static Ruleset Ruleset => CoreTestbed.Toy.Ruleset;

    /// <summary>The toy scenario's starting state.</summary>
    public static GameState InitialState() => CoreTestbed.InitialState();

    /// <summary>A dispatcher over the real engine assembly, publishing to <paramref name="sink"/>.</summary>
    public static CommandDispatcher Dispatcher(IEventSink? sink = null) => new(
        SystemRegistry.FromEngineAssembly(), CoreTestbed.Toy.Ruleset, CoreTestbed.Toy.World, sink ?? NullEventSink.Instance);

    /// <summary>
    /// A coordinator over the real engine assembly, narrowed to only the declaring types named in
    /// <paramref name="only"/> — the only way to reach an <see cref="IGameSystem"/> at all, since
    /// <c>SystemContext</c>'s constructor is internal and no test builds one directly (the same
    /// convention <c>EconomyTestbed.CoordinatorOnly</c> uses).
    /// </summary>
    public static TurnCoordinator CoordinatorOnly(IEventSink? sink, params Type[] only) =>
        new(
            SystemRegistry.FromAssemblies(new[] { typeof(RecruitmentSlotReadinessSystem).Assembly }, type => only.Contains(type)),
            CoreTestbed.Toy.Ruleset,
            CoreTestbed.Toy.World,
            sink ?? NullEventSink.Instance);

    /// <summary>Returns <paramref name="state"/> with one army replaced by <paramref name="updated"/>.</summary>
    public static GameState WithArmy(GameState state, ArmyState updated) =>
        state with
        {
            Armies = ValueList.From(state.Armies.Select(a =>
                string.Equals(a.Id, updated.Id, StringComparison.Ordinal) ? updated : a)),
        };

    /// <summary>Returns <paramref name="state"/> with one nation replaced by <paramref name="updated"/>.</summary>
    public static GameState WithNation(GameState state, NationState updated) =>
        state with
        {
            Nations = ValueList.From(state.Nations.Select(n =>
                string.Equals(n.Id, updated.Id, StringComparison.Ordinal) ? updated : n)),
        };

    /// <summary>Returns <paramref name="state"/> with its mercenary pool replaced outright.</summary>
    public static GameState WithMercenaryPool(GameState state, params MercenaryPoolSlot[] slots) =>
        state with { MercenaryPool = ValueList.Of(slots) };
}
