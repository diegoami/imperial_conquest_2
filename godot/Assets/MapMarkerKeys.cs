using IC2.Engine.Assets;
using IC2.Engine.Model;

namespace IC2.Slice.Assets;

/// <summary>
/// T94 "Godot main screen follow-ups" — the one place that decides which asset-pack icon a map marker
/// draws, from the engine's live <see cref="GameState"/> numbers and the loaded
/// <see cref="MapMarkerRules"/>. Godot-free by construction (the same seam <see cref="AssetKeyResolver"/>
/// established): the map screen (<c>godot/UI/GameMapView.cs</c>) and the thin slice
/// (<c>godot/Slice/Slice.cs</c>) both call it, and <c>tests/IC2.Engine.Tests/Ui/MapMarkerKeysTests.cs</c>
/// exercises it directly rather than through a hand-copied mirror.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Armies and fleets — <c>[confirmed]</c>.</strong> <c>docs/game-design.md</c>
/// §"Army and fleet markers scale with size" (the original's own <c>TUnitMap_SelectUnit</c> arithmetic:
/// <c>armyMarker = owner + (troopsThousands &lt; 25 ? 200 : troopsThousands &lt; 50 ? 216 : 232)</c>,
/// <c>fleetMarker = owner + (shipCount &lt; 25 ? 300 : shipCount &lt; 50 ? 316 : 332)</c>) gives three
/// tiers per type. The boundaries ship in every ruleset as
/// <see cref="MapMarkerRules.ArmyTroopTierThresholds"/> / <see cref="MapMarkerRules.FleetShipTierThresholds"/>
/// (25,000/50,000 and 25/50) and are read from there — never a C# literal — so an art pack can reskin
/// but cannot re-tier. This deliberately replaces T48's <strong>[designed]</strong> per-unit-type army
/// icon choice (issue <see href="https://github.com/diegoami/imperial_conquest_2/issues/454">#454</see>
/// item 3): the size tier is the original's own axis and the pack's <c>army.tierN.icon</c> keys exist
/// for it, while a mixed army's plurality unit type is not part of the original's marker at all.
/// </para>
/// <para>
/// <strong>Cities — <c>[confirmed: decompile + saves]</c>, five variants.</strong> Research report
/// <c>2026-10-07-city-marker-variants.md</c>: a city's map code is <c>20 + owner + 16 × variant</c>;
/// variants 0-3 are population tiers on the city's population in thousands (under 25, 25-49, 50-99,
/// 100 or more) and variant 4 is the national capital. So a capital draws
/// <see cref="AssetKeys.CityCapitalIcon"/> regardless of its <see cref="CityState.PopulationThousands"/>
/// (the capital is the nation's live <see cref="NationState.CapitalCityId"/>, mirrored from the DAT
/// nation record's capital-city index), and every other town draws one of the four
/// <c>city.tierN.icon</c> keys by <see cref="MapMarkerRules.CityPopulationTierThresholds"/>
/// (<c>[25, 50, 100]</c> in every shipped ruleset, read from there, never a C# literal).
/// </para>
/// <para>
/// <strong>Live tiers — <c>[designed]</c>, the user's decision of 2026-10-11 (bug #818).</strong> The
/// original sets a town's variant only when the town changes owner, so it goes stale as the town
/// grows. The clone recomputes the tier from the town's current population every time it is drawn:
/// a recorded deliberate difference.
/// </para>
/// </remarks>
public static class MapMarkerKeys
{
    /// <summary>
    /// The pack key a city's marker draws: <see cref="AssetKeys.CityCapitalIcon"/> when
    /// <paramref name="city"/> is its owner's <em>live</em> capital
    /// (<see cref="NationState.CapitalCityId"/>, which <c>Rebirth.cs</c> and <c>ConquestCascade.cs</c>
    /// mutate during play — never the scenario-start <see cref="NationDefinition.CapitalCityId"/>),
    /// otherwise the population tier from <paramref name="rules"/>'s own
    /// <see cref="MapMarkerRules.CityPopulationTierThresholds"/>.
    /// </summary>
    public static string CityIcon(GameState state, CityState city, MapMarkerRules rules)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(city);
        ArgumentNullException.ThrowIfNull(rules);

        var nation = state.NationById(city.Owner);
        if (nation is not null && string.Equals(nation.CapitalCityId, city.Id, StringComparison.Ordinal))
        {
            return AssetKeys.CityCapitalIcon;
        }

        return TieredIcon(
            city.PopulationThousands,
            rules.CityPopulationTierThresholds,
            AssetKeys.CityTier1Icon,
            AssetKeys.CityTier2Icon,
            AssetKeys.CityTier3Icon,
            AssetKeys.CityTier4Icon);
    }

    /// <summary>
    /// The pack key an army's marker draws: the three confirmed troop tiers
    /// (<see cref="ArmyState.TotalTroops"/> against
    /// <see cref="MapMarkerRules.ArmyTroopTierThresholds"/>).
    /// </summary>
    public static string ArmyIcon(ArmyState army, MapMarkerRules rules)
    {
        ArgumentNullException.ThrowIfNull(army);
        ArgumentNullException.ThrowIfNull(rules);

        return TieredIcon(
            army.TotalTroops,
            rules.ArmyTroopTierThresholds,
            AssetKeys.ArmyTier1Icon,
            AssetKeys.ArmyTier2Icon,
            AssetKeys.ArmyTier3Icon);
    }

    /// <summary>
    /// The pack key a fleet's marker draws: the three confirmed ship tiers
    /// (<see cref="FleetState.Ships"/> against <see cref="MapMarkerRules.FleetShipTierThresholds"/>).
    /// </summary>
    public static string FleetIcon(FleetState fleet, MapMarkerRules rules)
    {
        ArgumentNullException.ThrowIfNull(fleet);
        ArgumentNullException.ThrowIfNull(rules);

        return TieredIcon(
            fleet.Ships,
            rules.FleetShipTierThresholds,
            AssetKeys.FleetTier1Icon,
            AssetKeys.FleetTier2Icon,
            AssetKeys.FleetTier3Icon);
    }

    /// <summary>
    /// Index 0 below the first threshold, stepping up by one per threshold reached — the same band
    /// arithmetic <see cref="IC2.Engine.Naval.FleetMarker.Encode"/> and the original's own
    /// <c>&lt; threshold</c> comparisons use. Fewer thresholds than keys means the highest keys are
    /// simply never reached; more thresholds than keys is capped at the last key.
    /// </summary>
    /// <summary>
    /// #1309: a city's size rank for drawing, 0–3 for the population tiers (as <see cref="CityIcon"/> picks them)
    /// and <see cref="CapitalRank"/> for a live capital.
    /// </summary>
    public static int CityRank(GameState state, CityState city, MapMarkerRules rules)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(city);
        ArgumentNullException.ThrowIfNull(rules);
        var nation = state.NationById(city.Owner);
        if (nation is not null && string.Equals(nation.CapitalCityId, city.Id, StringComparison.Ordinal))
        {
            return CapitalRank;
        }

        return Math.Min(TierIndex(city.PopulationThousands, rules.CityPopulationTierThresholds), CapitalRank - 1);
    }

    /// <summary>#1309: <see cref="CityRank"/>'s value for a capital, above the four population tiers.</summary>
    public const int CapitalRank = 4;

    /// <summary>
    /// #1309: a city marker's half-size, in tiles, by <see cref="CityRank"/>: small towns smaller, cities and the
    /// capital larger, so size reads at a glance. <c>[designed]</c>, a recorded difference: the original draws every
    /// town at one size and tells them apart only by its art.
    /// </summary>
    public static float CityMarkerHalfSize(int rank) => rank switch
    {
        <= 0 => 0.34f,
        1 => 0.46f,
        2 => 0.58f,
        3 => 0.70f,
        _ => 0.82f,
    };

    private static int TierIndex(int value, IReadOnlyList<int> thresholds)
    {
        var tierIndex = 0;
        foreach (var threshold in thresholds)
        {
            if (value < threshold)
            {
                break;
            }

            tierIndex++;
        }

        return tierIndex;
    }

    private static string TieredIcon(int value, IReadOnlyList<int> thresholds, params string[] tierKeys)
    {
        return tierKeys[Math.Min(TierIndex(value, thresholds), tierKeys.Length - 1)];
    }
}
