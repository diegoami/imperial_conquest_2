using IC2.Engine.Economy;
using IC2.Engine.Model;
using IC2.Engine.Naval;

namespace IC2.Engine.Presentation;

/// <summary>
/// The pure query behind the strategic screen's End turn warning box. It mirrors the original's
/// record order and checks, but never changes the supplied state.
/// </summary>
public static class EndTurnWarnings
{
    public const string ArmyNeedsSupplies = "An army of yours needs supplies.";
    public const string ArmyCannotPayMercenaries = "An army of yours cannot afford to pay its mercenary units.";
    public const string FleetNotDocked = "One of your fleets is not docked at its own city.";
    public const string FleetNeedsSupplies = "A fleet of yours needs supplies.";
    public const string FleetNeedsRepairing = "A fleet of yours needs repairing.";

    /// <summary>The lines that the original would put in the modal box for one active seat.</summary>
    public sealed record Result(IReadOnlyList<string> Lines)
    {
        /// <summary>Whether at least one trigger opened the box.</summary>
        public bool OpensBox => Lines.Count != 0;
    }

    /// <summary>
    /// Returns the warning lines for <paramref name="nationId"/> in army-then-fleet order. A computer
    /// controlled seat never receives warnings: its end-turn gate proceeds directly.
    /// </summary>
    public static Result For(GameState state, World world, Ruleset ruleset, string nationId)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(ruleset);
        ArgumentNullException.ThrowIfNull(nationId);

        if (state.NationById(nationId) is not { Control: SeatControl.Human })
        {
            return new Result(Array.Empty<string>());
        }

        var lines = new List<string>(ruleset.EndTurnWarnings.MaximumLines);
        var ownedCities = state.Cities
            .Where(city => string.Equals(city.Owner, nationId, StringComparison.Ordinal))
            .ToArray();
        var ownsCity = ownedCities.Length != 0;
        var ownsPort = ownedCities.Any(city => CoastalCity.IsCoastal(city, world));

        foreach (var army in state.Armies)
        {
            if (!string.Equals(army.Nation, nationId, StringComparison.Ordinal) || army.IsEmbarked)
            {
                continue;
            }

            if (!IsChecked(army, ruleset))
            {
                continue;
            }

            if (ownsCity && NeedsArmySupplies(army, ruleset))
            {
                Add(lines, ruleset, ArmyNeedsSupplies);
            }

            if (CannotPayMercenaries(army, ruleset))
            {
                Add(lines, ruleset, ArmyCannotPayMercenaries);
            }
        }

        foreach (var fleet in state.Fleets)
        {
            if (!string.Equals(fleet.Nation, nationId, StringComparison.Ordinal) || fleet.IsUnderConstruction)
            {
                continue;
            }

            var carriedArmy = fleet.CarriedArmyId is { } armyId ? state.ArmyById(armyId) : null;
            if (!IsChecked(fleet, carriedArmy, ruleset))
            {
                continue;
            }

            // The repair line is informational, never a trigger. The original paints it here, before
            // this fleet's trigger lines, only after an earlier check has already opened the box.
            if (fleet.ConditionPercent < ruleset.EndTurnWarnings.RepairConditionThreshold && lines.Count != 0)
            {
                Add(lines, ruleset, FleetNeedsRepairing);
            }

            if (!HasOwnCityNearby(fleet, ownedCities, ruleset))
            {
                Add(lines, ruleset, FleetNotDocked);
            }

            if (ownsPort && fleet.SupplyTons < fleet.Ships / ruleset.EndTurnWarnings.FleetSupplyShipsDivisor)
            {
                Add(lines, ruleset, FleetNeedsSupplies);
            }

            if (carriedArmy is null)
            {
                continue;
            }

            if (ownsPort && NeedsArmySupplies(carriedArmy, ruleset))
            {
                Add(lines, ruleset, ArmyNeedsSupplies);
            }

            if (CannotPayMercenaries(carriedArmy, ruleset))
            {
                Add(lines, ruleset, ArmyCannotPayMercenaries);
            }
        }

        return new Result(lines);
    }

    private static bool IsChecked(ArmyState army, Ruleset ruleset)
    {
        return ruleset.Flags.EndTurnWarningScope == EndTurnWarningScope.EveryUnit
            || army.Moves == RecomputedArmyMoves(army, ruleset);
    }

    private static bool IsChecked(FleetState fleet, ArmyState? carriedArmy, Ruleset ruleset)
    {
        if (ruleset.Flags.EndTurnWarningScope == EndTurnWarningScope.EveryUnit)
        {
            return true;
        }

        if (fleet.Ships <= 0)
        {
            return false;
        }

        var carriedTroops = carriedArmy?.TotalTroops;
        var fullMoves = FleetAttritionRule.MovesForTurn(
            fleet.Ships,
            carriedTroops,
            fleet.SupplyTons == 0,
            fleet.ConditionPercent,
            ruleset);
        return fleet.Moves == fullMoves;
    }

    private static int RecomputedArmyMoves(ArmyState army, Ruleset ruleset)
    {
        var troops = Math.Max(1, army.TotalTroops);
        var moves = SupplyMoraleRule.BaseMoves(troops, ruleset);
        var supplyTest = (long)army.SupplyTons * troops / ruleset.Economy.SupplyPercentNumerator;
        if (supplyTest < ruleset.Economy.SupplyMorale.DecayThresholdPercent)
        {
            moves -= ruleset.Economy.SupplyMorale.MovesPenaltyOnDecay;
        }

        return moves;
    }

    private static bool NeedsArmySupplies(ArmyState army, Ruleset ruleset)
    {
        var troops = Math.Max(1, army.TotalTroops);
        return (long)ruleset.EndTurnWarnings.ArmySupplyThresholdNumerator * army.SupplyTons < troops;
    }

    private static bool CannotPayMercenaries(ArmyState army, Ruleset ruleset)
    {
        var mercenaryPay = 0;
        foreach (var unit in army.Units)
        {
            if (unit.IsMercenary && unit.Troops > 0)
            {
                mercenaryPay += ArmyUpkeep.ComputeUnit(unit, ruleset);
            }
        }

        return army.Money < mercenaryPay;
    }

    private static bool HasOwnCityNearby(FleetState fleet, IReadOnlyList<CityState> ownedCities, Ruleset ruleset)
    {
        var radius = ruleset.Economy.CommandAdjacencyRadiusTiles;
        return ownedCities.Any(city =>
            Math.Abs(city.X - fleet.X) <= radius && Math.Abs(city.Y - fleet.Y) <= radius);
    }

    private static void Add(List<string> lines, Ruleset ruleset, string line)
    {
        if (lines.Count < ruleset.EndTurnWarnings.MaximumLines)
        {
            lines.Add(line);
        }
    }
}
