using System.Globalization;
using IC2.Engine.Armies.Commands;
using IC2.Engine.Battle.Commands;
using IC2.Engine.Cities.Orders;
using IC2.Engine.Core;
using IC2.Engine.Diplomacy.Commands;
using IC2.Engine.Model;
using IC2.Engine.Naval.Commands;
using IC2.Engine.News;
using IC2.Engine.Recruitment.Commands;

namespace IC2.Engine.Presentation;

/// <summary>
/// <c>docs/task-catalogue.md</c> T23 Done-when 1: extends <see cref="GameSession"/>'s command set from
/// T41's two (<c>move</c>, <c>buy</c>) to every command type the engine declares. Every verb here follows
/// the same shape T41 established: parse the line, build the real typed <see cref="ICommand"/>, dispatch
/// it through the same <see cref="CommandDispatcher"/> a Godot seat will use, and route the result's
/// events through T10's <see cref="NewsLogWriter.Append(GameState, IEnumerable{DomainEvent}, NewsLogRules, Func{string, string}?)"/>
/// exactly as T41's <c>HandleMove</c>/<c>HandleBuy</c> already do (the hazard this task's entry names:
/// "events from commands dispatched between runs never reach <see cref="SystemContext.PublishedEvents"/>").
/// </summary>
/// <remarks>
/// <para>
/// <strong>The rendering is deliberately generic.</strong> <c>move</c> and <c>buy</c> keep their own
/// bespoke, richly-worded lines (T41's contract, and <c>SupplyPurchaseDemoTests</c> pins their exact
/// wording), but every command added here prints only <c>"{kind} accepted."</c> or
/// <c>"{kind} rejected ({code}): {message}"</c> — <see cref="IssueCommand"/>, used by every method below.
/// Twenty-four more bespoke renderers would each be a place a future change to an event's shape could
/// silently stop being exercised by anything; a generic renderer built from <see cref="ICommand.Kind"/>
/// and the dispatcher's own typed result cannot drift from what actually happened, and it is what proves
/// Done-when 1's determinism claim just as well as prose would — the golden transcript pins the exact
/// kind, code and message for every line.
/// </para>
/// <para>
/// <strong>Not every order in the demo script succeeds, and that is by design.</strong> The toy scenario
/// starts each nation with one army and one fleet, no mercenary pool, no queued recruitment slots and no
/// pending diplomatic offer, so several of these commands are legitimately refused by their own gates —
/// exactly the same typed rejection a Godot seat would see. Only <c>battle.attack-army</c>,
/// <c>battle.besiege-city</c> and <c>battle.attack-fleet</c> draw from <see cref="CommandContext.Rng"/> (T16's
/// resolver); every other command here is deterministic by construction (confirmed by inspection: no
/// <c>Armies</c>, <c>Naval</c>, <c>Recruitment</c>, <c>Diplomacy</c> or <c>Cities/Orders</c> command handler
/// reads <see cref="CommandContext.Rng"/>). The demo script therefore issues the three battle commands
/// against a target that fails a gate <em>before</em> the resolver ever runs (an army or fleet attacking
/// itself, a siege on the issuer's own city) — proving the command is wired in through a real typed
/// rejection without adding a fourth random draw to a transcript whose seed-sensitivity is already pinned
/// exactly (<see cref="Tests.Presentation.GameSessionTests"/>). The composed declare-war-then-attack
/// behaviour Done-when 4's #221 settles is proven instead by dedicated tests
/// (<c>AttackComposesDeclareWarTests</c>), independent of the golden transcript.
/// </para>
/// </remarks>
public sealed partial class GameSession
{
    /// <summary>
    /// Dispatches <paramref name="command"/> and renders a generic outcome line — see this class's
    /// remarks for why every command added by this task shares one renderer.
    /// </summary>
    /// <remarks>
    /// <strong>The single choke point every mutating command here funnels through</strong> is exactly why
    /// <c>docs/tasks/T83.md</c>'s watch-mode/seat-lost gate (<see cref="IsWatchModeActive"/>) lives here
    /// rather than as a verb allowlist in <see cref="Submit"/> — review round 1, N5: gating a fixed list of
    /// verb strings could not tell an unrecognised verb (a typo) from a real, refused command, so a typo
    /// like <c>stauts</c> was reported as a watch-mode rejection instead of "Unknown command". Gating here
    /// instead means an unrecognised verb never reaches this method at all, and still falls through to
    /// <see cref="Submit"/>'s own <c>default</c> case. <see cref="HandleMove"/> and <see cref="HandleBuy"/>
    /// gate themselves the same way, being the only two mutating commands that do not call this method.
    /// </remarks>
    private IReadOnlyList<string> IssueCommand(ICommand command)
    {
        if (IsWatchModeActive)
        {
            return new[] { WatchModeRejectionLine(command.Kind) };
        }

        var result = _dispatcher.Dispatch(State, command);
        if (result.IsRejected)
        {
            return new[] { $"{command.Kind} rejected ({result.Code}): {result.Rejection!.Message}" };
        }

        State = NewsLogWriter.Append(result.State, result.Events, Ruleset.NewsLog);

        var lines = new List<string> { $"{command.Kind} accepted." };

        // T88 (DoD 3, Hazard 1): a human-issued attack-army command is one of the two places a battle can
        // raise a post-battle treaty for the human -- the other is an AI seat's own turn, captured from
        // PlayUntilOneFullLapOrRepeat instead. A no-op for every command that is not a battle (the vast
        // majority of what flows through this one choke point), since none of them can publish
        // Battle.PeaceTreatyOffered.
        CapturePeaceTreatyOfferIfAny(lines, result.Events);

        return lines;
    }

    /// <summary>
    /// <c>peace-yes</c>/<c>peace-no</c> (T88, DoD 3): answers the pending <see cref="_pendingPeaceTreatyOffer"/>,
    /// if any. Yes dispatches <see cref="Diplomacy.Commands.AcceptPeaceTreatyCommand"/>, which always
    /// writes the honourable peace and its news, never reparations
    /// (<see cref="Diplomacy.PeaceTreatySystem.ApplyHumanConsentedPeace"/>'s own remarks). No writes
    /// nothing at all -- the war simply continues, exactly as the report's own "<c>TBattlePols_No</c> sets
    /// <c>ModalResult 7</c> and does nothing else" reads. Either answer clears the pending offer, whether
    /// or not the underlying command turns out to still be legal (see
    /// <see cref="Diplomacy.Commands.AcceptPeaceTreatyRejections.NotAtWar"/>'s own remarks for the one way
    /// that can happen) -- a stale offer is never left pending forever.
    /// </summary>
    private IReadOnlyList<string> HandlePeaceTreatyAnswer(string[] tokens, bool accept)
    {
        if (tokens.Length != 1)
        {
            return new[] { $"Usage: {(accept ? "peace-yes" : "peace-no")}" };
        }

        if (_pendingPeaceTreatyOffer is not { } pending)
        {
            return new[] { "There is no pending peace treaty offer." };
        }

        _pendingPeaceTreatyOffer = null;

        if (!accept)
        {
            return new[] { "Peace treaty declined. The war continues." };
        }

        return IssueCommand(
            new AcceptPeaceTreatyCommand(State.ActiveNationId, pending.WinnerNationId, pending.LoserNationId));
    }

    /// <summary>
    /// Composes T19's <see cref="DeclareWarCommand"/> ahead of an attack order when the two nations are
    /// not already at war — <c>docs/task-catalogue.md</c> T23 Done-when 4, follow-up
    /// <see href="https://github.com/diegoami/imperial_conquest_2/issues/221">#221</see>. The original
    /// auto-declares war in the same click that orders the attack (<c>AttackLegality</c>'s own remarks:
    /// "Attacking IS declaring war"); this engine spells that as two commands, because T54's own
    /// decoupling guard refuses to let a file under <c>Battle/Commands</c> name the diplomacy namespace.
    /// This is where the two are composed back together — at the CLI boundary, which <em>can</em> see
    /// both. A caller that wants the declaration as its own order still has the standalone
    /// <c>declare-war</c> verb; this composition never runs when the two nations are already at war, when
    /// the target cannot be resolved (the attack command's own gates report that instead), or when the
    /// target is the issuing nation itself.
    /// </summary>
    /// <remarks>
    /// <strong>Review round 1, B2 (a regression this fixes): the watch-mode/seat-lost gate has to run
    /// before this method ever dispatches anything</strong>, not only before the attack/siege command that
    /// follows it. This method's own <see cref="_dispatcher"/> call is a second, separate dispatch outside
    /// <see cref="IssueCommand"/>'s choke point — <see cref="HandleAttackArmy"/> and
    /// <see cref="HandleBesiegeCity"/> both call this <em>before</em> their own <see cref="IssueCommand"/>
    /// call, so gating only the attack/siege command itself left a live path for a watch-mode or seat-lost
    /// session to still declare war "for free" ahead of a refused attack (proof, round 1's re-review: the
    /// real CLI in watch mode accepted <c>diplomacy.declare-war</c> from <c>besiege-city</c> even though
    /// the siege itself was correctly refused). Gating here, first, closes it at the source rather than
    /// requiring every future caller of this method to remember to gate ahead of it.
    /// </remarks>
    private void ComposeDeclareWarIfNeeded(string? targetNationId, List<string> lines)
    {
        if (IsWatchModeActive
            || targetNationId is null
            || string.Equals(targetNationId, State.ActiveNationId, StringComparison.Ordinal)
            || IsAtWar(State.ActiveNationId, targetNationId))
        {
            return;
        }

        var declare = new DeclareWarCommand(State.ActiveNationId, targetNationId);
        var declared = _dispatcher.Dispatch(State, declare);
        if (declared.IsRejected)
        {
            // Left for the attack's own gates to report -- declaring war is not itself the order issued.
            return;
        }

        State = NewsLogWriter.Append(declared.State, declared.Events, Ruleset.NewsLog);
        lines.Add($"{declare.Kind} accepted (composed ahead of the attack).");
    }

    // ---- battle (T54) ----

    private IReadOnlyList<string> HandleAttackArmy(string[] tokens)
    {
        if (tokens.Length != 3)
        {
            return new[] { "Usage: attack-army <army> <target-army>" };
        }

        var lines = new List<string>();
        ComposeDeclareWarIfNeeded(State.ArmyById(tokens[2])?.Nation, lines);
        lines.AddRange(IssueCommand(new AttackArmyCommand(State.ActiveNationId, tokens[1], tokens[2])));
        return lines;
    }

    private IReadOnlyList<string> HandleBesiegeCity(string[] tokens)
    {
        if (tokens.Length != 3)
        {
            return new[] { "Usage: besiege-city <army> <city>" };
        }

        var lines = new List<string>();
        ComposeDeclareWarIfNeeded(State.CityById(tokens[2])?.Owner, lines);
        lines.AddRange(IssueCommand(new BesiegeCityCommand(State.ActiveNationId, tokens[1], tokens[2])));
        return lines;
    }

    private IReadOnlyList<string> HandleAttackFleet(string[] tokens)
    {
        if (tokens.Length != 3)
        {
            return new[] { "Usage: attack-fleet <fleet> <target-fleet>" };
        }

        var lines = new List<string>();
        ComposeDeclareWarIfNeeded(State.FleetById(tokens[2])?.Nation, lines);
        lines.AddRange(IssueCommand(new AttackFleetCommand(State.ActiveNationId, tokens[1], tokens[2])));
        return lines;
    }

    // ---- armies ----

    private IReadOnlyList<string> HandleDisbandArmy(string[] tokens)
    {
        if (tokens.Length != 2)
        {
            return new[] { "Usage: disband-army <army>" };
        }

        return IssueCommand(new DisbandArmyCommand(State.ActiveNationId, tokens[1]));
    }

    private IReadOnlyList<string> HandleJoinArmies(string[] tokens)
    {
        if (tokens.Length != 3)
        {
            return new[] { "Usage: join-armies <survivor-army> <absorbed-army>" };
        }

        return IssueCommand(new JoinArmiesCommand(State.ActiveNationId, tokens[1], tokens[2]));
    }

    private IReadOnlyList<string> HandleJoinUnits(string[] tokens)
    {
        if (tokens.Length != 4
            || !int.TryParse(tokens[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var first)
            || !int.TryParse(tokens[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var second))
        {
            return new[] { "Usage: join-units <army> <unit-index> <unit-index>" };
        }

        return IssueCommand(new JoinUnitsCommand(State.ActiveNationId, tokens[1], first, second));
    }

    private IReadOnlyList<string> HandleSplitArmy(string[] tokens)
    {
        if (tokens.Length != 4
            || !int.TryParse(tokens[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var unitIndex))
        {
            return new[] { "Usage: split-army <army> <new-army> <unit-index>" };
        }

        return IssueCommand(
            new SplitArmyCommand(State.ActiveNationId, tokens[1], tokens[2], ValueList.Of(unitIndex)));
    }

    // ---- cities ----

    private IReadOnlyList<string> HandleOrderCity(string[] tokens)
    {
        if (tokens.Length != 4
            || !int.TryParse(tokens[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var points))
        {
            return new[] { "Usage: order-city <city> <order-id> <points>" };
        }

        return IssueCommand(new OrderCityCommand(State.ActiveNationId, tokens[1], tokens[2], points));
    }

    // ---- diplomacy ----

    private IReadOnlyList<string> HandleDeclareWar(string[] tokens)
    {
        if (tokens.Length != 2)
        {
            return new[] { "Usage: declare-war <nation>" };
        }

        return IssueCommand(new DeclareWarCommand(State.ActiveNationId, tokens[1]));
    }

    private IReadOnlyList<string> HandleMakePeace(string[] tokens)
    {
        if (tokens.Length != 2)
        {
            return new[] { "Usage: make-peace <nation>" };
        }

        return IssueCommand(new MakePeaceCommand(State.ActiveNationId, tokens[1]));
    }

    private IReadOnlyList<string> HandleProposeAlliance(string[] tokens)
    {
        if (tokens.Length != 2)
        {
            return new[] { "Usage: propose-alliance <nation>" };
        }

        return IssueCommand(new ProposeAllianceCommand(State.ActiveNationId, tokens[1]));
    }

    private IReadOnlyList<string> HandleProposeTrade(string[] tokens)
    {
        if (tokens.Length != 2)
        {
            return new[] { "Usage: propose-trade <nation>" };
        }

        return IssueCommand(new ProposeTradeCommand(State.ActiveNationId, tokens[1]));
    }

    private IReadOnlyList<string> HandleAcceptOffer(string[] tokens)
    {
        if (tokens.Length != 1)
        {
            return new[] { "Usage: accept-offer" };
        }

        return IssueCommand(new AcceptPendingOfferCommand(State.ActiveNationId));
    }

    // ---- recruitment ----

    private IReadOnlyList<string> HandleMobilize(string[] tokens)
    {
        if (tokens.Length != 3
            || !int.TryParse(tokens[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var slotIndex))
        {
            return new[] { "Usage: mobilize <slot-index> <new-army>" };
        }

        return IssueCommand(new MobilizeRecruitSlotCommand(State.ActiveNationId, slotIndex, tokens[2]));
    }

    private IReadOnlyList<string> HandleHireMercenary(string[] tokens)
    {
        if (tokens.Length != 3
            || !int.TryParse(tokens[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var poolSlotIndex))
        {
            return new[] { "Usage: hire-mercenary <army> <pool-slot-index>" };
        }

        return IssueCommand(new HireMercenaryCommand(State.ActiveNationId, tokens[1], poolSlotIndex));
    }

    private IReadOnlyList<string> HandleRecruitStanding(string[] tokens)
    {
        if (tokens.Length != 4
            || !int.TryParse(tokens[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var troops))
        {
            return new[] { "Usage: recruit-standing <city> <unit-type> <troops>" };
        }

        return IssueCommand(new RecruitStandingUnitCommand(State.ActiveNationId, tokens[1], tokens[2], troops));
    }

    // ---- naval ----

    private IReadOnlyList<string> HandleMoveFleet(string[] tokens)
    {
        if (tokens.Length != 4
            || !int.TryParse(tokens[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var x)
            || !int.TryParse(tokens[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var y))
        {
            return new[] { "Usage: move-fleet <fleet> <x> <y>" };
        }

        return IssueCommand(new MoveFleetCommand(State.ActiveNationId, tokens[1], x, y));
    }

    private IReadOnlyList<string> HandleOrderFleet(string[] tokens)
    {
        if (tokens.Length != 4
            || !int.TryParse(tokens[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ships))
        {
            return new[] { "Usage: order-fleet <city> <ships> <new-fleet>" };
        }

        return IssueCommand(new OrderFleetCommand(State.ActiveNationId, tokens[1], ships, tokens[3]));
    }

    private IReadOnlyList<string> HandleRepairFleet(string[] tokens)
    {
        if (tokens.Length != 3
            || !int.TryParse(tokens[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var points))
        {
            return new[] { "Usage: repair-fleet <fleet> <points>" };
        }

        return IssueCommand(new RepairFleetCommand(State.ActiveNationId, tokens[1], points));
    }

    private IReadOnlyList<string> HandleScuttleFleet(string[] tokens)
    {
        if (tokens.Length != 2)
        {
            return new[] { "Usage: scuttle-fleet <fleet>" };
        }

        return IssueCommand(new ScuttleFleetCommand(State.ActiveNationId, tokens[1]));
    }

    private IReadOnlyList<string> HandleSplitFleet(string[] tokens)
    {
        if (tokens.Length != 4
            || !int.TryParse(tokens[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var shipsToNewFleet))
        {
            return new[] { "Usage: split-fleet <fleet> <new-fleet> <ships>" };
        }

        return IssueCommand(new SplitFleetCommand(State.ActiveNationId, tokens[1], tokens[2], shipsToNewFleet));
    }

    private IReadOnlyList<string> HandleJoinFleets(string[] tokens)
    {
        if (tokens.Length != 3)
        {
            return new[] { "Usage: join-fleets <survivor-fleet> <absorbed-fleet>" };
        }

        return IssueCommand(new JoinFleetsCommand(State.ActiveNationId, tokens[1], tokens[2]));
    }

    private IReadOnlyList<string> HandleEmbarkArmy(string[] tokens)
    {
        if (tokens.Length != 3)
        {
            return new[] { "Usage: embark-army <army> <fleet>" };
        }

        return IssueCommand(new EmbarkArmyCommand(State.ActiveNationId, tokens[1], tokens[2]));
    }

    private IReadOnlyList<string> HandleDisembarkArmy(string[] tokens)
    {
        if (tokens.Length != 2)
        {
            return new[] { "Usage: disembark-army <army>" };
        }

        return IssueCommand(new DisembarkArmyCommand(State.ActiveNationId, tokens[1], null, null));
    }

    private IReadOnlyList<string> HandleBuyFleetSupply(string[] tokens)
    {
        if (tokens.Length != 4
            || !int.TryParse(tokens[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var tons))
        {
            return new[] { "Usage: buy-fleet-supply <fleet> <city> <tons>" };
        }

        return IssueCommand(new BuyFleetSupplyCommand(State.ActiveNationId, tokens[1], tokens[2], null, tons));
    }

    private IReadOnlyList<string> HandleFleetTransfer(string[] tokens)
    {
        if (tokens.Length != 6
            || !int.TryParse(tokens[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ships)
            || !int.TryParse(tokens[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var supplyTons)
            || !int.TryParse(tokens[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var money))
        {
            return new[] { "Usage: fleet-transfer <from-fleet> <to-fleet> <ships> <supply-tons> <money>" };
        }

        return IssueCommand(
            new FleetToFleetTransferCommand(State.ActiveNationId, tokens[1], tokens[2], ships, supplyTons, money));
    }
}
