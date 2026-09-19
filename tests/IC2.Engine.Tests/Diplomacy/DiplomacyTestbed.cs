using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Serialization;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;
using ProductionDiplomacy = IC2.Engine.Diplomacy;

namespace IC2.Engine.Tests.Diplomacy;

/// <summary>
/// The pieces a Diplomacy test needs. The shipped toy scenario has only two nations (north, south) —
/// enough for the cap and legality tests, not enough for DoD 4's contagion or DoD 9's column-bug tests,
/// both of which need at least four — so this builds its own small, fully-controlled multi-nation
/// <see cref="GameState"/> directly from the toy <see cref="Ruleset"/>'s own constants, the same pattern
/// <c>BattleTestbed</c> and <c>RecruitmentTestbed</c> use for their own fixtures.
/// </summary>
/// <remarks>
/// Every gameplay number a test asserts against comes from <see cref="Ruleset"/>'s own fields (the loaded
/// toy ruleset) or is computed in the test body from them — nothing here pastes in a ruleset constant as
/// a bare literal.
/// </remarks>
public static class DiplomacyTestbed
{
    private static readonly Lazy<ResolvedScenario> LazyToy = new(
        () => GameDataRepository.Load(ModelTestPaths.DataRoot).Resolve("toy-3city"));

    /// <summary>The toy scenario's world and ruleset, loaded once.</summary>
    public static ResolvedScenario Toy => LazyToy.Value;

    /// <summary>
    /// The shipped toy ruleset, set the way <c>classical-faithful</c> is set (including
    /// <see cref="RulesetFlags.FaithfulThawColumnBug"/> true).
    /// </summary>
    public static Ruleset Ruleset => Toy.Ruleset;

    /// <summary>Nation ids for a small, ordered four-nation world — enough for two-hop contagion.</summary>
    public static readonly string[] FourNationIds = { "rome", "greece", "bithynia", "media" };

    /// <summary>
    /// Nation ids for a nine-nation world, enough to exercise DoD 9's column-bug boundary at index 8
    /// (<see cref="DiplomacyRules.FaithfulThawColumnLimit"/> in the shipped toy ruleset).
    /// </summary>
    public static readonly string[] NineNationIds =
        { "n0", "n1", "n2", "n3", "n4", "n5", "n6", "n7", "n8" };

    /// <summary>
    /// One nation, with every field at a harmless default except the ones a test actually varies.
    /// </summary>
    public static NationState Nation(
        string id,
        string name,
        SeatControl control = SeatControl.Ai,
        int unity = 500,
        int wealth = 0,
        int taxBase = 0,
        int taxRatePercent = 25,
        bool eliminated = false) =>
        new(
            Id: id,
            Name: name,
            ColorHex: "#000000",
            LeaderName: $"{name} Leader",
            CapitalCityId: null,
            Control: control,
            Personality: control == SeatControl.Ai ? new AiPersonality(0.5, 0.5, 0.5) : null,
            Treasury: 0,
            Unity: unity,
            Wealth: wealth,
            TaxBase: taxBase,
            TaxRatePercent: taxRatePercent,
            MobilizedPercent: 0,
            Population: 0,
            PopulationAtStart: 0,
            TreasuryAtStart: 0,
            CityCountAtStart: 0,
            RecruitmentSlots: ValueList<RecruitmentSlot>.Empty,
            Eliminated: eliminated);

    /// <summary>
    /// A state over exactly <paramref name="nations"/>, all at peace with each other, one seat each in
    /// declaration order, no cities/armies/fleets and an empty news log.
    /// </summary>
    public static GameState StateOf(params NationState[] nations)
    {
        ArgumentNullException.ThrowIfNull(nations);

        var ids = ValueList.From(nations.Select(n => n.Id));
        var peace = Ruleset.Diplomacy.StateCodes.Peace;

        return new GameState(
            SchemaVersion: 1,
            WorldId: Toy.World.Id,
            RulesetId: Ruleset.Id,
            ScenarioId: Toy.Scenario.Id,
            Calendar: new CalendarState(Week: Ruleset.Calendar.StartWeek, SeasonIndex: 0, YearBc: 270, TurnIndex: 0),
            TurnOrder: ids,
            ActiveSeatIndex: 0,
            RandomSeed: 12345UL,
            Nations: ValueList.From(nations),
            Cities: ValueList<CityState>.Empty,
            Armies: ValueList<ArmyState>.Empty,
            Fleets: ValueList<FleetState>.Empty,
            MercenaryPool: ValueList<MercenaryPoolSlot>.Empty,
            Relations: DiplomaticRelations.Uniform(ids, peace),
            NewsLog: NewsLog.Empty,
            PendingOffer: null);
    }

    /// <summary>A dispatcher over a registry scanning the whole production engine assembly.</summary>
    public static CommandDispatcher Dispatcher(IEventSink? sink = null) =>
        new(
            SystemRegistry.FromAssemblies(typeof(ProductionDiplomacy.RelationTransitions).Assembly),
            Ruleset,
            Toy.World,
            sink ?? NullEventSink.Instance);

    /// <summary>A fresh deterministic RNG on an arbitrary fixed seed, for tests that need one directly.</summary>
    public static IRng Rng(ulong seed = 0x7A17UL) => new SplitMix64Rng(seed);

    /// <summary>One unit slot, for army-power fixtures.</summary>
    public static UnitSlot Unit(string unitTypeId, int troops, int quality = 5) =>
        new(MercenaryLabel: 0, unitTypeId, troops, quality, Name: $"{unitTypeId} unit");

    /// <summary>One army on the map, for <c>HonourablePeaceGate</c> fixtures.</summary>
    public static ArmyState Army(string id, string nation, int morale, params UnitSlot[] units) =>
        new(
            id,
            nation,
            X: 0,
            Y: 0,
            Moves: 0,
            morale,
            Money: 0,
            SupplyTons: 0,
            CoveredTileCode: 2,
            AboardFleetId: null,
            ValueList.From(units));
}
