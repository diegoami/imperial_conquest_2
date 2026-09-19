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
/// buckets, and never touched again: every score in <see cref="AiWeights"/>'s scale is a <see cref="long"/>.
/// </para>
/// <para>
/// <strong>The default personality.</strong> A seat declared <see cref="SeatControl.Ai"/> with no
/// <see cref="AiPersonality"/> block is legal data — <see cref="Seat.Personality"/> is optional and
/// <c>toy-3city.json</c>'s own human seat carries <c>null</c>. Rather than refuse to play such a seat
/// (which would strand a scenario mid-run) or invent a hidden second set of numbers, every unset
/// parameter reads <see cref="AiWeights.DefaultPersonalityPermille"/> — the exact midpoint of the
/// declared range. <c>[designed]</c>: searched <c>docs/reports/</c> for any per-nation AI tuning in the
/// original and found none — <c>docs/design-audit.md</c> §1 records that <c>TPickLeaders</c> assigns a
/// nation to a human or the computer and a leader name only, with no personality at all, so there is no
/// original default to transcribe.
/// </para>
/// </remarks>
/// <param name="NationId">The nation these parameters belong to.</param>
/// <param name="AggressionPermille">
/// <c>0..1000</c>. Sets how favourable a strength ratio an attack, siege or naval attack must show before
/// the AI will place it — see <see cref="AiWeights.RequiredAttackRatioPermille"/>.
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
    /// <exception cref="ArgumentNullException"><paramref name="nation"/> is null.</exception>
    public static AiPersonalityProfile For(NationState nation)
    {
        ArgumentNullException.ThrowIfNull(nation);

        var personality = nation.Personality;
        return new AiPersonalityProfile(
            nation.Id,
            ToPermille(personality?.Aggression),
            ToPermille(personality?.ExpansionDrive),
            ToPermille(personality?.LoyaltyToAlliances));
    }

    /// <summary>
    /// The one place a <c>0..1</c> double becomes an integer. Clamped rather than validated: a value
    /// outside the range is already a loading error (<c>GameDataValidation.RequireUnitRange</c>), so by
    /// the time a <see cref="GameState"/> exists there is nothing left to reject, and a clamp keeps a
    /// hand-built test state from producing a nonsense score instead of a defensible one.
    /// </summary>
    private static int ToPermille(double? value)
    {
        if (value is not { } raw || double.IsNaN(raw))
        {
            return AiWeights.DefaultPersonalityPermille;
        }

        var scaled = (long)Math.Round(raw * AiWeights.PermilleScale, MidpointRounding.AwayFromZero);
        return (int)Math.Clamp(scaled, 0L, AiWeights.PermilleScale);
    }
}
