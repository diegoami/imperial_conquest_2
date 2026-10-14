using IC2.Data;
using IC2.Engine.Import;
using IC2.Engine.Tests.Battle;
using Xunit;

namespace IC2.Engine.Tests.Battle.Tactical.GoldenMaster;

public sealed class BlockComparisonTests
{
    // Synthetic block: every numeric field/grid cell is zero; slot 0 has a one-character ASCII name.
    private static byte[] Raw()
    {
        var raw = new byte[SaveBattleBlock.Length];
        raw[SaveBattleBlock.HeaderLength + 20] = (byte)'A';
        return raw;
    }

    private static IC2.Engine.Battle.Tactical.TacticalBattleState Port(byte[] raw) =>
        OriginalBattleBlockImport.Import(SaveBattleBlock.Parse(raw), BattleTestbed.StateWith(new[]
        {
            BattleTestbed.Army("army-0", "north", 1, 1, 60, 10, 20),
        }), new[] { false, false });

    [Fact]
    public void Zero_padded_names_match_all_twenty_four_bytes()
    {
        var raw = Raw();
        Assert.Null(BlockComparison.First(Port(raw), SaveBattleBlock.Parse(raw)));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(23)]
    public void Nonzero_bytes_after_the_terminator_are_discrepancies(int index)
    {
        var raw = Raw();
        var port = Port(raw);
        raw[SaveBattleBlock.HeaderLength + 20 + index] = 33;
        var original = SaveBattleBlock.Parse(raw);
        Assert.Equal("A", original.Slots[0].Name); // Decoded text is unchanged, so text equality cannot catch this.
        Assert.Equal(new BlockDifference("slot 0", $"name byte {index}", "0", "33"), BlockComparison.First(port, original));
    }

    [Fact]
    public void Lossy_ASCII_decoding_does_not_hide_original_name_bytes()
    {
        var raw = Raw();
        raw[SaveBattleBlock.HeaderLength + 20] = 233;
        var original = SaveBattleBlock.Parse(raw);
        var port = Port(raw);
        Assert.Equal("?", original.Slots[0].Name);
        Assert.Equal(new BlockDifference("slot 0", "name byte 0", "63", "233"), BlockComparison.First(port, original));
    }
}
