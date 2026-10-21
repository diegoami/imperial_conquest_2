using System.Text.Json;
using System.Text.Json.Serialization;
using IC2.Slice.Screens;

namespace IC2.Slice.UI;

/// <summary>
/// #1293: the player's own display settings, remembered across launches in <c>user://settings.json</c>.
/// Display-only: nothing here is ever in a save game or a ruleset, and no value changes a rule's
/// outcome. Godot-free so the tests can link it; <see cref="SettingsScreen"/> applies it.
/// </summary>
public sealed record UserSettings
{
    /// <summary>The settings file's schema; a file with another number is read as far as it matches.</summary>
    public const int CurrentSchema = 1;

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

    /// <summary>A custom background colour (<c>#RRGGBB</c>) over the scheme's, or <see langword="null"/>.</summary>
    public string? BackgroundHex { get; init; }

    /// <summary>A custom text colour (<c>#RRGGBB</c>) over the scheme's, or <see langword="null"/>.</summary>
    public string? TextHex { get; init; }

    /// <summary>One of <see cref="UiFonts.All"/>' ids.</summary>
    public string Font { get; init; } = UiFonts.DefaultId;

    /// <summary>The whole interface's size, in percent (the window's content scale).</summary>
    public int TextScalePercent { get; init; } = 100;

    public bool Fullscreen { get; init; }

    /// <summary>#1280 F7: whether battles between two computer nations pop up a report (T25's default).</summary>
    public bool ShowOtherBattleReports { get; init; } = true;

    /// <summary>#1280 F2: a move click only marks its destination; a second click on it moves. Off by default (the original moves at once).</summary>
    public bool ConfirmMoves { get; init; }

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
        BackgroundHex = ColorSchemes.NormalizeHex(BackgroundHex),
        TextHex = ColorSchemes.NormalizeHex(TextHex),
        Font = UiFonts.ById(Font).Id,
        TextScalePercent = Math.Clamp(TextScalePercent, MinTextScalePercent, MaxTextScalePercent),
    };

    /// <summary>The <see cref="BattleSpeed"/> preset these pauses match, or <see langword="null"/> for custom values.</summary>
    public BattleSpeed? BattleSpeedPreset => BattleSpeeds.Match(BattleShotDelaySeconds, BattleAttackDelaySeconds);

    /// <summary>These settings with <paramref name="speed"/>'s pauses.</summary>
    public UserSettings WithBattleSpeed(BattleSpeed speed) =>
        this with { BattleShotDelaySeconds = speed.ShotSeconds, BattleAttackDelaySeconds = speed.AttackSeconds };

    /// <summary>The colours the interface is drawn with: the scheme, with any custom background or text.</summary>
    public ColorScheme ResolvedColors()
    {
        var scheme = ColorSchemes.ById(ColorScheme);
        return scheme with
        {
            Background = ColorSchemes.NormalizeHex(BackgroundHex) ?? scheme.Background,
            Text = ColorSchemes.NormalizeHex(TextHex) ?? scheme.Text,
        };
    }
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

/// <summary>#1293: the curated colour schemes. "Dark" is <c>AppTheme.tres</c>'s own palette. [designed]</summary>
public static class ColorSchemes
{
    public const string DefaultId = "dark";

    public static readonly IReadOnlyList<ColorScheme> All = new[]
    {
        new ColorScheme(DefaultId, "Dark (default)", "#14110D", "#1E1912", "#2C251B", "#EDE3CC", "#B8AB8E", "#D9A55B"),
        new ColorScheme("parchment", "Parchment", "#E8DCC0", "#F3EAD3", "#DCCBA6", "#2A2016", "#5E4F3A", "#8A4B12"),
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

/// <summary>#1293: the curated body fonts, all bundled under the SIL Open Font License 1.1.</summary>
public static class UiFonts
{
    public const string DefaultId = "source-sans";

    public static readonly IReadOnlyList<UiFont> All = new[]
    {
        new UiFont(DefaultId, "Source Sans 3 (default)", "res://Assets/Fonts/SourceSans3/SourceSans3-wght.ttf"),
        new UiFont("atkinson", "Atkinson Hyperlegible (easy reading)", "res://Assets/Fonts/AtkinsonHyperlegible/AtkinsonHyperlegible-Regular.ttf"),
        new UiFont("garamond", "EB Garamond (classical serif)", "res://Assets/Fonts/EBGaramond/EBGaramond-Regular.ttf"),
    };

    /// <summary>The font with <paramref name="id"/>, or the default.</summary>
    public static UiFont ById(string? id) => All.FirstOrDefault(f => f.Id == id) ?? All[0];
}
