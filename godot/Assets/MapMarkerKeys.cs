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
/// <strong>Capital status — <c>[confirmed]</c>, orthogonal to population.</strong> Of
/// <c>docs/game-design.md</c> §"City markers" (itself <c>[open]</c>), exactly this part is confirmed:
/// a city's map cell code (20–199) is purely its identity/index with no population term
/// (<c>terrain-move-cost-table-in-dat.md</c>), and a nation's capital is a separate piece of state,
/// the DAT nation record's capital-city index (<c>NationRecord.CapitalCityIndex</c>, T30's parse) —
/// mirrored live as <see cref="NationState.CapitalCityId"/>. So a capital draws
/// <see cref="AssetKeys.CityCapitalIcon"/> regardless of its <see cref="CityState.PopulationThousands"/>.
/// <strong>Not</strong> confirmed, and not asserted here: any population-keyed city sprite banding by
/// the original's renderer (the section's own "has not been decompiled or confirmed either way").
/// </para>
/// <para>
/// <strong>City population tiers — <c>[designed]</c>, thresholds read from the ruleset.</strong> Every
/// shipped ruleset ships <see cref="MapMarkerRules.CityPopulationTierThresholds"/> empty (the field's
/// own provenance records exactly what was searched and what the reports establish);
/// <see cref="CityIcon"/> then returns <see cref="AssetKeys.CityTier1Icon"/> for every non-capital,
/// exactly as T48 shipped, until a ruleset supplies boundaries. No boundary is invented here.
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
            AssetKeys.CityTier3Icon);
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
    /// <c>&lt; threshold</c> comparisons use. Fewer than two thresholds means the highest tier is
    /// simply never reached; more than two is capped at the pack's third key, which is the highest one
    /// the confirmed three-tier set ships.
    /// </summary>
    private static string TieredIcon(
        int value,
        IReadOnlyList<int> thresholds,
        string tier1Key,
        string tier2Key,
        string tier3Key)
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

        return tierIndex switch
        {
            0 => tier1Key,
            1 => tier2Key,
            _ => tier3Key,
        };
    }
}
