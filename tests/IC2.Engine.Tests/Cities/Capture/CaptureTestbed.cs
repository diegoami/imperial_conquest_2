using IC2.Engine.Model;
using IC2.Engine.Tests.Core;

namespace IC2.Engine.Tests.Cities.Capture;

/// <summary>
/// The pieces a Cities/Capture test needs: the shipped toy <see cref="Ruleset"/> (reused from
/// <see cref="CoreTestbed"/>, exactly like every other task's tests), and small builders for the armies,
/// cities and nations a capture, defection or elimination fixture is made of.
/// </summary>
/// <remarks>
/// Every gameplay number the tests in this folder assert comes from <see cref="Ruleset"/> (loaded from
/// the shipped <c>toy-ruleset.json</c>, including this task's own <c>capture</c> block) or from the T04
/// fixtures corpus — never pasted in as a bare literal. What this file writes as literals is fixture
/// <em>data</em> (troop counts, coordinates, loyalty, treasury): the scenario under test, not a rule.
/// </remarks>
public static class CaptureTestbed
{
    /// <summary>The shipped toy ruleset, including this task's own <c>capture</c> block.</summary>
    public static Ruleset Ruleset => CoreTestbed.Toy.Ruleset;

    /// <summary>The unit type id the shipped rulesets use for archers (<c>type == 2</c> in the original).</summary>
    public const string ArcherUnitTypeId = "archers";

    /// <summary>The shipped ruleset's one city order, needed to decode a fortification word.</summary>
    public static string FortifyOrderId => Ruleset.CityOrders.Orders[0].Id;

    /// <summary>Builds a regular (non-mercenary) unit slot with a throwaway name.</summary>
    public static UnitSlot Unit(string unitTypeId, int troops, int quality = 6) =>
        new(MercenaryLabel: 0, UnitTypeId: unitTypeId, Troops: troops, Quality: quality, Name: "Test Battalion");

    /// <summary>One besieging (or already-victorious) army on the map.</summary>
    public static ArmyState Army(
        string id, string nation, int x, int y, int morale, params UnitSlot[] units) =>
        new(
            id, nation, x, y,
            Moves: 0,
            morale,
            Money: 0,
            SupplyTons: 0,
            CoveredTileCode: 2,
            AboardFleetId: null,
            ValueList.From(units));

    /// <summary>One city. <paramref name="tribute"/>/<paramref name="population"/>/<paramref name="maxPopulation"/> feed <c>CityTaxContribution</c>.</summary>
    public static CityState City(
        string id,
        string name,
        int x,
        int y,
        string owner,
        string allegiance,
        int loyalty,
        int fortificationCode,
        int populationThousands,
        int maxPopulationThousands,
        int tribute) =>
        new(
            id, name, x, y, owner, allegiance, loyalty,
            SupplyTons: 0,
            fortificationCode,
            populationThousands,
            maxPopulationThousands,
            tribute,
            UnderSiege: false,
            ValueList<UnitSlot>.Empty);

    /// <summary>One nation record with every field a capture/defection/elimination test might read or write.</summary>
    public static NationState Nation(
        string id,
        int treasury = 0,
        int unity = 600,
        int wealth = 0,
        int taxBase = 0,
        string? capitalCityId = null,
        bool eliminated = false,
        ValueList<RecruitmentSlot>? recruitmentSlots = null) =>
        new(
            Id: id, Name: id, ColorHex: "#000", LeaderName: "Leader", CapitalCityId: capitalCityId,
            Control: SeatControl.Ai, Personality: null,
            Treasury: treasury, Unity: unity, Wealth: wealth, TaxBase: taxBase, TaxRatePercent: 15,
            MobilizedPercent: 0, Population: 100, PopulationAtStart: 100, TreasuryAtStart: treasury,
            CityCountAtStart: 1, RecruitmentSlots: recruitmentSlots ?? ValueList<RecruitmentSlot>.Empty,
            Eliminated: eliminated);

    /// <summary>
    /// <paramref name="count"/> throwaway cities owned by <paramref name="nationId"/>, laid out well away
    /// from any capture/cascade fixture's own tiles — for a city-count assertion (DoD 3, DoD 4) that needs
    /// a nation starting with more than one city.
    /// </summary>
    public static IEnumerable<CityState> FillerCities(string nationId, int count, int startX, int y)
    {
        for (var i = 0; i < count; i++)
        {
            yield return City(
                $"filler-{nationId}-{i}", $"Filler {i}", startX + i, y,
                nationId, nationId, loyalty: 50, fortificationCode: 0,
                populationThousands: 10, maxPopulationThousands: 20, tribute: 0);
        }
    }

    /// <summary>A state built from an explicit set of nations, cities and armies (the toy scenario's own world/ruleset ids only).</summary>
    public static GameState StateWith(
        IEnumerable<NationState> nations,
        IEnumerable<CityState> cities,
        IEnumerable<ArmyState>? armies = null)
    {
        var initial = CoreTestbed.InitialState();
        return initial with
        {
            Nations = ValueList.From(nations),
            Cities = ValueList.From(cities),
            Armies = ValueList.From(armies ?? Array.Empty<ArmyState>()),
            Fleets = ValueList<FleetState>.Empty,
        };
    }
}
