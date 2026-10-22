using IC2.Slice.UI;
using Xunit;

namespace IC2.Engine.Tests.Ui;

/// <summary>#1293: display settings survive a restart, and a bad file never stops the game.</summary>
public sealed class UserSettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ic2-settings-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private string PathFor(string name) => Path.Combine(_dir, name);

    [Fact]
    public void Defaults_match_the_shipped_look_and_T127s_pauses()
    {
        var settings = new UserSettings();
        Assert.Equal("parchment", settings.ColorScheme);
        Assert.Equal("atkinson", settings.Font);
        Assert.Equal(100, settings.TextScalePercent);
        Assert.Equal(1.0, settings.BattleShotDelaySeconds);
        Assert.Equal(2.0, settings.BattleAttackDelaySeconds);
        Assert.Equal("normal", settings.BattleSpeedPreset?.Id);
        Assert.True(settings.SoundEnabled);
        Assert.False(settings.Fullscreen);
        Assert.True(settings.ShowOtherBattleReports);
        Assert.False(settings.ConfirmMoves);
        Assert.Equal("tiles", settings.MapStyle);
    }

    [Fact]
    public void A_saved_file_loads_back_equal()
    {
        var saved = new UserSettings
        {
            PackId = "placeholder", SoundEnabled = false, VolumePercent = 40, ColorScheme = "dark",
            Font = "garamond", TextScalePercent = 125, Fullscreen = true, ShowOtherBattleReports = false, ConfirmMoves = true,
            MapStyle = "painted",
        }.WithBattleSpeed(BattleSpeeds.All.Single(s => s.Id == "fast"));

        Assert.True(saved.TrySave(PathFor("settings.json")));
        var loaded = UserSettings.Load(PathFor("settings.json"));

        Assert.Equal(saved, loaded);
        Assert.Equal("fast", loaded.BattleSpeedPreset?.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2,3]")]
    [InlineData("{\"VolumePercent\": \"loud\"}")]
    public void A_missing_or_malformed_file_gives_the_defaults(string? content)
    {
        var path = PathFor("settings.json");
        if (content is not null)
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(path, content);
        }

        Assert.Equal(new UserSettings(), UserSettings.Load(path));
    }

    [Fact]
    public void Out_of_range_values_are_clamped_and_unknown_ids_fall_back()
    {
        var settings = new UserSettings
        {
            VolumePercent = 250, TextScalePercent = 10, ColorScheme = "neon", Font = "comic",
            BattleShotDelaySeconds = 9.87, BattleAttackDelaySeconds = 1.6, PackId = "  ",
            MapStyle = "watercolour",
        }.Normalized();

        Assert.Equal(100, settings.VolumePercent);
        Assert.Equal(UserSettings.MinTextScalePercent, settings.TextScalePercent);
        Assert.Equal("parchment", settings.ColorScheme);
        Assert.Equal("atkinson", settings.Font);
        Assert.Equal(3.0, settings.BattleShotDelaySeconds);
        Assert.Equal(1.6, settings.BattleAttackDelaySeconds);
        Assert.Null(settings.PackId);
        Assert.Null(settings.BattleSpeedPreset);
        Assert.Equal("tiles", settings.MapStyle);
    }

    [Fact]
    public void The_colours_are_the_schemes_and_an_old_files_custom_colours_are_ignored()
    {
        Assert.Equal(ColorSchemes.ById("contrast"), new UserSettings { ColorScheme = "contrast" }.ResolvedColors());

        Directory.CreateDirectory(_dir);
        var path = PathFor("old-settings.json");
        File.WriteAllText(path, """{ "ColorScheme": "contrast", "BackgroundHex": "#203040", "TextHex": "#fafafa" }""");
        Assert.Equal(ColorSchemes.ById("contrast"), UserSettings.Load(path).ResolvedColors());
    }

    [Fact]
    public void Every_preset_sits_on_T127s_pause_grid()
    {
        foreach (var speed in BattleSpeeds.All)
        {
            var settings = new UserSettings().WithBattleSpeed(speed);
            Assert.Equal(settings, settings.Normalized());
            Assert.Equal(speed, settings.BattleSpeedPreset);
        }
    }
}
