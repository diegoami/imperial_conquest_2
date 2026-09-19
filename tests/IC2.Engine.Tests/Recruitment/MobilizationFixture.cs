using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Tests.Core;

namespace IC2.Engine.Tests.Recruitment;

/// <summary>
/// The pieces T55's command-level tests need on top of <see cref="RecruitmentTestbed"/>: a dispatcher
/// over a world of this task's own choosing, and the two edits every mobilization fixture makes —
/// giving a nation recruitment slots, and putting a city and an army at chosen coordinates.
/// </summary>
/// <remarks>
/// The toy world is 8×6, which cannot hold the corpus pair's coordinates (Rome at <c>(101, 43)</c>,
/// its army at <c>(100, 42)</c>, the overflow army at <c>(102, 44)</c>). Rather than add a world file,
/// <see cref="OpenWorld"/> takes the shipped toy world and widens it to open ground, keeping its tile
/// types — so the terrain codes an army may stand on are still the shipped ones, read from
/// <c>data/worlds/toy-3city.json</c>, and only the extent changes.
/// </remarks>
public static class MobilizationFixture
{
    /// <summary>The toy world's plain-land tile code — <c>2</c>, the first code an army may stand on.</summary>
    public static int PlainTileCode { get; } = FindPlainCode();

    /// <summary>A <paramref name="width"/>×<paramref name="height"/> world of open plain, with the toy world's tile types.</summary>
    public static World OpenWorld(int width, int height) =>
        CoreTestbed.Toy.World with
        {
            Width = width,
            Height = height,
            Terrain = new TerrainGrid(TerrainEncoding.RunLength, ValueList.Of(new TerrainRun(PlainTileCode, width * height))),
        };

    /// <summary>A dispatcher over the real engine assembly, against <paramref name="world"/>.</summary>
    public static CommandDispatcher DispatcherOn(World world, IEventSink? sink = null) =>
        new(SystemRegistry.FromEngineAssembly(), CoreTestbed.Toy.Ruleset, world, sink ?? NullEventSink.Instance);

    /// <summary>
    /// A dispatcher over the real engine assembly, against <paramref name="world"/> and
    /// <paramref name="ruleset"/> — for the seat-asymmetry variants.
    /// </summary>
    public static CommandDispatcher DispatcherOn(World world, Ruleset ruleset, IEventSink? sink = null) =>
        new(SystemRegistry.FromEngineAssembly(), ruleset, world, sink ?? NullEventSink.Instance);

    /// <summary>Returns <paramref name="state"/> with <paramref name="nationId"/>'s recruitment queue replaced.</summary>
    public static GameState WithSlots(GameState state, string nationId, params RecruitmentSlot[] slots)
    {
        var nation = state.NationById(nationId)!;
        return RecruitmentTestbed.WithNation(state, nation with { RecruitmentSlots = ValueList.Of(slots) });
    }

    /// <summary>One regular unit slot, named as the auto-namer would name it.</summary>
    public static UnitSlot Unit(string name, string unitTypeId = "light_infantry", int troops = 1_000, int quality = 6) =>
        new(MercenaryLabel: 0, UnitTypeId: unitTypeId, Troops: troops, Quality: quality, Name: name);

    /// <summary>An empty unit slot — the original's <c>troops == 0</c> hole inside an army record.</summary>
    public static UnitSlot EmptyUnitSlot() =>
        new(MercenaryLabel: 0, UnitTypeId: "light_infantry", Troops: 0, Quality: 0, Name: string.Empty);

    /// <summary>A city fixture at chosen coordinates, owned by <paramref name="owner"/> and with no garrison.</summary>
    public static CityState City(string id, int x, int y, string owner) =>
        new(id, id, x, y, owner, owner, Loyalty: 100, SupplyTons: 0, FortificationCode: 0,
            PopulationThousands: 10, MaxPopulationThousands: 20, Tribute: 0, UnderSiege: false,
            Garrison: ValueList<UnitSlot>.Empty);

    /// <summary>An army fixture at chosen coordinates.</summary>
    public static ArmyState Army(
        string id,
        string nation,
        int x,
        int y,
        IEnumerable<UnitSlot> units,
        int moves = 5,
        int morale = 70,
        int money = 0,
        int supplyTons = 0) =>
        new(id, nation, x, y, moves, morale, money, supplyTons,
            CoveredTileCode: PlainTileCode, AboardFleetId: null, Units: ValueList.From(units));

    private static int FindPlainCode()
    {
        foreach (var tileType in CoreTestbed.Toy.World.TileTypes)
        {
            if (tileType.PassableByArmies)
            {
                return tileType.Code;
            }
        }

        throw new InvalidOperationException("The toy world defines no tile an army may stand on.");
    }
}
