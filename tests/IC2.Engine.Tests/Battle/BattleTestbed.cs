using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Tests.Core;

namespace IC2.Engine.Tests.Battle;

/// <summary>
/// The pieces a Battle test needs: the shipped toy scenario (reused from <see cref="CoreTestbed"/> so
/// this task's tests agree with every other task's on the same world and ruleset), the two
/// <c>combat.onDefeat</c> settings as two rulesets, and small builders for the armies, fleets, nations
/// and cities a battle fixture is made of.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Every gameplay number here comes from the loaded <see cref="Model.Ruleset"/> or from the T04
/// fixtures corpus.</strong> What this file does write as literals is fixture <em>data</em> — troop
/// counts, coordinates, morale values, ids — which is the scenario being tested, not a rule. The
/// expected values the tests assert are computed from ruleset fields in the test bodies, never pasted in.
/// </para>
/// <para>
/// <strong>The fixed seed.</strong> <see cref="Seed"/> is the one seed every Done-when check runs under.
/// It is an arbitrary constant with no meaning beyond reproducibility, exactly like the toy scenario's
/// own <c>randomSeed</c>. <see cref="BattleRng"/> hands out a fresh <see cref="SplitMix64Rng"/> on it, so
/// two calls in one test produce two identical draw sequences — which is what
/// <c>BattleDeterminismTests</c> asserts and what lets a test resolve the same battle twice under two
/// rulesets and compare.
/// </para>
/// </remarks>
public static class BattleTestbed
{
    /// <summary>
    /// The fixed seed every Done-when check runs under. Arbitrary, and carrying no meaning beyond
    /// reproducibility — but not picked blind: it was chosen (by a scan kept in this file's history) so
    /// that its draw sequence exercises both sides of every seeded rule rather than one side of each.
    /// For a three-slot winner in a scattered field battle it draws, in order:
    /// <list type="bullet">
    /// <item><description>
    /// three casualty divisors, <c>random(15) = 13, 5, 11</c> — three <em>different</em> divisors (118,
    /// 110, 116), so a per-unit expression cannot be mistaken for one shared multiplier;
    /// </description></item>
    /// <item><description>
    /// three promotion rolls, <c>random(4) = 2, 0, 1</c> — exactly the middle unit wins the further
    /// promotion and the other two do not;
    /// </description></item>
    /// <item><description>the peace roll, <c>random(5) = 1</c>, which passes the <c>&lt; 2</c> gate;</description></item>
    /// <item><description>
    /// two more casualty divisors for the loser, <c>random(15) = 6, 5</c> (111 and 110);
    /// </description></item>
    /// <item><description>
    /// the scatter distance, <c>3</c> from <c>[2, 5)</c> — the middle of the range rather than an endpoint,
    /// and the one distance whose ideal tile is blocked by a city, so the ring search is exercised too.
    /// </description></item>
    /// </list>
    /// Its first two naval bands also differ (<c>3</c> then <c>1</c>).
    /// </summary>
    public const ulong Seed = 0x183UL;

    /// <summary>The toy scenario's world.</summary>
    public static World World => CoreTestbed.Toy.World;

    /// <summary>
    /// The shipped toy ruleset, which is set the way <c>classical-faithful</c> is set — including
    /// <c>combat.onDefeat: "destroyed"</c>.
    /// </summary>
    public static Ruleset Destroyed => CoreTestbed.Toy.Ruleset;

    /// <summary>
    /// The same ruleset with <see cref="RulesetFlags.CombatOnDefeat"/> flipped to
    /// <see cref="DefeatOutcome.Scatter"/> — the <c>improved</c> preset's one relevant difference.
    /// Every other number is identical, which is what makes "unaffected by <c>combat.onDefeat</c>"
    /// assertable by resolving the same fixture under both.
    /// </summary>
    public static Ruleset Scatter { get; } =
        CoreTestbed.Toy.Ruleset with
        {
            Flags = CoreTestbed.Toy.Ruleset.Flags with { CombatOnDefeat = DefeatOutcome.Scatter },
        };

    /// <summary>A generator on <see cref="Seed"/>. A fresh one each call, so two calls agree draw for draw.</summary>
    public static IRng BattleRng() => new SplitMix64Rng(Seed);

    /// <summary>The toy scenario's starting state, used only for its nation records.</summary>
    public static GameState Initial() => CoreTestbed.InitialState();

    /// <summary>One army on the map.</summary>
    public static ArmyState Army(
        string id,
        string nation,
        int x,
        int y,
        int morale,
        int money,
        int supplyTons,
        params UnitSlot[] units) =>
        new(
            id,
            nation,
            x,
            y,
            Moves: 5,
            morale,
            money,
            supplyTons,
            // Any land code will do: nothing under test reads it, and an army on the map must carry one
            // (GameDataValidation pairs a null covered tile with being aboard a fleet).
            CoveredTileCode: 2,
            AboardFleetId: null,
            ValueList.From(units));

    /// <summary>One unit slot. <paramref name="quality"/> defaults below the ruleset's floor on purpose.</summary>
    public static UnitSlot Unit(string unitTypeId, int troops, int quality, string name) =>
        new(MercenaryLabel: 0, unitTypeId, troops, quality, name);

    /// <summary>One launched fleet.</summary>
    public static FleetState Fleet(
        string id,
        string nation,
        int x,
        int y,
        int ships,
        int conditionPercent,
        string? carriedArmyId = null) =>
        new(
            id,
            nation,
            x,
            y,
            Moves: 4,
            ships,
            conditionPercent,
            Money: 0,
            SupplyTons: 50,
            ConstructionTicksRemaining: null,
            BuildCityId: null,
            carriedArmyId,
            CoveredTileCode: 0);

    /// <summary>An army aboard <paramref name="fleetId"/>: off the map, so no covered tile.</summary>
    public static ArmyState EmbarkedArmy(
        string id,
        string nation,
        string fleetId,
        int x,
        int y,
        int morale,
        params UnitSlot[] units) =>
        new(
            id,
            nation,
            x,
            y,
            Moves: 0,
            morale,
            Money: 0,
            SupplyTons: 0,
            CoveredTileCode: null,
            fleetId,
            ValueList.From(units));

    /// <summary>A state built from the toy scenario's nations, with this fixture's own units and cities.</summary>
    public static GameState StateWith(
        IEnumerable<ArmyState>? armies = null,
        IEnumerable<FleetState>? fleets = null,
        IEnumerable<CityState>? cities = null,
        IEnumerable<NationState>? nations = null)
    {
        var initial = Initial();
        return initial with
        {
            Armies = ValueList.From(armies ?? Array.Empty<ArmyState>()),
            Fleets = ValueList.From(fleets ?? Array.Empty<FleetState>()),
            Cities = ValueList.From(cities ?? initial.Cities),
            Nations = ValueList.From(nations ?? initial.Nations),
        };
    }

    /// <summary>The toy state's nation record with <paramref name="unity"/> substituted.</summary>
    public static NationState NationWithUnity(GameState state, string nationId, int unity) =>
        state.NationById(nationId)! with { Unity = unity };

    /// <summary>
    /// <paramref name="count"/> throwaway cities owned by <paramref name="nationId"/>, laid out off the
    /// battle's own tiles, for the peace-treaty gate's city-count threshold.
    /// </summary>
    public static IEnumerable<CityState> FillerCities(string nationId, int count, int startX, int y)
    {
        for (var i = 0; i < count; i++)
        {
            yield return new CityState(
                $"filler-{nationId}-{i}",
                $"Filler {i}",
                startX + i,
                y,
                nationId,
                nationId,
                Loyalty: 50,
                SupplyTons: 0,
                FortificationCode: 0,
                PopulationThousands: 10,
                MaxPopulationThousands: 20,
                Tribute: 0,
                UnderSiege: false,
                ValueList<UnitSlot>.Empty);
        }
    }

    /// <summary>
    /// A square world of nothing but sea, with land at exactly the named tiles — the scripted
    /// fully-boxed-in fixture DoD 11 asks for. Put the loser on one land tile and the winner on the only
    /// other one and every ring around the loser is water, another unit, or off the map, so no scatter
    /// distance can find anywhere to put a survivor and the outcome has to fall back to destroyed.
    /// </summary>
    /// <param name="size">The map's width and height.</param>
    /// <param name="land">The tiles that are land. Every other tile is deep sea.</param>
    public static World IslandWorld(int size, params (int X, int Y)[] land)
    {
        ArgumentNullException.ThrowIfNull(land);

        var sea = World.TileTypes.FindById(t => t.Id, "sea_deep")!;
        var plain = World.TileTypes.FindById(t => t.Id, "plain")!;

        var codes = new int[size * size];
        Array.Fill(codes, sea.Code);
        foreach (var (x, y) in land)
        {
            codes[(y * size) + x] = plain.Code;
        }

        var runs = new List<TerrainRun>();
        foreach (var code in codes)
        {
            if (runs.Count > 0 && runs[^1].Code == code)
            {
                runs[^1] = runs[^1] with { Count = runs[^1].Count + 1 };
                continue;
            }

            runs.Add(new TerrainRun(code, 1));
        }

        return World with
        {
            Width = size,
            Height = size,
            Terrain = new TerrainGrid(TerrainEncoding.RunLength, ValueList.From(runs)),
        };
    }
}
