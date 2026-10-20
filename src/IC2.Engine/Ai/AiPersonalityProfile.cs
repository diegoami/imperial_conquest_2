using IC2.Engine.Model;

namespace IC2.Engine.Ai;

/// <summary>
/// One AI nation's three personality parameters, converted once from the scenario's <c>0..1</c> doubles
/// into clamped integer permille, so that every later arithmetic step in the scorer is integer.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why permille, and why the conversion happens exactly once.</strong>
/// <see cref="AiPersonality"/> stores fractions because <c>docs/game-design.md</c> §AI defines them as
/// fractions, and <c>GameDataValidation</c> already rejects anything outside <c>0..1</c> as malformed
/// data. But a scoring function that keeps multiplying doubles is a scoring function whose ties are
/// decided by floating-point rounding, and this task's determinism bar is "the same seed gives the same
/// game, twice, exactly". So the double is read once, here, rounded to the nearest of 1001 integer
/// buckets, and never touched again: every score on the AI's scale (<see cref="Model.AiWeightsRules"/>)
/// is a <see cref="long"/>.
/// </para>
/// <para>
/// <strong>The default personality.</strong> A seat declared <see cref="SeatControl.Ai"/> with no
/// <see cref="AiPersonality"/> block is legal data — <see cref="Seat.Personality"/> is optional and
/// <c>toy-3city.json</c>'s own human seat carries <c>null</c>. Rather than refuse to play such a seat
/// (which would strand a scenario mid-run) or invent a hidden second set of numbers, every unset
/// parameter reads <see cref="Model.AiWeightsRules.DefaultPersonalityPermille"/> — the exact midpoint of
/// the declared range. <c>[designed]</c>: searched <c>docs/reports/</c> for any per-nation AI tuning in
/// the original and found none — <c>docs/design-audit.md</c> §1 records that <c>TPickLeaders</c> assigns
/// a nation to a human or the computer and a leader name only, with no personality at all, so there is no
/// original default to transcribe. T79 (#355) moved these two weights from the C# constants
/// <c>AiWeights.cs</c> used to hold into <see cref="Model.Ruleset.Ai"/>, so this conversion now needs the
/// ruleset that is running, not just the nation.
/// </para>
/// </remarks>
/// <param name="NationId">The nation these parameters belong to.</param>
/// <param name="AggressionPermille">
/// <c>0..1000</c>. Sets how favourable a strength ratio a naval attack must show before the AI will place
/// it — see <see cref="AiView.RequiredAttackRatioPermille"/>. The army path's chosen action no longer reads
/// this ratio: T156 (issue #925) routes each army's decision through the original's target tree, and
/// <see cref="AiMilitaryPhase"/>'s army-branch executes the tree's call without consulting aggression.
/// Only the fleet half (<see cref="AiMilitaryPhase.ProposeFleetAttacks"/>) still gates the ratio.
/// </param>
/// <param name="ExpansionDrivePermille">
/// <c>0..1000</c>. Sets what share of the treasury the economy phase is willing to commit in one turn,
/// and tilts the choice between building (recruitment) and turtling (fortification).
/// </param>
/// <param name="LoyaltyToAlliancesPermille">
/// <c>0..1000</c>. Scales every diplomacy-phase candidate that proposes or keeps a positive relation.
/// </param>
public sealed record AiPersonalityProfile(
    string NationId,
    int AggressionPermille,
    int ExpansionDrivePermille,
    int LoyaltyToAlliancesPermille)
{
    /// <summary>Reads a nation's personality, substituting the declared default for an absent block.</summary>
    /// <param name="nation">The nation whose seat is being played.</param>
    /// <param name="ruleset">The ruleset this run is playing under, for the two weights this conversion needs.</param>
    /// <exception cref="ArgumentNullException"><paramref name="nation"/> or <paramref name="ruleset"/> is null.</exception>
    public static AiPersonalityProfile For(NationState nation, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(nation);
        ArgumentNullException.ThrowIfNull(ruleset);

        var personality = nation.Personality;
        return new AiPersonalityProfile(
            nation.Id,
            ToPermille(personality?.Aggression, ruleset.Ai),
            ToPermille(personality?.ExpansionDrive, ruleset.Ai),
            ToPermille(personality?.LoyaltyToAlliances, ruleset.Ai));
    }

    /// <summary>
    /// The one place a <c>0..1</c> double becomes an integer. Clamped rather than validated: a value
    /// outside the range is already a loading error (<c>GameDataValidation.RequireUnitRange</c>), so by
    /// the time a <see cref="GameState"/> exists there is nothing left to reject, and a clamp keeps a
    /// hand-built test state from producing a nonsense score instead of a defensible one.
    /// </summary>
    private static int ToPermille(double? value, AiWeightsRules weights)
    {
        if (value is not { } raw || double.IsNaN(raw))
        {
            return weights.DefaultPersonalityPermille;
        }

        var scaled = (long)Math.Round(raw * weights.PermilleScale, MidpointRounding.AwayFromZero);
        return (int)Math.Clamp(scaled, 0L, weights.PermilleScale);
    }
}
