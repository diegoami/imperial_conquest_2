namespace IC2.Engine.MapGen.Styles;

/// <summary>
/// T294c: the India recipe — a broad fertile river plain, an off-angle mountain barrier, a drier
/// interior plateau, wooded pockets and coastal lowlands on a coastline that runs along three
/// sides. The barrier sits diagonally in the south-east rather than echoing a subcontinent
/// (docs/design/v011-campaigns-and-random-maps.md).
/// </summary>
public sealed class IndiaStyle : IMapStyle
{
    public string Id => "india";

    public GeneratedMap Compose(MapSeedStream rng, MapSize size, IReadOnlyDictionary<string, string> parameters) =>
        StyleComposer.Compose(rng, size, Plan);

    public static StylePlan Plan { get; } = new(
        Id: "india",
        Title: "India",
        Land:
        [
            new(0.20, 0.30, 0.22), new(0.45, 0.20, 0.22), new(0.70, 0.25, 0.22),
            new(0.85, 0.45, 0.20), new(0.72, 0.68, 0.22), new(0.48, 0.80, 0.22),
            new(0.24, 0.70, 0.22), new(0.15, 0.45, 0.20), new(0.42, 0.50, 0.22),
            new(0.64, 0.48, 0.20),
        ],
        Islands: [new(0.08, 0.88, 0.03), new(0.9, 0.10, 0.03)],
        Water: [],
        Ridges:
        [
            new(0.76, 0.92, 0.96, 0.36, 2),
            new(0.12, 0.14, 0.44, 0.10, 1),
        ],
        Rivers: 4,
        Dryness: 0.30,
        Woodiness: 0.28,
        Seats: 14,
        CoastalSeats: 7,
        TownsMin: 4,
        TownsMax: 5,
        Fleets: 5,
        Names: new NameBank(
            Nations:
            [
                "Avantika", "Malendra", "Tamarasa", "Kalyira", "Dandara", "Urjana", "Parnika",
                "Sindhura", "Vatsala", "Nehara", "Talajara", "Ambira", "Kuntala", "Rohanta",
                "Mahisha", "Trapura", "Ujjara", "Kaverata",
            ],
            CityPrefixes:
            ["Ava", "Mal", "Tam", "Kal", "Dan", "Ur", "Par", "Sin", "Vat", "Neh", "Tal", "Amb", "Kun", "Roh", "Tra", "Uj"],
            CitySuffixes:
            ["antika", "endra", "arasa", "yira", "dara", "jana", "ika", "dhura", "sala", "naha", "jara", "ira", "tala", "nta", "pura", "jjara"],
            Leaders:
            [
                "Rishan", "Vimal", "Antrik", "Ekaraj", "Jayadr", "Kirtan", "Manjun", "Praval",
                "Rajata", "Suran", "Taruns", "Udayar", "Vasvan", "Yaudha", "Indran", "Chandrav",
                "Harivan", "Somdutt",
            ]));
}
