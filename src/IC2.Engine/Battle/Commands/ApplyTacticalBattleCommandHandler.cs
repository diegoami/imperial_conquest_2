using IC2.Engine.Battle.Tactical;
using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Battle.Commands;

/// <summary>What <see cref="ApplyTacticalBattleCommandHandler"/> returns: the written-back game, or why not.</summary>
/// <param name="State">The game after the write-back, with the random seed advanced once; <see langword="null"/> when rejected.</param>
/// <param name="Report">The <em>Battle ended</em> window's contents; <see langword="null"/> when rejected.</param>
/// <param name="DrawState">The battle's draw stream after the promotions' draws.</param>
/// <param name="Rejection">The rejection, or <see langword="null"/> when accepted.</param>
public sealed record ApplyTacticalBattleOutcome(
    GameState? State,
    TacticalBattleReport? Report,
    ulong DrawState,
    CommandRejection? Rejection)
{
    /// <summary>Whether the write-back was applied.</summary>
    public bool IsAccepted => Rejection is null;
}

/// <summary>
/// Applies an ended tactical battle to the game: T123's write-back, steps 1 to 5 of <c>TBattleOver_OK</c>
/// (<see cref="TacticalWriteBack"/>). Step 6, the peace dialog, is the session's, because its routine is
/// diplomacy's and no file under <c>Battle/**</c> may reference that namespace
/// (<c>FieldBattleTests.AssertNoBattleSourceReferencesDiplomacy</c>).
/// </summary>
/// <remarks>
/// <strong>[confirmed: code, static, <c>2026-10-04-decompiled-tactical-battle-rules.md</c> §9; designed: the
/// command shape]</strong>. The checks are the dispatcher's own (a known, living issuer that is the active
/// seat), then the battle's. On acceptance the state's random seed advances once, as
/// <see cref="CommandDispatcher"/> does after every accepted command, so the next command sees a different
/// seed. The promotions draw from the battle's stream, never from a stream of this command.
/// </remarks>
public sealed class ApplyTacticalBattleCommandHandler
{
    /// <summary>Applies <paramref name="command"/> to <paramref name="state"/>.</summary>
    /// <param name="command">The ended battle and its draw stream.</param>
    /// <param name="state">The game the battle was fought in.</param>
    /// <param name="ruleset">The ruleset (the write-back's constants).</param>
    /// <param name="events">Where the news event is published.</param>
    /// <returns>The outcome: the new state and the report, or a rejection.</returns>
    public ApplyTacticalBattleOutcome Handle(
        ApplyTacticalBattleCommand command, GameState state, Ruleset ruleset, IEventSink events)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ruleset);
        ArgumentNullException.ThrowIfNull(events);

        if (state.NationById(command.IssuingNationId) is not { } issuer)
        {
            return Reject(command, ApplyTacticalBattleRejections.UnknownNation, $"'{command.IssuingNationId}' is not a nation in this game.");
        }

        if (issuer.Eliminated)
        {
            return Reject(command, ApplyTacticalBattleRejections.Eliminated, $"'{issuer.Id}' has been eliminated.");
        }

        if (!string.Equals(issuer.Id, state.ActiveNationId, StringComparison.Ordinal))
        {
            return Reject(command, ApplyTacticalBattleRejections.NotActiveSeat, $"'{issuer.Id}' is not the active seat.");
        }

        var battle = command.Battle;
        if (!battle.IsOver)
        {
            return Reject(command, ApplyTacticalBattleRejections.NotOver, "The battle has not ended.");
        }

        var attacker = state.ArmyById(battle.AttackerArmyId);
        var defender = state.ArmyById(battle.DefenderArmyId);
        if (attacker is null || defender is null)
        {
            return Reject(command, ApplyTacticalBattleRejections.UnknownArmy, "One of the battle's armies is no longer in the game.");
        }

        if (!string.Equals(issuer.Id, attacker.Nation, StringComparison.Ordinal)
            && !string.Equals(issuer.Id, defender.Nation, StringComparison.Ordinal))
        {
            return Reject(command, ApplyTacticalBattleRejections.NotAParty, $"'{issuer.Id}' is not a party to this battle.");
        }

        // The stream continues from where the battle left it; its state is the whole stream position.
        var rng = new SplitMix64Rng(command.DrawState);
        var context = TacticalContext.From(ruleset);
        var written = TacticalWriteBack.Apply(state, battle, context, new RngBattleDraws(rng), events);

        var next = written.Game with { RandomSeed = RngStreams.Advance(written.Game.RandomSeed) };
        return new ApplyTacticalBattleOutcome(next, written.Report, rng.State, null);
    }

    private static ApplyTacticalBattleOutcome Reject(ApplyTacticalBattleCommand command, RejectionCode code, string message) =>
        new(null, null, command.DrawState, new CommandRejection(code, message));
}
