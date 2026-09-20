namespace IC2.Engine.Ai;

/// <summary>
/// Every tunable number the heuristic AI uses, in one place, with the reasoning for each.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why these are C# constants and not <see cref="Model.Ruleset"/> fields.</strong> They would be
/// better as ruleset data, and a later task should move them there. They are not here by preference:
/// <c>docs/task-catalogue.md</c>'s T22 entry grants this task exactly <em>one</em> additive
/// <c>EconomyRules</c> field (<c>AutoResupplyRadiusTiles</c>) and its matching key in
/// <c>toy-ruleset.json</c>, and nothing else in <c>src/IC2.Engine/Model/**</c> or <c>data/**</c>. Adding
/// a whole <c>AiRules</c> block would be a scope breach (<c>docs/build-process.md</c> §4.2 gate 4), and
/// the entry's own Scope line puts the AI's data-driven tuning surface in <em>scenario</em> data — the
/// three <see cref="Model.AiPersonality"/> parameters — not in the ruleset. So: the personality is data,
/// the scoring shape is code, and this file is the whole of the scoring shape.
/// </para>
/// <para>
/// <strong>Every number below is <c>[designed]</c>, and that is the expected answer for this task.</strong>
/// <c>docs/game-design.md</c> §AI is headed "<em>designed, intentionally out of scope for RE by the
/// project's own standing decision</em>", and <c>docs/design-audit.md</c> §1 records why: the original's
/// AI "lives in unnamed AI-only code" that the roadmap deliberately does not chase. What was searched,
/// once, for the whole file: every report in the research repository was searched for an AI decision
/// rule, a threat threshold, a strength ratio at which the original's AI attacks, a recruitment budget
/// share, or any per-nation tuning parameter. Three things came back, and all three are consumed here as
/// engine calls rather than re-derived — the 9-cell city-threat test
/// (<c>city-population-growth.md</c>, via <see cref="Economy.HostileArmyAdjacent"/>), the AI army and
/// fleet resupply passes (<c>supply-capacity-rounding.md</c>, via
/// <see cref="Economy.AutomaticResupply"/> and <c>EconomyRules.AutoResupplyRadiusTiles</c>) and the
/// AI-to-AI reparation trigger (<c>decompiled-diplomacy-peace-terms-and-instant-battles.md</c>, already
/// merged inside T19's peace-treaty system, which this AI does not duplicate). Nothing was found that
/// states how the original's AI <em>chooses</em>, so nothing here is transcribed and nothing here claims
/// to be. Each constant's own remark says what job it does and why the value is where it is; a reviewer
/// can disagree with any of them without the AI being wrong, which is the point of keeping them together.
/// </para>
/// <para>
/// <strong>Everything is an integer.</strong> Scores are <see cref="long"/> on one scale shared by all
/// four phases, so a candidate from the economy phase and a candidate from the military phase are
/// directly comparable and the ordering between phases is a visible number rather than an emergent
/// accident of which loop ran first. No score is ever a <see cref="double"/>: see
/// <see cref="AiPersonalityProfile"/> for why.
/// </para>
/// </remarks>
public static class AiWeights
{
    /// <summary>The fixed-point denominator every personality parameter and every ratio is expressed in.</summary>
    public const int PermilleScale = 1000;

    /// <summary>
    /// The value substituted for an absent personality parameter: the exact midpoint of the declared
    /// <c>0..1</c> range, so an unset seat is neither a pacifist nor a warmonger. See
    /// <see cref="AiPersonalityProfile"/> for the search that found no original default.
    /// </summary>
    public const int DefaultPersonalityPermille = PermilleScale / 2;

    /// <summary>
    /// The hard ceiling on commands one AI seat may place in one turn, checked before anything else.
    /// </summary>
    /// <remarks>
    /// The soak's non-termination hazard has two halves. The turn cap
    /// (<see cref="AiGameRunner"/>) bounds the number of turns; this bounds the work inside one turn, so
    /// that a scoring bug which keeps re-proposing an accepted-but-inert command cannot hang a single
    /// turn forever. It is set well above what the toy world can ever use — three cities, two armies and
    /// two fleets cannot generate 24 distinct useful actions in one turn, because an attack zeroes its
    /// army's moves and a recruitment order spends treasury — so on the shipped data it is a guard that
    /// never binds, which is exactly what a guard should be. <see cref="AiTurnOutcome.HitActionCap"/>
    /// reports when it does bind, and the soak asserts it never did.
    /// </remarks>
    public const int MaxActionsPerTurn = 24;

    /// <summary>
    /// The score a candidate must reach to be worth placing at all. One, not zero: a candidate scored at
    /// zero has had every positive term cancelled out and is not worth a command, but the gates —
    /// not this threshold — are what keep illegal or pointless actions out. Kept as a named constant so
    /// that "the AI did nothing this turn" always has one place to look.
    /// </summary>
    public const long MinimumActionScore = 1;

    // ---------------------------------------------------------------------------------------------
    // Per-phase base scores. These set the ORDER the AI prefers kinds of action in when their
    // situational terms are equal.
    //
    // Review round 1: a second sentence here used to claim the spread between them was "deliberately wide
    // enough that a marginal situational term cannot reorder two different kinds of action by accident".
    // That was simply false of these numbers -- the smallest gaps are 100 (1300/1200, 1000/900/800,
    // 600/500) against situational terms of 800, up to 2000, 150 per tile and a doubling victory
    // multiplier -- so situational terms reorder kinds of action routinely, by design. Deleted rather
    // than softened: the true statement is the first sentence, which was always there.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Base score for besieging an adjacent enemy city. The highest of all of them, because taking a city
    /// is the only action in the game that moves a nation toward the shipped victory condition
    /// (<c>totalConquest</c>: own every city — <c>VictoryEvaluator.EvaluateTotalConquest</c>), and the
    /// only one that can end a soak seed before its turn cap.
    /// </summary>
    public const long BesiegeCityBaseScore = 6000;

    /// <summary>
    /// Base score for attacking an adjacent enemy army. Below a siege because destroying an army does not
    /// itself take ground, and above everything else because it is the action that removes an opponent's
    /// ability to take ground back.
    /// </summary>
    public const long AttackArmyBaseScore = 3000;

    /// <summary>
    /// Base score for attacking an adjacent enemy fleet. Below a land attack because, on any world where
    /// cities are taken by armies, a fleet is a means rather than an end — and because a naval battle's
    /// outcome (unlike a field battle's or a siege's) is <em>not</em> a pure function of the state, so the
    /// AI is betting on an estimate rather than on arithmetic. See <see cref="AiMilitaryPhase"/>.
    /// </summary>
    public const long AttackFleetBaseScore = 2000;

    /// <summary>Base score for marching an army at an enemy city it is not yet adjacent to.</summary>
    public const long ApproachCityBaseScore = 1500;

    /// <summary>
    /// Base score for sailing a fleet at an enemy fleet. Below an army's march because the objective is
    /// worth less — a fleet takes no ground — and above a reinforcement because a fleet has nothing else
    /// it can usefully do with its moves.
    /// </summary>
    public const long SailAtFleetBaseScore = 1300;

    /// <summary>
    /// Base score for marching an army at one of its own cities that a hostile army is standing next to.
    /// Below an approach, so that an AI with nothing else to do advances rather than shuffling defensively
    /// — an AI that always reinforces never ends a game, and a soak of games that never end is the hazard
    /// this task's entry names.
    /// </summary>
    public const long ReinforceCityBaseScore = 1200;

    /// <summary>Base score for placing a standing-recruitment order.</summary>
    public const long RecruitBaseScore = 1000;

    /// <summary>
    /// Base score for mobilizing a fully ready standing-recruitment slot into an army unit —
    /// <c>docs/task-catalogue.md</c> T57, the AI side of T55's
    /// <see cref="Recruitment.Commands.MobilizeRecruitSlotCommand"/>. Set above
    /// <see cref="RecruitBaseScore"/> and below <see cref="ReinforceCityBaseScore"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Why above recruiting.</strong> A ready slot has already been paid for
    /// (<see cref="Recruitment.Commands.RecruitStandingUnitCommandHandler"/> debits the treasury the
    /// moment the order is placed) and has already reached its permanent quality
    /// (<see cref="Recruitment.MobilizationReadiness.QualityFor"/> — mobilizing later never improves
    /// it). Converting it costs nothing further and carries no risk, so it should never lose to spending
    /// fresh treasury on a brand new order: an AI that keeps recruiting while a ready unit sits
    /// uncollected is accumulating idle assets rather than growing its army, which is exactly the gap
    /// this task closes — <c>docs/task-catalogue.md</c> T57's own Scope line: before it, <c>grep -rn
    /// "Mobiliz" src/IC2.Engine/Ai/</c> returned nothing, so every ready recruit stayed a recruit.
    /// </para>
    /// <para>
    /// <strong>Why below reinforcing.</strong> A besieged or threatened city's own defence still
    /// outranks a routine conversion elsewhere on the map — the same ordering
    /// <see cref="ThreatenedCityBonus"/> already gives reinforcement over economy work. Mobilization is
    /// not given a threat bonus of its own, because the unit it creates never joins the training city's
    /// garrison: <see cref="Cities.Capture.CompleteDefenderStrength.GarrisonTerm"/> counts a slot only
    /// while it is still <em>pending</em>, so mobilizing trades one form of defensive value (the
    /// garrison term) for another (a field army standing beside the city) rather than adding to it.
    /// <see cref="Ai.AiEconomyPhase"/> declines to mobilize a slot whose city is currently under siege
    /// for exactly that reason — see its own remarks.
    /// </para>
    /// <para>
    /// <strong><c>[designed]</c>, and what was searched:</strong> this file's own header remark's search
    /// (every report in the research repository, for an AI decision rule or priority) plus
    /// <c>decompiled-mobilization-and-mercenary-restock.md</c> itself, specifically for a stated
    /// priority between the original's mobilization pass and its other AI actions. Nothing was found —
    /// the original's <c>FUN_004504f4</c> mobilizes inside a budget loop with no comparison to any other
    /// action class, a shape this engine's per-action scoring has no analogue for.
    /// </para>
    /// </remarks>
    public const long MobilizeReadyRecruitBaseScore = 1100;

    /// <summary>
    /// Base score for proposing peace. Above recruitment because a nation that has decided it is losing
    /// should act on that before it spends, and below any military action because the military phase's
    /// own gates have already refused to attack if the ratio is bad.
    /// </summary>
    public const long MakePeaceBaseScore = 900;

    /// <summary>Base score for ordering fortification at an owned city.</summary>
    public const long FortifyBaseScore = 800;

    /// <summary>Base score for proposing an alliance.</summary>
    public const long ProposeAllianceBaseScore = 600;

    /// <summary>Base score for proposing trade. The least urgent thing a turn can contain.</summary>
    public const long ProposeTradeBaseScore = 500;

    // ---------------------------------------------------------------------------------------------
    // Situational terms.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The strength ratio, in permille, that an attack must show at <c>aggression = 0</c>: 2.2 times the
    /// defender. Paired with <see cref="RequiredAttackRatioAtFullAggressionPermille"/>, this is the whole
    /// of <c>docs/game-design.md</c> §AI's "if <c>aggression</c> and a favorable strength ratio both clear
    /// a threshold" — one line of it each. The value is chosen so that a timid nation demands a margin
    /// large enough to survive the casualties a won battle still costs, rather than merely to win it.
    /// </summary>
    public const long RequiredAttackRatioAtZeroAggressionPermille = 2200;

    /// <summary>
    /// The strength ratio an attack must show at <c>aggression = 1</c>: 0.9 times the defender — that is,
    /// a fully aggressive nation will attack at a slight disadvantage, but not at any disadvantage. Below
    /// 1.0 deliberately: a field battle and a siege are both decided by <c>defender &lt; attacker</c>
    /// exactly (<c>InstantBattleResolver</c>), so a threshold at or above 1000 would mean a maximally
    /// aggressive AI never takes a losing fight, which is not what "aggression 1.0" should mean, and a
    /// threshold far below it would mean throwing armies away.
    /// </summary>
    public const long RequiredAttackRatioAtFullAggressionPermille = 900;

    /// <summary>
    /// How much of the strength ratio is allowed into an attack's score, capped. Without a cap, one
    /// overwhelming matchup would outrank every other action the nation could take for the rest of the
    /// game; with it, "obviously winnable" saturates and the base scores keep deciding the order.
    /// </summary>
    public const long MaxRatioScoreContribution = 2000;

    /// <summary>
    /// What a threatened city adds to the score of every action that answers the threat — reinforcing it,
    /// fortifying it, recruiting into it. "Threatened" is not this task's invention: it is
    /// <see cref="Economy.HostileArmyAdjacent.IsThreatened"/>, the original's own confirmed nine-cell
    /// test (<c>city-population-growth.md</c>), called rather than reimplemented. Set to twice the spread
    /// between <see cref="FortifyBaseScore"/> and <see cref="ReinforceCityBaseScore"/> so that a real
    /// threat reliably outranks routine economy work, and nothing smaller does.
    /// </summary>
    public const long ThreatenedCityBonus = 800;

    /// <summary>
    /// How far a score decays per tile of distance between an army and what it is marching at. Chosen
    /// against <see cref="ApproachCityBaseScore"/>: at 150 a march loses its base score after ten tiles,
    /// so on any map an army prefers the nearer of two equally valuable objectives and never sets out
    /// across the whole world at one that is not.
    /// </summary>
    public const long DistancePenaltyPerTile = 150;

    /// <summary>
    /// The weight victory-awareness puts on a nation's progress toward the victory condition. A nation
    /// holding a fraction <c>p</c> of the map's cities multiplies every city-taking action's score by
    /// <c>(1000 + 1000·p)/1000</c> — so a nation one city from the win values that city twice what a
    /// nation at the start of the game values its first. This is
    /// <c>docs/game-design.md</c> §AI phase 4, "<em>nations close to the scenario's victory condition
    /// weight their decisions toward securing it</em>", read exactly as written: it changes what an AI
    /// <em>prefers</em>, never what it is <em>willing</em> to do. The attack gate stays a pure function of
    /// aggression and the strength ratio, so that the personality demonstration
    /// (<c>docs/task-catalogue.md</c> T22 Done-when 3) tests one variable rather than two.
    /// </summary>
    public const long VictoryProgressWeight = 1000;

    /// <summary>
    /// The share of the treasury an AI with <c>expansionDrive = 0</c> will commit in one turn: 10%.
    /// <c>docs/game-design.md</c> §AI phase 1 asks for "an affordability threshold scaled by
    /// <c>expansionDrive</c>"; this is that threshold's floor.
    /// </summary>
    public const long TreasuryCommitFloorPermille = 100;

    /// <summary>
    /// How much more of the treasury full <c>expansionDrive</c> unlocks, so the band runs 10% to 60%.
    /// Capped well below the whole treasury because upkeep is billed quarterly
    /// (<see cref="Economy.QuarterlyEconomySystem"/>) and a nation that spends everything it has on
    /// recruitment goes into debt and is deposed by T39's rule — an AI that bankrupts itself every quarter
    /// is not an AI, it is a bug with a personality file.
    /// </summary>
    public const long TreasuryCommitExpansionPermille = 500;

    /// <summary>
    /// How many standing-recruitment orders the AI will leave open at one city at a time. Two, because a
    /// recruitment slot's only effect before it completes is the defender-strength garrison term
    /// (<see cref="Cities.Capture.CompleteDefenderStrength.GarrisonTerm"/>), and piling orders into one
    /// city converts treasury into that term with no diminishing return — a degenerate strategy that
    /// would make every soak seed look the same.
    /// </summary>
    public const int MaxOpenRecruitmentOrdersPerCity = 2;

    /// <summary>
    /// The largest fortification order the AI will place in one command. The order's own ceiling
    /// (<c>FortificationCode.MaxOrderablePoints</c>) and the treasury both bind first; this keeps a rich
    /// AI from converting a whole quarter's income into one city's walls in a single turn.
    /// </summary>
    public const int MaxFortifyPointsPerOrder = 10;

    /// <summary>
    /// The army-strength ratio, in permille, below which a nation at war considers itself to be losing and
    /// will sue for peace: its own total field strength under 60% of the enemy's.
    /// <c>docs/game-design.md</c> §AI phase 3 asks for "seek peace if losing and strength ratio is poor";
    /// both halves are this one number, measured with <see cref="Strength.ArmyPower.Compute"/> — the same
    /// function <see cref="Diplomacy.HonourablePeaceGate"/> measures the confirmed honourable-peace test
    /// with, so the AI's idea of "losing" and the engine's cannot drift apart.
    /// </summary>
    public const long SuePeaceStrengthRatioPermille = 600;

    /// <summary>
    /// What a shared enemy adds to an alliance proposal's score — <c>docs/game-design.md</c> §AI phase 3,
    /// "consider alliance offers from nations with a shared enemy". Large enough to lift an alliance above
    /// a trade proposal outright, since a shared war is the one situation in which the design names an
    /// alliance as the right move.
    /// </summary>
    public const long SharedEnemyBonus = 400;
}
