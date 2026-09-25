using IC2.Engine.Model;
using IC2.Engine.Tests.Core;

namespace IC2.Engine.Tests.Cities.Capture;

/// <summary>
/// Builders <c>EliminationForcesTests</c> needs beyond what <see cref="CaptureTestbed"/> already provides:
/// fleets (launched and under construction), an embarked army, and a full <see cref="GameState"/> whose
/// turn order and relation matrix actually match its own nation list.
/// </summary>
/// <remarks>
/// <see cref="CaptureTestbed.StateWith"/> leaves the toy scenario's own two-nation turn order and relation
/// matrix in place, which is fine for tests that never validate the assembled state. T84's Done-when 3
/// needs a state that <see cref="IC2.Engine.Serialization.GameDataValidation.Validate"/> and a save/load
/// round trip both accept, and both of those check the turn order and the relation matrix against the
/// state's actual <see cref="GameState.Nations"/> — so this file's own <see cref="StateWith"/> rebuilds
/// both from whatever nations a test passes in, the same way <c>BattleCommandTestbed.StateWith</c> already
/// does for the Battle folder's own tests.
/// </remarks>
public static class EliminationForcesTestbed
{
    /// <summary>The shipped toy ruleset — the same one every other Cities/Capture test reads.</summary>
    public static Ruleset Ruleset => CaptureTestbed.Ruleset;

    /// <summary>One launched fleet: on the map, not under construction, at the toy ruleset's own baseline condition.</summary>
    public static FleetState Fleet(string id, string nation, int x, int y, int ships = 5, string? carriedArmyId = null) =>
        new(
            id, nation, x, y,
            Moves: 4, ships,
            ConditionPercent: 100,
            Money: 0,
            SupplyTons: 20,
            ConstructionTicksRemaining: null,
            BuildCityId: null,
            carriedArmyId,
            CoveredTileCode: 0);

    /// <summary>An army aboard <paramref name="fleetId"/>: off the map, so no covered tile — mirrors <c>BattleTestbed.EmbarkedArmy</c>.</summary>
    public static ArmyState EmbarkedArmy(string id, string nation, string fleetId, int x, int y) =>
        new(
            id, nation, x, y,
            Moves: 0,
            Morale: 50,
            Money: 0,
            SupplyTons: 0,
            CoveredTileCode: null,
            fleetId,
            ValueList.From(new[] { CaptureTestbed.Unit("heavy_infantry", 500) }));

    /// <summary>
    /// Assembles a state with its own turn order, active seat and a peace-uniform relation matrix sized to
    /// <paramref name="nations"/> — unlike <see cref="CaptureTestbed.StateWith"/>, this round-trips through
    /// <see cref="IC2.Engine.Serialization.GameDataValidation"/> and a save/load for any nation set, not
    /// just the toy scenario's original two.
    /// </summary>
    public static GameState StateWith(
        IEnumerable<NationState> nations,
        IEnumerable<CityState> cities,
        IEnumerable<ArmyState>? armies = null,
        IEnumerable<FleetState>? fleets = null)
    {
        var nationList = ValueList.From(nations);
        var nationIds = ValueList.From(nationList.Select(n => n.Id));

        return CoreTestbed.InitialState() with
        {
            Nations = nationList,
            Cities = ValueList.From(cities),
            Armies = ValueList.From(armies ?? Array.Empty<ArmyState>()),
            Fleets = ValueList.From(fleets ?? Array.Empty<FleetState>()),
            TurnOrder = nationIds,
            ActiveSeatIndex = 0,
            Relations = DiplomaticRelations.Uniform(nationIds, Ruleset.Diplomacy.StateCodes.Peace),
        };
    }
}
