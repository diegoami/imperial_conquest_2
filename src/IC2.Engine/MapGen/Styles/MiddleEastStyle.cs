namespace IC2.Engine.MapGen.Styles;

/// <summary>
/// T294c: the Middle East recipe — a broad dry interior with a few fertile river corridors,
/// northern and southern uplands with usable plain passes, and a gulf cutting in from the
/// bottom-right corner as the style's coastal edge (docs/design/v011-campaigns-and-random-maps.md).
/// </summary>
public sealed class MiddleEastStyle : IMapStyle
{
    public string Id => "middle-east";

    public GeneratedMap Compose(MapSeedStream rng, MapSize size, IReadOnlyDictionary<string, string> parameters) =>
        StyleComposer.Compose(rng, size, Plan);

    public static StylePlan Plan { get; } = new(
        Id: "middle-east",
        Title: "Middle East",
        Land:
        [
            new(0.20, 0.30, 0.22), new(0.45, 0.25, 0.22), new(0.70, 0.28, 0.22),
            new(0.86, 0.42, 0.20), new(0.72, 0.62, 0.22), new(0.48, 0.72, 0.22),
            new(0.24, 0.68, 0.22), new(0.32, 0.46, 0.22), new(0.58, 0.46, 0.22),
        ],
        Islands: [],
        Water:
        [
            new(0.86, 0.94, 0.13), new(0.94, 0.74, 0.12), new(0.80, 0.82, 0.10),
        ],
        Ridges:
        [
            new(0.14, 0.20, 0.42, 0.34, 2),
            new(0.55, 0.88, 0.84, 0.62, 2),
        ],
        Rivers: 3,
        Dryness: 0.45,
        Woodiness: 0.12,
        Seats: 12,
        CoastalSeats: 3,
        TownsMin: 4,
        TownsMax: 5,
        Fleets: 2,
        ForceWeights: [4, 3, 3, 2, 1], // infantry and bow with a slower mounted arm
        Names: StyleNames.MiddleEast);
}
