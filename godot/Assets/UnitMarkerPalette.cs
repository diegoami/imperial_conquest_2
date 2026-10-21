using IC2.Engine.Model;

namespace IC2.Slice.Assets;

/// <summary>
/// The colours one owner's army or fleet marker is drawn with on the strategic map (#1291, the user's
/// request of 2026-10-21 for "less boring" unit markers "with some different colour hues on it").
/// </summary>
/// <param name="Top">The square's top edge: <see cref="BattleIconColors.First"/> lightened.</param>
/// <param name="Bottom">The square's bottom edge: <see cref="BattleIconColors.First"/> darkened.</param>
/// <param name="Figure">The silhouette: <see cref="BattleIconColors.Second"/>.</param>
/// <param name="Accent">The inner frame: <see cref="BattleIconColors.Third"/>.</param>
/// <param name="Edge">The one-pixel outer edge that parts the marker from the terrain.</param>
public readonly record struct UnitMarkerPalette(MarkerTint Top, MarkerTint Bottom, MarkerTint Figure, MarkerTint Accent, MarkerTint Edge)
{
    /// <summary>How far <see cref="Top"/> moves toward white and <see cref="Bottom"/> toward black. [designed]</summary>
    public const float Shade = 0.3f;

    /// <summary>How far <see cref="Edge"/> moves toward black. [designed]</summary>
    public const float EdgeShade = 0.6f;

    /// <summary>
    /// The palette for <paramref name="nationId"/>: the three colours the original draws that nation's
    /// unit icons with (<see cref="BattleIconTint.ForNation"/>, the world's <c>battleColorsHex</c> —
    /// the user's decision of 2026-10-09, #891 — or its documented fallback), the field shaded top to
    /// bottom. <see langword="null"/> when the nation is unknown or has no parseable colour.
    /// Cities keep <see cref="MarkerTint.ForOwner"/>'s flat pair.
    /// </summary>
    public static UnitMarkerPalette? ForOwner(World? world, string nationId)
    {
        if (BattleIconTint.ForNation(world, nationId) is not { } colors)
        {
            return null;
        }

        var field = Tint(colors.First);
        return new UnitMarkerPalette(
            Mix(field, White, Shade),
            Mix(field, Black, Shade),
            Tint(colors.Second),
            Tint(colors.Third),
            Mix(field, Black, EdgeShade));
    }

    private static readonly MarkerTint White = new(1f, 1f, 1f, 1f);
    private static readonly MarkerTint Black = new(0f, 0f, 0f, 1f);

    private static MarkerTint Tint((byte R, byte G, byte B) c) => new(c.R / 255f, c.G / 255f, c.B / 255f, 1f);

    private static MarkerTint Mix(MarkerTint from, MarkerTint to, float amount) => new(
        from.Red + ((to.Red - from.Red) * amount),
        from.Green + ((to.Green - from.Green) * amount),
        from.Blue + ((to.Blue - from.Blue) * amount),
        1f);
}
