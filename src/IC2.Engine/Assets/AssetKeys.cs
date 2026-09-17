namespace IC2.Engine.Assets;

/// <summary>
/// The stable asset keys that engine and UI code reference — never file paths, always keys that are resolved
/// through an <see cref="AssetPack"/> manifest.
/// </summary>
/// <remarks>
/// <para>
/// Every game value (unit type, terrain type, etc.) with a rendered visual or audio output needs an asset key.
/// The key format is hierarchical: `category.subcategory.aspect` or similar — examples:
/// `unit.light_infantry.icon`, `terrain.plain.tile`, `sfx.city_captured`.
/// </para>
/// <para>
/// This list is **complete at engine build time**. Later tasks (Godot UI, custom asset packs) cannot
/// add new keys; they can only choose a different asset pack manifest.
/// </para>
/// </remarks>
public static class AssetKeys
{
    // ===== Unit icons =====

    /// <summary>Icon for light infantry units.</summary>
    public const string UnitLightInfantryIcon = "unit.light_infantry.icon";

    /// <summary>Icon for heavy infantry units.</summary>
    public const string UnitHeavyInfantryIcon = "unit.heavy_infantry.icon";

    /// <summary>Icon for archer units.</summary>
    public const string UnitArchersIcon = "unit.archers.icon";

    /// <summary>Icon for light cavalry units.</summary>
    public const string UnitLightCavalryIcon = "unit.light_cavalry.icon";

    /// <summary>Icon for heavy cavalry units.</summary>
    public const string UnitHeavyCavalryIcon = "unit.heavy_cavalry.icon";

    // ===== Army markers (size-tiered) =====

    /// <summary>
    /// Icon for armies with &lt; 25,000 troops.
    /// From <c>game-design.md</c> §"Army and fleet markers scale with size" — the original's marker
    /// arithmetic in <c>decompiled-unit-map-orders-and-record-fields.md</c> identifies three size bands.
    /// </summary>
    public const string ArmyTier1Icon = "army.tier1.icon";

    /// <summary>
    /// Icon for armies with 25,000–49,999 troops.
    /// From <c>game-design.md</c> §"Army and fleet markers scale with size".
    /// </summary>
    public const string ArmyTier2Icon = "army.tier2.icon";

    /// <summary>
    /// Icon for armies with ≥ 50,000 troops.
    /// From <c>game-design.md</c> §"Army and fleet markers scale with size".
    /// </summary>
    public const string ArmyTier3Icon = "army.tier3.icon";

    // ===== Fleet markers (size-tiered) =====

    /// <summary>
    /// Icon for fleets with &lt; 25 ships.
    /// From <c>game-design.md</c> §"Army and fleet markers scale with size" — the original's marker
    /// arithmetic identifies three size bands for fleets as well.
    /// </summary>
    public const string FleetTier1Icon = "fleet.tier1.icon";

    /// <summary>
    /// Icon for fleets with 25–49 ships.
    /// From <c>game-design.md</c> §"Army and fleet markers scale with size".
    /// </summary>
    public const string FleetTier2Icon = "fleet.tier2.icon";

    /// <summary>
    /// Icon for fleets with ≥ 50 ships.
    /// From <c>game-design.md</c> §"Army and fleet markers scale with size".
    /// </summary>
    public const string FleetTier3Icon = "fleet.tier3.icon";

    // ===== City markers (population-tiered, designed placeholder tiers pending confirmation) =====

    /// <summary>
    /// Icon for small cities.
    /// From <c>game-design.md</c> §"City markers" — marked as <c>[designed]</c> pending decompilation
    /// confirmation of the original's own city-size display logic. Serves as a placeholder tier 1.
    /// </summary>
    public const string CityTier1Icon = "city.tier1.icon";

    /// <summary>
    /// Icon for medium cities.
    /// From <c>game-design.md</c> §"City markers" — placeholder tier 2.
    /// </summary>
    public const string CityTier2Icon = "city.tier2.icon";

    /// <summary>
    /// Icon for large cities.
    /// From <c>game-design.md</c> §"City markers" — placeholder tier 3.
    /// </summary>
    public const string CityTier3Icon = "city.tier3.icon";

    /// <summary>
    /// Icon for capital cities.
    /// From <c>game-design.md</c> §"City markers" — capital status is orthogonal to population size.
    /// </summary>
    public const string CityCapitalIcon = "city.capital.icon";

    // ===== Terrain tiles =====

    /// <summary>Tile sprite for plain terrain.</summary>
    public const string TerrainPlainTile = "terrain.plain.tile";

    /// <summary>Tile sprite for desert terrain.</summary>
    public const string TerrainDesertTile = "terrain.desert.tile";

    /// <summary>Tile sprite for forest terrain.</summary>
    public const string TerrainForestTile = "terrain.forest.tile";

    /// <summary>Tile sprite for mountain terrain.</summary>
    public const string TerrainMountainTile = "terrain.mountain.tile";

    /// <summary>Tile sprite for river terrain.</summary>
    public const string TerrainRiverTile = "terrain.river.tile";

    /// <summary>Tile sprite for shallow sea terrain.</summary>
    public const string TerrainSeaCoastalTile = "terrain.sea_coastal.tile";

    /// <summary>Tile sprite for deep sea terrain.</summary>
    public const string TerrainSeaDeepTile = "terrain.sea_deep.tile";

    // ===== Sound effects =====

    /// <summary>Sound effect played when a city is captured.</summary>
    public const string SfxCityCaptured = "sfx.city_captured";

    /// <summary>Sound effect played during battle.</summary>
    public const string SfxBattle = "sfx.battle";

    /// <summary>Sound effect played when a unit moves.</summary>
    public const string SfxUnitMove = "sfx.unit_move";

    /// <summary>
    /// Returns an enumeration of all known asset keys.
    /// Used for validation: every key here must resolve to a file in the asset pack.
    /// </summary>
    public static IEnumerable<string> AllKeys
    {
        get
        {
            // Unit icons
            yield return UnitLightInfantryIcon;
            yield return UnitHeavyInfantryIcon;
            yield return UnitArchersIcon;
            yield return UnitLightCavalryIcon;
            yield return UnitHeavyCavalryIcon;

            // Army markers
            yield return ArmyTier1Icon;
            yield return ArmyTier2Icon;
            yield return ArmyTier3Icon;

            // Fleet markers
            yield return FleetTier1Icon;
            yield return FleetTier2Icon;
            yield return FleetTier3Icon;

            // City markers
            yield return CityTier1Icon;
            yield return CityTier2Icon;
            yield return CityTier3Icon;
            yield return CityCapitalIcon;

            // Terrain tiles
            yield return TerrainPlainTile;
            yield return TerrainDesertTile;
            yield return TerrainForestTile;
            yield return TerrainMountainTile;
            yield return TerrainRiverTile;
            yield return TerrainSeaCoastalTile;
            yield return TerrainSeaDeepTile;

            // Sound effects
            yield return SfxCityCaptured;
            yield return SfxBattle;
            yield return SfxUnitMove;
        }
    }
}
