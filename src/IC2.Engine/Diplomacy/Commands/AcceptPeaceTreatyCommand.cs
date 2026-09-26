using IC2.Engine.Core;
using IC2.Engine.Diplomacy;
using IC2.Engine.Model;

namespace IC2.Engine.Diplomacy.Commands;

/// <summary>
/// The human's Yes to a pending post-battle treaty offer (T88, DoD 3) — <c>TBattlePols_Yes</c>
/// <strong>[confirmed: decompiled-war-cascade-and-peace-paths.md §2.3]</strong>, which "calls
/// <c>FUN_00450C68(W, L)</c> and closes with <c>ModalResult 6</c>". Always writes the honourable branch,
/// never reparations — see <see cref="PeaceTreatySystem.ApplyHumanConsentedPeace"/>'s own remarks.
/// </summary>
/// <remarks>
/// <strong>Where the pending offer lives.</strong> <c>Battle.PeaceTreatyOffered</c> carries only the two
/// nation ids — no in-progress "offer" is stored in <see cref="Model.GameState"/> itself, so this command
/// is not "accept the state's own pending offer" the way <c>AcceptPendingOfferCommand</c> is for a
/// trade/alliance offer. <c>Presentation.GameSession</c> is what remembers a
/// <see cref="Battle.PeaceTreatyOffered"/> it has seen and supplies the winner/loser ids back here when
/// the human answers Yes — see that type's own remarks for why (a battle can be resolved from inside an
/// AI seat's own turn, when the human is not "at the prompt" to answer immediately, so the decision has
/// to survive past that call without blocking the AI's turn loop; <see cref="Model.GameState"/> is outside
/// this task's Owns list, so the pending decision cannot live there either).
/// </remarks>
/// <param name="IssuingNationId">
/// The human seat answering — <see cref="Core.ICommand"/>'s own contract ("every command names the
/// nation issuing it", checked against the active seat before any handler runs), so this is always
/// whichever of <paramref name="WinnerNationId"/>/<paramref name="LoserNationId"/> is human: the CLI only
/// ever lets the human answer while paused on that seat's own turn.
/// </param>
/// <param name="WinnerNationId">The battle's winner, as <see cref="Battle.PeaceTreatyOffered"/> named it.</param>
/// <param name="LoserNationId">The battle's loser.</param>
public sealed record AcceptPeaceTreatyCommand(
    string IssuingNationId, string WinnerNationId, string LoserNationId) : ICommand
{
    /// <inheritdoc/>
    public string Kind => "diplomacy.accept-peace-treaty";
}

/// <summary>Rejection codes <see cref="AcceptPeaceTreatyCommandHandler"/> declares.</summary>
public static class AcceptPeaceTreatyRejections
{
    /// <summary>The command names a nation id the state does not contain (defensive; never expected from <see cref="Presentation.GameSession"/>).</summary>
    public static readonly RejectionCode UnknownNation = new("diplomacy.unknown-nation");

    /// <summary>
    /// The pair is no longer at war — the war this treaty would end already ended some other way (a
    /// second battle's own treaty, an elimination, the human's own <c>make-peace</c>) since the offer was
    /// raised. Defensive: nothing in this build's CLI lets a second command run ahead of a pending offer's
    /// own answer, but the handler does not assume that of every future caller.
    /// </summary>
    public static readonly RejectionCode NotAtWar = new("diplomacy.not-at-war");

    /// <summary>
    /// Rework round 1, B3(c): <see cref="AcceptPeaceTreatyCommand.IssuingNationId"/> names a nation
    /// that is neither this treaty's <see cref="AcceptPeaceTreatyCommand.WinnerNationId"/> nor its
    /// <see cref="AcceptPeaceTreatyCommand.LoserNationId"/> — the command dispatcher's own active-seat
    /// check (<c>CommandDispatcher</c>) only proves the issuer is whoever's turn it currently is, not that
    /// they are a party to this particular war. Without this, any third nation, active at the moment a
    /// stale offer is answered, could accept a peace it has no part in — the gate-free AI-to-human peace
    /// writer bug #384 exists to close.
    /// </summary>
    public static readonly RejectionCode IssuerNotPartyToTreaty = new("diplomacy.issuer-not-party-to-treaty");

    /// <summary>
    /// Rework round 1, B3(c): the issuer is a party to the treaty but is not human-controlled. The whole
    /// point of the post-battle treaty (report §2.3, #384) is that only the human's own Yes/No answers it
    /// — an AI never ends a war with a human without consent. Without this check, the AI side of a treaty
    /// could accept its own offer (or, in hotseat, a second human seat could accept an offer addressed to
    /// the first human, once it becomes the active seat) — the same consent hole either way.
    /// </summary>
    public static readonly RejectionCode IssuerNotHuman = new("diplomacy.issuer-not-human");
}

/// <inheritdoc cref="AcceptPeaceTreatyCommand"/>
[CommandHandler]
public sealed class AcceptPeaceTreatyCommandHandler : ICommandHandler<AcceptPeaceTreatyCommand>
{
    /// <inheritdoc/>
    public CommandOutcome Handle(AcceptPeaceTreatyCommand command, CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        var state = context.State;
        var ruleset = context.Ruleset;

        var winner = state.NationById(command.WinnerNationId);
        var loser = state.NationById(command.LoserNationId);
        if (winner is null || loser is null)
        {
            return CommandOutcome.Reject(
                AcceptPeaceTreatyRejections.UnknownNation, "The treaty's own nations are not both known.");
        }

        var issuerIsWinner = string.Equals(command.IssuingNationId, command.WinnerNationId, StringComparison.Ordinal);
        var issuerIsLoser = string.Equals(command.IssuingNationId, command.LoserNationId, StringComparison.Ordinal);
        if (!issuerIsWinner && !issuerIsLoser)
        {
            return CommandOutcome.Reject(
                AcceptPeaceTreatyRejections.IssuerNotPartyToTreaty,
                $"'{command.IssuingNationId}' is not a party to the war between "
                + $"'{winner.Name}' and '{loser.Name}'.");
        }

        var issuer = issuerIsWinner ? winner : loser;
        if (issuer.Control != SeatControl.Human)
        {
            return CommandOutcome.Reject(
                AcceptPeaceTreatyRejections.IssuerNotHuman,
                $"'{issuer.Name}' is not human-controlled, so it cannot answer a peace treaty offer.");
        }

        if (state.Relations.Get(command.WinnerNationId, command.LoserNationId) != ruleset.Diplomacy.StateCodes.War)
        {
            return CommandOutcome.Reject(
                AcceptPeaceTreatyRejections.NotAtWar,
                $"'{winner.Name}' and '{loser.Name}' are no longer at war.");
        }

        state = PeaceTreatySystem.ApplyHumanConsentedPeace(
            state, ruleset, context.World, command.WinnerNationId, command.LoserNationId);

        return CommandOutcome.Accept(state);
    }
}
