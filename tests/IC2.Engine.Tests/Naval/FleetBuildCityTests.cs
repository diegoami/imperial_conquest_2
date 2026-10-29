using IC2.Engine.Ai;
using IC2.Engine.Model;
using IC2.Engine.Naval;
using IC2.Engine.Presentation;
using IC2.Engine.Serialization;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Naval;

/// <summary>
/// T241 (<c>docs/tasks/T241.md</c>, #918): a fleet order is built at the nation's free coastal city nearest
/// its capital, by Chebyshev distance, through <see cref="CoastalCity.FleetBuildCity"/>. The original's
/// six Rome orders in one turn went to Caere, Antium, Tarquinii, Pisae, Neapolis and Ariminum
/// (<c>2026-10-09-fleet-build-city-nearest-capital.md</c>, research <c>c36b255</c>); before T241 the
/// clone took the first free coastal city in world order, Pisae. Ties go to the lower city id [designed];
/// no capital falls back to world order; the AI's <see cref="AiFaithfulEconomy.ChoosePort"/> keeps its
/// own any-nation free test and its port choices.
/// </summary>
public sealed class FleetBuildCityTests
{
    private static ResolvedScenario Classical() =>
        GameDataRepository.Load(ModelTestPaths.DataRoot).Resolve("classical-mediterranean");

    private static GameSession Session(string nationId)
    {
        var classical = Classical();
        return new GameSession(
            classical.World, classical.Ruleset, classical.Scenario, seedOverride: 1, humanSeatNationId: nationId);
    }

    /// <summary>
    /// Orders <paramref name="count"/> fleets for the session's seat in one turn, each at the city the
    /// helper names, through the engine's own <c>order-fleet</c> command, and returns the cities.
    /// </summary>
    private static List<string> OrderInOneTurn(GameSession session, string nationId, int count)
    {
        var cities = new List<string>();
        var turn = session.State.Calendar.TurnIndex;
        for (var i = 0; i < count; i++)
        {
            var nation = session.State.NationById(nationId)!;
            var city = CoastalCity.FleetBuildCity(session.State, session.World, nation);
            Assert.NotNull(city);
            var fleetId = $"t241-{nationId}-{i}";
            session.Submit($"order-fleet {city!.Id} 10 {fleetId}");
            var fleet = session.State.FleetById(fleetId);
            Assert.True(fleet is not null, $"order {i + 1} at {city.Id} was accepted");
            Assert.Equal(city.Id, fleet!.BuildCityId);
            cities.Add(city.Id);
        }

        Assert.Equal(turn, session.State.Calendar.TurnIndex);
        return cities;
    }

    [Fact]
    public void Romes_orders_in_one_turn_are_built_nearest_the_capital_first()
    {
        var session = Session("rome");

        var cities = OrderInOneTurn(session, "rome", 6);

        Assert.Equal(new[] { "caere", "antium", "tarquinii", "pisae", "neapolis", "ariminum" }, cities);
    }

    /// <summary>
    /// Carthage's Clupea and Utica are both 2 from Carthago; Utica comes first in the world, Clupea first
    /// by id, and the lower id wins [designed]. Thracia's Aenus, Sestos and Odessus are all 12 from
    /// Byzantium, in that world order, and go Aenus, Odessus, Sestos.
    /// </summary>
    [Fact]
    public void Equally_near_cities_go_to_the_lower_city_id()
    {
        var carthage = Session("carthage");
        var world = carthage.World;
        var carthago = carthage.State.CityById("carthago")!;
        Assert.Equal(
            AiView.Distance(carthago.X, carthago.Y, carthage.State.CityById("utica")!.X, carthage.State.CityById("utica")!.Y),
            AiView.Distance(carthago.X, carthago.Y, carthage.State.CityById("clupea")!.X, carthage.State.CityById("clupea")!.Y));
        Assert.True(
            IndexOf(carthage.State, "utica") < IndexOf(carthage.State, "clupea"),
            "Utica precedes Clupea in the world's order, so world order would pick it");

        Assert.Equal(new[] { "carthago", "clupea", "utica" }, OrderInOneTurn(carthage, "carthage", 3));

        var thracia = Session("thracia");
        Assert.Equal(
            new[] { "byzantium", "perinthus", "apollonia", "aenus", "odessus", "sestos", "maronea" },
            OrderInOneTurn(thracia, "thracia", 7));
        Assert.True(CoastalCity.IsCoastal(thracia.State.CityById("odessus")!, world));
    }

    [Fact]
    public void Without_a_capital_the_first_free_coastal_city_in_world_order_builds()
    {
        var session = Session("rome");
        var state = session.State;
        var rome = state.NationById("rome")!;
        Assert.Equal("caere", CoastalCity.FleetBuildCity(state, session.World, rome)!.Id);

        var firstInWorldOrder = CoastalCity.FreeForFleetOrder(state, session.World, "rome")[0];
        Assert.Equal("pisae", firstInWorldOrder.Id);

        Assert.Equal("pisae", CoastalCity.FleetBuildCity(state, session.World, rome with { CapitalCityId = null })!.Id);
        Assert.Equal(
            "pisae", CoastalCity.FleetBuildCity(state, session.World, rome with { CapitalCityId = "no-such-city" })!.Id);
    }

    [Fact]
    public void No_free_coastal_city_gives_none()
    {
        var session = Session("rome");
        var busy = CoastalCity.FreeForFleetOrder(session.State, session.World, "rome")
            .Select((c, i) => Fleet($"busy-{i}", "rome", c.Id));
        var state = session.State with { Fleets = ValueList.From(session.State.Fleets.Concat(busy)) };

        Assert.Null(CoastalCity.FleetBuildCity(state, session.World, state.NationById("rome")!));
    }

    /// <summary>
    /// The free test: by default only the nation's own fleet under construction makes a city busy (T237);
    /// with <c>busyWhenAnyNationBuilds</c> a foreign one does too, the AI's test.
    /// </summary>
    [Fact]
    public void A_foreign_fleet_under_construction_makes_the_port_busy_only_for_the_AIs_test()
    {
        var session = Session("rome");
        var state = session.State with
        {
            Fleets = ValueList.From(session.State.Fleets.Append(Fleet("foreign", "carthage", "caere"))),
        };
        var rome = state.NationById("rome")!;

        Assert.Equal("caere", CoastalCity.FleetBuildCity(state, session.World, rome)!.Id);
        Assert.Equal("antium", CoastalCity.FleetBuildCity(state, session.World, rome, busyWhenAnyNationBuilds: true)!.Id);
        Assert.Equal("antium", AiFaithfulEconomy.ChoosePort(state, session.World, rome)!.Id);
    }

    /// <summary>
    /// The AI's port choice is unchanged by T241: for every nation at the classical start, and after each
    /// of up to twelve further orders placed alternately by the nation and by a foreign nation at the port
    /// just chosen, <see cref="AiFaithfulEconomy.ChoosePort"/> names the city <see cref="ChoosePortBeforeT241"/>,
    /// a verbatim copy of the pre-T241 body, names. Rome's and Thracia's sequences are pinned too; a
    /// world-order choice would give Pisae and Maronea.
    /// </summary>
    [Fact]
    public void The_AIs_port_choices_are_unchanged()
    {
        var session = Session("rome");
        var world = session.World;
        var sequences = new Dictionary<string, List<string>>();
        foreach (var start in session.State.Nations)
        {
            var state = session.State;
            var sequence = new List<string>();
            for (var i = 0; i < 12; i++)
            {
                var nation = state.NationById(start.Id)!;
                var expected = ChoosePortBeforeT241(state, world, nation);
                var actual = AiFaithfulEconomy.ChoosePort(state, world, nation);
                Assert.Equal(expected?.Id, actual?.Id);
                if (actual is null)
                {
                    break;
                }

                sequence.Add(actual.Id);
                var builder = i % 2 == 0 ? start.Id : "foreign";
                state = state with { Fleets = ValueList.From(state.Fleets.Append(Fleet($"ai-{i}", builder, actual.Id))) };
            }

            sequences[start.Id] = sequence;
        }

        Assert.Equal(
            new[] { "caere", "antium", "tarquinii", "pisae", "neapolis", "ariminum", "paestum", "teanum-apulum", "thurii", "heraclea", "locri", "rhegium" },
            sequences["rome"]);
        Assert.Equal(
            new[] { "byzantium", "perinthus", "apollonia", "aenus", "odessus", "sestos", "maronea", "tomi", "istrus", "ophiusa" },
            sequences["thracia"]);
        Assert.Empty(sequences["dacia"]);

        var noCapital = session.State.NationById("rome")! with { CapitalCityId = null };
        Assert.Null(AiFaithfulEconomy.ChoosePort(session.State, world, noCapital));
    }

    /// <summary><c>AiFaithfulEconomy.ChoosePort</c> exactly as it was before T241, the AI-unchanged reference.</summary>
    private static CityState? ChoosePortBeforeT241(GameState state, World world, NationState nation)
    {
        var capital = nation.CapitalCityId is { } id ? state.CityById(id) : null;
        if (capital is null)
        {
            return null;
        }

        CityState? best = null;
        var bestDistance = int.MaxValue;
        foreach (var city in state.Cities)
        {
            if (!string.Equals(city.Owner, nation.Id, StringComparison.Ordinal)
                || !CoastalCity.IsCoastal(city, world)
                || state.Fleets.Any(f => f.IsUnderConstruction && string.Equals(f.BuildCityId, city.Id, StringComparison.Ordinal)))
            {
                continue;
            }

            var distance = AiView.Distance(city.X, city.Y, capital.X, capital.Y);
            if (distance < bestDistance
                || (distance == bestDistance && best is not null && string.CompareOrdinal(city.Id, best.Id) < 0))
            {
                best = city;
                bestDistance = distance;
            }
        }

        return best;
    }

    private static FleetState Fleet(string id, string nation, string buildCityId) =>
        new(id, nation, 0, 0, 0, 10, 100, 0, 0, 24, buildCityId, null, null);

    private static int IndexOf(GameState state, string cityId)
    {
        for (var i = 0; i < state.Cities.Count; i++)
        {
            if (string.Equals(state.Cities[i].Id, cityId, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }
}
