namespace IC2.Engine.Model;

/// <summary>Scenario-authored barbarian arrivals; absent means the existing campaign has no barbarians.</summary>
public sealed record BarbarianScenario(string NationId, ValueList<UnitSlot> ArrivalUnits, int FleetShips,
    int ArmyMorale = 70, int ChancePercent = 25, int ArmyCap = 4, int FleetCap = 4);
