using IC2.Engine.Model;

namespace IC2.Slice.UI;

/// <summary>
/// One of the Area map's four <strong>Show</strong> highlight layers — the original's
/// <c>TAreaMap_ShowCities</c>, <c>ShowCapital</c>, <c>ShowArmies</c> and <c>ShowFleets</c>
/// (<c>docs/investigations/original-ui-command-audit.md</c> §1.5: "Each paints a highlight over the
/// Area map for the *viewed nation* (all nations when All nations is selected)"). Show all is the
/// union of all four and is built by <see cref="AreaMapHighlights.AllTiles"/>.
/// </summary>
public enum AreaMapHighlightKind
{
    Cities,
    Capital,
    Armies,
    Fleets,
}

/// <summary>
/// Which tiles each Area map <strong>Show</strong> entry marks, for a viewed nation — the whole
/// Godot-free rule behind <see cref="AreaMapView"/>'s highlight layer.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Evidence.</strong> The audit's §1.5 table gives the four tiles sets directly:
/// <c>Show cities</c> "Highlights every city of the viewed nation" (<c>TAreaMap_ShowCities</c>
/// <c>0x0043E210</c>); <c>Show capital</c> "Highlights each nation's capital, the viewed one or all"
/// (<c>0x0043E288</c>); <c>Show armies</c> "Highlights the viewed nation's armies"
/// (<c>0x0043E31C</c>); <c>Show fleets</c> "Highlights the viewed nation's launched fleets"
/// (<c>0x0043E3AC</c>); and <c>Show all</c> "Show cities, capital, fleets and armies, together"
/// (<c>0x0043E440</c>). None of them hides anything, which is the user's decision of 2026-10-01
/// ("Show cities, armies and fleets highlight like the original's, and the hide-layer toggles go").
/// </para>
/// <para>
/// A <see langword="null"/> <c>viewedNationId</c> is the original's <em>All nations</em> selection
/// (viewed nation index 16, audit §1.4). The sets are plain tile coordinates, never colours or
/// pixels: <see cref="AreaMapView"/> maps them through <see cref="AreaMapGeometry"/>.
/// </para>
/// <para>
/// The same tile can belong to two layers (a city and a capital, or an army standing on its city).
/// Returning a set, not a list, is what makes <see cref="AllTiles"/> a union rather than a
/// double-drawn coordinate.
/// </para>
/// </remarks>
public static class AreaMapHighlights
{
    /// <summary>
    /// The tiles <paramref name="kind"/> marks for the viewed nation, or for every nation when
    /// <paramref name="viewedNationId"/> is <see langword="null"/> (the original's All nations).
    /// </summary>
    public static IReadOnlySet<(int X, int Y)> TilesFor(
        GameState state,
        AreaMapHighlightKind kind,
        string? viewedNationId)
    {
        ArgumentNullException.ThrowIfNull(state);

        var tiles = new HashSet<(int X, int Y)>();
        switch (kind)
        {
            case AreaMapHighlightKind.Cities:
                AddCities(state, viewedNationId, tiles);
                break;
            case AreaMapHighlightKind.Capital:
                AddCapitals(state, viewedNationId, tiles);
                break;
            case AreaMapHighlightKind.Armies:
                AddArmies(state, viewedNationId, tiles);
                break;
            case AreaMapHighlightKind.Fleets:
                AddFleets(state, viewedNationId, tiles);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }

        return tiles;
    }

    /// <summary>
    /// Show all — the union of the four layers, exactly as <c>TAreaMap_ShowAll</c>
    /// (<c>0x0043E440</c>) sets cities, capital, fleets and armies together.
    /// </summary>
    public static IReadOnlySet<(int X, int Y)> AllTiles(GameState state, string? viewedNationId)
    {
        ArgumentNullException.ThrowIfNull(state);

        var tiles = new HashSet<(int X, int Y)>();
        foreach (var kind in Enum.GetValues<AreaMapHighlightKind>())
        {
            tiles.UnionWith(TilesFor(state, kind, viewedNationId));
        }

        return tiles;
    }

    private static void AddCities(GameState state, string? viewedNationId, HashSet<(int X, int Y)> tiles)
    {
        foreach (var city in state.Cities)
        {
            if (IsViewed(city.Owner, viewedNationId))
            {
                tiles.Add((city.X, city.Y));
            }
        }
    }

    private static void AddCapitals(GameState state, string? viewedNationId, HashSet<(int X, int Y)> tiles)
    {
        foreach (var nation in state.Nations)
        {
            if (!IsViewed(nation.Id, viewedNationId) || nation.CapitalCityId is null)
            {
                continue;
            }

            // The capital is a city id on the nation, not a flag on the city (NationState.CapitalCityId),
            // so the tile is the city's own position. A conquered nation whose capital moved keeps the
            // live id and marks the new city; a capital id with no live city marks nothing.
            if (state.CityById(nation.CapitalCityId) is { } capital)
            {
                tiles.Add((capital.X, capital.Y));
            }
        }
    }

    private static void AddArmies(GameState state, string? viewedNationId, HashSet<(int X, int Y)> tiles)
    {
        foreach (var army in state.Armies)
        {
            // An army aboard a fleet is off the map (GameMapView.DrawArmy skips it too); marking its
            // stale X/Y would highlight a tile the army is not standing on.
            if (army.AboardFleetId is null && IsViewed(army.Nation, viewedNationId))
            {
                tiles.Add((army.X, army.Y));
            }
        }
    }

    private static void AddFleets(GameState state, string? viewedNationId, HashSet<(int X, int Y)> tiles)
    {
        foreach (var fleet in state.Fleets)
        {
            // "Launched fleets" (audit §1.5): a fleet still under construction is not on the map.
            if (!fleet.IsUnderConstruction && IsViewed(fleet.Nation, viewedNationId))
            {
                tiles.Add((fleet.X, fleet.Y));
            }
        }
    }

    private static bool IsViewed(string nationId, string? viewedNationId) =>
        viewedNationId is null || string.Equals(nationId, viewedNationId, StringComparison.Ordinal);
}
