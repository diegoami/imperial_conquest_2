namespace IC2.Engine.MapGen.Styles;

/// <summary>
/// T294c: the China recipe — several large river basins separated by wooded hill belts with
/// deliberate passes, broad plains between them, a rugged mountain frontier along the west edge,
/// and a coast with bays cut into the right edge (docs/design/v011-campaigns-and-random-maps.md).
/// </summary>
public sealed class ChinaStyle : IMapStyle
{
    public string Id => "china";

    public GeneratedMap Compose(MapSeedStream rng, MapSize size, IReadOnlyDictionary<string, string> parameters) =>
        StyleComposer.Compose(rng, size, Plan);

    public static StylePlan Plan { get; } = new(
        Id: "china",
        Title: "China",
        Land:
        [
            new(0.12, 0.25, 0.22), new(0.35, 0.15, 0.22), new(0.60, 0.18, 0.22),
            new(0.80, 0.28, 0.20), new(0.86, 0.52, 0.20), new(0.72, 0.78, 0.22),
            new(0.48, 0.86, 0.22), new(0.24, 0.76, 0.22), new(0.10, 0.45, 0.20),
            new(0.38, 0.50, 0.22), new(0.62, 0.50, 0.20),
        ],
        Islands: [new(0.94, 0.88, 0.035)],
        Water:
        [
            new(0.96, 0.34, 0.11), new(0.90, 0.50, 0.10), new(0.96, 0.64, 0.11), new(0.86, 0.76, 0.10),
        ],
        Ridges:
        [
            new(0.08, 0.90, 0.20, 0.12, 2),   // the rugged western frontier
            new(0.30, 0.52, 0.62, 0.44, 2),   // a hill belt separating the basins
            new(0.55, 0.28, 0.82, 0.42, 1),   // and another, shorter
        ],
        Rivers: 5,
        Dryness: 0.12,
        Woodiness: 0.35,
        Seats: 16,
        CoastalSeats: 5,
        TownsMin: 3,
        TownsMax: 4,
        Fleets: 4,
        Names: new NameBank(
            Nations:
            [
                "Beilan", "Changwu", "Donglin", "Huaizhou", "Linrao", "Minyan", "Ningtai", "Qianzhou",
                "Ruizhou", "Tailing", "Wuzhan", "Yunqi", "Zhaoyang", "Anxuan", "Beiheng", "Cangming",
                "Shuolai", "Tieyun",
            ],
            CityPrefixes:
            ["Jin", "Chang", "Luo", "Gui", "Qing", "Ping", "Yan", "Fu", "Hai", "Ning", "Teng", "Xin", "An", "Bao"],
            CitySuffixes:
            ["shan", "yang", "zhou", "rao", "tai", "ling", "qi", "an", "jiang", "cheng", "pu", "xi"],
            Leaders:
            [
                "Liangyu", "Hongzhan", "Shiuan", "Wenzao", "Kangze", "Yuanlang", "Zhaoming", "Fuyan",
                "Guanhao", "Jingru", "Shenmu", "Taizhong", "Xiangyu", "Yonglin", "Zhiran", "Baoshan",
                "Chengen", "Daowu",
            ]));
}
