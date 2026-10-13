using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Battle.Commands;

/// <summary>
/// The write-back of a finished tactical battle onto the game (T125): the order the session issues when the
/// battle fought on the board ends, and the input of <see cref="ApplyTacticalBattleCommandHandler"/>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>[designed]</strong>, the shape only. Searched and found empty: <c>2026-10-04-decompiled-tactical-battle-rules.md</c> (the "After the battle" steps
/// and the TBattleOver description) and <c>decompiled-diplomacy-peace-terms-and-instant-battles.md</c> settle WHAT the write-back does (the order of its
/// effects and its draws, confirmed), and say nothing about how a clone packages it: the original runs it inline in <c>TBattleOver_OK</c>, with no
/// command or registry. The command shape below is therefore this engine's default, chosen to fit the engine's rules.
/// </para>
/// <para>
/// <strong>Two conflicts with the task entry, reported.</strong> (1) The command is deliberately not an
/// <see cref="ICommand"/>: the engine's reflection-driven checks (<c>CommandCoverageTests</c>,
/// <c>CommandOutcomeWordingTests</c>, the Godot <c>CommandVerbCatalog</c>) list every <see cref="ICommand"/> type and
/// demand a CLI verb, an accepted golden line and a readable wording for each, and the entry forbids changing those
/// tests and <c>godot/</c>. The write-back is an internal step of a battle that an <c>attack-army</c> order opened,
/// not a verb of its own, so it has the shape of a command (an issuer, a kind, a handler that accepts or rejects,
/// one advance of the state's random seed) without joining the registry. (2) <c>BattleDeterminismTests</c> forbids
/// any file under <c>Battle/</c> outside <c>Battle/Tactical/</c> to name the tactical battle's types, so this file
/// cannot call the tactical write-back itself: the session, which may, hands the actual write-back in as
/// <see cref="WriteBack"/>, and the handler runs the checks around it.
/// </para>
/// <para>
/// The write-back draws from the battle's own stream (the promotions, then the peace dialog's draw), which the
/// session's closure holds; no stream of this command is used.
/// </para>
/// </remarks>
/// <param name="IssuingNationId">The nation that ordered the attack, the active seat.</param>
/// <param name="AttackerArmyId">The battle's attacking army.</param>
/// <param name="DefenderArmyId">The battle's defending army.</param>
/// <param name="WriteBack">
/// The write-back itself (steps 1 to 5 of the original's battle-over routine): the game before it and the sink for its
/// news event in, the game after it out.
/// </param>
public sealed record ApplyTacticalBattleCommand(
    string IssuingNationId,
    string AttackerArmyId,
    string DefenderArmyId,
    Func<GameState, IEventSink, GameState> WriteBack)
{
    /// <summary>The command's kind, in the engine's <c>area.verb</c> form.</summary>
    public string Kind => "battle.apply-tactical";
}

/// <summary>The rejection codes of <see cref="ApplyTacticalBattleCommandHandler"/>.</summary>
public static class ApplyTacticalBattleRejections
{
    /// <summary>The issuing nation is not a nation of this game.</summary>
    public static readonly RejectionCode UnknownNation = new("battle.tactical-unknown-nation");

    /// <summary>The issuing nation has been eliminated.</summary>
    public static readonly RejectionCode Eliminated = new("battle.tactical-nation-eliminated");

    /// <summary>The issuing nation is not the active seat.</summary>
    public static readonly RejectionCode NotActiveSeat = new("battle.tactical-not-active-seat");

    /// <summary>One of the battle's armies is no longer in the game.</summary>
    public static readonly RejectionCode UnknownArmy = new("battle.tactical-unknown-army");

    /// <summary>The issuing nation is neither the attacker's nor the defender's.</summary>
    public static readonly RejectionCode NotAParty = new("battle.tactical-not-a-party");
}
