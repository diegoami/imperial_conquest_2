using IC2.Engine.Model;

namespace IC2.Engine.Economy;

/// <summary>
/// The "hostile army adjacent" predicate — <c>FUN_004497cc</c>, read by both T35's quarterly population
/// growth (which skips a threatened city) and, by reuse, T37's weekly city-supply production (which caps
/// a threatened city's gain at zero) <strong>[confirmed: city-population-growth.md]</strong>.
/// </summary>
/// <remarks>
/// For each of the 9 cells centred on the city (the city's own cell plus its 8 neighbours — a 3×3 block,
/// <see cref="EconomyRules.ThreatenedCityAdjacencyRadius"/> cells in every direction), the original takes
/// the first live army standing there. If that army's owner is at <see cref="RelationStateCodes.War"/>
/// with the city's owner, the city counts as threatened. An army aboard a fleet
/// (<see cref="ArmyState.IsEmbarked"/>) covers no map cell and cannot threaten a city by this rule.
/// </remarks>
public static class HostileArmyAdjacent
{
    /// <summary>Whether a hostile army (relative to <paramref name="city"/>'s owner) stands adjacent to it.</summary>
    public static bool IsThreatened(CityState city, GameState state, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(city);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ruleset);

        var radius = ruleset.Economy.ThreatenedCityAdjacencyRadius;
        var warCode = ruleset.Diplomacy.StateCodes.War;

        foreach (var army in state.Armies)
        {
            if (army.CoveredTileCode is null)
            {
                // Aboard a fleet: off the map, cannot occupy a cell adjacent to the city.
                continue;
            }

            if (Math.Abs(army.X - city.X) > radius || Math.Abs(army.Y - city.Y) > radius)
            {
                continue;
            }

            // No same-nation special case: the relation matrix decides this uniformly, exactly as the
            // original reads it, and a nation's relation to itself is Peace by construction (never War)
            // for every state this engine produces.
            if (state.Relations.Get(city.Owner, army.Nation) == warCode)
            {
                return true;
            }
        }

        return false;
    }
}
