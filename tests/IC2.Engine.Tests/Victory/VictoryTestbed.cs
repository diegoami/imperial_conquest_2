using IC2.Engine.Model;
using IC2.Engine.Tests.Core;

namespace IC2.Engine.Tests.Victory;

/// <summary>
/// The pieces every Victory test needs: the real shipped toy <see cref="World"/>, <see cref="Ruleset"/>
/// and <see cref="Scenario"/> (reused from <see cref="CoreTestbed"/>, not re-loaded, so a "the all-cities
/// test uses the shipped world's own city count" check is checking the same world every other task's
/// tests agree on), plus small <c>with</c>-based mutators so a test can build the one state it needs
/// without repeating <see cref="GameState"/>'s constructor.
/// </summary>
/// <remarks>
/// North owns <c>arx</c> and <c>portus</c>; South owns <c>meridia</c> — <c>data/worlds/toy-3city.json</c>.
/// Every mutator below reads ids off the loaded state rather than assuming a fixed city or nation count,
/// so nothing here breaks if the toy world's own shape ever changes.
/// </remarks>
public static class VictoryTestbed
{
    /// <summary>The shipped toy world. <see cref="World.Cities"/>'s own count is the "334" of this world.</summary>
    public static World World => CoreTestbed.Toy.World;

    /// <summary>The shipped toy ruleset. Every constant a Victory test needs comes from here.</summary>
    public static Ruleset Ruleset => CoreTestbed.Toy.Ruleset;

    /// <summary>The shipped toy scenario (<c>victory.type = "totalConquest"</c>, <c>turnLimit = 50</c>).</summary>
    public static Scenario Scenario => CoreTestbed.Toy.Scenario;

    /// <summary>The toy scenario's starting state: North holds 2 of 3 cities, South holds the third.</summary>
    public static GameState InitialState() => CoreTestbed.InitialState();

    /// <summary>Reassigns every city in <paramref name="state"/> to one nation.</summary>
    public static GameState WithAllCitiesOwnedBy(GameState state, string nationId) =>
        state with { Cities = ValueList.From(state.Cities.Select(c => c with { Owner = nationId })) };

    /// <summary>Reassigns one named city's owner.</summary>
    public static GameState WithCityOwner(GameState state, string cityId, string ownerId) =>
        state with
        {
            Cities = ValueList.From(state.Cities.Select(c =>
                string.Equals(c.Id, cityId, StringComparison.Ordinal) ? c with { Owner = ownerId } : c)),
        };

    /// <summary>Sets the calendar's year (BC, counting down).</summary>
    public static GameState WithYear(GameState state, int yearBc) =>
        state with { Calendar = state.Calendar with { YearBc = yearBc } };

    /// <summary>Sets the calendar's elapsed-turn counter.</summary>
    public static GameState WithTurnIndex(GameState state, int turnIndex) =>
        state with { Calendar = state.Calendar with { TurnIndex = turnIndex } };

    /// <summary>Sets the symmetric relation between two nations to a state code.</summary>
    public static GameState WithRelation(GameState state, string nationA, string nationB, int stateCode) =>
        state with { Relations = state.Relations.WithRelation(nationA, nationB, stateCode) };

    /// <summary>Sets one nation's treasury.</summary>
    public static GameState WithTreasury(GameState state, string nationId, int treasury) =>
        WithNation(state, nationId, n => n with { Treasury = treasury });

    /// <summary>Sets one nation's unity.</summary>
    public static GameState WithUnity(GameState state, string nationId, int unity) =>
        WithNation(state, nationId, n => n with { Unity = unity });

    /// <summary>Marks one nation eliminated.</summary>
    public static GameState WithEliminated(GameState state, string nationId) =>
        WithNation(state, nationId, n => n with { Eliminated = true });

    private static GameState WithNation(GameState state, string nationId, Func<NationState, NationState> update) =>
        state with
        {
            Nations = ValueList.From(state.Nations.Select(n =>
                string.Equals(n.Id, nationId, StringComparison.Ordinal) ? update(n) : n)),
        };
}
