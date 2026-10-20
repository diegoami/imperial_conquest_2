using System.Globalization;

namespace IC2.Engine.Ai;

/// <summary>
/// A turn-scoped count of what the military phase's army-side decision did: how many army/enemy-city
/// pairs stood adjacent at all, and, of those, how many the target tree (<see cref="AiArmyTargetTree"/>)
/// actually chose to attack.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why this exists.</strong> <c>docs/task-catalogue.md</c> T60 Done-when 1 asked which of the
/// three gates past ownership rejected a siege; T156 replaces those gates with the original's tree
/// decision, so the tally now measures the new gate: of the adjacent army/city pairs the tree saw,
/// how many did it select for attack? An AI that never attacks is visible at a glance; an AI that
/// attacks the wrong thing is visible at a glance; and a run with the tally and a run without it play
/// the same game, exactly as before.
/// </para>
/// <para>
/// <strong>It is diagnostic state, not game state.</strong> It lives for one seat's turn, is never
/// serialized, never reaches <see cref="Model.GameState"/>, and nothing reads it back to make a decision.
/// <see cref="AiMilitaryPhase.Propose"/> takes it as an optional argument for exactly that reason: the
/// parameter defaults to <see langword="null"/>, and every record-method writes to it only after it
/// has already been decided.
/// </para>
/// <para>
/// <strong>One sample per turn, taken at the turn's first proposal pass.</strong>
/// <see cref="AiTurn"/> re-proposes after every action, so a tally fed from every pass would count the
/// same standing adjacencies once per action and the totals would mean "gate observations" rather than
/// "situations". The tally is therefore filled on the turn's first pass only — see
/// <see cref="AiTurn.Run"/>.
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
    /// Army/enemy-city pairs that were adjacent, and so reached the tree at all. Zero across a whole
    /// soak would mean no army was ever next to a city it could besiege, and nothing downstream is
    /// exercised.
    /// </summary>
    public int Adjacent { get; private set; }

    /// <summary>
    /// Adjacent pairs the tree selected <see cref="AiArmyTargetTree.Kind.AttackCity"/> for. A soak with
    /// zero here while <see cref="Adjacent"/> is non-zero is the Done-when 4 smoking gun: the army
    /// sits on the city's tile and never attacks.
    /// </summary>
    public int TreeSelectedAttackCity { get; private set; }

    /// <summary>
    /// Counts once per army per turn (review round 1, N2: for each of the seat's own armies that are
    /// not embarked and still have moves left this turn — <see cref="AiMilitaryPhase.Propose"/>'s
    /// own gate, which is what reaches <see cref="AiArmyTargetTree.Decide"/> at all), whether the
    /// ruleset declared no archer unit type or no fortification order, so the tree could not have
    /// proposed any army decision under any circumstances.
    /// </summary>
    public int RulesetCannotSiege { get; private set; }

    /// <summary>Adjacent and tree-selected: a besiege candidate was issued this turn.</summary>
    public int Proposed { get; private set; }

    /// <summary>The best tree-city-score any adjacent pair reached this turn, or <c>long.MinValue</c> when none was measured.</summary>
    public long BestCityScore { get; private set; } = long.MinValue;

    /// <summary>The best tree-army-score any adjacent pair reached this turn, or <c>long.MinValue</c> when none was measured.</summary>
    public long BestArmyScore { get; private set; } = long.MinValue;

    /// <summary>The army of <see cref="BestCityScore"/>'s (or army score's) pair.</summary>
    public string? BestArmyId { get; private set; }

    /// <summary>The city of <see cref="BestCityScore"/>'s pair.</summary>
    public string? BestCityId { get; private set; }

    /// <summary>Whether anything at all was observed, and therefore whether there is a line to write.</summary>
    public bool IsEmpty => Adjacent == 0 && RulesetCannotSiege == 0;

    /// <summary>Records that the ruleset itself makes a siege impossible.</summary>
    public void RecordRulesetCannotSiege() => RulesetCannotSiege++;

    /// <summary>Records that an adjacent pair reached the tree's city scorer.</summary>
    public void RecordAdjacentPair(
        string armyId,
        string cityId,
        long cityScore,
        long armyScore)
    {
        Adjacent++;

        if (cityScore > BestCityScore)
        {
            BestCityScore = cityScore;
            BestArmyId = armyId;
            BestCityId = cityId;
        }

        if (armyScore > BestArmyScore)
        {
            BestArmyScore = armyScore;
        }
    }

    /// <summary>Records that the tree selected <c>AttackCity</c> for the given pair (and therefore issued a besiege).</summary>
    public void RecordTreeSelection(string armyId, string cityId)
    {
        TreeSelectedAttackCity++;
        Proposed++;
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
            "siege gates: {0} adjacent, {1} tree-selected for attack city, {2} proposed",
            Adjacent, TreeSelectedAttackCity, Proposed);

        if (RulesetCannotSiege > 0)
        {
            head += string.Format(
                CultureInfo.InvariantCulture,
                ", {0} armies blocked by the ruleset declaring no archer type or fortify order",
                RulesetCannotSiege);
        }

        if (BestCityScore == long.MinValue)
        {
            return head;
        }

        return head + string.Format(
            CultureInfo.InvariantCulture,
            " | best pair {0} vs {1}: city score {2} (the army's best city-target score across all candidates; this adjacent city may not be the highest-scoring target), army score {3}",
            BestArmyId, BestCityId, BestCityScore, BestArmyScore);
    }
}