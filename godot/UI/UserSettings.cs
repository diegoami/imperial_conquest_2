using System.Text.Json;
using System.Text.Json.Serialization;
using IC2.Engine.Model;
using IC2.Slice.Screens;

namespace IC2.Slice.UI;

/// <summary>
/// The player's global settings, remembered across launches in <c>user://settings.json</c>.
/// Display settings never change rules; T207's opt-in automation issues ordinary player commands
/// before a confirmed End turn and can therefore change outcomes. No value is in a native save or ruleset.
/// Godot-free so the tests can link it; <see cref="SettingsScreen"/> applies it.
/// </summary>
public sealed record UserSettings
{
    /// <summary>The settings file's schema; a file with another number is read as far as it matches.</summary>
    public const int CurrentSchema = 3;

    public const int MaxAutoPurseTargetTalents = 100_000;
    public const int MaxAutoTreasuryReserveTalents = 1_000_000_000;

    /// <summary>The smallest and largest text size, in percent.</summary>
    public const int MinTextScalePercent = 80, MaxTextScalePercent = 160;

    public int Schema { get; init; } = CurrentSchema;

    /// <summary>The asset pack; <see langword="null"/> keeps the loader's default.</summary>
    public string? PackId { get; init; }

    public bool SoundEnabled { get; init; } = true;

    /// <summary>The master volume, 0 to 100.</summary>
    public int VolumePercent { get; init; } = 100;

    /// <summary>T127's pause after a shot, on <see cref="BattlePauses"/>'s grid.</summary>
    public double BattleShotDelaySeconds { get; init; } = 1.0;

    /// <summary>T127's pause after a melee attack, on <see cref="BattlePauses"/>'s grid.</summary>
    public double BattleAttackDelaySeconds { get; init; } = 2.0;

    /// <summary>One of <see cref="ColorSchemes.All"/>' ids.</summary>
    public string ColorScheme { get; init; } = ColorSchemes.DefaultId;

    /// <summary>One of <see cref="UiFonts.All"/>' ids.</summary>
    public string Font { get; init; } = UiFonts.DefaultId;

    /// <summary>The whole interface's size, in percent (the window's content scale).</summary>
    public int TextScalePercent { get; init; } = 100;

    public bool Fullscreen { get; init; }

    /// <summary>#1280 F7: whether battles between two computer nations pop up a report (T25's default).</summary>
    public bool ShowOtherBattleReports { get; init; } = true;

    /// <summary>#1280 F2: a move click only marks its destination; a second click on it moves. Off by default (the original moves at once).</summary>
    public bool ConfirmMoves { get; init; }

    /// <summary>T207: opt-in supply top-up for the active human nation's armies before End turn.</summary>
    public bool AutoSupplyArmies { get; init; }

    /// <summary>T207: opt-in treasury-to-army purse top-up before End turn.</summary>
    public bool AutoFundArmies { get; init; }

    /// <summary>T208: opt-in town-to-fleet supply before confirmed End turn.</summary>
    public bool AutoSupplyFleets { get; init; }

    /// <summary>T208: opt-in repair of eligible fleets beside owned towns.</summary>
    public bool AutoRepairFleets { get; init; }

    /// <summary>T209: opt-in mobilization of every ready slot before confirmed End turn.</summary>
    public bool AutoMobilizeReady { get; init; }

    /// <summary>T210: globally enable the world/city plans below.</summary>
    public bool AutoRecruitEnabled { get; init; }

    /// <summary>One global plan per world/city; shared by campaigns of that world on this machine.</summary>
    public ValueList<AutoRecruitCityPlan> AutoRecruitPlans { get; init; } = ValueList<AutoRecruitCityPlan>.Empty;

    /// <summary>The army's supply target as a percentage of its current ruleset capacity.</summary>
    public int AutoSupplyTargetPercent { get; init; } = 100;

    /// <summary>Target purse in talents, further capped by the active ruleset.</summary>
    public int AutoPurseTargetTalents { get; init; } = 1_000;

    /// <summary>Automatic funding never deliberately lowers treasury below this many talents.</summary>
    public int AutoTreasuryReserveTalents { get; init; } = 1_000;

    public int AutoFleetSupplyTargetPercent { get; init; } = 100;

    public int AutoFleetRepairTargetPercent { get; init; } = 100;

    /// <summary>One of <see cref="MapStyles.All"/>' ids: the map drawn as tiles (the default) or painted.</summary>
    public string MapStyle { get; init; } = MapStyles.DefaultId;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// The settings in <paramref name="path"/>, normalised; the defaults when the file is missing or is
    /// not a settings object. Never throws: settings must not stop the game from starting.
    /// </summary>
    public static UserSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new UserSettings();
            }

            return (JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(path), Json) ?? new UserSettings()).Normalized();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
        {
            return new UserSettings();
        }
    }

    /// <summary>Writes the normalised settings to <paramref name="path"/>; false when it could not.</summary>
    public bool TrySave(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, JsonSerializer.Serialize(Normalized(), Json));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>Every value clamped or snapped into range; unknown ids and malformed colours fall back.</summary>
    public UserSettings Normalized() => this with
    {
        Schema = CurrentSchema,
        PackId = string.IsNullOrWhiteSpace(PackId) ? null : PackId.Trim(),
        VolumePercent = Math.Clamp(VolumePercent, 0, 100),
        BattleShotDelaySeconds = BattlePauses.SnapShot(BattleShotDelaySeconds),
        BattleAttackDelaySeconds = BattlePauses.SnapAttack(BattleAttackDelaySeconds),
        ColorScheme = ColorSchemes.ById(ColorScheme).Id,
        Font = UiFonts.ById(Font).Id,
        TextScalePercent = Math.Clamp(TextScalePercent, MinTextScalePercent, MaxTextScalePercent),
        MapStyle = MapStyles.ById(MapStyle).Id,
        AutoSupplyTargetPercent = Math.Clamp(AutoSupplyTargetPercent, 0, 100),
        AutoPurseTargetTalents = Math.Clamp(AutoPurseTargetTalents, 0, MaxAutoPurseTargetTalents),
        AutoTreasuryReserveTalents = Math.Clamp(AutoTreasuryReserveTalents, 0, MaxAutoTreasuryReserveTalents),
        AutoFleetSupplyTargetPercent = Math.Clamp(AutoFleetSupplyTargetPercent, 0, 100),
        AutoFleetRepairTargetPercent = Math.Clamp(AutoFleetRepairTargetPercent, 0, 100),
        AutoRecruitPlans = NormalizeRecruitPlans(AutoRecruitPlans),
    };

    public AutoRecruitCityPlan? RecruitPlanFor(string worldId, string cityId) =>
        AutoRecruitPlans.FirstOrDefault(plan => plan.WorldId == worldId && plan.CityId == cityId);

    public UserSettings WithRecruitPlan(AutoRecruitCityPlan plan) => this with
    {
        AutoRecruitPlans = ValueList.From(AutoRecruitPlans
            .Where(existing => existing.WorldId != plan.WorldId || existing.CityId != plan.CityId)
            .Append(plan)),
    };

    public UserSettings WithoutRecruitPlan(string worldId, string cityId) => this with
    {
        AutoRecruitPlans = ValueList.From(AutoRecruitPlans
            .Where(plan => plan.WorldId != worldId || plan.CityId != cityId)),
    };

    private static ValueList<AutoRecruitCityPlan> NormalizeRecruitPlans(
        ValueList<AutoRecruitCityPlan>? plans)
    {
        var normalized = new List<AutoRecruitCityPlan>();
        foreach (var plan in plans ?? ValueList<AutoRecruitCityPlan>.Empty)
        {
            if (plan is null || string.IsNullOrWhiteSpace(plan.WorldId)
                || string.IsNullOrWhiteSpace(plan.CityId) || string.IsNullOrWhiteSpace(plan.UnitTypeId)) continue;
            var clean = plan with
            {
                WorldId = plan.WorldId.Trim(), CityId = plan.CityId.Trim(),
                UnitTypeId = plan.UnitTypeId.Trim(), Troops = Math.Max(0, plan.Troops),
                TargetQueued = Math.Clamp(plan.TargetQueued, 1, 40),
            };
            var prior = normalized.FindIndex(other => other.WorldId == clean.WorldId && other.CityId == clean.CityId);
            if (prior >= 0) normalized[prior] = clean;
            else normalized.Add(clean);
        }

        return ValueList.From(normalized);
    }

    /// <summary>The <see cref="BattleSpeed"/> preset these pauses match, or <see langword="null"/> for custom values.</summary>
    public BattleSpeed? BattleSpeedPreset => BattleSpeeds.Match(BattleShotDelaySeconds, BattleAttackDelaySeconds);

    /// <summary>These settings with <paramref name="speed"/>'s pauses.</summary>
    public UserSettings WithBattleSpeed(BattleSpeed speed) =>
        this with { BattleShotDelaySeconds = speed.ShotSeconds, BattleAttackDelaySeconds = speed.AttackSeconds };

    /// <summary>
    /// The colours the interface is drawn with: the chosen scheme's. The custom background and text colours were
    /// removed (the owner's decision of 2026-10-22); an older settings file's values for them are ignored on load.
    /// </summary>
    public ColorScheme ResolvedColors() => ColorSchemes.ById(ColorScheme);
}

public sealed record AutoRecruitCityPlan(string WorldId, string CityId, string UnitTypeId,
    int Troops, int TargetQueued);

/// <summary>How the strategic map draws its terrain.</summary>
public sealed record MapStyle(string Id, string Name);

/// <summary>
/// The owner's request of 2026-10-21: "Classic tiles" lays one patterned 32-px tile per cell, as the
/// original draws its map, and is the default; "Painted" is T148's blended surface. [designed]
/// </summary>
public static class MapStyles
{
    public const string Tiles = "tiles";
    public const string Painted = "painted";
    public const string DefaultId = Tiles;

    public static readonly IReadOnlyList<MapStyle> All = new[]
    {
        new MapStyle(Tiles, "Classic tiles"),
        new MapStyle(Painted, "Painted"),
    };

    /// <summary>The style with <paramref name="id"/>, or the default.</summary>
    public static MapStyle ById(string? id) => All.FirstOrDefault(s => s.Id == id) ?? All[0];
}

/// <summary>A named battle-speed preset over T127's two pauses.</summary>
public sealed record BattleSpeed(string Id, string Name, double ShotSeconds, double AttackSeconds);

/// <summary>#1293: Slow / Normal / Fast / Instant over T127's pause grid. [designed]</summary>
public static class BattleSpeeds
{
    public static readonly IReadOnlyList<BattleSpeed> All = new[]
    {
        new BattleSpeed("slow", "Slow", 2.0, 3.0),
        new BattleSpeed("normal", "Normal", 1.0, 2.0),
        new BattleSpeed("fast", "Fast", 0.3, 1.0),
        new BattleSpeed("instant", "Instant", 0.0, 0.0),
    };

    /// <summary>The preset with exactly these pauses, or <see langword="null"/>.</summary>
    public static BattleSpeed? Match(double shotSeconds, double attackSeconds) =>
        All.FirstOrDefault(s => Math.Abs(s.ShotSeconds - shotSeconds) < 0.01 && Math.Abs(s.AttackSeconds - attackSeconds) < 0.01);
}

/// <summary>
/// One interface colour scheme, as <c>#RRGGBB</c> per role of <c>AppTheme.tres</c>'s <c>App/colors</c>:
/// <see cref="Background"/> behind every screen, <see cref="Panel"/> and <see cref="Raised"/> for
/// panels and buttons, <see cref="Text"/>, <see cref="Muted"/> for secondary text, <see cref="Accent"/>
/// for headings, borders and focus.
/// </summary>
public sealed record ColorScheme(string Id, string Name, string Background, string Panel, string Raised, string Text, string Muted, string Accent);

/// <summary>
/// #1293: the curated colour schemes. "Dark" is <c>AppTheme.tres</c>'s own palette; Parchment is the default
/// (the owner's decision of 2026-10-22). [designed]
/// </summary>
public static class ColorSchemes
{
    public const string DefaultId = "parchment";

    /// <summary>The scheme whose colours <c>AppTheme.tres</c> itself carries.</summary>
    public const string ThemePaletteId = "dark";

    public static readonly IReadOnlyList<ColorScheme> All = new[]
    {
        new ColorScheme(DefaultId, "Parchment (default)", "#E8DCC0", "#F3EAD3", "#DCCBA6", "#2A2016", "#5E4F3A", "#8A4B12"),
        new ColorScheme("dark", "Dark", "#14110D", "#1E1912", "#2C251B", "#EDE3CC", "#B8AB8E", "#D9A55B"),
        new ColorScheme("contrast", "High contrast", "#000000", "#101010", "#262626", "#FFFFFF", "#E0E0E0", "#FFD23F"),
        new ColorScheme("slate", "Slate blue", "#11161F", "#1A2230", "#263247", "#E3E9F2", "#A5B2C6", "#6FB3E8"),
    };

    /// <summary>The scheme with <paramref name="id"/>, or the default.</summary>
    public static ColorScheme ById(string? id) => All.FirstOrDefault(s => s.Id == id) ?? All[0];

    /// <summary><c>#RRGGBB</c> in upper case for a 6-digit hex colour (the <c>#</c> optional), else <see langword="null"/>.</summary>
    public static string? NormalizeHex(string? hex)
    {
        var text = hex?.Trim().TrimStart('#');
        return text is { Length: 6 } && text.All(Uri.IsHexDigit) ? "#" + text.ToUpperInvariant() : null;
    }
}

/// <summary>A bundled interface font: its id, its caption and its file under <c>res://Assets/Fonts</c>.</summary>
public sealed record UiFont(string Id, string Name, string ResourcePath);

/// <summary>
/// #1293: the curated body fonts, all bundled under the SIL Open Font License 1.1. Atkinson Hyperlegible is the
/// default (the owner's decision of 2026-10-22).
/// </summary>
public static class UiFonts
{
    public const string DefaultId = "atkinson";

    public static readonly IReadOnlyList<UiFont> All = new[]
    {
        new UiFont(DefaultId, "Atkinson Hyperlegible (default, easy reading)", "res://Assets/Fonts/AtkinsonHyperlegible/AtkinsonHyperlegible-Regular.ttf"),
        new UiFont("source-sans", "Source Sans 3", "res://Assets/Fonts/SourceSans3/SourceSans3-wght.ttf"),
        new UiFont("garamond", "EB Garamond (classical serif)", "res://Assets/Fonts/EBGaramond/EBGaramond-Regular.ttf"),
    };

    /// <summary>The font with <paramref name="id"/>, or the default.</summary>
    public static UiFont ById(string? id) => All.FirstOrDefault(f => f.Id == id) ?? All[0];
}
