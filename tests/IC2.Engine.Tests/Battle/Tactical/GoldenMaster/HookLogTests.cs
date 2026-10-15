using Xunit;

namespace IC2.Engine.Tests.Battle.Tactical.GoldenMaster;

public sealed class HookLogTests
{
    private static HookRecord[] Records() => new[]
    {
        new HookRecord(1, "start", 0, 0, 1, 1, 0, 0, 0),
        new HookRecord(2, "random", 0x439188, 5, 1, 2, 1, 1, 0),
        new HookRecord(3, "random", 0x439188, 5, 2, 3, 2, 2, 1),
    };

    [Fact]
    public void Correct_seed_unbroken_chain_and_last_counter_pair()
    {
        Assert.Null(new HookLog(Records()).PairingError(1, 2));
    }

    [Fact]
    public void Wrong_start_seed_is_a_fixture_error()
    {
        Assert.Equal("the battle-start record's RandSeed is 1, the series' seed is 9", new HookLog(Records()).PairingError(9, 2));
    }

    [Fact]
    public void Missing_battle_start_is_a_fixture_error()
    {
        Assert.Equal("the log has no battle-start record", new HookLog(Records().Skip(1).ToArray()).PairingError(1, 2));
    }

    [Fact]
    public void Broken_seed_chain_is_a_fixture_error()
    {
        var records = Records();
        records[2] = records[2] with { SeedBefore = 9 };
        Assert.Equal("the seed chain breaks at record 3: seed-before 9, previous seed-after 2", new HookLog(records).PairingError(1, 2));
    }

    [Fact]
    public void Wrong_highest_counter_is_a_fixture_error()
    {
        Assert.Equal("the highest half-round counter is 2, the last BATTLE file is 3", new HookLog(Records()).PairingError(1, 3));
    }
}
