using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Tests.Core;

namespace IC2.Engine.Tests.Battle.Commands;

/// <summary>
/// The pieces a <c>Battle/Commands</c> test needs: the shipped toy scenario (through
/// <see cref="CoreTestbed"/>, like every other task's tests), a dispatcher over the real engine assembly,
/// and the two things these tests need that no existing testbed has — a state whose relation matrix
/// covers whichever nations the fixture invented, and a shorthand for putting two of them at war.
/// </summary>
/// <remarks>
/// Army, unit, fleet and city fixtures are reused from <see cref="BattleTestbed"/> and
/// <see cref="Tests.Cities.Capture.CaptureTestbed"/> rather than copied: these tests wire T16's and T17's
/// merged resolvers, so they should be resolving the same shapes those tasks' own tests resolve.
/// </remarks>
public static class BattleCommandTestbed
{
    /// <summary>The toy scenario's human seat, and the active seat at turn 0 — so it can issue commands.</summary>
    public const string NorthNationId = "north";

    /// <summary>The toy scenario's AI seat.</summary>
    public const string SouthNationId = "south";

    /// <summary>The shipped toy ruleset (set the way <c>classical-faithful</c> is set).</summary>
    public static Ruleset ToyRuleset => CoreTestbed.Toy.Ruleset;

    /// <summary>The toy world.</summary>
    public static World ToyWorld => CoreTestbed.Toy.World;

    /// <summary>A dispatcher over the real engine assembly — the same seam a seat uses.</summary>
    public static CommandDispatcher Dispatcher(IEventSink? sink = null) =>
        new(SystemRegistry.FromEngineAssembly(), ToyRuleset, ToyWorld, sink ?? NullEventSink.Instance);

    /// <summary>
    /// The random stream a command of <paramref name="commandKind"/> draws from when dispatched against
    /// <paramref name="state"/> — the dispatcher's own derivation, so a test can resolve the very same
    /// battle by hand and compare.
    /// </summary>
    /// <param name="state">The state the command is dispatched against.</param>
    /// <param name="commandKind">The command's <see cref="ICommand.Kind"/>.</param>
    /// <returns>A fresh generator positioned exactly where the handler's own will start.</returns>
    public static IRng RngFor(GameState state, string commandKind) =>
        SplitMix64Rng.ForStream(state.RandomSeed, CommandDispatcher.StreamNameFor(commandKind));

    /// <summary>Returns <paramref name="state"/> with two nations at war.</summary>
    /// <param name="state">The state to change.</param>
    /// <param name="a">One nation.</param>
    /// <param name="b">The other.</param>
    /// <returns>The state with that relation set to the ruleset's war code.</returns>
    public static GameState AtWar(GameState state, string a, string b) =>
        state with { Relations = state.Relations.WithRelation(a, b, ToyRuleset.Diplomacy.StateCodes.War) };

    /// <summary>
    /// A state built from an explicit set of nations, cities, armies and fleets, with a relation matrix
    /// that actually covers those nations.
    /// </summary>
    /// <remarks>
    /// <see cref="BattleTestbed.StateWith"/> and <c>CaptureTestbed.StateWith</c> both keep the toy
    /// scenario's own two-nation matrix, which is fine for a resolver that never reads it. These commands
    /// do read it, so a fixture that invents a third nation needs a matrix with a row for it — otherwise
    /// the war gate would refuse for the wrong reason and the test would pass while proving nothing.
    /// </remarks>
    /// <param name="nations">The fixture's nations.</param>
    /// <param name="cities">The fixture's cities.</param>
    /// <param name="armies">The fixture's armies, if any.</param>
    /// <param name="fleets">The fixture's fleets, if any.</param>
    /// <returns>The assembled state, every relation at peace.</returns>
    public static GameState StateWith(
        IEnumerable<NationState> nations,
        IEnumerable<CityState> cities,
        IEnumerable<ArmyState>? armies = null,
        IEnumerable<FleetState>? fleets = null)
    {
        var nationList = ValueList.From(nations);
        return CoreTestbed.InitialState() with
        {
            Nations = nationList,
            Cities = ValueList.From(cities),
            Armies = ValueList.From(armies ?? Array.Empty<ArmyState>()),
            Fleets = ValueList.From(fleets ?? Array.Empty<FleetState>()),
            Relations = DiplomaticRelations.Uniform(
                ValueList.From(nationList.Select(n => n.Id)), ToyRuleset.Diplomacy.StateCodes.Peace),
        };
    }
}
