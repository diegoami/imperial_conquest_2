using System.Globalization;
using IC2.Engine.Armies.Commands;
using IC2.Engine.Battle.Commands;
using IC2.Engine.Cities.Orders;
using IC2.Engine.Core;
using IC2.Engine.Diplomacy.Commands;
using IC2.Engine.Economy.Commands;
using IC2.Engine.Model;
using IC2.Engine.Naval.Commands;
using IC2.Engine.News;
using IC2.Engine.Persistence;
using IC2.Engine.Recruitment.Commands;
using IC2.Engine.Serialization;

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

        var nationsBeforeDispatch = State.Nations;
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
        CaptureBattleResultsIfAny(result.Events);

        // T87 (DoD 3): a human-issued capture (besiege-city, attack-army/attack-fleet's own cascade) can
        // eliminate a *different* human seat outright -- only reachable in hotseat, since the CLI's own
        // --seat mode never has a second human seat to eliminate.
        // AnnounceAndAdoptWatchModeIfSeatIsLost only ever speaks for the CLI's own --seat nation, so this
        // is the one place left that shows the fall message for anyone else, and the one place a human
        // command (rather than a played AI turn or this task's own turn-start check) can be the last thing
        // that leaves no human seat at all.
        AppendFallMessagesForNewlyLostHumanSeats(lines, nationsBeforeDispatch);
        AnnounceGameOverIfNoHumanSeatRemains(lines);

        return lines;
    }

    /// <summary>
    /// <c>peace-yes</c>/<c>peace-no</c> (T88, DoD 3): answers the pending offer addressed to the active
    /// seat, if any, read from <see cref="GameSession._pendingPeaceTreatyOffers"/>'s own per-human slot
    /// (T87, N-g). Yes dispatches <see cref="Diplomacy.Commands.AcceptPeaceTreatyCommand"/>, which always
    /// writes the honourable peace and its news, never reparations
    /// (<see cref="Diplomacy.PeaceTreatySystem.ApplyHumanConsentedPeace"/>'s own remarks). No writes
    /// nothing at all -- the war simply continues, exactly as the report's own "<c>TBattlePols_No</c> sets
    /// <c>ModalResult 7</c> and does nothing else" reads. An answer from the offer's own human clears its
    /// own slot, whether or not the underlying command turns out to still be legal (see
    /// <see cref="Diplomacy.Commands.AcceptPeaceTreatyRejections.NotAtWar"/>'s own remarks for the one way
    /// that can happen). <strong>Rework round 3, R3:</strong> this method alone does not guarantee a stale
    /// offer is never left pending forever -- an answer from anyone else is refused without touching it
    /// (see below), so if the offer's own human is eliminated and can never answer again, this method
    /// never clears it either; <see cref="GameSession.CapturePeaceTreatyOfferIfAny"/> is what drops that
    /// offer, not this one. <strong>T87 (bug #380):</strong> a deposed human with a pending offer <em>can</em>
    /// occur now that human deposition is checked before a seat's own prompt, not only at that seat's own
    /// <c>end</c> — the same method also drops it, via the widened <see cref="GameSession.IsOfferedHumanGone"/>.
    /// </summary>
    /// <remarks>
    /// Rework round 2, R1: this only answers on the offer's own
    /// <see cref="PendingPeaceTreatyOffer.OfferedHumanNationId"/>'s behalf. Before T87 this was a runtime
    /// check ("checked before either branch, so a mismatch refuses without touching the pending offer at
    /// all"); T87's per-seat dictionary makes it true by construction instead -- the active seat's own id
    /// is the lookup key, so a lookup miss already means "nothing pending for you", never "something
    /// pending for someone else that this call might touch by mistake" (round 1's own regression: a
    /// wrong-seat "yes" reaching <see cref="Diplomacy.Commands.AcceptPeaceTreatyRejections.IssuerNotPartyToTreaty"/>
    /// dispatched anyway with <c>State.ActiveNationId</c> as the issuer, using the offer up on a
    /// rejection). In hotseat, the CLI can pause on a human who is not this offer's own party -- an AI
    /// seat's battle against human A can leave the loop stopped at human B's prompt next -- and B's own
    /// slot is simply empty, or holds B's own, unrelated offer; either way this method only ever touches
    /// the active seat's own entry.
    /// </remarks>
    private IReadOnlyList<string> HandlePeaceTreatyAnswer(string[] tokens, bool accept)
    {
        if (tokens.Length != 1)
        {
            return new[] { $"Usage: {(accept ? "peace-yes" : "peace-no")}" };
        }

        if (!_pendingPeaceTreatyOffers.TryGetValue(State.ActiveNationId, out var pending))
        {
            // T87, N-g: naming who a stray offer is "addressed to" only when exactly one is live and it
            // is not this seat's own -- unambiguous either way. With more than one human seat's own offer
            // pending at once, picking one from a dictionary whose enumeration order the language does not
            // guarantee would make this line non-deterministic; a bare "not pending" is exactly as true.
            return _pendingPeaceTreatyOffers.Count == 1
                ? new[]
                {
                    $"This peace treaty offer is addressed to "
                    + $"{NationDisplay(_pendingPeaceTreatyOffers.Values.Single().OfferedHumanNationId)}, not you.",
                }
                : new[] { "There is no pending peace treaty offer." };
        }

        _pendingPeaceTreatyOffers.Remove(State.ActiveNationId);

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

    private IReadOnlyList<string> HandleSplitUnit(string[] tokens)
    {
        if (tokens.Length != 4
            || !int.TryParse(tokens[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var unitIndex)
            || !int.TryParse(tokens[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var troops))
        {
            return new[] { "Usage: split-unit <army> <unit-index> <troops>" };
        }

        return IssueCommand(new SplitUnitCommand(State.ActiveNationId, tokens[1], unitIndex, troops));
    }

    private IReadOnlyList<string> HandleRenameUnit(string[] tokens)
    {
        if (tokens.Length < 4
            || !int.TryParse(tokens[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var unitIndex))
        {
            return new[] { "Usage: rename-unit <army> <unit-index> <name>" };
        }

        // The name is free text and may contain spaces ("Legio I"); the tokenizer splits only on spaces, so
        // runs of spaces collapse to one on rejoin. A tab survives the split and is rejected anyway by
        // RenameUnitCommandHandler.IsValidName, as is a leading or trailing space.
        var name = string.Join(' ', tokens[3..]);
        return IssueCommand(new RenameUnitCommand(State.ActiveNationId, tokens[1], unitIndex, name));
    }

    private IReadOnlyList<string> HandleDisbandUnit(string[] tokens)
    {
        if (tokens.Length != 3
            || !int.TryParse(tokens[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var unitIndex))
        {
            return new[] { "Usage: disband-unit <army> <unit-index>" };
        }

        return IssueCommand(new DisbandUnitCommand(State.ActiveNationId, tokens[1], unitIndex));
    }

    /// <summary>
    /// <c>split-army &lt;army&gt; &lt;new-army&gt; &lt;i,j,…&gt; [supply=&lt;tons&gt;] [money=&lt;talents&gt;]</c> —
    /// <c>docs/tasks/T141.md</c>. Only the option syntax is parsed here: the third token is a
    /// comma-separated list of non-negative unit indexes (a single index still parses), and
    /// <c>supply=</c>/<c>money=</c> are whole non-negative numbers, each at most once, both defaulting to
    /// 0. Everything the rules disallow — an unknown, foreign or embarked army, the minimum-unit and
    /// 198-army caps, an out-of-range index, amounts beyond the parent's stock or the receiver's purse
    /// bound, and the placement scan — is enforced by
    /// <see cref="IC2.Engine.Armies.Commands.SplitArmyCommandHandler"/> (which also runs the original's
    /// supply rebalance), so this method never restates it.
    /// </summary>
    private IReadOnlyList<string> HandleSplitArmy(string[] tokens)
    {
        const string usage =
            "Usage: split-army <army> <new-army> <i,j,...> [supply=<tons>] [money=<talents>]";

        if (tokens.Length < 4 || !TryParseUnitIndexes(tokens[3], out var unitIndexes))
        {
            return new[] { usage };
        }

        var supplyTons = 0;
        var money = 0;
        var seenOptions = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 4; i < tokens.Length; i++)
        {
            var separator = tokens[i].IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0 || separator == tokens[i].Length - 1)
            {
                return new[] { usage };
            }

            var key = tokens[i][..separator];
            var value = tokens[i][(separator + 1)..];
            if (!seenOptions.Add(key))
            {
                return new[] { usage };
            }

            switch (key)
            {
                case "supply":
                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out supplyTons)
                        || supplyTons < 0)
                    {
                        return new[] { usage };
                    }

                    break;
                case "money":
                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out money)
                        || money < 0)
                    {
                        return new[] { usage };
                    }

                    break;
                default:
                    return new[] { usage };
            }
        }

        return IssueCommand(new SplitArmyCommand(
            State.ActiveNationId, tokens[1], tokens[2], ValueList.From(unitIndexes),
            MoneyToNewArmy: money, SupplyTonsToNewArmy: supplyTons));

        // Kept local to this method: Owns permits changes here only within HandleSplitArmy.
        static bool TryParseUnitIndexes(string value, out List<int> indexes)
        {
            indexes = new List<int>();
            foreach (var part in value.Split(','))
            {
                if (!int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
                    || index < 0)
                {
                    return false;
                }

                indexes.Add(index);
            }

            return true;
        }
    }

    /// <summary>
    /// <c>army-transfer &lt;selected&gt; &lt;partner&gt; [units=&lt;i,j,...&gt;] [supply=&lt;tons&gt;]
    /// [money=&lt;talents&gt;] [back-units=&lt;i,j,...&gt;] [back-supply=&lt;tons&gt;]
    /// [back-money=&lt;talents&gt;]</c> — <c>docs/tasks/T106.md</c> corrected by T117 to the original's one
    /// dialog <c>OK</c>, which carries both directions. Only the option syntax is parsed here (each option
    /// once, whole numbers, comma-separated non-negative unit indexes); everything the rules disallow — an
    /// unknown or foreign army, a distance other than 1, an out-of-range unit index, a giver that cannot
    /// cover the move, a composition cap, and the purse cap — is enforced by
    /// <see cref="IC2.Engine.Armies.Commands.ArmyTransferCommandHandler"/>, so this method never restates it.
    /// </summary>
    private IReadOnlyList<string> HandleArmyTransfer(string[] tokens)
    {
        const string usage =
            "Usage: army-transfer <selected> <partner> [units=<i,j,...>] [supply=<tons>] [money=<talents>] "
            + "[back-units=<i,j,...>] [back-supply=<tons>] [back-money=<talents>]";

        if (tokens.Length < 3)
        {
            return new[] { usage };
        }

        var unitIndexes = new List<int>();
        var supplyTons = 0;
        var money = 0;
        var backUnitIndexes = new List<int>();
        var backSupplyTons = 0;
        var backMoney = 0;
        var seenOptions = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 3; i < tokens.Length; i++)
        {
            var separator = tokens[i].IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0 || separator == tokens[i].Length - 1)
            {
                return new[] { usage };
            }

            var key = tokens[i][..separator];
            var value = tokens[i][(separator + 1)..];
            if (!seenOptions.Add(key))
            {
                return new[] { usage };
            }

            switch (key)
            {
                case "units":
                    if (!TryParseUnitIndexes(value, unitIndexes))
                    {
                        return new[] { usage };
                    }

                    break;
                case "back-units":
                    if (!TryParseUnitIndexes(value, backUnitIndexes))
                    {
                        return new[] { usage };
                    }

                    break;
                case "supply":
                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out supplyTons))
                    {
                        return new[] { usage };
                    }

                    break;
                case "back-supply":
                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out backSupplyTons))
                    {
                        return new[] { usage };
                    }

                    break;
                case "money":
                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out money))
                    {
                        return new[] { usage };
                    }

                    break;
                case "back-money":
                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out backMoney))
                    {
                        return new[] { usage };
                    }

                    break;
                default:
                    return new[] { usage };
            }
        }

        return IssueCommand(new ArmyTransferCommand(
            State.ActiveNationId, tokens[1], tokens[2], ValueList.From(unitIndexes), supplyTons, money,
            ValueList.From(backUnitIndexes), backSupplyTons, backMoney));

        // Kept local to this method: Owns permits changes here only within HandleArmyTransfer.
        static bool TryParseUnitIndexes(string value, List<int> into)
        {
            foreach (var part in value.Split(','))
            {
                if (!int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
                    || index < 0)
                {
                    return false;
                }

                into.Add(index);
            }

            return true;
        }
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

    // ---- economy ----

    /// <summary>
    /// <c>set-tax &lt;percent&gt;</c> — <c>docs/tasks/T103.md</c>. The only parse-level rule is that the
    /// percent is a whole integer: a blank or non-numeric argument never reaches the command layer and
    /// prints the usage line instead, leaving the state untouched. The inclusive 0–40 range is the
    /// ruleset's own <c>economy</c> bounds and is enforced by
    /// <see cref="IC2.Engine.Economy.Commands.SetTaxRateCommandHandler"/>, so this method never restates it.
    /// </summary>
    private IReadOnlyList<string> HandleSetTax(string[] tokens)
    {
        if (tokens.Length != 2
            || !int.TryParse(tokens[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var percent))
        {
            return new[] { "Usage: set-tax <percent>" };
        }

        return IssueCommand(new SetTaxRateCommand(State.ActiveNationId, percent));
    }

    /// <summary>
    /// <c>transfer-money &lt;unit&gt; &lt;amount&gt; [via &lt;fleet&gt;]</c> — <c>docs/tasks/T105.md</c>.
    /// The amount is a signed whole integer; a blank or non-numeric one never reaches the command layer and
    /// prints the usage line instead, leaving the state untouched. Everything else — whether the id is an
    /// army or fleet, whether it and the <c>via</c> fleet are the issuing nation's own, and the signed
    /// amount's 1,000 ceiling — is enforced by
    /// <see cref="IC2.Engine.Economy.Commands.TransferMoneyCommandHandler"/>, so this method never restates it.
    /// </summary>
    private IReadOnlyList<string> HandleTransferMoney(string[] tokens)
    {
        // transfer-money <unit> <amount>            (3 tokens)
        // transfer-money <unit> <amount> via <fleet> (5 tokens)
        var viaSyntaxOk = tokens.Length == 3
            || (tokens.Length == 5 && string.Equals(tokens[3], "via", StringComparison.Ordinal));
        if (!viaSyntaxOk
            || !int.TryParse(tokens[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var amount))
        {
            return new[] { "Usage: transfer-money <unit> <amount> [via <fleet>]" };
        }

        var viaFleetId = tokens.Length == 5 ? tokens[4] : null;
        return TransferMoney(new TransferMoneyCommand(State.ActiveNationId, tokens[1], amount, viaFleetId));
    }

    /// <summary>
    /// Dispatches one <see cref="TransferMoneyCommand"/> and renders the player-facing reply — fix #710.
    /// The reply names the amount actually moved (never the requested one as if it had happened), the
    /// funding and receiving purses, and, when the move was clamped, the amount asked for and the reason.
    /// <c>move</c> and <c>buy</c> are the session's other bespoke renderers; every other verb keeps the
    /// generic <c>"{kind} accepted."</c>.
    /// </summary>
    /// <remarks>
    /// <strong>The amount actually applied is the receiving purse's own before/after delta.</strong>
    /// <see cref="IC2.Engine.Economy.TreasuryPurseTransfer"/> is money-conserving, so that delta is exactly
    /// what left the funding side; the handler's own <c>AppliedTalents</c> is not returned to the session
    /// seam, so the delta is read off the two states instead of widening that seam. A rejection keeps the
    /// generic line <see cref="IssueCommand"/> would print.
    /// </remarks>
    private IReadOnlyList<string> TransferMoney(TransferMoneyCommand command)
    {
        if (IsWatchModeActive)
        {
            return new[] { WatchModeRejectionLine(command.Kind) };
        }

        var destinationBefore = MoneyOf(State, command.UnitId);
        var fundingBefore = command.Amount > 0
            ? command.ViaFleetId is null
                ? State.NationById(command.IssuingNationId)?.Treasury ?? 0
                : MoneyOf(State, command.ViaFleetId)
            : destinationBefore;

        var result = _dispatcher.Dispatch(State, command);
        if (result.IsRejected)
        {
            return new[] { $"{command.Kind} rejected ({result.Code}): {result.Rejection!.Message}" };
        }

        State = NewsLogWriter.Append(result.State, result.Events, Ruleset.NewsLog);
        var moved = MoneyOf(result.State, command.UnitId) - destinationBefore;

        return new[] { DescribeTransferMoney(command, moved, fundingBefore) };
    }

    /// <summary>
    /// The one sentence the session prints for an accepted <c>transfer-money</c> — fix #710. Always
    /// <c>"&lt;to&gt; received &lt;moved&gt; talents from &lt;from&gt;"</c> (written from the receiving side,
    /// so both purses are named without a direction branch), with the requested amount and the clamp's
    /// reason appended when the source's balance or the receiving purse's cap limited it.
    /// </summary>
    private string DescribeTransferMoney(TransferMoneyCommand command, int moved, int fundingBefore)
    {
        var counterparty = command.ViaFleetId is null ? "the treasury" : $"fleet {command.ViaFleetId}";
        var positive = command.Amount > 0;
        var from = positive ? counterparty : command.UnitId;
        var to = positive ? command.UnitId : counterparty;
        var requested = Math.Abs(command.Amount);

        if (moved == command.Amount)
        {
            return $"{to} received {requested} talents from {from}.";
        }

        var movedMagnitude = Math.Abs(moved);
        var reason = requested > Math.Max(0, fundingBefore)
            ? $"{from} held {fundingBefore}"
            : $"{to} is at its {Ruleset.Economy.PurseCapPerUnit}-talent cap";
        return $"{to} received {movedMagnitude} of {requested} requested talents from {from} ({reason}).";
    }

    /// <summary>The purse balance of an army or fleet id, or 0 when the id is neither.</summary>
    private static int MoneyOf(GameState state, string unitId) =>
        state.ArmyById(unitId)?.Money ?? state.FleetById(unitId)?.Money ?? 0;

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

    /// <summary>
    /// <c>split-fleet &lt;fleet&gt; &lt;new-fleet&gt; &lt;ships&gt; [supply=&lt;tons&gt;] [money=&lt;talents&gt;]</c> —
    /// <c>docs/tasks/T141.md</c>. Only the option syntax is parsed here, exactly as
    /// <see cref="HandleSplitArmy"/> does: <c>supply=</c>/<c>money=</c> are whole non-negative numbers,
    /// each at most once, both defaulting to 0. Everything the rules disallow — an unknown or foreign
    /// fleet, an under-construction or army-carrying fleet, the minimum-ship and ship-count rules, amounts
    /// beyond the parent's stock, and the placement scan — is enforced by
    /// <see cref="IC2.Engine.Naval.Commands.SplitFleetCommandHandler"/>, so this method never restates it.
    /// </summary>
    private IReadOnlyList<string> HandleSplitFleet(string[] tokens)
    {
        const string usage =
            "Usage: split-fleet <fleet> <new-fleet> <ships> [supply=<tons>] [money=<talents>]";

        if (tokens.Length < 4
            || !int.TryParse(tokens[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var shipsToNewFleet))
        {
            return new[] { usage };
        }

        var supplyTons = 0;
        var money = 0;
        var seenOptions = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 4; i < tokens.Length; i++)
        {
            var separator = tokens[i].IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0 || separator == tokens[i].Length - 1)
            {
                return new[] { usage };
            }

            var key = tokens[i][..separator];
            var value = tokens[i][(separator + 1)..];
            if (!seenOptions.Add(key))
            {
                return new[] { usage };
            }

            switch (key)
            {
                case "supply":
                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out supplyTons)
                        || supplyTons < 0)
                    {
                        return new[] { usage };
                    }

                    break;
                case "money":
                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out money)
                        || money < 0)
                    {
                        return new[] { usage };
                    }

                    break;
                default:
                    return new[] { usage };
            }
        }

        return IssueCommand(new SplitFleetCommand(
            State.ActiveNationId, tokens[1], tokens[2], shipsToNewFleet,
            SupplyTonsToNewFleet: supplyTons, MoneyToNewFleet: money));
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

    /// <summary>
    /// <c>disembark-army &lt;army&gt; [&lt;x&gt; &lt;y&gt;]</c> — bug #594. The two-token form names no
    /// landing tile: an AI seat's own automatic branch then picks one, while a human seat is still refused
    /// with <see cref="IC2.Engine.Naval.Commands.DisembarkArmyRejections.LandingTileRequired"/> exactly as
    /// before. The four-token form names the landing tile, which the engine's own adjacency and passability
    /// gates still check — this parser restates neither. A malformed tile (non-numeric, a missing
    /// coordinate, or a negative one — bug #594 rework R1) never reaches the command layer and prints the
    /// usage line instead.
    /// </summary>
    private IReadOnlyList<string> HandleDisembarkArmy(string[] tokens)
    {
        if (tokens.Length == 2)
        {
            return IssueCommand(new DisembarkArmyCommand(State.ActiveNationId, tokens[1], null, null));
        }

        // Bug #594 rework R1: int.TryParse accepts int.MinValue, and that negative coordinate then reaches
        // LandingTile.ChebyshevDistance, where Math.Abs(int.MinValue) throws OverflowException out of Submit.
        // A landing tile is a non-negative map index, so refuse a negative coordinate here, at the parser
        // boundary, and print the usage line instead. A positive off-map tile is left to the engine's own
        // distance and passability gates, which reject it cleanly (and no non-negative pair can overflow the
        // subtraction).
        if (tokens.Length != 4
            || !int.TryParse(tokens[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var x)
            || !int.TryParse(tokens[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var y)
            || x < 0
            || y < 0)
        {
            return new[] { "Usage: disembark-army <army> [<x> <y>]" };
        }

        return IssueCommand(new DisembarkArmyCommand(State.ActiveNationId, tokens[1], x, y));
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

    /// <summary>
    /// <c>save &lt;path&gt;</c> — <c>docs/tasks/T95.md</c> (#467), Done-when 1: writes the current game
    /// through <see cref="SaveManager.WriteFile"/>. Never gated by <see cref="IsWatchModeActive"/>: saving
    /// is not a game order (nothing about it is rejected the way <see cref="IssueCommand"/>'s own mutating
    /// commands are), so it works in watch mode, mid-hotseat, or after the CLI's own <c>--seat</c> nation
    /// has fallen, exactly like <c>status</c>/<c>news</c>/<c>help</c> do.
    /// </summary>
    /// <remarks>
    /// <strong>Rework round 1, B1 (the user's decision, 2026-09-28): refused while a post-battle peace
    /// treaty offer is pending.</strong> <see cref="_pendingPeaceTreatyOffers"/> has no counterpart in
    /// <see cref="Model.SaveGame"/> or <see cref="Model.GameState"/> (see <see cref="ResumeFrom"/>'s own
    /// remarks), so a save taken while one is awaiting an answer would resume with the dialog silently
    /// gone — the war continuing as if the offer had never been raised, a different game from the one that
    /// was saved (the independent reviewer's own probe on PR #481: the uninterrupted session's own
    /// <c>peace-yes</c> writes the honourable peace and its cooldown; the resumed one answers into a war
    /// that never had an offer to accept). Persisting the offer needs a <see cref="Model.SaveGame"/>/
    /// <see cref="Model.GameState"/> change outside this task's Owns, so the fix is here instead: refuse
    /// the save outright, for every pending offer regardless of which seat it addresses (a hotseat game
    /// can have more than one <see cref="_pendingPeaceTreatyOffers"/> entry at once), so no save can ever
    /// capture the lost state. The Godot Save button gets this refusal for free — it renders through
    /// <c>MainGameScreen</c>'s own <c>_saveConfirmationLabel</c>, exactly like a successful save's own
    /// confirmation.
    /// </remarks>
    private IReadOnlyList<string> HandleSave(string trimmedLine)
    {
        var path = PathArgument(trimmedLine);
        if (path is null)
        {
            return new[] { "Usage: save <path>" };
        }

        if (_pendingPeaceTreatyOffers.Count > 0)
        {
            return new[] { "Answer the pending peace treaty offer (peace-yes / peace-no) before saving." };
        }

        try
        {
            SaveManager.WriteFile(path, BuildSaveGame());
        }
        catch (SaveWriteException ex)
        {
            return new[] { $"Could not save to '{path}': {ex.Message}" };
        }

        return new[] { $"Saved to '{path}'." };
    }

    /// <summary>
    /// <c>load &lt;path&gt;</c> — <c>docs/tasks/T95.md</c> (#467), Done-when 2: resumes a previously saved
    /// game into this same session, through <see cref="ResumeFrom"/>. Also never gated by
    /// <see cref="IsWatchModeActive"/>, for the same reason <see cref="HandleSave"/> is not — a session
    /// stuck in watch mode (or one whose <c>--seat</c> nation has fallen) is exactly the case a player
    /// most wants to be able to resume a different save from.
    /// </summary>
    /// <remarks>
    /// Done-when 5: a missing file, a malformed save, and a save for a different world or ruleset are all
    /// <see cref="GameDataException"/> (<see cref="SaveManager.LoadFile"/>'s own documented exception
    /// surface — a missing file surfaces as <see cref="Serialization.MalformedGameDataException"/>, "the
    /// file could not be read"), so one catch covers all three: the session's own <see cref="State"/> and
    /// every other field are left exactly as they were (nothing here runs before the catch can still
    /// throw), and the session keeps running.
    /// </remarks>
    private IReadOnlyList<string> HandleLoad(string trimmedLine)
    {
        var path = PathArgument(trimmedLine);
        if (path is null)
        {
            return new[] { "Usage: load <path>" };
        }

        SaveGame save;
        try
        {
            save = SaveManager.LoadFile(path, World, Ruleset);
        }
        catch (GameDataException ex)
        {
            return new[] { $"Could not load '{path}': {ex.Message}" };
        }

        ResumeFrom(save);
        return new[]
        {
            $"Loaded '{path}': {save.Label} (turn {State.Calendar.TurnIndex}). "
            + $"Active seat: {NationDisplay(State.ActiveNationId)}.",
        };
    }

    /// <summary>
    /// The current game as a <see cref="SaveGame"/> — <see cref="HandleSave"/>'s own builder, and the one
    /// place a save's <see cref="SaveGame.Id"/>/<see cref="SaveGame.Label"/> are chosen. Both are
    /// deterministic (the scenario id and the calendar's own turn index), never <c>DateTime.Now</c> or
    /// <c>Guid.NewGuid</c> — see this task's PR for why a wall-clock label was not needed here.
    /// </summary>
    private SaveGame BuildSaveGame() => new(
        SchemaVersion: GameDataSchema.CurrentVersion,
        Id: $"{Scenario.Id}-turn-{State.Calendar.TurnIndex}",
        Label: $"{Scenario.Id}, turn {State.Calendar.TurnIndex}",
        ScenarioId: State.ScenarioId,
        WorldId: State.WorldId,
        RulesetId: State.RulesetId,
        State: State);

    /// <summary>
    /// Everything after <paramref name="trimmedLine"/>'s first space, or <see langword="null"/> if there
    /// is none or it is empty — a file path, unlike every other command's arguments, can itself contain
    /// spaces, so this reads the rest of the line raw rather than reusing <see cref="Submit"/>'s own
    /// whitespace-split <c>tokens</c> array (which would silently mangle a path like <c>C:\a b\save.json</c>).
    /// </summary>
    private static string? PathArgument(string trimmedLine)
    {
        var spaceIndex = trimmedLine.IndexOf(' ');
        if (spaceIndex < 0)
        {
            return null;
        }

        var path = trimmedLine[(spaceIndex + 1)..].Trim();
        return path.Length == 0 ? null : path;
    }
}
