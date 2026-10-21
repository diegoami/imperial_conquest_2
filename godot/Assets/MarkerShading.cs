namespace IC2.Slice.Assets;

/// <summary>
/// The shading every strategic-map marker shares (the user's request of 2026-10-21 for better-looking
/// armies, towns and ships): a field lit from above, a dark edge against the terrain, and a halo behind the
/// figure so it reads on any owner colour. Godot-free, so the exact colours are testable.
/// </summary>
/// <remarks>[designed]. The original draws flat markers; these are presentation only.</remarks>
public static class MarkerShading
{
    /// <summary>How far a field's top moves toward white and its bottom toward black.</summary>
    public const float Shade = UnitMarkerPalette.Shade;

    /// <summary>How far a marker's outer edge moves toward black.</summary>
    public const float EdgeShade = UnitMarkerPalette.EdgeShade;

    /// <summary>The opacity of the halo drawn behind a figure.</summary>
    public const float HaloAlpha = 0.85f;

    /// <summary>The opacity of the drop shadow under a marker.</summary>
    public const float ShadowAlpha = 0.45f;

    /// <summary>A field's lit top edge.</summary>
    public static MarkerTint Top(MarkerTint field) => Mix(field, White, Shade);

    /// <summary>A field's shaded bottom edge.</summary>
    public static MarkerTint Bottom(MarkerTint field) => Mix(field, Black, Shade);

    /// <summary>The edge that parts a marker from the terrain.</summary>
    public static MarkerTint Edge(MarkerTint field) => Mix(field, Black, EdgeShade);

    /// <summary>
    /// The halo behind a figure: near-black behind a light figure and near-white behind a dark one, so the
    /// figure keeps an outline even when it is close to its field's colour (Rome's blue on purple).
    /// </summary>
    public static MarkerTint Halo(MarkerTint figure) =>
        Luminance(figure) > 0.5f
            ? new MarkerTint(0.05f, 0.05f, 0.07f, HaloAlpha)
            : new MarkerTint(0.97f, 0.96f, 0.92f, HaloAlpha);

    /// <summary>The drop shadow's colour.</summary>
    public static MarkerTint Shadow { get; } = new(0f, 0f, 0f, ShadowAlpha);

    /// <summary>Relative luminance (sRGB weights, no gamma), 0 for black to 1 for white.</summary>
    public static float Luminance(MarkerTint c) => (0.2126f * c.Red) + (0.7152f * c.Green) + (0.0722f * c.Blue);

    private static readonly MarkerTint White = new(1f, 1f, 1f, 1f);
    private static readonly MarkerTint Black = new(0f, 0f, 0f, 1f);

    private static MarkerTint Mix(MarkerTint from, MarkerTint to, float amount) => new(
        from.Red + ((to.Red - from.Red) * amount),
        from.Green + ((to.Green - from.Green) * amount),
        from.Blue + ((to.Blue - from.Blue) * amount),
        1f);
}
