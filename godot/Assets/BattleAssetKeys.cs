namespace IC2.Slice.Assets;

/// <summary>
/// T128: the asset-pack keys the tactical battle screen (T127) draws, Godot-free like
/// <see cref="MapMarkerKeys"/> so <c>tests/IC2.Engine.Tests</c> can link it and assert them. They live
/// here and not in the engine's <c>AssetKeys</c> because only the Godot front end draws them; the
/// asset tests take the union of <c>AssetKeys.AllKeys</c> and <see cref="AllKeys"/>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>[confirmed: code and the <c>TBATTLEMAP</c> resource, static]</strong>
/// (<c>2026-10-04-decompiled-tactical-battle-rules.md</c> section 10): 15 unit icons of 32 x 32 (five
/// types at three sizes, the size by <c>min(2, troops div (standardBattalionSize div 3))</c>), one
/// ground tile, one cursor and eight 20 x 20 toolbar glyphs. The icon size rule was read from the
/// running game at exactly <c>std div 3</c> and <c>2 x (std div 3)</c> troops
/// (<c>2026-10-04-tactical-battle-sweep.md</c> B8, a Wine candidate). The key names are
/// <strong>[designed]</strong>, following <c>docs/asset-specification.md</c> 1.5's convention.
/// </para>
/// <para>
/// The pictures are the clone's own drawings; only the three recolourable regions are the
/// original's scheme (<see cref="BattleIconTint"/>).
/// </para>
/// </remarks>
public static class BattleAssetKeys
{
    /// <summary>The side of a unit icon, the ground tile and the cursor, in pixels.</summary>
    public const int CellSize = 32;

    /// <summary>The side of a toolbar glyph, in pixels.</summary>
    public const int GlyphSize = 20;

    public const string Ground = "battle.ground";
    public const string Cursor = "battle.cursor";

    /// <summary>The five unit types, in the order of the engine's <c>unit.&lt;type&gt;.icon</c> keys.</summary>
    public static readonly IReadOnlyList<string> UnitTypes =
        new[] { "light_infantry", "heavy_infantry", "archers", "light_cavalry", "heavy_cavalry" };

    /// <summary>The three icon sizes, indexed by <see cref="SizeIndex"/>.</summary>
    public static readonly IReadOnlyList<string> Sizes = new[] { "small", "medium", "large" };

    /// <summary>The eight toolbar buttons in the original's order (Computer general on is the seventh).</summary>
    public static readonly IReadOnlyList<string> ToolbarButtons = new[]
    {
        "unit_moves", "friendly_units", "enemy_units", "cancel_selection",
        "end_turn", "change_pauses", "computer_general", "surrender",
    };

    /// <summary>
    /// The size index of a unit's icon: <c>min(2, troops div (standardBattalionSize div 3))</c>,
    /// recomputed after every loss. <paramref name="standardBattalionSize"/> comes from the ruleset.
    /// </summary>
    public static int SizeIndex(int troops, int standardBattalionSize)
    {
        var third = standardBattalionSize / 3;
        if (third <= 0 || troops <= 0)
        {
            return 0;
        }

        return Math.Min(2, troops / third);
    }

    /// <summary>The key of a unit icon: <c>battle.unit.&lt;type&gt;.&lt;small|medium|large&gt;</c>.</summary>
    public static string UnitIcon(string unitType, int sizeIndex)
    {
        ArgumentNullException.ThrowIfNull(unitType);
        return $"battle.unit.{unitType}.{Sizes[Math.Clamp(sizeIndex, 0, Sizes.Count - 1)]}";
    }

    /// <summary>The key of a unit icon for a unit of <paramref name="troops"/> troops.</summary>
    public static string UnitIcon(string unitType, int troops, int standardBattalionSize) =>
        UnitIcon(unitType, SizeIndex(troops, standardBattalionSize));

    /// <summary>The key of a toolbar glyph: <c>battle.toolbar.&lt;button&gt;</c>.</summary>
    public static string ToolbarGlyph(string button)
    {
        ArgumentNullException.ThrowIfNull(button);
        return $"battle.toolbar.{button}";
    }

    /// <summary>All 25 keys: 15 unit icons, the ground, the cursor, the eight glyphs.</summary>
    public static IEnumerable<string> AllKeys
    {
        get
        {
            foreach (var type in UnitTypes)
            {
                for (var size = 0; size < Sizes.Count; size++)
                {
                    yield return UnitIcon(type, size);
                }
            }

            yield return Ground;
            yield return Cursor;

            foreach (var button in ToolbarButtons)
            {
                yield return ToolbarGlyph(button);
            }
        }
    }

    /// <summary>True for a <c>battle.unit.*</c> key (the three-region recolourable icons).</summary>
    public static bool IsUnitIcon(string key) => key.StartsWith("battle.unit.", StringComparison.Ordinal);

    /// <summary>True for a <c>battle.toolbar.*</c> key.</summary>
    public static bool IsToolbarGlyph(string key) => key.StartsWith("battle.toolbar.", StringComparison.Ordinal);

    /// <summary>The side in pixels of the square image behind <paramref name="key"/>.</summary>
    public static int ExpectedSize(string key) => IsToolbarGlyph(key) ? GlyphSize : CellSize;

    /// <summary>
    /// The BMP bit depth of <paramref name="key"/>'s file. docs/asset-specification.md 1.2: 24-bit
    /// opaque for a full-tile background (the ground tile, like a terrain tile) and 32-bit BGRA for
    /// every other asset, the unit icons included (they fill their cell, so their alpha is 255
    /// throughout), the cursor and the toolbar glyphs.
    /// </summary>
    public static int ExpectedBitCount(string key) =>
        key == Ground ? 24 : 32;
}
