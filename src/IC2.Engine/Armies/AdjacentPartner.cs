using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Naval;

namespace IC2.Engine.Armies;

/// <summary>
/// The partner the original picks for an army or fleet order that needs one — <c>docs/tasks/T106.md</c>
/// "Army-to-army transfer of units, supply and money". The UI uses these for Transfer unit, Join armies,
/// Join fleets and Transfer ships, so the player never picks the partner; the CLI keeps naming both ids.
/// </summary>
/// <remarks>
/// <para>
/// <strong>[derived: code; audit §1.6]:</strong> <c>FUN_00449D64</c> (army) and <c>FUN_00449DD8</c>
/// (fleet) return the <em>last</em> own unit at Chebyshev distance <strong>exactly</strong> 1 from the
/// selected one — the <c>distance == 1</c> inside <c>FUN_004492A0</c>. Distance 0 (the selected unit
/// itself, or a co-located sibling) and distance 2 are therefore both excluded, and the last match in
/// the state's own order wins, not the nearest. This is <c>[derived: code]</c> and not yet promoted by a
/// research session (audit §5, gap 1); see <c>docs/tasks/T106.md</c>'s own Hazards.
/// </para>
/// <para>
/// <strong>No re-sorting.</strong> "Last" only means the same unit as the original while this engine's
/// <see cref="GameState.Armies"/> / <see cref="GameState.Fleets"/> order matches the original's index
/// order — see this task's PR body.
/// </para>
/// </remarks>
public static class AdjacentPartner
{
    /// <summary>
    /// The last own army at Chebyshev distance exactly 1 from <paramref name="armyId"/>, or
    /// <see langword="null"/> when the selected army is unknown or none stands adjacent.
    /// </summary>
    public static ArmyState? Army(GameState state, string armyId)
    {
        ArgumentNullException.ThrowIfNull(state);

        var selected = state.ArmyById(armyId);
        if (selected is null)
        {
            return null;
        }

        ArmyState? partner = null;
        foreach (var candidate in state.Armies)
        {
            if (string.Equals(candidate.Id, selected.Id, StringComparison.Ordinal)
                || !string.Equals(candidate.Nation, selected.Nation, StringComparison.Ordinal)
                || ChebyshevDistanceTo(candidate.X, candidate.Y, selected.X, selected.Y) != 1)
            {
                continue;
            }

            partner = candidate;
        }

        return partner;
    }

    /// <summary>
    /// The last own <em>launched</em> fleet at Chebyshev distance exactly 1 from
    /// <paramref name="fleetId"/>, or <see langword="null"/> when the selected fleet is unknown or none
    /// stands adjacent. A fleet still under construction is skipped, as is a foreign one.
    /// </summary>
    public static FleetState? Fleet(GameState state, string fleetId)
    {
        ArgumentNullException.ThrowIfNull(state);

        var selected = state.FleetById(fleetId);
        if (selected is null)
        {
            return null;
        }

        FleetState? partner = null;
        foreach (var candidate in state.Fleets)
        {
            if (string.Equals(candidate.Id, selected.Id, StringComparison.Ordinal)
                || !string.Equals(candidate.Nation, selected.Nation, StringComparison.Ordinal)
                || candidate.IsUnderConstruction
                || ChebyshevDistanceTo(candidate.X, candidate.Y, selected.X, selected.Y) != 1)
            {
                continue;
            }

            partner = candidate;
        }

        return partner;
    }

    /// <summary>
    /// Chebyshev distance through the engine's single shared metric
    /// (<see cref="LandingTile.ChebyshevDistance"/>, the same one every other adjacency rule uses).
    /// </summary>
    private static int ChebyshevDistanceTo(int fromX, int fromY, int toX, int toY) =>
        LandingTile.ChebyshevDistance(new GridPoint(fromX, fromY), new GridPoint(toX, toY));
}
