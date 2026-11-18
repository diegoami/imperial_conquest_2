namespace IC2.Engine.MapGen.Styles;

/// <summary>
/// T294c: the Central/South Asia recipe — mountain belts across the north and the south-west, a
/// drier interior basin country between them, long river valleys and wooded foothill bands, with
/// passes keeping the pockets connected (docs/design/v011-campaigns-and-random-maps.md). A small
/// corner sea at the bottom-right is the only fleet water.
/// </summary>
public sealed class CentralAsiaStyle : IMapStyle
{
    public string Id => "central-asia";

    public GeneratedMap Compose(MapSeedStream rng, MapSize size, IReadOnlyDictionary<string, string> parameters) =>
        StyleComposer.Compose(rng, size, Plan);

    public static StylePlan Plan { get; } = new(
        Id: "central-asia",
        Title: "Central Asia",
        Land:
        [
            new(0.15, 0.20, 0.22), new(0.40, 0.15, 0.22), new(0.65, 0.20, 0.22),
            new(0.85, 0.30, 0.20), new(0.82, 0.58, 0.22), new(0.62, 0.80, 0.22),
            new(0.36, 0.76, 0.22), new(0.15, 0.50, 0.22), new(0.45, 0.48, 0.22),
            new(0.68, 0.42, 0.20),
        ],
        Islands: [],
        Water:
        [
            new(0.92, 0.90, 0.13), new(0.80, 0.96, 0.11),
        ],
        Ridges:
        [
            new(0.10, 0.26, 0.80, 0.14, 3),
            new(0.35, 0.92, 0.76, 0.70, 2),
        ],
        Rivers: 5,
        Dryness: 0.30,
        Woodiness: 0.20,
        Seats: 14,
        CoastalSeats: 2,
        TownsMin: 3,
        TownsMax: 5,
        Fleets: 1,
        Names: new NameBank(
            Nations:
            [
                "Karchal", "Tengir", "Ortagai", "Beshkul", "Saratal", "Uzunda", "Kalmir", "Argalyn",
                "Dzhamir", "Tepstan", "Urmand", "Zharken", "Ishtyk", "Kobdan", "Lapkhan", "Mirash",
                "Azhgar", "Kelteber",
            ],
            CityPrefixes:
            ["Ka", "Or", "Bes", "Sar", "Uz", "Kha", "Dzha", "Te", "Zh", "Ish", "Ko", "La", "Mi", "Az"],
            CitySuffixes:
            ["kent", "kul", "tash", "gai", "tal", "anda", "mir", "gan", "stan", "lyk", "dan", "ken", "tyk", "han"],
            Leaders:
            [
                "Altan", "Batur", "Chogan", "Dorji", "Ergal", "Temir", "Gilgar", "Khangal", "Orman",
                "Pervan", "Saryan", "Togan", "Uzgal", "Yanchar", "Zheren", "Ardzhu", "Kartan",
                "Selbek", "Tural", "Ombar",
            ]));
}
