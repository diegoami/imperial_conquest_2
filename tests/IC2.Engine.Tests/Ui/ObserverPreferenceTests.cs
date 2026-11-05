using IC2.Slice.UI;
using Xunit;

namespace IC2.Engine.Tests.Ui;

public sealed class ObserverPreferenceTests
{
    [Fact]
    public void Observer_view_preferences_roundtrip_with_world_isolation_all_and_none()
    {
        var prefs = new UserSettings { ObserverSpeedPercent = 200, ObserverFollowMoves = false, ObserverTacticalBattles = false }
            .WithWatchedNations("classical", new[] { "rome", "gaul", "rome" })
            .WithWatchedNations("italy", null).WithWatchedNations("toy", Array.Empty<string>());
        var path = Path.Combine(Path.GetTempPath(), "ic2-observer-pref-" + Guid.NewGuid().ToString("N"), "settings.json");
        try
        {
            Assert.True(prefs.TrySave(path)); var back = UserSettings.Load(path);
            Assert.Equal(prefs, back);
            Assert.Equal(new[] { "rome", "gaul" }, back.WatchedNationsFor("classical"));
            Assert.Null(back.WatchedNationsFor("italy"));
            Assert.Empty(back.WatchedNationsFor("toy")!);
            Assert.Null(back.WatchedNationsFor("another-world"));
        }
        finally { if (File.Exists(path)) File.Delete(path); if (Directory.Exists(Path.GetDirectoryName(path))) Directory.Delete(Path.GetDirectoryName(path)!); }
    }

    [Theory]
    [InlineData(-1, 25)]
    [InlineData(50, 50)]
    [InlineData(10000, 800)]
    public void Observer_speed_is_bounded_without_changing_other_preferences(int input, int expected)
    {
        var prefs = new UserSettings { ObserverSpeedPercent = input, VolumePercent = 17, AutoSupplyArmies = true };
        var normalized = prefs.Normalized();
        Assert.Equal(expected, normalized.ObserverSpeedPercent);
        Assert.Equal(17, normalized.VolumePercent); Assert.True(normalized.AutoSupplyArmies);
    }
}
