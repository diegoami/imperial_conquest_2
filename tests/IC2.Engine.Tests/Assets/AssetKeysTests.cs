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
}
