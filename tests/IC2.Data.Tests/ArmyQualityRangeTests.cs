using Xunit;

namespace IC2.Data.Tests;

public class ArmyQualityRangeTests
{
    [Theory]
    [InlineData(4)]
    [InlineData(9)]
    public void Live_unit_quality_in_original_range_is_preserved(ushort quality)
    {
        var data = SyntheticSaveBuilder.MinimalSav(1,
            (0, (bytes, offset) => SyntheticSaveBuilder.WriteArmyWithUnit(
                bytes, offset, type: 2, quality: quality, name: "Bowmen")));

        var army = Assert.Single(SaveArmyTable.Parse(data).Armies);
        Assert.Equal(quality, Assert.Single(army.Units).QualityCode);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(10)]
    public void Unsupported_live_unit_quality_is_rejected(ushort quality)
    {
        var data = SyntheticSaveBuilder.MinimalSav(1,
            (0, (bytes, offset) => SyntheticSaveBuilder.WriteArmyWithUnit(
                bytes, offset, type: 2, quality: quality, name: "Bowmen")));

        var ex = Assert.Throws<InvalidDataException>(() => SaveArmyTable.Parse(data));
        Assert.Contains("quality code", ex.Message);
    }
}
