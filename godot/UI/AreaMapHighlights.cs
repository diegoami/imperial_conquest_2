using IC2.Engine.Model;

namespace IC2.Slice.UI;

/// <summary>
/// One of the Area map's <strong>Show</strong> highlight layers — the original's
/// <c>TAreaMap_ShowCities</c>, <c>ShowCapital</c>, <c>ShowArmies</c>, <c>ShowFleets</c> and the six
/// <c>TAreaMap_ShowMercs</c> bitmaps
/// (<c>docs/investigations/original-ui-command-audit.md</c> §1.5: "Each paints a highlight over the
/// Area map for the *viewed nation* (all nations when All nations is selected)"). Show all is the
/// union of all four (T110's set) and "All mercenaries" is the union of the five mercenary layers
/// (T113); each is built by <see cref="AreaMapHighlights.AllTiles"/>.
/// </summary>
public enum AreaMapHighlightKind
{
    Cities,
    Capital,
    Armies,
    Fleets,

    /// <summary>Show mercenaries → Light infantry (T113): every live light-infantry offer's tile
    /// <strong>[derived: code, <c>TAreaMap_ShowMercs</c> bitmaps 4–8 (the five type bitmaps) at
    /// DAT offset of the mercenary offer table]</strong>. The offers belong to no nation, so the type
    /// layer is the same under any viewed-nation selection
    /// <strong>[designed: the original sets no nation gate, the bitmaps are global]</strong>.</summary>
    MercenariesLightInfantry,

    /// <summary>Show mercenaries → Heavy infantry (T113): every live heavy-infantry offer's tile.</summary>
    MercenariesHeavyInfantry,

    /// <summary>Show mercenaries → Archers (T113): every live archers offer's tile.</summary>
    MercenariesArchers,

    /// <summary>Show mercenaries → Light cavalry (T113): every live light-cavalry offer's tile.</summary>
    MercenariesLightCavalry,

    /// <summary>Show mercenaries → Heavy cavalry (T113): every live heavy-cavalry offer's tile.</summary>
    MercenariesHeavyCavalry,

    /// <summary>Show mercenaries → All mercenaries (T113): the union of the five type layers.</summary>
    MercenariesAll,
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
/// The six <c>Show mercenaries</c> entries' bitmaps are at <c>TAreaMap_ShowMercs</c>
/// <strong>[derived: code, <c>TAreaMap_ShowMercs</c> bitmaps 4–8 — the five type bitmaps and the
/// "All mercenaries" union — at the audit's §1.5 row]</strong>. The mercenary pool is a global
/// resource ("offers not scoped by the viewed nation, because they belong to no nation" — Scope).
/// A slot is empty once hired; the <c>0xFFFF</c> hired-slot sentinel is the engine's own mark, and is
/// not an offer
/// <strong>[confirmed: <c>decompiled-fleet-tax-and-mercenary-formulas.md</c>, 0xFFFF;
/// <c>tests/fixtures/corpus.json</c> <c>mercenary.felsina.sentinelAfterHire</c> = 65535]</strong>.
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
    /// <summary>The sentinel that marks a hired mercenary slot — not a live offer.</summary>
    public const int HiredSlotSentinelTroops = 0xFFFF;

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
            case AreaMapHighlightKind.MercenariesLightInfantry:
                AddMercenariesOfType(state, "light_infantry", tiles);
                break;
            case AreaMapHighlightKind.MercenariesHeavyInfantry:
                AddMercenariesOfType(state, "heavy_infantry", tiles);
                break;
            case AreaMapHighlightKind.MercenariesArchers:
                AddMercenariesOfType(state, "archers", tiles);
                break;
            case AreaMapHighlightKind.MercenariesLightCavalry:
                AddMercenariesOfType(state, "light_cavalry", tiles);
                break;
            case AreaMapHighlightKind.MercenariesHeavyCavalry:
                AddMercenariesOfType(state, "heavy_cavalry", tiles);
                break;
            case AreaMapHighlightKind.MercenariesAll:
                AddAllMercenaries(state, tiles);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }

        return tiles;
    }

    /// <summary>
    /// Show all — the union of the four layers, exactly as <c>TAreaMap_ShowAll</c>
    /// (<c>0x0043E440</c>) sets cities, capital, fleets and armies together. The five mercenary
    /// layers each have their own "All mercenaries" union (<see cref="AddAllMercenaries"/>); the
    /// stock "Show all" stays the four original layers and does not pull in the mercenary ones, so
    /// the audit's §1.5 row owns its own set — never a wider one this task invents.
    /// </summary>
    public static IReadOnlySet<(int X, int Y)> AllTiles(GameState state, string? viewedNationId)
    {
        ArgumentNullException.ThrowIfNull(state);

        var tiles = new HashSet<(int X, int Y)>();
        foreach (var kind in StockShowAllKinds())
        {
            tiles.UnionWith(TilesFor(state, kind, viewedNationId));
        }

        return tiles;
    }

    /// <summary>
    /// The four kinds the audit names <c>TAreaMap_ShowAll</c> for, in the audit's order — the
    /// "Show all" union without the mercenary ones, so the stock command's tileset is unchanged
    /// when T113 lands (T110's <see cref="AllTiles"/> test owns this list).
    /// </summary>
    public static IEnumerable<AreaMapHighlightKind> StockShowAllKinds()
    {
        yield return AreaMapHighlightKind.Cities;
        yield return AreaMapHighlightKind.Capital;
        yield return AreaMapHighlightKind.Armies;
        yield return AreaMapHighlightKind.Fleets;
    }

    /// <summary>The five mercenary type kinds, in the audit's §1.5 submenu order.</summary>
    public static IEnumerable<AreaMapHighlightKind> MercenaryTypeKinds()
    {
        yield return AreaMapHighlightKind.MercenariesLightInfantry;
        yield return AreaMapHighlightKind.MercenariesHeavyInfantry;
        yield return AreaMapHighlightKind.MercenariesArchers;
        yield return AreaMapHighlightKind.MercenariesLightCavalry;
        yield return AreaMapHighlightKind.MercenariesHeavyCavalry;
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

    /// <summary>
    /// Every live offer of <paramref name="unitTypeId"/>'s tile — the "Show mercenaries → &lt;type&gt;"
    /// layer's underlying set. The mercenary pool is a global resource, scoped to no nation; the
    /// <paramref name="viewedNationId"/> is therefore unused here
    /// <strong>[designed: the audit's §1.5 row's bitmaps are global; an offer's nationality is the
    /// hired unit's culture label, not the source city]</strong>. The hired-slot sentinel
    /// (<c>0xFFFF</c>) is the engine's own "empty slot" mark and is not an offer.
    /// </summary>
    private static void AddMercenariesOfType(GameState state, string unitTypeId, HashSet<(int X, int Y)> tiles)
    {
        foreach (var slot in state.MercenaryPool)
        {
            if (slot.Troops == HiredSlotSentinelTroops)
            {
                continue;
            }

            if (!string.Equals(slot.UnitTypeId, unitTypeId, StringComparison.Ordinal))
            {
                continue;
            }

            tiles.Add((slot.X, slot.Y));
        }
    }

    /// <summary>
    /// The "Show mercenaries → All mercenaries" union of the five type layers, exactly as
    /// <c>TAreaMap_ShowMercs</c> sets the five type bitmaps and reads them as one set.
    /// A type this ruleset does not define is skipped silently, the same way the engine ignores
    /// an unknown bitmap — the shipped ruleset ships all five types.
    /// </summary>
    private static void AddAllMercenaries(GameState state, HashSet<(int X, int Y)> tiles)
    {
        foreach (var slot in state.MercenaryPool)
        {
            if (slot.Troops == HiredSlotSentinelTroops)
            {
                continue;
            }

            tiles.Add((slot.X, slot.Y));
        }
    }
}
