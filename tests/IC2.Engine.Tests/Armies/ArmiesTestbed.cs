using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Tests.Core;

namespace IC2.Engine.Tests.Armies;

/// <summary>
/// The pieces an Armies test needs: the real shipped toy scenario (reused from <see cref="CoreTestbed"/>,
/// not re-loaded, so this task's tests agree with every other task's on the same starting state), a
/// dispatcher over the real engine assembly, and small helpers for building unit and army fixtures
/// without repeating the same constructor call everywhere.
/// </summary>
public static class ArmiesTestbed
{
    /// <summary>
    /// <c>north</c>: the toy scenario's human seat (<c>data/scenarios/toy-3city.json</c>). The active
    /// seat at the scenario's start, so commands issued by it need no extra turn-order setup.
    /// </summary>
    public const string NorthNationId = "north";

    /// <summary><c>south</c>: the toy scenario's AI seat.</summary>
    public const string SouthNationId = "south";

    /// <summary>The shipped toy ruleset. Every constant an Armies test needs comes from here.</summary>
    public static Ruleset Ruleset => CoreTestbed.Toy.Ruleset;

    /// <summary>The toy scenario's starting state.</summary>
    public static GameState InitialState() => CoreTestbed.InitialState();

    /// <summary>A dispatcher over the real engine assembly, publishing to <paramref name="sink"/>.</summary>
    public static CommandDispatcher Dispatcher(IEventSink? sink = null) => new(
        SystemRegistry.FromEngineAssembly(), CoreTestbed.Toy.Ruleset, CoreTestbed.Toy.World, sink ?? NullEventSink.Instance);

    /// <summary>
    /// A dispatcher over the real engine assembly built against <paramref name="ruleset"/> instead of
    /// the toy scenario's own — for a test that needs a different <see cref="RulesetFlags"/> value (for
    /// example, <c>seatAsymmetry</c> under <c>improved</c>) without a second shipped ruleset file.
    /// </summary>
    public static CommandDispatcher DispatcherWithRuleset(Ruleset ruleset, IEventSink? sink = null) => new(
        SystemRegistry.FromEngineAssembly(), ruleset, CoreTestbed.Toy.World, sink ?? NullEventSink.Instance);

    /// <summary>One regular unit slot of a given type, troops and quality, auto-named for the test.</summary>
    public static UnitSlot RegularUnit(string name, string unitTypeId = "light_infantry", int troops = 1000, int quality = 6) =>
        new(MercenaryLabel: 0, UnitTypeId: unitTypeId, Troops: troops, Quality: quality, Name: name);

    /// <summary>One mercenary unit slot — a non-zero <see cref="UnitSlot.MercenaryLabel"/>.</summary>
    public static UnitSlot MercenaryUnit(string name, string unitTypeId = "light_infantry", int troops = 1000, int quality = 6, int label = 7) =>
        new(MercenaryLabel: label, UnitTypeId: unitTypeId, Troops: troops, Quality: quality, Name: name);

    /// <summary>An army fixture with sensible defaults, overridable per test.</summary>
    public static ArmyState Army(
        string id,
        string nation,
        int x,
        int y,
        IEnumerable<UnitSlot> units,
        int moves = 5,
        int morale = 68,
        int money = 0,
        int supplyTons = 0,
        int? coveredTileCode = 2,
        string? aboardFleetId = null) =>
        new(id, nation, x, y, moves, morale, money, supplyTons, coveredTileCode, aboardFleetId, ValueList.From(units));

    /// <summary>Returns <paramref name="state"/> with its army list replaced outright.</summary>
    public static GameState WithArmies(GameState state, params ArmyState[] armies) =>
        state with { Armies = ValueList.Of(armies) };

    /// <summary>Returns <paramref name="state"/> with one nation replaced by <paramref name="updated"/>.</summary>
    public static GameState WithNation(GameState state, NationState updated) =>
        state with
        {
            Nations = ValueList.From(state.Nations.Select(n =>
                string.Equals(n.Id, updated.Id, StringComparison.Ordinal) ? updated : n)),
        };

    /// <summary>Returns <paramref name="state"/> with one city replaced by <paramref name="updated"/>.</summary>
    public static GameState WithCity(GameState state, CityState updated) =>
        state with
        {
            Cities = ValueList.From(state.Cities.Select(c =>
                string.Equals(c.Id, updated.Id, StringComparison.Ordinal) ? updated : c)),
        };
}
