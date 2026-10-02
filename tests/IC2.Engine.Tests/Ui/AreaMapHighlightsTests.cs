using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Engine.Serialization;
using IC2.Slice.UI;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Ui;

/// <summary>
/// T110 (docs/tasks/T110.md, Done-when 1): the overview mini-map's four <strong>Show</strong> highlight
/// layers mark exactly the tiles the audit's §1.5 table gives — every city of the viewed nation, its
/// capital, its on-map armies and its launched fleets — and All nations widens each to every nation.
/// </summary>
/// <remarks>
/// The state is the shipped <c>classical-mediterranean</c> world/ruleset resolved exactly the way the
/// CLI and the Godot checks resolve it (the same fixture <c>RecruitmentPanelViewModelTests</c> uses), so
/// every expected tile comes from the live <see cref="GameState"/> rather than a hand-copied coordinate.
/// The fleet rule is proved on an arranged state only where the shipped start is silent: Rome starts
/// with no fleet at all, so its own set would be empty and the "launched" filter untested. The two
/// arranged <see cref="FleetState"/> records carry the record's real null-launched / non-null-under-
/// construction shape and no invented engine behaviour.
/// </remarks>
public sealed class AreaMapHighlightsTests
{
    private const string RomeId = "rome";
    private const string CarthageId = "carthage";

    private static ResolvedScenario Classical() =>
        GameDataRepository.Load(ModelTestPaths.DataRoot).Resolve("classical-mediterranean");

    private static GameSession RomeSession(ulong seed = 1)
    {
        var classical = Classical();
        return new GameSession(
            classical.World, classical.Ruleset, classical.Scenario, seedOverride: seed, humanSeatNationId: RomeId);
    }

    /// <summary>
    /// Done-when 1, the viewed nation: with Rome viewed, Show cities marks exactly Rome's city tiles and
    /// Show capital marks exactly Rome's capital. Both are checked against the live state, and the
    /// non-empty assertions stop the "exactly" from passing on two empty sets.
    /// </summary>
    [Fact]
    public void Cities_and_capital_are_scoped_to_the_viewed_nation()
    {
        var session = RomeSession();
        var state = session.State;

        var expectedCities = state.Cities
            .Where(city => string.Equals(city.Owner, RomeId, StringComparison.Ordinal))
            .Select(city => (city.X, city.Y));
        var capitalId = state.NationById(RomeId)!.CapitalCityId;
        var capital = state.CityById(capitalId!)!;

        var cities = AreaMapHighlights.TilesFor(state, AreaMapHighlightKind.Cities, RomeId);
        var capitals = AreaMapHighlights.TilesFor(state, AreaMapHighlightKind.Capital, RomeId);

        Assert.True(cities.Count > 0, "Rome owns at least one city at the start (the assertion is not vacuous)");
        AssertSameTiles(expectedCities, cities);
        AssertSameTiles(new[] { (capital.X, capital.Y) }, capitals);

        // Scoping is real: a foreign nation's city is never in Rome's set.
        var foreignCity = state.Cities.First(city => !string.Equals(city.Owner, RomeId, StringComparison.Ordinal));
        Assert.DoesNotContain((foreignCity.X, foreignCity.Y), cities);
    }

    /// <summary>
    /// Done-when 1, the All-nations selection (a <see langword="null"/> viewed nation): each layer covers
    /// every nation, and Show capital marks the 16 capitals.
    /// </summary>
    [Fact]
    public void All_nations_covers_every_nation_and_marks_the_sixteen_capitals()
    {
        var session = RomeSession();
        var state = session.State;

        Assert.Equal(16, state.Nations.Count);

        var expectedCities = state.Cities.Select(city => (city.X, city.Y));
        var expectedCapitals = state.Nations
            .Select(nation => state.CityById(nation.CapitalCityId!))
            .Where(city => city is not null)
            .Select(city => (city!.X, city.Y))
            .ToList();

        var cities = AreaMapHighlights.TilesFor(state, AreaMapHighlightKind.Cities, viewedNationId: null);
        var capitals = AreaMapHighlights.TilesFor(state, AreaMapHighlightKind.Capital, viewedNationId: null);
        var armies = AreaMapHighlights.TilesFor(state, AreaMapHighlightKind.Armies, viewedNationId: null);

        AssertSameTiles(expectedCities, cities);
        Assert.Equal(16, capitals.Count);
        AssertSameTiles(expectedCapitals, capitals);
        AssertSameTiles(
            state.Armies.Where(army => army.AboardFleetId is null).Select(army => (army.X, army.Y)),
            armies);
    }

    /// <summary>
    /// Done-when 1, the fleet half: only a <em>launched</em> fleet is on the map, and the viewed nation's
    /// launched fleet is marked. The arranged Rome fleet pair is the non-vacuous case (the shipped start
    /// gives Rome none); the Carthage assertion pins the rule against the shipped world's real fleet.
    /// </summary>
    [Fact]
    public void Fleets_marks_only_launched_fleets_of_the_viewed_nation()
    {
        var session = RomeSession();
        var state = session.State;

        var underConstruction = new FleetState(
            "test-rome-under-construction", RomeId, 5, 5, 0, 30, 100, 0, 0, 12, "rome", null, null);
        var launched = new FleetState(
            "test-rome-launched", RomeId, 6, 6, 5, 30, 100, 0, 0, null, null, null, null);
        var arranged = state with
        {
            Fleets = ValueList.From(state.Fleets.Concat(new[] { underConstruction, launched })),
        };

        var romeFleets = AreaMapHighlights.TilesFor(arranged, AreaMapHighlightKind.Fleets, RomeId);
        Assert.Contains((6, 6), romeFleets);
        Assert.DoesNotContain((5, 5), romeFleets);

        // The shipped world's one Carthage fleet is launched and on the map; its tile is in Carthage's set.
        var carthageFleet = state.Fleets.Single(fleet => string.Equals(fleet.Nation, CarthageId, StringComparison.Ordinal));
        Assert.False(carthageFleet.IsUnderConstruction);
        Assert.Contains(
            (carthageFleet.X, carthageFleet.Y),
            AreaMapHighlights.TilesFor(state, AreaMapHighlightKind.Fleets, CarthageId));
    }

    /// <summary>
    /// Done-when 1: Show all is the union of the four layers, for the viewed nation and for All nations.
    /// </summary>
    [Fact]
    public void Show_all_is_the_union()
    {
        var session = RomeSession();
        var state = session.State;

        foreach (var viewed in new string?[] { RomeId, null })
        {
            var expected = new HashSet<(int X, int Y)>();
            foreach (var kind in Enum.GetValues<AreaMapHighlightKind>())
            {
                expected.UnionWith(AreaMapHighlights.TilesFor(state, kind, viewed));
            }

            AssertSameTiles(expected, AreaMapHighlights.AllTiles(state, viewed));
        }
    }

    private static void AssertSameTiles(IEnumerable<(int X, int Y)> expected, IReadOnlySet<(int X, int Y)> actual)
    {
        var expectedSet = expected.ToHashSet();
        Assert.Equal(expectedSet.Count, actual.Count);
        Assert.All(expectedSet, tile => Assert.Contains(tile, actual));
    }
}
