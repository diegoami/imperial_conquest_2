using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Battle.Commands;

/// <summary>What <see cref="ApplyTacticalBattleCommandHandler"/> returns: the written-back game, or why not.</summary>
/// <param name="State">The game after the write-back, with the random seed advanced once; <see langword="null"/> when rejected.</param>
/// <param name="Rejection">The rejection, or <see langword="null"/> when accepted.</param>
public sealed record ApplyTacticalBattleOutcome(GameState? State, CommandRejection? Rejection)
{
    /// <summary>Whether the write-back was applied.</summary>
    public bool IsAccepted => Rejection is null;
}

/// <summary>
/// Applies an ended tactical battle to the game: the checks of a command, then the write-back the command carries
/// (steps 1 to 5 of the original's battle-over routine), then the one advance of the random seed every accepted
/// command makes. Step 6, the peace dialog, is the session's, because its routine is diplomacy's and no file under
/// <c>Battle/**</c> may reference that namespace (<c>FieldBattleTests.AssertNoBattleSourceReferencesDiplomacy</c>).
/// </summary>
/// <remarks>
/// <strong>[confirmed: code, static, <c>2026-10-04-decompiled-tactical-battle-rules.md</c> §9; designed: the
/// command shape]</strong>. The checks are the dispatcher's own (a known, living issuer that is the active seat),
/// then the battle's (both armies still in the game, the issuer a party to it). On acceptance the state's random seed
/// advances once, as <see cref="CommandDispatcher"/> does after every accepted command, so the next command sees a
/// different seed.
/// </remarks>
public sealed class ApplyTacticalBattleCommandHandler
{
    /// <summary>Applies <paramref name="command"/> to <paramref name="state"/>.</summary>
    /// <param name="command">The battle's parties and its write-back.</param>
    /// <param name="state">The game the battle was fought in.</param>
    /// <param name="events">Where the write-back's news event is published.</param>
    /// <returns>The outcome: the new state, or a rejection.</returns>
    public ApplyTacticalBattleOutcome Handle(ApplyTacticalBattleCommand command, GameState state, IEventSink events)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(events);

        if (state.NationById(command.IssuingNationId) is not { } issuer)
        {
            return Reject(ApplyTacticalBattleRejections.UnknownNation, $"'{command.IssuingNationId}' is not a nation in this game.");
        }

        if (issuer.Eliminated)
        {
            return Reject(ApplyTacticalBattleRejections.Eliminated, $"'{issuer.Id}' has been eliminated.");
        }

        if (!string.Equals(issuer.Id, state.ActiveNationId, StringComparison.Ordinal))
        {
            return Reject(ApplyTacticalBattleRejections.NotActiveSeat, $"'{issuer.Id}' is not the active seat.");
        }

        var attacker = state.ArmyById(command.AttackerArmyId);
        var defender = state.ArmyById(command.DefenderArmyId);
        if (attacker is null || defender is null)
        {
            return Reject(ApplyTacticalBattleRejections.UnknownArmy, "One of the battle's armies is no longer in the game.");
        }

        if (!string.Equals(issuer.Id, attacker.Nation, StringComparison.Ordinal)
            && !string.Equals(issuer.Id, defender.Nation, StringComparison.Ordinal))
        {
            return Reject(ApplyTacticalBattleRejections.NotAParty, $"'{issuer.Id}' is not a party to this battle.");
        }

        var written = command.WriteBack(state, events);
        return new ApplyTacticalBattleOutcome(written with { RandomSeed = RngStreams.Advance(written.RandomSeed) }, null);
    }

    private static ApplyTacticalBattleOutcome Reject(RejectionCode code, string message) =>
        new(null, new CommandRejection(code, message));
}
