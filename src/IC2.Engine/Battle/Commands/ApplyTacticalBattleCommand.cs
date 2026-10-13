using IC2.Engine.Battle.Tactical;
using IC2.Engine.Core;

namespace IC2.Engine.Battle.Commands;

/// <summary>
/// The write-back of a finished tactical battle onto the game (T125): the order the session issues when the
/// battle fought on the board ends, and the input of <see cref="ApplyTacticalBattleCommandHandler"/>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Deliberately not an <see cref="ICommand"/> (a conflict with the task entry, reported).</strong> The
/// entry asks for the write-back to be "dispatched like any command". The engine's reflection-driven checks
/// (<c>CommandCoverageTests</c>, <c>CommandOutcomeWordingTests</c>, the Godot <c>CommandVerbCatalog</c>) list every
/// <see cref="ICommand"/> type and demand a CLI verb, an accepted golden line and a readable wording for each,
/// and the entry forbids changing those tests and <c>godot/</c>. The write-back is an internal step of a battle
/// that an <c>attack-army</c> order opened, not a verb of its own, so it has the shape of a command (an issuer, a
/// kind, a handler that accepts or rejects, one advance of the state's random seed) without joining the registry.
/// </para>
/// <para>
/// The battle's draw stream travels as <see cref="DrawState"/>, the raw state of the stream the session derived
/// at the battle's start, so the promotions' draws continue the battle's own stream and the session can take the
/// peace dialog's draw from the same stream afterwards.
/// </para>
/// </remarks>
/// <param name="IssuingNationId">The nation that ordered the attack, the active seat.</param>
/// <param name="Battle">The ended battle (<see cref="TacticalBattleState.IsOver"/>).</param>
/// <param name="DrawState">The raw state of the battle's draw stream (<see cref="IRng.State"/>).</param>
public sealed record ApplyTacticalBattleCommand(string IssuingNationId, TacticalBattleState Battle, ulong DrawState)
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

    /// <summary>The battle has not ended.</summary>
    public static readonly RejectionCode NotOver = new("battle.tactical-not-over");

    /// <summary>One of the battle's armies is no longer in the game.</summary>
    public static readonly RejectionCode UnknownArmy = new("battle.tactical-unknown-army");

    /// <summary>The issuing nation is neither the attacker's nor the defender's.</summary>
    public static readonly RejectionCode NotAParty = new("battle.tactical-not-a-party");
}
