namespace IC2.Engine.MapGen.Styles;

/// <summary>
/// T294c: the Mediterranean recipe — one great irregular landmass cut by two deep gulfs from the
/// west and east edges and by northern and southern bays, with islands and peninsulas narrowing
/// the waters into straits. The gulfs share the connected edge sea, so every port reaches every
/// shore, while armies cross the solid centre at near-direct cost.
/// </summary>
public sealed class MediterraneanStyle : IMapStyle
{
    public string Id => "mediterranean";

    public GeneratedMap Compose(MapSeedStream rng, MapSize size, IReadOnlyDictionary<string, string> parameters) =>
        StyleComposer.Compose(rng, size, Plan);

    public static StylePlan Plan { get; } = new(
        Id: "mediterranean",
        Title: "Mediterranean",
        Land:
        [
            new(0.10, 0.25, 0.20), new(0.28, 0.16, 0.21), new(0.46, 0.22, 0.20),
            new(0.64, 0.15, 0.21), new(0.82, 0.24, 0.20), new(0.90, 0.42, 0.18),
            new(0.88, 0.62, 0.18), new(0.80, 0.80, 0.20), new(0.62, 0.86, 0.20),
            new(0.42, 0.84, 0.20), new(0.22, 0.78, 0.20), new(0.10, 0.55, 0.18),
            new(0.30, 0.40, 0.18), new(0.50, 0.50, 0.16), new(0.68, 0.40, 0.16),
            new(0.70, 0.66, 0.16), new(0.45, 0.68, 0.16), new(0.55, 0.30, 0.15),
            new(0.35, 0.60, 0.15),
        ],
        Islands:
        [
            new(0.10, 0.47, 0.03), new(0.22, 0.50, 0.03), new(0.88, 0.46, 0.03),
            new(0.76, 0.50, 0.03), new(0.47, 0.05, 0.02), new(0.52, 0.95, 0.02),
        ],
        Water:
        [
            new(0.02, 0.50, 0.16), new(0.14, 0.42, 0.12), new(0.20, 0.55, 0.11), new(0.28, 0.46, 0.09),
            new(0.98, 0.48, 0.15), new(0.86, 0.40, 0.12), new(0.80, 0.54, 0.11), new(0.72, 0.46, 0.09),
            new(0.44, 0.03, 0.10), new(0.52, 0.06, 0.08),
            new(0.50, 0.97, 0.10), new(0.56, 0.94, 0.08),
        ],
        Ridges:
        [
            new(0.14, 0.10, 0.40, 0.18, 2),
            new(0.62, 0.86, 0.90, 0.70, 2),
        ],
        Rivers: 4,
        Dryness: 0.20,
        Woodiness: 0.25,
        Seats: 13,
        CoastalSeats: 9,
        TownsMin: 3,
        TownsMax: 5,
        Fleets: 8,
        Names: new NameBank(
            Nations:
            [
                "Meridia", "Corvana", "Thalassia", "Ostria", "Seloria", "Varencia", "Calissa", "Norica",
                "Ithara", "Pelagia", "Torvia", "Essaria", "Ligessa", "Arvenia", "Casuria", "Meloria",
                "Ophira", "Tesseria",
            ],
            CityPrefixes:
            ["Al", "Bar", "Cor", "Del", "Far", "Gal", "Mar", "Nor", "Pel", "Tor", "Val", "Ser", "Cal", "Ost", "Ven", "Tar", "Mes", "Ral"],
            CitySuffixes:
            ["adia", "essa", "oria", "inda", "ona", "ara", "ium", "ena", "issa", "anto", "ura", "elia", "asto", "ovia"],
            Leaders:
            [
                "Athar", "Belor", "Cador", "Darion", "Emun", "Feras", "Gilar", "Haron", "Joran",
                "Kaleth", "Lumor", "Miron", "Noran", "Olfar", "Peran", "Quirin", "Rastan", "Sedor",
                "Tavor", "Ulmar", "Voran", "Yoran", "Zarel", "Ondar",
            ]));
}
