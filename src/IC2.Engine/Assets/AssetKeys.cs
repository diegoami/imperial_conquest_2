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

    // ===== T148: terrain tile variants, river connectivity pieces and shore overlays =====
    //
    // The seven keys above are the flat, one-per-tile-type sprites. T148 draws the map from per-cell
    // tiles and adds: three extra variants per land/sea type (the existing `.tile` is variant 1), one
    // piece per river connectivity code 6..11, and four 32-bit shore overlays composited over a land
    // cell whose neighbour is sea. The mapping from a grid cell to these keys is
    // godot/Assets/TerrainTileKeys.cs; asset-specification.md 4.5 records the river gap this closes.

    /// <summary>Terrain tile variant 2 for plain terrain.</summary>
    public const string TerrainPlainTile2 = "terrain.plain.tile.2";

    /// <summary>Terrain tile variant 3 for plain terrain.</summary>
    public const string TerrainPlainTile3 = "terrain.plain.tile.3";

    /// <summary>Terrain tile variant 4 for plain terrain.</summary>
    public const string TerrainPlainTile4 = "terrain.plain.tile.4";

    /// <summary>Terrain tile variant 2 for desert terrain.</summary>
    public const string TerrainDesertTile2 = "terrain.desert.tile.2";

    /// <summary>Terrain tile variant 3 for desert terrain.</summary>
    public const string TerrainDesertTile3 = "terrain.desert.tile.3";

    /// <summary>Terrain tile variant 4 for desert terrain.</summary>
    public const string TerrainDesertTile4 = "terrain.desert.tile.4";

    /// <summary>Terrain tile variant 2 for forest terrain.</summary>
    public const string TerrainForestTile2 = "terrain.forest.tile.2";

    /// <summary>Terrain tile variant 3 for forest terrain.</summary>
    public const string TerrainForestTile3 = "terrain.forest.tile.3";

    /// <summary>Terrain tile variant 4 for forest terrain.</summary>
    public const string TerrainForestTile4 = "terrain.forest.tile.4";

    /// <summary>Terrain tile variant 2 for mountain terrain.</summary>
    public const string TerrainMountainTile2 = "terrain.mountain.tile.2";

    /// <summary>Terrain tile variant 3 for mountain terrain.</summary>
    public const string TerrainMountainTile3 = "terrain.mountain.tile.3";

    /// <summary>Terrain tile variant 4 for mountain terrain.</summary>
    public const string TerrainMountainTile4 = "terrain.mountain.tile.4";

    /// <summary>Terrain tile variant 2 for shallow sea terrain.</summary>
    public const string TerrainSeaCoastalTile2 = "terrain.sea_coastal.tile.2";

    /// <summary>Terrain tile variant 3 for shallow sea terrain.</summary>
    public const string TerrainSeaCoastalTile3 = "terrain.sea_coastal.tile.3";

    /// <summary>Terrain tile variant 4 for shallow sea terrain.</summary>
    public const string TerrainSeaCoastalTile4 = "terrain.sea_coastal.tile.4";

    /// <summary>Terrain tile variant 2 for deep sea terrain.</summary>
    public const string TerrainSeaDeepTile2 = "terrain.sea_deep.tile.2";

    /// <summary>Terrain tile variant 3 for deep sea terrain.</summary>
    public const string TerrainSeaDeepTile3 = "terrain.sea_deep.tile.3";

    /// <summary>Terrain tile variant 4 for deep sea terrain.</summary>
    public const string TerrainSeaDeepTile4 = "terrain.sea_deep.tile.4";

    /// <summary>River piece: east–west (grid code 6).</summary>
    public const string TerrainRiverEw = "terrain.river.ew";

    /// <summary>River piece: north–south (grid code 7).</summary>
    public const string TerrainRiverNs = "terrain.river.ns";

    /// <summary>River piece: east + north (grid code 8).</summary>
    public const string TerrainRiverEn = "terrain.river.en";

    /// <summary>River piece: east + south (grid code 9).</summary>
    public const string TerrainRiverEs = "terrain.river.es";

    /// <summary>River piece: west + south (grid code 10).</summary>
    public const string TerrainRiverWs = "terrain.river.ws";

    /// <summary>River piece: west + north (grid code 11).</summary>
    public const string TerrainRiverWn = "terrain.river.wn";

    /// <summary>Shore overlay along a land cell's north edge (32-bit BGRA).</summary>
    public const string TerrainShoreN = "terrain.shore.n";

    /// <summary>Shore overlay along a land cell's east edge (32-bit BGRA).</summary>
    public const string TerrainShoreE = "terrain.shore.e";

    /// <summary>Shore overlay along a land cell's south edge (32-bit BGRA).</summary>
    public const string TerrainShoreS = "terrain.shore.s";

    /// <summary>Shore overlay along a land cell's west edge (32-bit BGRA).</summary>
    public const string TerrainShoreW = "terrain.shore.w";

    /// <summary>
    /// T148: the 28 terrain keys this task added — the three extra variants of each of the six
    /// variant-bearing types, the six river connectivity pieces and the four shore overlays. Exposed
    /// so tests can name them exactly, and so <c>AssetSpecificationCoverageTests</c> can treat them
    /// as the one documented gap: <c>docs/asset-specification.md</c> 6's machine-checked ground-truth
    /// block is a Markdown file a task branch must not edit, so it names them only once the main
    /// session applies the PR's "Docs affected" pass. The existing seven keys are not in this list.
    /// </summary>
    public static IReadOnlyList<string> TerrainKeysAddedByT148 { get; } = new[]
    {
        TerrainPlainTile2, TerrainPlainTile3, TerrainPlainTile4,
        TerrainDesertTile2, TerrainDesertTile3, TerrainDesertTile4,
        TerrainForestTile2, TerrainForestTile3, TerrainForestTile4,
        TerrainMountainTile2, TerrainMountainTile3, TerrainMountainTile4,
        TerrainSeaCoastalTile2, TerrainSeaCoastalTile3, TerrainSeaCoastalTile4,
        TerrainSeaDeepTile2, TerrainSeaDeepTile3, TerrainSeaDeepTile4,
        TerrainRiverEw, TerrainRiverNs, TerrainRiverEn, TerrainRiverEs, TerrainRiverWs, TerrainRiverWn,
        TerrainShoreN, TerrainShoreE, TerrainShoreS, TerrainShoreW,
    };

    // ===== Sound effects =====

    /// <summary>Sound effect played when a city is captured.</summary>
    public const string SfxCityCaptured = "sfx.city_captured";

    /// <summary>Sound effect played during battle.</summary>
    public const string SfxBattle = "sfx.battle";

    /// <summary>Sound effect played when a unit moves.</summary>
    public const string SfxUnitMove = "sfx.unit_move";

    // ===== Toolbar command icons (ui.command.*) =====
    //
    // One pictorial icon per toolbar command (T101), catalogued in
    // docs/asset-specification.md 4.7: 9 main-toolbar commands, 12 Area-map strip commands and
    // 15 unit-map strip commands. The total of 36 equals the audit's 3.5 total
    // (docs/investigations/original-ui-command-audit.md), but not its per-strip split (9 / 13 / 14
    // plus one [open]): this grouping drops the Area-map strip's ToggleMap and gold-coin buttons,
    // whose meanings are [open] (audit 3.2), and adds find_city (audit 3.2's candidate for the coin)
    // and cancel_selection. The 16 nation buttons and All nations are colour swatches and need no
    // key. Every icon is 32x32 32-bit BGRA with straight alpha (asset-specification.md 1.2 and 1.3).

    // --- Main toolbar (9) ---

    /// <summary>Main-toolbar icon: open a saved game.</summary>
    public const string UiCommandOpenIcon = "ui.command.open.icon";

    /// <summary>Main-toolbar icon: save the current game.</summary>
    public const string UiCommandSaveIcon = "ui.command.save.icon";

    /// <summary>Main-toolbar icon: end the active nation's turn.</summary>
    public const string UiCommandEndTurnIcon = "ui.command.end_turn.icon";

    /// <summary>Strategy-toolbar icon: the news log.</summary>
    public const string UiCommandNewsIcon = "ui.command.news.icon";

    /// <summary>Strategy-toolbar icon: international relations.</summary>
    public const string UiCommandRelationsIcon = "ui.command.relations.icon";

    /// <summary>Strategy-toolbar icon: the taxation dialog.</summary>
    public const string UiCommandTaxationIcon = "ui.command.taxation.icon";

    /// <summary>Strategy-toolbar icon: the balance sheet.</summary>
    public const string UiCommandBalanceSheetIcon = "ui.command.balance_sheet.icon";

    /// <summary>Strategy-toolbar icon: recruit a unit.</summary>
    public const string UiCommandRecruitUnitIcon = "ui.command.recruit_unit.icon";

    /// <summary>Strategy-toolbar icon: build a fleet.</summary>
    public const string UiCommandBuildFleetIcon = "ui.command.build_fleet.icon";

    // --- Area-map strip (12) ---

    /// <summary>Area-map strip icon: highlight every city of the viewed nation.</summary>
    public const string UiCommandShowCitiesIcon = "ui.command.show_cities.icon";

    /// <summary>Area-map strip icon: highlight each nation's capital.</summary>
    public const string UiCommandShowCapitalIcon = "ui.command.show_capital.icon";

    /// <summary>Area-map strip icon: highlight the viewed nation's armies.</summary>
    public const string UiCommandShowArmiesIcon = "ui.command.show_armies.icon";

    /// <summary>Area-map strip icon: highlight the viewed nation's fleets.</summary>
    public const string UiCommandShowFleetsIcon = "ui.command.show_fleets.icon";

    /// <summary>Area-map strip icon: highlight cities, capital, fleets and armies together.</summary>
    public const string UiCommandShowAllIcon = "ui.command.show_all.icon";

    /// <summary>Area-map strip icon: highlight every light-infantry mercenary offer.</summary>
    public const string UiCommandShowMercsLightInfantryIcon = "ui.command.show_mercs_light_infantry.icon";

    /// <summary>Area-map strip icon: highlight every heavy-infantry mercenary offer.</summary>
    public const string UiCommandShowMercsHeavyInfantryIcon = "ui.command.show_mercs_heavy_infantry.icon";

    /// <summary>Area-map strip icon: highlight every archer mercenary offer.</summary>
    public const string UiCommandShowMercsArchersIcon = "ui.command.show_mercs_archers.icon";

    /// <summary>Area-map strip icon: highlight every light-cavalry mercenary offer.</summary>
    public const string UiCommandShowMercsLightCavalryIcon = "ui.command.show_mercs_light_cavalry.icon";

    /// <summary>Area-map strip icon: highlight every heavy-cavalry mercenary offer.</summary>
    public const string UiCommandShowMercsHeavyCavalryIcon = "ui.command.show_mercs_heavy_cavalry.icon";

    /// <summary>Area-map strip icon: highlight mercenary offers of every type.</summary>
    public const string UiCommandShowMercsAllIcon = "ui.command.show_mercs_all.icon";

    /// <summary>Area-map strip icon: find a city and centre the map on it.</summary>
    public const string UiCommandFindCityIcon = "ui.command.find_city.icon";

    // --- Unit-map strip: army (7) ---

    /// <summary>Unit-map strip icon: buy supply for the selected army.</summary>
    public const string UiCommandArmySupplyIcon = "ui.command.army_supply.icon";

    /// <summary>Unit-map strip icon: recruit mercenaries into the selected army.</summary>
    public const string UiCommandArmyRecruitMercenariesIcon = "ui.command.army_recruit_mercenaries.icon";

    /// <summary>Unit-map strip icon: transfer a unit to an adjacent own army.</summary>
    public const string UiCommandArmyTransferUnitIcon = "ui.command.army_transfer_unit.icon";

    /// <summary>Unit-map strip icon: split the selected army.</summary>
    public const string UiCommandArmySplitIcon = "ui.command.army_split.icon";

    /// <summary>Unit-map strip icon: join the selected army with an adjacent own army.</summary>
    public const string UiCommandArmyJoinIcon = "ui.command.army_join.icon";

    /// <summary>Unit-map strip icon: change the selected army's units (rename, split, join, disband).</summary>
    public const string UiCommandArmyChangeUnitsIcon = "ui.command.army_change_units.icon";

    /// <summary>Unit-map strip icon: disband the selected army.</summary>
    public const string UiCommandArmyDisbandIcon = "ui.command.army_disband.icon";

    // --- Unit-map strip: fleet (6) ---

    /// <summary>Unit-map strip icon: buy supply for the selected fleet.</summary>
    public const string UiCommandFleetSupplyIcon = "ui.command.fleet_supply.icon";

    /// <summary>Unit-map strip icon: repair the selected fleet at an own city.</summary>
    public const string UiCommandFleetRepairIcon = "ui.command.fleet_repair.icon";

    /// <summary>Unit-map strip icon: transfer ships to an adjacent own fleet.</summary>
    public const string UiCommandFleetTransferShipsIcon = "ui.command.fleet_transfer_ships.icon";

    /// <summary>Unit-map strip icon: split the selected fleet.</summary>
    public const string UiCommandFleetSplitIcon = "ui.command.fleet_split.icon";

    /// <summary>Unit-map strip icon: join the selected fleet with an adjacent own fleet.</summary>
    public const string UiCommandFleetJoinIcon = "ui.command.fleet_join.icon";

    /// <summary>Unit-map strip icon: scuttle the selected fleet.</summary>
    public const string UiCommandFleetScuttleIcon = "ui.command.fleet_scuttle.icon";

    // --- Unit-map strip: city and selection (2) ---

    /// <summary>Unit-map strip icon: order fortification for the selected city.</summary>
    public const string UiCommandCityFortifyIcon = "ui.command.city_fortify.icon";

    /// <summary>
    /// Unit-map strip icon: cancel the selection. [designed]: the strip's 15th button is unread
    /// in the original (audit 3.3) and Cancel selection fits it.
    /// </summary>
    public const string UiCommandCancelSelectionIcon = "ui.command.cancel_selection.icon";

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

            // Terrain variants, river pieces and shore overlays (T148)
            foreach (var key in TerrainKeysAddedByT148)
            {
                yield return key;
            }

            // Sound effects
            yield return SfxCityCaptured;
            yield return SfxBattle;
            yield return SfxUnitMove;

            // Toolbar command icons - main toolbar (9)
            yield return UiCommandOpenIcon;
            yield return UiCommandSaveIcon;
            yield return UiCommandEndTurnIcon;
            yield return UiCommandNewsIcon;
            yield return UiCommandRelationsIcon;
            yield return UiCommandTaxationIcon;
            yield return UiCommandBalanceSheetIcon;
            yield return UiCommandRecruitUnitIcon;
            yield return UiCommandBuildFleetIcon;

            // Toolbar command icons - Area-map strip (12)
            yield return UiCommandShowCitiesIcon;
            yield return UiCommandShowCapitalIcon;
            yield return UiCommandShowArmiesIcon;
            yield return UiCommandShowFleetsIcon;
            yield return UiCommandShowAllIcon;
            yield return UiCommandShowMercsLightInfantryIcon;
            yield return UiCommandShowMercsHeavyInfantryIcon;
            yield return UiCommandShowMercsArchersIcon;
            yield return UiCommandShowMercsLightCavalryIcon;
            yield return UiCommandShowMercsHeavyCavalryIcon;
            yield return UiCommandShowMercsAllIcon;
            yield return UiCommandFindCityIcon;

            // Toolbar command icons - unit-map strip (15)
            yield return UiCommandArmySupplyIcon;
            yield return UiCommandArmyRecruitMercenariesIcon;
            yield return UiCommandArmyTransferUnitIcon;
            yield return UiCommandArmySplitIcon;
            yield return UiCommandArmyJoinIcon;
            yield return UiCommandArmyChangeUnitsIcon;
            yield return UiCommandArmyDisbandIcon;
            yield return UiCommandFleetSupplyIcon;
            yield return UiCommandFleetRepairIcon;
            yield return UiCommandFleetTransferShipsIcon;
            yield return UiCommandFleetSplitIcon;
            yield return UiCommandFleetJoinIcon;
            yield return UiCommandFleetScuttleIcon;
            yield return UiCommandCityFortifyIcon;
            yield return UiCommandCancelSelectionIcon;
        }
    }
}
