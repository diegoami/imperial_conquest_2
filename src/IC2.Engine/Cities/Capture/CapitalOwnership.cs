using IC2.Engine.Model;

namespace IC2.Engine.Cities.Capture;

/// <summary>
/// <c>FUN_0044B8D0</c>: true when <em>any</em> of the sixteen nation records' own
/// <see cref="NationState.CapitalCityId"/> names <paramref name="cityId"/> — whether that nation is still
/// alive or not (the function loops every nation slot with no liveness check of its own, T90/#409). The
/// decompile calls <c>FUN_0044B8D0</c> from eleven places in total — besides the four below, the quarterly
/// rebellion's own capital skip (<c>FUN_00451B40</c>, implemented separately by T89's
/// <c>QuarterlyCityEconomySystem</c>), two AI scorers, <c>FUN_0044A794</c>, and three UI functions, none of
/// which this type serves. T91 (bugs #415/#416, #409 S4) centralises the reading for the four
/// capture-and-siege places, extracted from <see cref="CityCaptureResolver.RunCascade"/>'s own gate (T90's
/// original inline form) so the same test-proven predicate backs each of them:
/// <list type="number">
/// <item><description>the cascade's own exclusion gate (<see cref="CityCaptureResolver.RunCascade"/>, corrected by T90);</description></item>
/// <item><description>the post-sweep branch that decides capital-move-or-conquer versus the plain city-count conquest (<see cref="CityCaptureResolver.Capture"/>'s own <c>wasCapital</c>, bug #416);</description></item>
/// <item><description>defender strength's own ×5/3 capital scaling at the initiating siege (<see cref="Battle.InstantBattleResolver.ResolveSiege"/>, bug #409 S4);</description></item>
/// <item><description>the same ×5/3 scaling for each capital-move destination candidate (<see cref="ConquestTrigger"/>'s own <c>FindCapitalMoveDestination</c>, bug #409 S4).</description></item>
/// </list>
/// A fifth caller, the AI's own siege-strength estimate (<c>AiMilitaryPhase.IsControllerCapital</c>), still
/// checks only the current owner's capital and is now a separate bug (#425) rather than folded in here.
/// </summary>
/// <remarks>
/// <see cref="Battle.InstantBattleResolver"/> referencing this type is a deliberate, narrow exception to
/// the general <c>Battle → Cities.Capture</c> direction <see cref="CompleteDefenderStrength"/>'s own
/// remarks describe avoiding for the garrison-troops addend: that case chose two independent call sites
/// <em>instead of</em> a <c>Battle → Cities.Capture</c> reference, because <see cref="CityCaptureResolver"/>
/// already depends on <c>Battle</c> for <see cref="Battle.BattleResult"/> and a reference back the other
/// way would be circular in spirit even though same-assembly. This type is different in kind: a single,
/// pure, side-effect-free predicate over <see cref="GameState.Nations"/> (not the multi-step, per-slot
/// garrison sum that call chose to duplicate instead -- R1-N2: an earlier revision of this remark said
/// "avoided duplicating", backwards from what that call actually did), shared by four call sites (three of them outside
/// <see cref="Battle"/> entirely) — Done-when 3 asks for exactly "one helper decides 'any nation's capital'
/// for all three [capital-strength] places", and duplicating this one instead would leave three copies of
/// the same loop to keep in sync by hand. Both directions are same-assembly namespace references either
/// way; no project-level circularity is introduced by either choice.
/// </remarks>
public static class CapitalOwnership
{
    /// <summary>
    /// <c>FUN_0044B8D0(cityId)</c>: true when some nation's own <see cref="NationState.CapitalCityId"/> —
    /// any of them, eliminated or not — equals <paramref name="cityId"/>.
    /// </summary>
    public static bool IsAnyNationsCapital(GameState state, string cityId)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(cityId);

        foreach (var nation in state.Nations)
        {
            if (nation.CapitalCityId is { } capitalId && string.Equals(capitalId, cityId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
