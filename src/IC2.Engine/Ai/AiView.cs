using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Naval;
using IC2.Engine.Strength;

namespace IC2.Engine.Ai;

/// <summary>
/// The reads every phase shares: whose units are whose, who is at war with whom, how strong a nation is,
/// and how close it is to winning — each one a call into already-merged engine code, never a second copy
/// of a rule.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Ordering is the whole point of this type.</strong> Every list it returns is built by walking a
/// <see cref="ValueList{T}"/> from <see cref="GameState"/> front to back, which is the one stable order
/// the model guarantees (<c>ValueList</c> exists precisely so that <c>serialize → deserialize</c> gives
/// an equal tree). Nothing here builds a <c>Dictionary</c>, a <c>HashSet</c> or a <c>GroupBy</c>: an AI
/// that enumerated one would be irreproducible in the way that shows up as one flaky seed in fifty, and
/// <c>tests/IC2.Engine.Tests/Core/Determinism</c>'s scanner fails the build on the declaration.
/// </para>
/// <para>
/// <strong>The terrain grid is decoded once per turn and carried here.</strong>
/// <see cref="LandingTile.IsPassableForArmy"/> decodes the whole world on every call, which is correct
/// but is called once per candidate per cell; this type holds one decode for the life of a turn and
/// answers from it. The decode itself is <see cref="TerrainGrid.Decode"/>, the engine's own.
/// </para>
/// </remarks>
public sealed class AiView
{
    private readonly int[] _terrainCells;

    /// <summary>Builds a view over one state.</summary>
    /// <param name="state">The state being decided against.</param>
    /// <param name="ruleset">The loaded ruleset.</param>
    /// <param name="world">The loaded world.</param>
    /// <param name="nationId">The AI nation whose turn it is.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="nationId"/> is not a nation in <paramref name="state"/>.</exception>
    public AiView(GameState state, Ruleset ruleset, World world, string nationId)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ruleset);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentException.ThrowIfNullOrWhiteSpace(nationId);

        State = state;
        Ruleset = ruleset;
        World = world;
        NationId = nationId;
        Nation = state.NationById(nationId)
                 ?? throw new ArgumentException($"'{nationId}' is not a nation in this game.", nameof(nationId));
        _terrainCells = world.Terrain.Decode(world.Width, world.Height);
    }

    /// <summary>The state being decided against.</summary>
    public GameState State { get; }

    /// <summary>The loaded ruleset.</summary>
    public Ruleset Ruleset { get; }

    /// <summary>The loaded world.</summary>
    public World World { get; }

    /// <summary>The acting nation's id.</summary>
    public string NationId { get; }

    /// <summary>The acting nation's live state.</summary>
    public NationState Nation { get; }

    /// <summary>The ruleset's code for the war relation.</summary>
    public int WarCode => Ruleset.Diplomacy.StateCodes.War;

    /// <summary>The ruleset's code for the peace relation.</summary>
    public int PeaceCode => Ruleset.Diplomacy.StateCodes.Peace;

    /// <summary>Whether two nations are at war, answered safely for a nation outside the relation matrix.</summary>
    /// <remarks>
    /// <see cref="DiplomaticRelations.Get"/> throws for an unknown nation, which a candidate generator
    /// must never do, so the pair is resolved through <see cref="DiplomaticRelations.IndexOf"/> first —
    /// the same guard <see cref="Battle.Commands.AttackLegality"/>'s own war check uses, for the same
    /// reason.
    /// </remarks>
    public bool IsAtWar(string a, string b) => RelationBetween(a, b) == WarCode;

    /// <summary>The relation value between two nations, or <see langword="null"/> if either is not in the matrix.</summary>
    public int? RelationBetween(string a, string b)
    {
        var relations = State.Relations;
        if (relations.IndexOf(a) < 0 || relations.IndexOf(b) < 0)
        {
            return null;
        }

        return relations.Get(a, b);
    }

    /// <summary>
    /// Whether a city is "non-hostile" to the acting nation for supply purposes: its own city, or a city
    /// whose owner it is not at war with.
    /// </summary>
    /// <remarks>
    /// The same reading the merged supply gates use — <c>BuySupplyCommandHandler</c> refuses a foreign
    /// city only when <c>Relations.Get(buyer, cityOwner) == war</c>, and calls everything else
    /// non-hostile. <c>supply-capacity-rounding.md</c> uses the same word for the AI pass this feeds.
    /// </remarks>
    public bool IsNonHostileCity(CityState city, string unitNationId)
    {
        ArgumentNullException.ThrowIfNull(city);

        if (string.Equals(city.Owner, unitNationId, StringComparison.Ordinal))
        {
            return true;
        }

        return RelationBetween(unitNationId, city.Owner) != WarCode;
    }

    /// <summary>Every army the acting nation owns, in <see cref="GameState.Armies"/> order.</summary>
    public List<ArmyState> OwnArmies() => ArmiesOf(NationId);

    /// <summary>Every army a nation owns, in <see cref="GameState.Armies"/> order.</summary>
    public List<ArmyState> ArmiesOf(string nationId)
    {
        var armies = new List<ArmyState>();
        foreach (var army in State.Armies)
        {
            if (string.Equals(army.Nation, nationId, StringComparison.Ordinal))
            {
                armies.Add(army);
            }
        }

        return armies;
    }

    /// <summary>Every fleet the acting nation owns, in <see cref="GameState.Fleets"/> order.</summary>
    public List<FleetState> OwnFleets()
    {
        var fleets = new List<FleetState>();
        foreach (var fleet in State.Fleets)
        {
            if (string.Equals(fleet.Nation, NationId, StringComparison.Ordinal))
            {
                fleets.Add(fleet);
            }
        }

        return fleets;
    }

    /// <summary>Every city the acting nation owns, in <see cref="GameState.Cities"/> order.</summary>
    public List<CityState> OwnCities()
    {
        var cities = new List<CityState>();
        foreach (var city in State.Cities)
        {
            if (string.Equals(city.Owner, NationId, StringComparison.Ordinal))
            {
                cities.Add(city);
            }
        }

        return cities;
    }

    /// <summary>Every nation other than the acting one that is still in the game, in turn-order-table order.</summary>
    public List<NationState> OtherLivingNations()
    {
        var nations = new List<NationState>();
        foreach (var nation in State.Nations)
        {
            if (!nation.Eliminated && !string.Equals(nation.Id, NationId, StringComparison.Ordinal))
            {
                nations.Add(nation);
            }
        }

        return nations;
    }

    /// <summary>
    /// A nation's total field-battle strength, summed over its armies with
    /// <see cref="ArmyPower.Compute"/> — the same function
    /// <see cref="Diplomacy.HonourablePeaceGate"/> sums for the confirmed honourable-peace test, so the
    /// AI's idea of "who is stronger" is the engine's.
    /// </summary>
    public long TotalArmyPower(string nationId)
    {
        long total = 0;
        foreach (var army in State.Armies)
        {
            if (string.Equals(army.Nation, nationId, StringComparison.Ordinal))
            {
                total += ArmyPower.Compute(army.Units, army.Morale, Ruleset);
            }
        }

        return total;
    }

    /// <summary>
    /// How far along the acting nation is toward the shipped victory condition, in permille: the share of
    /// the map's cities it owns. <c>GameState.CountCitiesOwnedBy</c> is the engine's own count, the one
    /// <c>VictoryEvaluator.EvaluateTotalConquest</c> compares against <c>Cities.Count</c>.
    /// </summary>
    /// <remarks>
    /// Deliberately keyed to city share rather than to the loaded <see cref="VictoryConditionType"/>. All
    /// four shipped conditions are decided wholly or partly by cities held
    /// (<c>totalConquest</c> and <c>domination</c> outright, <c>scoreAtTurnLimit</c> as its largest term,
    /// and a <c>custom</c> goal is scenario text this engine cannot read a target out of), and no
    /// <see cref="Scenario"/> reaches a running system anyway — <see cref="Victory.VictoryCheckSystem"/>
    /// says so in its own remarks, and widening that seam is a <c>Core/Pipeline/**</c> change outside this
    /// task's Owns list. So the awareness term is city share, stated here rather than implied.
    /// </remarks>
    public long VictoryProgressPermille()
    {
        var total = State.Cities.Count;
        if (total <= 0)
        {
            return 0;
        }

        return (long)State.CountCitiesOwnedBy(NationId) * AiWeights.PermilleScale / total;
    }

    /// <summary>Whether a hostile army stands next to a city — <see cref="Economy.HostileArmyAdjacent.IsThreatened"/>.</summary>
    public bool IsThreatened(CityState city) => Economy.HostileArmyAdjacent.IsThreatened(city, State, Ruleset);

    /// <summary>Whether an army may stand on a tile, answered from this turn's single terrain decode.</summary>
    public bool IsArmyPassable(GridPoint point)
    {
        if ((uint)point.X >= (uint)World.Width || (uint)point.Y >= (uint)World.Height)
        {
            return false;
        }

        var code = _terrainCells[(point.Y * World.Width) + point.X];
        return World.TileTypeByCode(code)?.PassableByArmies == true;
    }

    /// <summary>
    /// Whether a fleet may enter a tile, on <see cref="Naval.Commands.MoveFleetCommandHandler"/>'s own
    /// terms: a sea tile, or any city cell (the naval handler's blocked predicate exempts a city cell
    /// from the passability test, so a fleet may moor at a port that stands on land).
    /// </summary>
    public bool IsFleetPassable(GridPoint point)
    {
        if ((uint)point.X >= (uint)World.Width || (uint)point.Y >= (uint)World.Height)
        {
            return false;
        }

        foreach (var city in State.Cities)
        {
            if (city.X == point.X && city.Y == point.Y)
            {
                return true;
            }
        }

        var code = _terrainCells[(point.Y * World.Width) + point.X];
        return World.TileTypeByCode(code)?.PassableByFleets == true;
    }

    /// <summary>Chebyshev distance — <see cref="LandingTile.ChebyshevDistance"/>, the engine's shared metric.</summary>
    public static int Distance(int ax, int ay, int bx, int by) =>
        LandingTile.ChebyshevDistance(new GridPoint(ax, ay), new GridPoint(bx, by));

    /// <summary>
    /// <paramref name="numerator"/> as a permille fraction of <paramref name="denominator"/>, saturating
    /// rather than dividing by zero. A zero-strength defender gives
    /// <see cref="AiWeights.RequiredAttackRatioAtZeroAggressionPermille"/> outright — "infinitely
    /// favourable", expressed as the largest ratio any gate ever asks for, so that no arithmetic
    /// downstream has to special-case infinity.
    /// </summary>
    public static long RatioPermille(long numerator, long denominator)
    {
        if (denominator <= 0)
        {
            return numerator <= 0
                ? 0
                : Math.Max(
                    AiWeights.RequiredAttackRatioAtZeroAggressionPermille,
                    AiWeights.MaxRatioScoreContribution);
        }

        return numerator * AiWeights.PermilleScale / denominator;
    }

    /// <summary>
    /// The strength ratio an attack must show before this personality will place it: a straight line from
    /// <see cref="AiWeights.RequiredAttackRatioAtZeroAggressionPermille"/> at <c>aggression = 0</c> down to
    /// <see cref="AiWeights.RequiredAttackRatioAtFullAggressionPermille"/> at <c>aggression = 1</c>.
    /// </summary>
    public static long RequiredAttackRatioPermille(int aggressionPermille)
    {
        var span = AiWeights.RequiredAttackRatioAtZeroAggressionPermille
                   - AiWeights.RequiredAttackRatioAtFullAggressionPermille;
        return AiWeights.RequiredAttackRatioAtZeroAggressionPermille
               - (span * aggressionPermille / AiWeights.PermilleScale);
    }

    /// <summary>How much of a strength ratio is allowed into a score — see <see cref="AiWeights.MaxRatioScoreContribution"/>.</summary>
    public static long RatioScoreContribution(long ratioPermille) =>
        Math.Clamp(ratioPermille, 0, AiWeights.MaxRatioScoreContribution);

    /// <summary>
    /// <c>docs/game-design.md</c> §AI phase 4, applied: a score is multiplied by
    /// <c>(1000 + VictoryProgressWeight · progress) / 1000</c>.
    /// </summary>
    public static long WithVictoryAwareness(long score, long progressPermille) =>
        score
        * (AiWeights.PermilleScale + (AiWeights.VictoryProgressWeight * progressPermille / AiWeights.PermilleScale))
        / AiWeights.PermilleScale;
}
