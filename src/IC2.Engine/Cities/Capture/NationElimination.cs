using IC2.Engine.Model;

namespace IC2.Engine.Cities.Capture;

/// <summary>
/// DoD 4: "losing the last city eliminates the nation (capital sentinel set, unity reset)" — shared by
/// both <see cref="CityCaptureResolver.Capture"/> and <see cref="CityCaptureResolver.Defect"/>, since
/// either mechanism can take a nation's last city.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What this deliberately does not do.</strong> <c>nation-tax-base-and-city-economy-fields.md</c>
/// names an untraced writer, <c>FUN_0044c360</c>, "resetting the collapsing nation (unity 450, other
/// fields)" — a different unity value and, per its own wording, more than unity alone. Its caller and
/// trigger are not established, and DoD 4's own wording is narrower than "a collapsing nation" — a direct
/// "losing the last city eliminates the nation" consequence, checked inline at every ownership change
/// rather than through a separate, untraced routine. The empirically observed Galatia elimination
/// (<c>galatia-elimination-and-city-resupply-confirmed.md</c>: unity <c>668 → 0</c> exactly) is used
/// instead — directly-applicable evidence for the mechanism this DoD line actually describes, and the
/// evidence DoD 1's own scripted reproduction needs to match. Treasury, wealth and tax base are left
/// exactly as the capture or defection transfer already set them: DoD 4 asks for the capital sentinel and
/// the unity reset only, and inventing a reset for fields it does not name would not be reproducing
/// confirmed evidence.
/// </para>
/// <para>
/// <strong>The nation record is kept, not removed.</strong> Turn order and diplomacy both reference
/// nations by id; deleting the record would leave every such reference dangling
/// (<c>docs/build-process.md</c> §4.2's "delete leaves something behind" class of defect).
/// <see cref="IC2.Engine.Calendar.SeatRotationSystem"/> DoD 8 is the caller that must skip an eliminated
/// nation's seat.
/// </para>
/// <para>
/// <strong>Diplomacy is reset by the caller, not here.</strong> Rework round 1, B1: the original also
/// resets every relation the eliminated nation holds (see
/// <see cref="IC2.Engine.Diplomacy.RelationTransitions.ResetAllOnElimination"/>'s own remarks for the
/// decompiled citations). That reset writes <see cref="GameState.Relations"/>, a whole-state matrix, not
/// a <see cref="NationState"/> field this method could return alongside <c>eliminated</c> above — so both
/// call sites in <c>CityCaptureResolver</c> apply it themselves, gated on this method's own
/// <c>JustEliminated</c>, after replacing the nation record and disposing of its forces (T84's own
/// <see cref="EliminationForces.Dispose"/>, merged between the two calls since <c>adfbdae</c>; re-review
/// round 2, R3). The two are order-independent — <c>Dispose</c> touches only armies and fleets, this
/// reset only <see cref="GameState.Relations"/> — so which one runs first does not change the outcome.
/// </para>
/// </remarks>
public static class NationElimination
{
    /// <summary>
    /// Checks whether <paramref name="nation"/> now owns zero cities in <paramref name="state"/> and, if
    /// so, returns it eliminated: <see cref="NationState.CapitalCityId"/> cleared to the sentinel
    /// (<see langword="null"/>) and <see cref="NationState.Unity"/> reset to
    /// <see cref="CaptureRules.EliminationUnityReset"/>. Already-eliminated is idempotent: a nation that
    /// was eliminated earlier in the round is returned unchanged, with <c>JustEliminated</c> false, so a
    /// caller never re-publishes <see cref="NationConquered"/> for the same nation twice.
    /// </summary>
    /// <param name="state">
    /// The state <em>after</em> the ownership change that might have taken the nation's last city — this
    /// reads <see cref="GameState.Cities"/> as it stands right now, not before the transfer.
    /// </param>
    /// <param name="nation">The nation to check, already carrying whatever this ownership change did to it.</param>
    /// <param name="ruleset">Supplies <see cref="CaptureRules.EliminationUnityReset"/>.</param>
    /// <returns>
    /// The (possibly updated) nation, and whether this call is what eliminated it — as opposed to it
    /// already being eliminated, or still owning at least one city.
    /// </returns>
    public static (NationState Nation, bool JustEliminated) ApplyIfLastCityLost(
        GameState state, NationState nation, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(nation);
        ArgumentNullException.ThrowIfNull(ruleset);

        if (nation.Eliminated || state.CountCitiesOwnedBy(nation.Id) > 0)
        {
            return (nation, false);
        }

        var eliminated = nation with
        {
            Eliminated = true,
            CapitalCityId = null,
            Unity = ruleset.Capture.EliminationUnityReset,
        };

        return (eliminated, true);
    }
}
