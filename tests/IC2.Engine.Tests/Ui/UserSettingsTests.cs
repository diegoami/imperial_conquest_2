using IC2.Slice.UI;
using Xunit;

namespace IC2.Engine.Tests.Ui;

/// <summary>#1293: display settings survive a restart, and a bad file never stops the game.</summary>
public sealed class UserSettingsTests : IDisposable
{
    [Fact]
    public void Parchment_secondary_text_remains_readable_on_panel_and_raised_backgrounds()
    {
        var colors = ColorSchemes.ById("parchment");
        Assert.True(Contrast(colors.Muted, colors.Panel) >= 7);
        Assert.True(Contrast(colors.Muted, colors.Raised) >= 7);

        static double Contrast(string a, string b)
        {
            var x = Luminance(a); var y = Luminance(b);
            return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
        }
        static double Luminance(string hex)
        {
            double Channel(int start)
            {
                var c = Convert.ToInt32(hex.Substring(start, 2), 16) / 255.0;
                return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Channel(1) + 0.7152 * Channel(3) + 0.0722 * Channel(5);
        }
    }

    [Fact]
    public void Legacy_auto_recruit_configuration_is_ignored_without_resetting_other_preferences()
    {
        Directory.CreateDirectory(_dir);
        var path = PathFor("legacy-plans.json");
        File.WriteAllText(path, """
            {"AutoRecruitEnabled":true,"AutoRecruitPlans":[{"WorldId":"toy-3city","CityId":"arx","UnitTypeId":"light_infantry","Troops":1000,"TargetQueued":2}],
             "AutoMobilizeReady":true,"VolumePercent":55,"ColorScheme":"dark"}
            """);
        var loaded = UserSettings.Load(path);
        Assert.False(loaded.AutoRecruitEnabled);
        Assert.Empty(loaded.AutoRecruitPlans);
        Assert.True(loaded.AutoMobilizeReady);
        Assert.Equal(55, loaded.VolumePercent);
        Assert.Equal("dark", loaded.ColorScheme);
        Assert.True(loaded.TrySave(path));
        Assert.DoesNotContain("AutoRecruit", File.ReadAllText(path));
    }

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ic2-settings-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private string PathFor(string name) => Path.Combine(_dir, name);

    [Fact]
    public void Hundredth_second_custom_pauses_survive_and_do_not_match_a_neighboring_preset()
    {
        var settings = new UserSettings { BattleShotDelaySeconds = 0.11, BattleAttackDelaySeconds = 0.21 }.Normalized();
        Assert.Equal(0.11, settings.BattleShotDelaySeconds);
        Assert.Equal(0.21, settings.BattleAttackDelaySeconds);
        Assert.Null(settings.BattleSpeedPreset);
    }

    [Fact]
    public void Defaults_match_the_shipped_look_and_T127s_pauses()
    {
        var settings = new UserSettings();
        Assert.Equal("parchment", settings.ColorScheme);
        Assert.Equal("atkinson", settings.Font);
        Assert.Equal(100, settings.TextScalePercent);
        Assert.Equal(0.1, settings.BattleShotDelaySeconds);
        Assert.Equal(0.2, settings.BattleAttackDelaySeconds);
        Assert.Equal("normal", settings.BattleSpeedPreset?.Id);
        Assert.True(settings.SoundEnabled);
        Assert.False(settings.Fullscreen);
        Assert.True(settings.ShowOtherBattleReports);
        Assert.False(settings.ConfirmMoves);
        Assert.False(settings.AutoSupplyArmies);
        Assert.False(settings.AutoFundArmies);
        Assert.False(settings.AutoSupplyFleets);
        Assert.False(settings.AutoRepairFleets);
        Assert.False(settings.AutoMobilizeReady);
        Assert.False(settings.AutoRecruitEnabled);
        Assert.Empty(settings.AutoRecruitPlans);
        Assert.Equal(100, settings.AutoSupplyTargetPercent);
        Assert.Equal(1_000, settings.AutoPurseTargetTalents);
        Assert.Equal(1_000, settings.AutoTreasuryReserveTalents);
        Assert.Equal(100, settings.AutoFleetSupplyTargetPercent);
        Assert.Equal(100, settings.AutoFleetRepairTargetPercent);
        Assert.Equal("tiles", settings.MapStyle);
    }

    [Fact]
    public void A_saved_file_loads_back_equal()
    {
        var saved = new UserSettings
        {
            PackId = "placeholder", SoundEnabled = false, VolumePercent = 40, ColorScheme = "dark",
            Font = "garamond", TextScalePercent = 125, Fullscreen = true, ShowOtherBattleReports = false, ConfirmMoves = true,
            MapStyle = "painted", AutoSupplyArmies = true, AutoFundArmies = true,
            AutoSupplyTargetPercent = 75, AutoPurseTargetTalents = 800, AutoTreasuryReserveTalents = 2_500,
            AutoSupplyFleets = true, AutoRepairFleets = true,
            AutoFleetSupplyTargetPercent = 65, AutoFleetRepairTargetPercent = 85,
            AutoMobilizeReady = true,
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
            MapStyle = "watercolour", AutoSupplyTargetPercent = -10,
            AutoPurseTargetTalents = int.MaxValue, AutoTreasuryReserveTalents = -7,
            AutoFleetSupplyTargetPercent = -10, AutoFleetRepairTargetPercent = 150,
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
        Assert.Equal(0, settings.AutoSupplyTargetPercent);
        Assert.Equal(UserSettings.MaxAutoPurseTargetTalents, settings.AutoPurseTargetTalents);
        Assert.Equal(0, settings.AutoTreasuryReserveTalents);
        Assert.Equal(0, settings.AutoFleetSupplyTargetPercent);
        Assert.Equal(100, settings.AutoFleetRepairTargetPercent);
    }

    [Fact]
    public void The_colours_are_the_schemes_and_an_old_files_custom_colours_are_ignored()
    {
        Assert.Equal(ColorSchemes.ById("contrast"), new UserSettings { ColorScheme = "contrast" }.ResolvedColors());

        Directory.CreateDirectory(_dir);
        var path = PathFor("old-settings.json");
        File.WriteAllText(path, """{ "ColorScheme": "contrast", "BackgroundHex": "#203040", "TextHex": "#fafafa" }""");
        Assert.Equal(ColorSchemes.ById("contrast"), UserSettings.Load(path).ResolvedColors());
        Assert.False(UserSettings.Load(path).AutoSupplyArmies);
        Assert.False(UserSettings.Load(path).AutoFundArmies);
        Assert.False(UserSettings.Load(path).AutoSupplyFleets);
        Assert.False(UserSettings.Load(path).AutoRepairFleets);
        Assert.False(UserSettings.Load(path).AutoMobilizeReady);
        Assert.False(UserSettings.Load(path).AutoRecruitEnabled);
        Assert.Empty(UserSettings.Load(path).AutoRecruitPlans);
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

    [Fact]
    public void Obsolete_plan_objects_are_discarded_on_normalization_and_restart()
    {
        var first = new AutoRecruitCityPlan("classical-mediterranean", "rome", "archers", 2_000, 1);
        var replacement = first with { UnitTypeId = "heavy_infantry", Troops = 4_000, TargetQueued = 2 };
        var italy = new AutoRecruitCityPlan("italy-500bc", "rome", "light_infantry", 1_000, 1);
        var settings = new UserSettings { AutoRecruitEnabled = true,
            AutoRecruitPlans = IC2.Engine.Model.ValueList.Of(first, italy, replacement) }.Normalized();
        Assert.False(settings.AutoRecruitEnabled);
        Assert.Empty(settings.AutoRecruitPlans);
        Assert.True(settings.TrySave(PathFor("plans.json")));
        Assert.Equal(settings, UserSettings.Load(PathFor("plans.json")));
        Assert.DoesNotContain("AutoRecruit", File.ReadAllText(PathFor("plans.json")));
    }
}
