using IC2.Engine.Core;
using IC2.Engine.Economy;
using IC2.Engine.Economy.Commands;
using IC2.Engine.Model;
using IC2.Engine.Serialization;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Economy;

/// <summary>
/// T134 Done-when 1 and 2: <see cref="SupplyProviders.ForArmy"/> lists exactly the entities
/// <c>TAFSupply_FindProviders</c> offers an army, and it agrees with
/// <see cref="BuySupplyCommandHandler"/> entity by entity — a listed provider is accepted by the
/// dispatcher and an unlisted one is refused with its own rejection code.
/// </summary>
/// <remarks>
/// <strong>A scripted classical state.</strong> The world, ruleset and scenario are the shipped
/// <c>classical-mediterranean</c>; only the armies, cities and fleets are scripted, so one army sits at
/// (100,100) surrounded by exactly one of each candidate the query has a rule for, at a chosen Chebyshev
/// distance. The foreign cities are owned by two different nations — <c>carthage</c> at peace,
/// <c>gaul</c> at war — because a single foreign nation cannot be both at once. Every number the handler
/// and the query read (the adjacency radius, the supply rate, the war code) comes from the ruleset.
/// </remarks>
public sealed class SupplyProvidersTests
{
    private const string ArmyId = "t134-army";
    private const string BuyerNationId = "rome";
    private const string PeaceNationId = "carthage";
    private const string WarNationId = "gaul";

    private const string OwnCityId = "t134-own-city";
    private const string PeaceCityId = "t134-peace-city";
    private const string WarCityId = "t134-war-city";
    private const string FarOwnCityId = "t134-far-own-city";
    private const string OwnFleetId = "t134-own-fleet";
    private const string UnderConstructionFleetId = "t134-uc-fleet";
    private const string ForeignFleetId = "t134-foreign-fleet";

    private static readonly Lazy<ResolvedScenario> LazyClassical = new(
        () => GameDataRepository.Load(ModelTestPaths.DataRoot).Resolve("classical-mediterranean"));

    private static ResolvedScenario Classical => LazyClassical.Value;

    private static Ruleset Ruleset => Classical.Ruleset;

    /// <summary>The buying army's own room: 10,000 troops / 100 + the dialog bonus 1 = 101 tons.</summary>
    private static GameState ScriptedState()
    {
        var initial = GameStateFactory.CreateInitial(Classical.World, Classical.Ruleset, Classical.Scenario);

        var army = new ArmyState(
            ArmyId, BuyerNationId, X: 100, Y: 100, Moves: 4, Morale: 70, Money: 100, SupplyTons: 0,
            CoveredTileCode: null, AboardFleetId: null,
            Units: ValueList.Of(new UnitSlot(0, "heavy_infantry", 10_000, 6, "T134 Test Battalion")));

        var cities = ValueList.Of(new[]
        {
            City(OwnCityId, X: 101, Y: 100, Owner: BuyerNationId),           // distance 1, own -> free
            City(PeaceCityId, X: 100, Y: 101, Owner: PeaceNationId),         // distance 1, foreign, peace -> paid
            City(WarCityId, X: 101, Y: 101, Owner: WarNationId),             // distance 1, foreign, war -> out
            City(FarOwnCityId, X: 102, Y: 100, Owner: BuyerNationId),        // distance 2, own -> out
        });

        var fleets = ValueList.Of(new[]
        {
            Fleet(OwnFleetId, BuyerNationId, X: 99, Y: 100, underConstruction: false),  // distance 1, own -> free
            Fleet(UnderConstructionFleetId, BuyerNationId, X: 100, Y: 99, underConstruction: true), // d1, building -> out
            Fleet(ForeignFleetId, PeaceNationId, X: 101, Y: 99, underConstruction: false), // distance 1, foreign -> out
        });

        return initial with
        {
            Armies = ValueList.Of(new[] { army }),
            Cities = cities,
            Fleets = fleets,
            Relations = initial.Relations.WithRelation(BuyerNationId, WarNationId, Classical.Ruleset.Diplomacy.StateCodes.War),
        };
    }

    private static CityState City(string id, int X, int Y, string Owner) => new(
        id, id, X, Y, Owner, Owner, Loyalty: 90, SupplyTons: 100, FortificationCode: 0,
        PopulationThousands: 100, MaxPopulationThousands: 100, Tribute: 0, UnderSiege: false,
        Garrison: ValueList<UnitSlot>.Empty);

    private static FleetState Fleet(string id, string nation, int X, int Y, bool underConstruction) => new(
        id, nation, X, Y, Moves: 4, Ships: 10, ConditionPercent: 100, Money: 0, SupplyTons: 100,
        ConstructionTicksRemaining: underConstruction ? 3 : null, BuildCityId: null, CarriedArmyId: null,
        CoveredTileCode: null);

    private static CommandDispatcher Dispatcher() => new(
        SystemRegistry.FromEngineAssembly(), Ruleset, Classical.World, NullEventSink.Instance);

    private static IReadOnlyList<SupplyProvider> Query(GameState state) =>
        SupplyProviders.ForArmy(state, ArmyId, Ruleset);

    private static bool Lists(IReadOnlyList<SupplyProvider> providers, string id) =>
        providers.Any(p => string.Equals(p.Id, id, StringComparison.Ordinal));

    /// <summary>Dispatches one 10-ton purchase naming <paramref name="id"/> as a city or a fleet.</summary>
    private static CommandResult Buy(GameState state, string id, bool asFleet) => Dispatcher().Dispatch(
        state,
        asFleet
            ? new BuySupplyCommand(BuyerNationId, ArmyId, CityId: null, Tons: 10, ProviderFleetId: id)
            : new BuySupplyCommand(BuyerNationId, ArmyId, id, Tons: 10));

    /// <summary>Every candidate, its id and whether the dialog's own fleet form names it.</summary>
    public static IEnumerable<object[]> Candidates()
    {
        yield return new object[] { OwnCityId, false };
        yield return new object[] { PeaceCityId, false };
        yield return new object[] { WarCityId, false };
        yield return new object[] { FarOwnCityId, false };
        yield return new object[] { OwnFleetId, true };
        yield return new object[] { UnderConstructionFleetId, true };
        yield return new object[] { ForeignFleetId, true };
    }

    /// <summary>
    /// Done-when 1: the query returns exactly the own city, the peaceful foreign city and the own fleet,
    /// in that order, marked free, paid and free.
    /// </summary>
    [Fact]
    public void The_query_lists_the_own_city_the_peaceful_foreign_city_and_the_own_fleet_in_that_order()
    {
        var providers = Query(ScriptedState());

        Assert.Equal(
            new[]
            {
                new SupplyProvider(SupplyProviderKind.City, OwnCityId, IsFree: true),
                new SupplyProvider(SupplyProviderKind.City, PeaceCityId, IsFree: false),
                new SupplyProvider(SupplyProviderKind.Fleet, OwnFleetId, IsFree: true),
            },
            providers);
    }

    /// <summary>
    /// Done-when 1: for every candidate, a <c>BuySupplyCommand</c> for 10 tons naming it is accepted by
    /// the dispatcher if and only if the query lists it.
    /// </summary>
    [Theory]
    [MemberData(nameof(Candidates))]
    public void The_query_agrees_with_the_handler_entity_by_entity(string id, bool asFleet)
    {
        var state = ScriptedState();

        Assert.Equal(Lists(Query(state), id), Buy(state, id, asFleet).IsAccepted);
    }

    /// <summary>
    /// Done-when 2: each candidate the query leaves out is refused with its own code, and each listed
    /// candidate is accepted and admits a positive amount.
    /// </summary>
    [Fact]
    public void Every_rejected_candidate_carries_its_own_rejection_code()
    {
        var state = ScriptedState();

        Assert.Equal(BuySupplyRejections.CityOwnerAtWar, Buy(state, WarCityId, asFleet: false).Code);
        Assert.Equal(BuySupplyRejections.CityNotWithinRange, Buy(state, FarOwnCityId, asFleet: false).Code);
        Assert.Equal(BuySupplyRejections.ProviderFleetUnderConstruction, Buy(state, UnderConstructionFleetId, asFleet: true).Code);
        Assert.Equal(BuySupplyRejections.ProviderFleetNotYours, Buy(state, ForeignFleetId, asFleet: true).Code);
    }

    [Fact]
    public void Every_listed_candidate_is_accepted_with_a_positive_admitted_amount()
    {
        var state = ScriptedState();
        var sink = new RecordingEventSink();
        var dispatcher = new CommandDispatcher(
            SystemRegistry.FromEngineAssembly(), Ruleset, Classical.World, sink);

        dispatcher.Dispatch(state, new BuySupplyCommand(BuyerNationId, ArmyId, OwnCityId, 10));
        Assert.True(Assert.IsType<ArmySupplyPurchased>(Assert.Single(sink.Drain())).AdmittedTons > 0);

        dispatcher.Dispatch(state, new BuySupplyCommand(BuyerNationId, ArmyId, PeaceCityId, 10));
        Assert.True(Assert.IsType<ArmySupplyPurchased>(Assert.Single(sink.Drain())).AdmittedTons > 0);

        dispatcher.Dispatch(state, new BuySupplyCommand(BuyerNationId, ArmyId, CityId: null, Tons: 10, ProviderFleetId: OwnFleetId));
        Assert.True(Assert.IsType<ArmySupplyPurchasedFromFleet>(Assert.Single(sink.Drain())).AdmittedTons > 0);
    }
}
