using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Battle.Candidates;

// T59 (docs/tasks/T59.md): the common seam every auto-resolve candidate sits behind, exactly as
// docs/investigations/auto-resolve-approaches.md §3 specifies it. MEASUREMENT ONLY: nothing in the game
// calls anything in this namespace, no Ruleset flag selects it, and InstantBattleResolver is untouched.

/// <summary>One unit as a candidate sees it: <c>(type, troops, quality)</c>, §3's input.</summary>
/// <param name="UnitTypeId">A <see cref="UnitTypeRules.Id"/> from the ruleset.</param>
/// <param name="Troops">Troops at the start of the battle.</param>
/// <param name="Quality">The tier code 5–9 (§3).</param>
/// <param name="MercenaryLabel">The merged <see cref="UnitSlot.MercenaryLabel"/>; only C1 reads it (its deletion pass).</param>
public sealed record CandidateUnit(string UnitTypeId, int Troops, int Quality, int MercenaryLabel = 0);

/// <summary>An army: an ordered list of at most 20 units (K31), plus its strategic morale <c>M</c> (army record <c>+14</c>).</summary>
public sealed record CandidateArmy(ValueList<CandidateUnit> Units, int Morale)
{
    /// <summary>Total troops across every unit.</summary>
    public long TotalTroops
    {
        get
        {
            long total = 0;
            foreach (var unit in Units)
            {
                total += unit.Troops;
            }

            return total;
        }
    }
}

/// <summary>§3's input record: two armies, the ruleset, and <c>onDefeat</c>. The <c>IRng</c> is passed separately.</summary>
public sealed record CandidateBattle(CandidateArmy Attacker, CandidateArmy Defender, Ruleset Ruleset, DefeatOutcome OnDefeat);

/// <summary>§8.7's endings, plus C1's and C3's one-shot <see cref="Decided"/>.</summary>
public enum CandidateEnding
{
    /// <summary>A one-shot comparison (C1, C3).</summary>
    Decided,

    /// <summary>The loser's last live unit left through the strength floor (K08), or its troops reached 0; for C4, a side reached 0 troops.</summary>
    Annihilation,

    /// <summary>The loser's last live unit left through a morale break (K09, K11, K13); for C4, the pool broke.</summary>
    Collapse,

    /// <summary>C5's ordered army-level withdrawal (§6.5.3).</summary>
    Withdrawal,

    /// <summary>The round cap decided the battle (D09, D33).</summary>
    Cap,
}

/// <summary>Why a unit left the field, for §8.7 and the event log.</summary>
public enum BreakCause
{
    /// <summary>Not broken.</summary>
    None,

    /// <summary><c>troops &lt; standardBattalionSize / 25</c> (K08), including troops at 0.</summary>
    StrengthFloor,

    /// <summary><c>m ≤ 19</c> (K09).</summary>
    MoraleFloor,

    /// <summary>The probabilistic band (K10, K11).</summary>
    Band,

    /// <summary>Removed by a friend's rout, <c>m &lt; 30</c> after the −6 (K12, K13).</summary>
    Cascade,

    /// <summary>C5's ordered withdrawal (§6.5.3).</summary>
    Withdrawal,

    /// <summary>Removed at the round cap (D09).</summary>
    Cap,
}

/// <summary>What happened in one event of a battle: the log §8.5 evaluates each variable draw formula on.</summary>
public enum CandidateEventKind
{
    /// <summary>C2/C5 setup: one <c>Random(q × 4)</c> per unit (K26).</summary>
    MoraleSeed,

    /// <summary>C2/C5: one shot (K21), two draws.</summary>
    Shot,

    /// <summary>C2/C5: one melee exchange (K16), four draws.</summary>
    MeleeExchange,

    /// <summary>C2/C5: a rout check that reached the band (K11), two draws.</summary>
    BandCheck,

    /// <summary>C2/C5: a unit broke (<see cref="CandidateEvent.Cause"/> says how). No draw.</summary>
    Break,

    /// <summary>C5: one cavalry pursuit hit (§6.5.2 step 3), two draws.</summary>
    PursuitHit,

    /// <summary>C5: one pursuing shot (§6.5.2 step 4), two draws.</summary>
    PursuitShot,

    /// <summary>C4: one unit's fire in a fire round (D30), two draws.</summary>
    FireVolley,

    /// <summary>C4: one shock round (D31), two draws (one die per side).</summary>
    ShockRound,

    /// <summary>C4: the pursuit round (D33), one draw.</summary>
    Pursuit,

    /// <summary>C1/C3: one casualty-divisor draw (K04).</summary>
    CasualtyDivisor,
}

/// <summary>One logged event.</summary>
/// <param name="Kind">What happened.</param>
/// <param name="Round">The round (1-based) or 0 for setup and one-shot candidates.</param>
/// <param name="Side">0 = attacker, 1 = defender: the side of the acting or broken unit.</param>
/// <param name="Slot">The acting or broken unit's slot, or −1.</param>
/// <param name="Cause">For <see cref="CandidateEventKind.Break"/>, how the unit broke.</param>
public readonly record struct CandidateEvent(CandidateEventKind Kind, int Round, int Side, int Slot, BreakCause Cause = BreakCause.None)
{
    /// <summary>The draws this event consumes, as the candidate sections state them (§6).</summary>
    public int StatedDraws => Kind switch
    {
        CandidateEventKind.MoraleSeed => 1,
        CandidateEventKind.Shot => 2,
        CandidateEventKind.MeleeExchange => 4,
        CandidateEventKind.BandCheck => 2,
        CandidateEventKind.Break => 0,
        CandidateEventKind.PursuitHit => 2,
        CandidateEventKind.PursuitShot => 2,
        CandidateEventKind.FireVolley => 2,
        CandidateEventKind.ShockRound => 2,
        CandidateEventKind.Pursuit => 1,
        CandidateEventKind.CasualtyDivisor => 1,
        _ => 0,
    };
}

/// <summary>§3's output record, one per battle.</summary>
/// <param name="Winner">Attacker or defender.</param>
/// <param name="WinnerAfter">
/// Troops of each of the winner's units after the battle, indexed by the winner's <em>input</em> slot; 0 is
/// a removed unit. C5's rejoined fled units carry their <c>fledTroops</c> (D45).
/// </param>
/// <param name="LoserSurvivors">
/// Troops of each loser unit that leaves the field (what <c>scatter</c> receives), indexed by the loser's
/// input slot. All zero under <see cref="DefeatOutcome.Destroyed"/>, which discards them.
/// </param>
/// <param name="Ending">§8.7's classification.</param>
/// <param name="BattleDraws">The draws the battle phase consumed (counted).</param>
/// <param name="TotalDraws">Every draw counted on the <see cref="IRng"/> handed in (C1 includes its post-battle draws).</param>
/// <param name="StatedDraws">The draw count the candidate's §6 formula gives for this battle, evaluated independently of the counter.</param>
/// <param name="CascadeBreak">Whether at least one unit was removed by K13's <c>&lt; 30</c> cascade (C2, C5).</param>
/// <param name="Rounds">Rounds fought (0 for a one-shot candidate).</param>
/// <param name="Events">The per-event log, when requested (C2, C4, C5); otherwise empty.</param>
public sealed record CandidateOutcome(
    BattleSide Winner,
    int[] WinnerAfter,
    int[] LoserSurvivors,
    CandidateEnding Ending,
    long BattleDraws,
    long TotalDraws,
    long StatedDraws,
    bool CascadeBreak,
    int Rounds,
    IReadOnlyList<CandidateEvent> Events);

/// <summary>The one interface every candidate implements (Done-when 1).</summary>
public interface IAutoResolveCandidate
{
    /// <summary>The neutral key, C1–C5, in T58's Done-when 1 order (not an order of merit).</summary>
    string Key { get; }

    /// <summary>A short, neutral name.</summary>
    string Name { get; }

    /// <summary>Resolves one battle. Every draw goes through <paramref name="rng"/>.</summary>
    /// <param name="battle">The two armies, the ruleset and <c>onDefeat</c>.</param>
    /// <param name="rng">The battle's random stream; the only randomness source a candidate may use.</param>
    /// <param name="recordEvents">Whether to fill <see cref="CandidateOutcome.Events"/> (§8.5's log).</param>
    CandidateOutcome Resolve(CandidateBattle battle, IRng rng, bool recordEvents = false);
}

/// <summary>
/// §8.5's "wrapper around <c>IRng</c> that increments on every call". Counts every draw of every kind, so
/// a draw a candidate makes outside its stated formula shows up as a mismatch.
/// </summary>
public sealed class DrawCountingRng : IRng
{
    private readonly IRng _inner;

    /// <summary>Wraps <paramref name="inner"/>.</summary>
    public DrawCountingRng(IRng inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    /// <summary>Draws made so far through this wrapper.</summary>
    public long Draws { get; private set; }

    /// <inheritdoc />
    public ulong Seed => _inner.Seed;

    /// <inheritdoc />
    public ulong State => _inner.State;

    /// <inheritdoc />
    public ulong NextUInt64()
    {
        Draws++;
        return _inner.NextUInt64();
    }

    /// <inheritdoc />
    public int NextInt(int exclusiveUpperBound)
    {
        Draws++;
        return _inner.NextInt(exclusiveUpperBound);
    }

    /// <inheritdoc />
    public int NextInt(int inclusiveLowerBound, int exclusiveUpperBound)
    {
        Draws++;
        return _inner.NextInt(inclusiveLowerBound, exclusiveUpperBound);
    }

    /// <inheritdoc />
    public bool NextChance(int numerator, int denominator)
    {
        Draws++;
        return _inner.NextChance(numerator, denominator);
    }

    /// <inheritdoc />
    public IRng ForStream(string streamName) =>
        throw new NotSupportedException("A candidate draws from the one stream it is handed (§3); it never forks.");
}
