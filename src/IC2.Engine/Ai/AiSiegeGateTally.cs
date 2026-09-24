using System.Globalization;

namespace IC2.Engine.Ai;

/// <summary>
/// A turn-scoped count of what happened to every siege the military phase considered: how many
/// army/enemy-city pairs stood adjacent at all, and, of those, how many were turned away by each of
/// <see cref="AiMilitaryPhase"/>'s gates.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why this exists.</strong> <c>docs/task-catalogue.md</c> T60 Done-when 1 asks which of the
/// three gates past ownership rejects a siege, how often, and on what values — and requires the answer to
/// be measured rather than inferred. Before this type the per-seed log recorded only what the AI
/// <em>chose</em>, so a candidate that was never proposed left no trace at all and the three gates were
/// indistinguishable from outside: a siege that was never legal, a siege whose ratio fell short, and an
/// army that never reached adjacency all looked identical (silence). This makes each of them a number.
/// </para>
/// <para>
/// <strong>It is diagnostic state, not game state.</strong> It lives for one seat's turn, is never
/// serialized, never reaches <see cref="Model.GameState"/>, and nothing reads it back to make a decision —
/// so it cannot change what the AI does, and a run with the tally and a run without it play the same
/// game. <see cref="AiMilitaryPhase.Propose"/> takes it as an optional argument for exactly that reason:
/// the parameter defaults to <see langword="null"/>, and every gate writes to it only after it has already
/// decided.
/// </para>
/// <para>
/// <strong>One sample per turn, taken at the turn's first proposal pass.</strong>
/// <see cref="AiTurn"/> re-proposes after every action, so a tally fed from every pass would count the
/// same standing adjacency once per action and the totals would mean "gate observations" rather than
/// "situations". The tally is therefore filled on the turn's first pass only — see
/// <see cref="AiTurn.Run"/> — so one line of a per-seed log is one turn's worth of siege situations, and
/// summing the lines over the soak gives the Done-when 1 distribution directly.
/// </para>
/// <para>
/// <strong>Nothing here is a collection.</strong> Only counters and one "closest attempt" snapshot, so
/// the determinism scanner has nothing to object to and the line a turn writes is fixed by the state
/// alone.
/// </para>
/// </remarks>
public sealed class AiSiegeGateTally
{
    /// <summary>
    /// Army/enemy-city pairs that were adjacent, and so reached the gates at all. Zero across a whole
    /// soak would mean gate (c) — the army never arrives — and nothing downstream is ever exercised.
    /// </summary>
    public int Adjacent { get; private set; }

    /// <summary>
    /// Counts once per army per turn: for each of the seat's own armies, whether the ruleset declared no
    /// archer unit type or no fortification order, so <see cref="AiMilitaryPhase"/> could not have
    /// proposed a siege for that army under any circumstances.
    /// </summary>
    public int RulesetCannotSiege { get; private set; }

    /// <summary>Gate (a): adjacent, but <c>AttackLegality.IsLegal</c> refused the projected siege.</summary>
    public int RejectedByLegality { get; private set; }

    /// <summary>Gate (b): adjacent and legal, but the strength ratio fell short of the required one.</summary>
    public int RejectedByRatio { get; private set; }

    /// <summary>Adjacent, legal and strong enough: a besiege candidate was placed in front of the scorer.</summary>
    public int Proposed { get; private set; }

    /// <summary>The highest ratio any pair reached this turn, in permille, or <c>-1</c> if none was measured.</summary>
    public long BestRatioPermille { get; private set; } = -1;

    /// <summary>The ratio the personality demanded of <see cref="BestRatioPermille"/>'s pair.</summary>
    public long BestRequiredRatioPermille { get; private set; }

    /// <summary><see cref="Strength.SiegeStrength.Attacker"/> for <see cref="BestRatioPermille"/>'s pair.</summary>
    public long BestAttackerPower { get; private set; }

    /// <summary><c>CompleteDefenderStrength.Compute</c> for <see cref="BestRatioPermille"/>'s pair.</summary>
    public long BestDefenderPower { get; private set; }

    /// <summary>The army of <see cref="BestRatioPermille"/>'s pair.</summary>
    public string? BestArmyId { get; private set; }

    /// <summary>The city of <see cref="BestRatioPermille"/>'s pair.</summary>
    public string? BestCityId { get; private set; }

    /// <summary>Whether anything at all was observed, and therefore whether there is a line to write.</summary>
    public bool IsEmpty => Adjacent == 0 && RulesetCannotSiege == 0;

    /// <summary>Records that the ruleset itself makes a siege impossible.</summary>
    public void RecordRulesetCannotSiege() => RulesetCannotSiege++;

    /// <summary>Records an adjacent pair the legality gate refused.</summary>
    public void RecordLegalityRejection()
    {
        Adjacent++;
        RejectedByLegality++;
    }

    /// <summary>
    /// Records an adjacent, legal pair together with the three numbers the ratio gate compared, whichever
    /// way that gate went.
    /// </summary>
    /// <param name="accepted">Whether the ratio cleared <paramref name="requiredRatioPermille"/>.</param>
    /// <param name="armyId">The besieging army.</param>
    /// <param name="cityId">The target city.</param>
    /// <param name="attackerPower">The attacker's siege strength.</param>
    /// <param name="defenderPower">The city's complete defender strength.</param>
    /// <param name="ratioPermille">Attacker over defender, in permille.</param>
    /// <param name="requiredRatioPermille">What this personality demanded.</param>
    public void RecordRatioGate(
        bool accepted,
        string armyId,
        string cityId,
        long attackerPower,
        long defenderPower,
        long ratioPermille,
        long requiredRatioPermille)
    {
        Adjacent++;
        if (accepted)
        {
            Proposed++;
        }
        else
        {
            RejectedByRatio++;
        }

        if (ratioPermille <= BestRatioPermille)
        {
            return;
        }

        BestRatioPermille = ratioPermille;
        BestRequiredRatioPermille = requiredRatioPermille;
        BestAttackerPower = attackerPower;
        BestDefenderPower = defenderPower;
        BestArmyId = armyId;
        BestCityId = cityId;
    }

    /// <summary>
    /// The one line this turn contributes to the per-seed log, or <see langword="null"/> when there was
    /// nothing to say. Culture-invariant, for the reason <see cref="AiFormat"/> gives.
    /// </summary>
    public string? Describe()
    {
        if (IsEmpty)
        {
            return null;
        }

        var head = string.Format(
            CultureInfo.InvariantCulture,
            "siege gates: {0} adjacent, {1} illegal, {2} below ratio, {3} proposed",
            Adjacent, RejectedByLegality, RejectedByRatio, Proposed);

        if (RulesetCannotSiege > 0)
        {
            head += string.Format(
                CultureInfo.InvariantCulture,
                ", {0} armies blocked by the ruleset declaring no archer type or fortify order",
                RulesetCannotSiege);
        }

        if (BestRatioPermille < 0)
        {
            return head;
        }

        return head + string.Format(
            CultureInfo.InvariantCulture,
            " | closest {0} vs {1}: attacker {2} vs defender {3}, ratio {4} permille, need {5}",
            BestArmyId, BestCityId, BestAttackerPower, BestDefenderPower,
            BestRatioPermille, BestRequiredRatioPermille);
    }
}
