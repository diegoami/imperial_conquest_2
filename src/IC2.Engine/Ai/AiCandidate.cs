using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Ai;

/// <summary>Which of <c>docs/game-design.md</c> §AI's four phases proposed a candidate.</summary>
public enum AiPhase
{
    /// <summary>Phase 1: recruit and build, scaled by <c>expansionDrive</c>.</summary>
    Economy,

    /// <summary>Phase 2: threat, reinforcement, and attack.</summary>
    Military,

    /// <summary>Phase 3: peace, alliances and trade.</summary>
    Diplomacy,
}

/// <summary>
/// One action the AI could take this turn, already checked against every gate its handlers apply, already
/// scored, and carrying the sentence that explains the score.
/// </summary>
/// <remarks>
/// <para>
/// <strong>A candidate is a <em>sequence</em> of commands, not one command.</strong> The original issues
/// an attack as a single click, but that click does two things in a confirmed order:
/// <c>TUnitMap_SelectUnit</c> "calls <c>FUN_00449B40(you, them, 3)</c> <em>before</em> resolving the
/// attack. Attacking <strong>is</strong> declaring war"
/// (<c>decompiled-diplomacy-peace-terms-and-instant-battles.md</c>). This engine spells that as
/// <see cref="Diplomacy.Commands.DeclareWarCommand"/> then the attack, and
/// <see cref="Battle.Commands.AttackLegality"/> refuses an attack that would resolve without the
/// declaration. So the unit the AI weighs and commits to is the pair, never half of it — a candidate that
/// could declare war and then find its attack refused would be exactly the "zero rejected commands"
/// failure this type exists to prevent.
/// </para>
/// <para>
/// <strong>Nothing constructs a candidate that has not been gated.</strong> Every producer in
/// <see cref="AiEconomyPhase"/>, <see cref="AiMilitaryPhase"/> and <see cref="AiDiplomacyPhase"/> checks
/// the same conditions its command's handler checks, against the state the command will actually be
/// decided against, before it builds one. For the three attack commands that check is literally the
/// handler's own: <see cref="Battle.Commands.AttackLegality.Check(GameState, Ruleset, Battle.Commands.AttackArmyCommand)"/>
/// is the entire legality step of the handler as well as the AI's probe, so the two cannot drift.
/// </para>
/// </remarks>
/// <param name="Phase">Which design phase proposed this.</param>
/// <param name="Kind">A short stable label for the log, e.g. <c>"besiege"</c>.</param>
/// <param name="Commands">The commands to dispatch, in order. Never empty.</param>
/// <param name="Score">The candidate's score on <see cref="AiWeights"/>'s shared scale.</param>
/// <param name="Rationale">One line saying how the score was reached, written into the per-seed log.</param>
/// <param name="SubjectId">
/// The unit this candidate commits, when it commits one: an army id for a march, an attack or a siege, a
/// fleet id for a naval attack, and <see langword="null"/> for anything that commits no unit. Read by
/// <see cref="AiTurn"/> to enforce one march per army per turn — see
/// <see cref="AiMilitaryPhase.Propose"/>'s remarks for why that rule exists.
/// </param>
public sealed record AiCandidate(
    AiPhase Phase,
    string Kind,
    IReadOnlyList<ICommand> Commands,
    long Score,
    string Rationale,
    string? SubjectId = null)
{
    /// <summary>The label a march candidate carries, and the one <see cref="AiTurn"/> rations per army.</summary>
    public const string ApproachKind = "approach";

    /// <summary>The second march label — the same ration applies to it.</summary>
    public const string ReinforceKind = "reinforce";

    /// <summary>The naval march label — the same ration applies to it, per fleet.</summary>
    public const string SailKind = "sail";

    /// <summary>Whether this candidate is a march, and therefore rationed one per unit per turn.</summary>
    public bool IsMarch =>
        string.Equals(Kind, ApproachKind, StringComparison.Ordinal)
        || string.Equals(Kind, ReinforceKind, StringComparison.Ordinal)
        || string.Equals(Kind, SailKind, StringComparison.Ordinal);

    /// <summary>Builds a single-command candidate.</summary>
    public static AiCandidate Single(
        AiPhase phase, string kind, ICommand command, long score, string rationale, string? subjectId = null) =>
        new(phase, kind, new[] { command }, score, rationale, subjectId);

    /// <summary>Builds a two-command candidate — the declare-war-then-attack pair.</summary>
    public static AiCandidate Pair(
        AiPhase phase, string kind, ICommand first, ICommand second, long score, string rationale,
        string? subjectId = null) =>
        new(phase, kind, new[] { first, second }, score, rationale, subjectId);
}
