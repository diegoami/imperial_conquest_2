using IC2.Engine.Assets;
using Xunit;

namespace IC2.Engine.Tests.Assets;

/// <summary>
/// Tests for the AssetKeys constant definitions.
/// </summary>
public class AssetKeysTests
{
    [Fact]
    public void AllKeys_ReturnsAllDefinedKeys()
    {
        // Act
        var allKeys = AssetKeys.AllKeys.ToList();

        // Assert
        Assert.NotEmpty(allKeys);

        // Should include army tiers
        Assert.Contains(AssetKeys.ArmyTier1Icon, allKeys);
        Assert.Contains(AssetKeys.ArmyTier2Icon, allKeys);
        Assert.Contains(AssetKeys.ArmyTier3Icon, allKeys);

        // Should include fleet tiers
        Assert.Contains(AssetKeys.FleetTier1Icon, allKeys);
        Assert.Contains(AssetKeys.FleetTier2Icon, allKeys);
        Assert.Contains(AssetKeys.FleetTier3Icon, allKeys);

        // Should include city markers
        Assert.Contains(AssetKeys.CityTier1Icon, allKeys);
        Assert.Contains(AssetKeys.CityTier2Icon, allKeys);
        Assert.Contains(AssetKeys.CityTier3Icon, allKeys);
        Assert.Contains(AssetKeys.CityCapitalIcon, allKeys);

        // Should include sound effects
        Assert.Contains(AssetKeys.SfxCityCaptured, allKeys);
        Assert.Contains(AssetKeys.SfxBattle, allKeys);
        Assert.Contains(AssetKeys.SfxUnitMove, allKeys);
    }

    [Fact]
    public void AllKeys_ContainsNoNullOrEmpty()
    {
        // Act
        var allKeys = AssetKeys.AllKeys;

        // Assert
        foreach (var key in allKeys)
        {
            Assert.NotNull(key);
            Assert.NotEmpty(key);
        }
    }

    [Fact]
    public void AllKeys_ContainsNoDuplicates()
    {
        // Act
        var allKeys = AssetKeys.AllKeys.ToList();
        var uniqueKeys = allKeys.Distinct().ToList();

        // Assert
        Assert.Equal(allKeys.Count, uniqueKeys.Count);
    }

    [Fact]
    public void ArmyTierIcons_AreDistinct()
    {
        // Assert that each tier has a different key
        var tier1 = AssetKeys.ArmyTier1Icon;
        var tier2 = AssetKeys.ArmyTier2Icon;
        var tier3 = AssetKeys.ArmyTier3Icon;

        Assert.NotEqual(tier1, tier2);
        Assert.NotEqual(tier2, tier3);
        Assert.NotEqual(tier1, tier3);
    }

    [Fact]
    public void FleetTierIcons_AreDistinct()
    {
        // Assert that each tier has a different key
        var tier1 = AssetKeys.FleetTier1Icon;
        var tier2 = AssetKeys.FleetTier2Icon;
        var tier3 = AssetKeys.FleetTier3Icon;

        Assert.NotEqual(tier1, tier2);
        Assert.NotEqual(tier2, tier3);
        Assert.NotEqual(tier1, tier3);
    }

    [Fact]
    public void CityTierIcons_AreDistinct()
    {
        // Assert that each tier and capital are different
        var tier1 = AssetKeys.CityTier1Icon;
        var tier2 = AssetKeys.CityTier2Icon;
        var tier3 = AssetKeys.CityTier3Icon;
        var capital = AssetKeys.CityCapitalIcon;

        Assert.NotEqual(tier1, tier2);
        Assert.NotEqual(tier2, tier3);
        Assert.NotEqual(tier1, tier3);
        Assert.NotEqual(capital, tier1);
        Assert.NotEqual(capital, tier2);
        Assert.NotEqual(capital, tier3);
    }

    [Fact]
    public void KeyFormat_IsHierarchical()
    {
        // All keys should be in the format "category.subcategory.aspect"
        foreach (var key in AssetKeys.AllKeys)
        {
            var parts = key.Split('.');
            Assert.True(parts.Length >= 2, $"Key '{key}' should have at least 2 dot-separated parts");
        }
    }

    /// <summary>
    /// T101 DoD 1, widened by T148: <see cref="AssetKeys.AllKeys"/> yields exactly the 25 pre-T101
    /// keys plus the 36 <c>ui.command.*</c> keys (audit §3.5: 9 main, 12 Area-map, 15 unit-map) plus
    /// T148's 28 terrain keys, and nothing else.
    /// </summary>
    [Fact]
    public void AllKeys_HasExactlyThePreT101AndToolbarCommandKeys()
    {
        var allKeys = AssetKeys.AllKeys.ToList();

        Assert.Equal(89, allKeys.Count);
        Assert.Equal(53, allKeys.Count(k => !k.StartsWith("ui.command.", StringComparison.Ordinal)));
        Assert.Equal(36, allKeys.Count(k => k.StartsWith("ui.command.", StringComparison.Ordinal)));
    }

    /// <summary>
    /// T148 DoD 1: the exact string of each of the 28 new terrain keys — three variants of each of the
    /// six variant-bearing types, the six river connectivity pieces (in the order
    /// <c>MapViewer.DrawRiver</c> reads codes 6–11) and the four shore overlays — so a rename fails
    /// here instead of changing what the map resolves. The seven pre-existing terrain keys stay
    /// untouched.
    /// </summary>
    [Fact]
    public void T148_NewTerrainKeys_AreExactlyTheTwentyEightAddedKeys()
    {
        var expected = new[]
        {
            "terrain.plain.tile.2", "terrain.plain.tile.3", "terrain.plain.tile.4",
            "terrain.desert.tile.2", "terrain.desert.tile.3", "terrain.desert.tile.4",
            "terrain.forest.tile.2", "terrain.forest.tile.3", "terrain.forest.tile.4",
            "terrain.mountain.tile.2", "terrain.mountain.tile.3", "terrain.mountain.tile.4",
            "terrain.sea_coastal.tile.2", "terrain.sea_coastal.tile.3", "terrain.sea_coastal.tile.4",
            "terrain.sea_deep.tile.2", "terrain.sea_deep.tile.3", "terrain.sea_deep.tile.4",
            "terrain.river.ew", "terrain.river.ns", "terrain.river.en",
            "terrain.river.es", "terrain.river.ws", "terrain.river.wn",
            "terrain.shore.n", "terrain.shore.e", "terrain.shore.s", "terrain.shore.w",
        };

        Assert.Equal(28, expected.Length);
        Assert.Equal(
            expected.OrderBy(k => k, StringComparer.Ordinal),
            AssetKeys.TerrainKeysAddedByT148.OrderBy(k => k, StringComparer.Ordinal));
        Assert.DoesNotContain("terrain.river.tile", AssetKeys.TerrainKeysAddedByT148);
        Assert.All(expected, key => Assert.Contains(key, AssetKeys.AllKeys));
    }

    /// <summary>
    /// T101 DoD 1: every new toolbar-command key's exact string, one per row, so a rename of any
    /// id fails here rather than silently changing what a parallel task (T100, #558) expects.
    /// </summary>
    [Theory]
    [InlineData(AssetKeys.UiCommandOpenIcon, "ui.command.open.icon")]
    [InlineData(AssetKeys.UiCommandSaveIcon, "ui.command.save.icon")]
    [InlineData(AssetKeys.UiCommandEndTurnIcon, "ui.command.end_turn.icon")]
    [InlineData(AssetKeys.UiCommandNewsIcon, "ui.command.news.icon")]
    [InlineData(AssetKeys.UiCommandRelationsIcon, "ui.command.relations.icon")]
    [InlineData(AssetKeys.UiCommandTaxationIcon, "ui.command.taxation.icon")]
    [InlineData(AssetKeys.UiCommandBalanceSheetIcon, "ui.command.balance_sheet.icon")]
    [InlineData(AssetKeys.UiCommandRecruitUnitIcon, "ui.command.recruit_unit.icon")]
    [InlineData(AssetKeys.UiCommandBuildFleetIcon, "ui.command.build_fleet.icon")]
    [InlineData(AssetKeys.UiCommandShowCitiesIcon, "ui.command.show_cities.icon")]
    [InlineData(AssetKeys.UiCommandShowCapitalIcon, "ui.command.show_capital.icon")]
    [InlineData(AssetKeys.UiCommandShowArmiesIcon, "ui.command.show_armies.icon")]
    [InlineData(AssetKeys.UiCommandShowFleetsIcon, "ui.command.show_fleets.icon")]
    [InlineData(AssetKeys.UiCommandShowAllIcon, "ui.command.show_all.icon")]
    [InlineData(AssetKeys.UiCommandShowMercsLightInfantryIcon, "ui.command.show_mercs_light_infantry.icon")]
    [InlineData(AssetKeys.UiCommandShowMercsHeavyInfantryIcon, "ui.command.show_mercs_heavy_infantry.icon")]
    [InlineData(AssetKeys.UiCommandShowMercsArchersIcon, "ui.command.show_mercs_archers.icon")]
    [InlineData(AssetKeys.UiCommandShowMercsLightCavalryIcon, "ui.command.show_mercs_light_cavalry.icon")]
    [InlineData(AssetKeys.UiCommandShowMercsHeavyCavalryIcon, "ui.command.show_mercs_heavy_cavalry.icon")]
    [InlineData(AssetKeys.UiCommandShowMercsAllIcon, "ui.command.show_mercs_all.icon")]
    [InlineData(AssetKeys.UiCommandFindCityIcon, "ui.command.find_city.icon")]
    [InlineData(AssetKeys.UiCommandArmySupplyIcon, "ui.command.army_supply.icon")]
    [InlineData(AssetKeys.UiCommandArmyRecruitMercenariesIcon, "ui.command.army_recruit_mercenaries.icon")]
    [InlineData(AssetKeys.UiCommandArmyTransferUnitIcon, "ui.command.army_transfer_unit.icon")]
    [InlineData(AssetKeys.UiCommandArmySplitIcon, "ui.command.army_split.icon")]
    [InlineData(AssetKeys.UiCommandArmyJoinIcon, "ui.command.army_join.icon")]
    [InlineData(AssetKeys.UiCommandArmyChangeUnitsIcon, "ui.command.army_change_units.icon")]
    [InlineData(AssetKeys.UiCommandArmyDisbandIcon, "ui.command.army_disband.icon")]
    [InlineData(AssetKeys.UiCommandFleetSupplyIcon, "ui.command.fleet_supply.icon")]
    [InlineData(AssetKeys.UiCommandFleetRepairIcon, "ui.command.fleet_repair.icon")]
    [InlineData(AssetKeys.UiCommandFleetTransferShipsIcon, "ui.command.fleet_transfer_ships.icon")]
    [InlineData(AssetKeys.UiCommandFleetSplitIcon, "ui.command.fleet_split.icon")]
    [InlineData(AssetKeys.UiCommandFleetJoinIcon, "ui.command.fleet_join.icon")]
    [InlineData(AssetKeys.UiCommandFleetScuttleIcon, "ui.command.fleet_scuttle.icon")]
    [InlineData(AssetKeys.UiCommandCityFortifyIcon, "ui.command.city_fortify.icon")]
    [InlineData(AssetKeys.UiCommandCancelSelectionIcon, "ui.command.cancel_selection.icon")]
    public void UiCommandConstant_HasItsExactString(string actual, string expected)
    {
        Assert.Equal(expected, actual);
        Assert.Contains(actual, AssetKeys.AllKeys);
    }
}
