using System.Globalization;
using IC2.Engine.Battle;
using IC2.Engine.Battle.Commands;
using IC2.Engine.Core;
using IC2.Engine.Diplomacy.Commands;
using IC2.Engine.Economy;
using IC2.Engine.Economy.Commands;
using IC2.Engine.Model;
using IC2.Engine.Movement.Commands;
using IC2.Engine.News;

namespace IC2.Engine.Presentation;

/// <summary>
/// A playable text session over the real engine — <c>docs/task-catalogue.md</c> "T41 Thin CLI demo on the
/// toy world (a walking skeleton)". Parses one command line at a time, runs it through the real
/// <see cref="TurnCoordinator"/>/<see cref="CommandDispatcher"/> over every registered
/// <c>[GameSystem]</c>/<c>[CommandHandler]</c> this build declares, and renders the result as text.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This class holds no game rule.</strong> Every number it prints is read off <see cref="State"/>
/// or <see cref="Ruleset"/>; every decision (can this army move there, does this purchase cost anything)
/// is made by the command handlers in <c>src/IC2.Engine/Movement/Commands</c> and
/// <c>src/IC2.Engine/Economy/Commands</c>, which in turn re-implement no rule of their own — they wire
/// T09's <see cref="Movement.MovementWalker"/> and T08's <see cref="Economy.SupplyPurchase"/>. This is
/// what <c>tests/IC2.Engine.Tests/Presentation/GameplayConstantScannerTests.cs</c> checks mechanically.
/// </para>
/// <para>
/// <strong>Order news</strong> (<c>docs/task-catalogue.md</c>'s hazard note, T40 follow-up
/// <see href="https://github.com/diegoami/imperial_conquest_2/issues/87">#87</see>): a command dispatched
/// outside a turn run never reaches <see cref="SystemContext.PublishedEvents"/>, so <see cref="HandleMove"/>
/// and <see cref="HandleBuy"/> route their own <see cref="CommandResult.Events"/> through T10's public
/// <see cref="NewsLogWriter.Append(GameState, IEnumerable{DomainEvent}, NewsLogRules, Func{string, string}?)"/>
/// themselves. Only T10's catalog kinds actually render — this task's own <see cref="ArmyMoved"/> and
/// <see cref="ArmySupplyPurchased"/> are not news-worthy, so today that call is a no-op for them; it is
/// still the correct call to make; a later task that does add a catalog entry for one of them needs no
/// change here.
/// </para>
/// </remarks>
public sealed partial class GameSession
{
    private readonly SystemRegistry _registry;
    private readonly TurnCoordinator _coordinator;
    private readonly CommandDispatcher _dispatcher;

    /// <summary>
    /// The nation the CLI's own <c>--seat</c> flag names, or <see langword="null"/> when it was not
    /// given — <c>docs/tasks/T83.md</c> Done-when 1. Set once, in the constructor, and never mutated:
    /// this is <em>which seat the CLI itself plays</em>, distinct from <see cref="Model.NationState.Control"/>
    /// (which the engine also reads, for T82's AI-vs-human diplomacy checks and everything else that
    /// predates this task). Both are kept in step below: the seat named here is also marked
    /// <see cref="Model.SeatControl.Human"/> in the state the engine reads, so nothing downstream needs to
    /// know the CLI exists.
    /// </summary>
    /// <remarks>
    /// T95, Done-when 4 (#382's hazard): no longer <see langword="readonly"/> — <see cref="ResumeFrom"/>
    /// reassigns this after a <c>load &lt;path&gt;</c> command or the resume constructor, read fresh from
    /// the loaded state's own <see cref="Model.NationState.Control"/> rather than kept from however this
    /// session was originally started (never from <paramref name="humanSeatNationId"/>'s own value, nor
    /// from <paramref name="scenario"/>).
    /// </remarks>
    private string? _humanSeatNationId;

    /// <summary>
    /// Whether this session has no seat to command at all — no <c>--seat</c> flag, and the scenario's own
    /// seats are every one <see cref="Model.SeatControl.Ai"/> — <c>docs/tasks/T83.md</c> Done-when 3
    /// ("watch mode"). Computed once, from the scenario's own seat assignments as loaded, not from
    /// <see cref="Model.GameState.Nations"/>'s live <c>Control</c> (which a system such as
    /// <c>HumanDepositionSystem</c> can flip mid-game): the CLI's own mode is a property of how the
    /// session was started, not of anything a turn can later change.
    /// </summary>
    /// <remarks>
    /// T95: no longer <see langword="readonly"/> for the same reason as <see cref="_humanSeatNationId"/>
    /// above — <see cref="ResumeFrom"/> recomputes it too, deliberately from the loaded state's own live
    /// <c>Control</c> (there is no scenario-time seat list left to fall back to once a game has been
    /// played forward and saved).
    /// </remarks>
    private bool _isWatchMode;

    /// <summary>
    /// Whether the CLI's own <c>--seat</c> nation has fallen — eliminated, or deposed and handed to the
    /// AI — since the session started: <c>docs/tasks/T83.md</c> Done-when 7, the user's decision on PR
    /// #375's review (its N2/N3). Starts <see langword="false"/>, is set (never cleared — neither
    /// elimination nor deposition reverses) by <see cref="AnnounceAndAdoptWatchModeIfSeatIsLost"/>, and
    /// from then on every <see cref="HandleEnd"/> and every mutating command behaves exactly as watch mode
    /// always has (<see cref="IsWatchModeActive"/> is what every such check actually reads).
    /// </summary>
    private bool _seatLost;

    /// <summary>
    /// T87, DoD 3: set once <see cref="AnnounceGameOverIfNoHumanSeatRemains"/> finds no
    /// <see cref="Model.SeatControl.Human"/> nation left anywhere in <see cref="State"/> — every human
    /// seat, not only the CLI's own <c>--seat</c> nation (contrast <see cref="_seatLost"/>). Read by
    /// <see cref="Submit"/> to set <see cref="SessionOutput.ShouldExit"/> on whichever call first reaches
    /// it, the same signal <c>quit</c> already uses. Never cleared.
    /// </summary>
    private bool _gameOver;

    /// <summary>
    /// Lines produced by fast-forwarding past AI seats that come before <see cref="_humanSeatNationId"/>
    /// in the very first round — <c>docs/tasks/T83.md</c> Done-when 1 ("The CLI pauses on that seat every
    /// round"): the session has to reach the named seat before it can accept the first command, and this
    /// is what that fast-forward produced. Flushed onto the very first <see cref="Submit"/> call, so it
    /// reads exactly like any later round's AI summary lines rather than vanishing silently — see this
    /// class's PR for why a silent construction-time advance was rejected. <see langword="null"/> once
    /// consumed, and always <see langword="null"/> when <see cref="_humanSeatNationId"/> is itself
    /// <see langword="null"/> or already the starting active seat.
    /// </summary>
    private List<string>? _pendingPrelude;

    /// <summary>
    /// The news log's slots exactly as they stood before the construction-time prelude ran — review round
    /// 1, N4: "news written during the prelude must appear in the first <c>end</c>'s summary." Without
    /// this, <see cref="HandleEndSeated"/>'s own <c>newsBefore</c> snapshot would be taken only once the
    /// prelude has already run, so any news the prelude's own AI turns produced (e.g. an alliance formed
    /// against the player's own nation before their first turn) would never appear in any <c>end</c>'s
    /// "News:" section — it would only ever surface through the standalone <c>news</c> command. Consumed
    /// (set back to <see langword="null"/>) by the first <see cref="HandleEndSeated"/> call, the same way
    /// <see cref="_pendingPrelude"/> is consumed by the first <see cref="Submit"/> call. Always
    /// <see langword="null"/> when <see cref="_humanSeatNationId"/> is itself <see langword="null"/>.
    /// </summary>
    private IReadOnlyList<NewsEntry>? _pendingNewsBaseline;

    /// <summary>
    /// A post-battle peace treaty <see cref="Battle.PeaceTreatyOffered"/> raised, waiting on the human's
    /// own <c>peace-yes</c>/<c>peace-no</c> answer (T88, DoD 3, Hazard 1) — keyed by
    /// <see cref="PendingPeaceTreatyOffer.OfferedHumanNationId"/>, <strong>one slot per human seat, not one
    /// for the whole session (T87, <c>#389</c>, folding #404's N-g)</strong>. The same
    /// <see cref="Model.GameState.PendingOffer"/> convention this started from is itself one-per-session,
    /// but a single shared slot meant a hotseat human A's own unanswered offer occupied the one slot
    /// through every other human's whole turn, silently dropping a qualifying offer raised for a
    /// <em>different</em> human B in the meantime (#404's own probe: "the single offer slot is now held
    /// through other humans' whole turns"). Keying by the offered human closes that without touching the
    /// single-offer-per-human rule itself — B's own new offer no longer competes with A's for the one
    /// slot, because each human now has their own. This cannot live on <see cref="Model.GameState"/>
    /// itself, which is outside this task's Owns list, so it lives here instead: a session-scoped field,
    /// exactly like <see cref="_pendingPrelude"/> above.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A battle raises an offer whether the human it is addressed to is "at the prompt" or not — it can
    /// happen inside an AI seat's own turn, when the human's army is the one that lost — so
    /// <see cref="_dispatcher"/>'s own per-command <see cref="Battle.PeaceTreatyOffered"/> (via
    /// <see cref="IssueCommand"/>) and every AI seat played through <see cref="PlayUntilOneFullLapOrRepeat"/>
    /// both feed <see cref="CapturePeaceTreatyOfferIfAny"/>, so the decision survives to be shown and
    /// answered later without blocking the AI's own turn loop — the hazard's own "smallest design" choice.
    /// A second offer raised for the <em>same</em> human while one is already pending for them is still
    /// dropped rather than replacing it: the original's own dialog is modal (one battle's treaty at a time
    /// <em>per human</em>), and this build has no queue for a second one either — only the "one slot for
    /// the whole session" limitation is lifted, not "one slot per human".
    /// </para>
    /// <para>
    /// Cleared by <see cref="HandlePeaceTreatyAnswer"/> once the offered human answers, and — rework round
    /// 1, B3(a)/(b); rework round 2, R1 — by <see cref="HandleEnd"/> too, but only on <em>that same human's
    /// own</em> <c>end</c>, not anyone else's (T87's own dictionary keying makes this the natural, no
    /// longer special-cased, behaviour: removing <em>that human's own</em> entry can never touch another
    /// human's). <strong>Rework round 3, R3:</strong> an offer whose own human is eliminated before their
    /// next prompt could never be answered <em>or</em> lapsed by either mechanism above, since neither is
    /// ever reached for a seat that gets no further prompt. <see cref="CapturePeaceTreatyOfferIfAny"/> also
    /// drops any such entry once <see cref="IsOfferedHumanGone"/>, closing that gap without a new flag
    /// (deposition needs no matching check — see that method's own remarks for why); <see cref="HandleEnd"/>
    /// does not need the same check a second time (see its own remarks) -- every path that could eliminate
    /// an offered human already reaches <see cref="CapturePeaceTreatyOfferIfAny"/> at least once before
    /// <see cref="HandleEnd"/>'s own check could ever run against it.
    /// </para>
    /// <para>
    /// <strong>T87, #404's N-j (rework round 1, review B9): no branch added.</strong> #404's own scope:
    /// "if the engine cannot reach that ordering, the PR shows why, and no branch is added for it" — the
    /// ordering is an AI turn that both loses a gated battle to human X and then takes X's last city, in
    /// that order, within the same turn. A first attempt at this check (round 0 of this PR) added an
    /// unconditional <c>if (offeredHuman.Eliminated) continue;</c> with no test proving that ordering is
    /// reachable — deleting it left the whole suite green (review B9's own M10). It is removed here
    /// rather than kept unproven. Reaching it needs an AI army that loses an attack against a human's own
    /// field army (clearing <c>AiMilitaryPhase</c>'s own required-attack-ratio gate at the odds needed to
    /// actually lose sometimes, not merely refuse to attack), <em>and</em>, in the very same turn, a
    /// second AI action taking that same human's last, separately-undefended city — the independent
    /// reviewer of PR #400 (round 3) already attempted the equivalent construction and reported "the AI
    /// took the city without attacking the army"; this task's own attempt in the time available did not
    /// improve on that. <strong>Rework round 2, N-c: the concrete reason.</strong>
    /// <see cref="IC2.Engine.Ai.AiMilitaryPhase.Propose"/>'s own per-army loop proposes every siege candidate for that
    /// army before that same army's own attack candidates (<c>ProposeSieges</c> then
    /// <c>ProposeArmyAttacks</c>), and <see cref="IC2.Engine.Ai.AiTurn"/>'s own action loop re-proposes and re-scores
    /// from scratch after <em>every single action</em> it takes, rather than working through a plan built
    /// once at the top of the turn — so whenever a human's last city is already undefended enough to
    /// clear <c>AttackLegality</c>'s own siege gate, that candidate exists from the very first
    /// re-proposal of the turn, before an attack against that same human's own field army has had any
    /// chance to run at all. The ordering #404 asks for needs the field-army attack to run — and lose —
    /// <em>first</em>, with the siege only becoming reachable afterward (the army's own loss leaving the
    /// city undefended in a way it was not before); constructing that means the siege candidate must be
    /// invalid or absent at the turn's own start and only become legal because of the earlier attack's own
    /// outcome, not merely under-scored against it — a narrower, harder case than "make the attack lose"
    /// alone, and the independent reviewer's own probe and this task's own attempt both found the AI
    /// reaching the city directly instead. If a later task does construct it,
    /// <see cref="CapturePeaceTreatyOfferIfAny"/> is where the branch belongs — reading <see cref="State"/>,
    /// already advanced past the triggering <see cref="TurnCoordinator.RunTurn"/> or dispatch by the time
    /// this runs, would already show the side as <see cref="Model.NationState.Eliminated"/> the moment
    /// such an event is found.
    /// </para>
    /// </remarks>
    private readonly Dictionary<string, PendingPeaceTreatyOffer> _pendingPeaceTreatyOffers =
        new(StringComparer.Ordinal);

    /// <summary>
    /// See <see cref="_pendingPeaceTreatyOffers"/>.
    /// </summary>
    /// <param name="WinnerNationId">The battle's winner, as <see cref="PeaceTreatyOffered"/> named it.</param>
    /// <param name="LoserNationId">The battle's loser.</param>
    /// <param name="OfferedHumanNationId">
    /// Rework round 2, R1: whichever of <paramref name="WinnerNationId"/>/<paramref name="LoserNationId"/>
    /// is human-controlled — the one human seat this specific offer is addressed to, fixed at the moment
    /// the offer is raised. Before this field existed, <see cref="HandlePeaceTreatyAnswer"/> answered as
    /// whichever seat happened to be <see cref="Model.GameState.ActiveNationId"/> when the CLI read the
    /// command, and <see cref="HandleEnd"/> lapsed the offer on any human's own <c>end</c> — both correct
    /// only when there is exactly one human seat in the game. In hotseat, an offer an AI seat's turn
    /// raises against human A can land the CLI paused on human B's own prompt next: B's own <c>yes</c> was
    /// only ever refused by <see cref="Diplomacy.Commands.AcceptPeaceTreatyRejections.IssuerNotPartyToTreaty"/>
    /// at the command layer (consuming the offer in the process), B's own <c>no</c> silently declined A's
    /// treaty, and B's own <c>end</c> lapsed it before A ever saw a prompt. Storing the offered human here
    /// lets both call sites bind to the right seat instead.
    /// </param>
    private sealed record PendingPeaceTreatyOffer(string WinnerNationId, string LoserNationId, string OfferedHumanNationId);

    /// <summary>
    /// T25 (plan #474): every <see cref="Battle.BattleResult"/> a <see cref="Battle.BattleResolved"/> event
    /// published since the last flush — accumulated at the same three call sites
    /// <see cref="CapturePeaceTreatyOfferIfAny"/> already runs from (<see cref="IssueCommand"/>'s own human
    /// dispatch, <see cref="PlayUntilOneFullLapOrRepeat"/>'s per-seat AI turn, and
    /// <see cref="HandleEndSeated"/>'s own ending seat's <c>RunTurn</c>), and drained into
    /// <see cref="SessionOutput.Battles"/> by every <see cref="Submit"/> return path. A construction-time
    /// prelude (<see cref="AdvanceToHumanSeat"/>) can also populate this before any <see cref="Submit"/>
    /// call exists — those battles are flushed on the very first <see cref="Submit"/> call, the same way
    /// <see cref="_pendingPrelude"/>'s own lines are.
    /// </summary>
    private readonly List<Battle.BattleResult> _pendingBattleResults = new();

    /// <summary>
    /// Appends every <see cref="Battle.BattleResult"/> found in <paramref name="events"/> to
    /// <see cref="_pendingBattleResults"/> — see that field's own remarks for the call sites and why. A
    /// no-op for any event stream that resolved no battle, which is most of them.
    /// </summary>
    private void CaptureBattleResultsIfAny(IEnumerable<DomainEvent> events)
    {
        foreach (var resolved in events.OfType<Battle.BattleResolved>())
        {
            _pendingBattleResults.Add(resolved.Result);
        }
    }

    /// <summary>
    /// Drains <see cref="_pendingBattleResults"/> for one <see cref="Submit"/> return — called at every
    /// return path in <see cref="Submit"/>, exactly as <see cref="SessionOutput.Battles"/> requires. Returns
    /// an empty array (not merely an empty list) when nothing is pending, matching
    /// <see cref="SessionOutput.Battles"/>'s own default.
    /// </summary>
    private IReadOnlyList<Battle.BattleResult> FlushPendingBattleResults()
    {
        if (_pendingBattleResults.Count == 0)
        {
            LastBattles = Array.Empty<Battle.BattleResult>();
            return LastBattles;
        }

        var flushed = _pendingBattleResults.ToArray();
        _pendingBattleResults.Clear();
        LastBattles = flushed;
        return flushed;
    }

    /// <summary>
    /// T138: every human seat that fell while the current <see cref="Submit"/> call ran, accumulated at
    /// the session's own fall sites (<see cref="DepositActiveHumanSeatIfItShouldFallAtTurnStart"/>,
    /// <see cref="AppendFallMessagesForNewlyLostHumanSeats"/> and
    /// <see cref="AnnounceAndAdoptWatchModeIfSeatIsLost"/>) and drained into
    /// <see cref="SessionOutput.SeatFalls"/> by every <see cref="Submit"/> return path. A construction-time
    /// prelude (<see cref="AdvanceToHumanSeat"/>) can populate this before any <see cref="Submit"/> call
    /// exists — those falls are flushed on the very first call, the same way <see cref="_pendingPrelude"/>'s
    /// own lines and <see cref="_pendingBattleResults"/> are.
    /// </summary>
    private readonly List<SeatFall> _pendingSeatFalls = new();

    /// <summary>
    /// Drains <see cref="_pendingSeatFalls"/> for one <see cref="Submit"/> return — called at every return
    /// path in <see cref="Submit"/>, exactly as <see cref="SessionOutput.SeatFalls"/> requires. Returns an
    /// empty array (not merely an empty list) when nothing is pending, matching
    /// <see cref="SessionOutput.SeatFalls"/>'s own default.
    /// </summary>
    private IReadOnlyList<SeatFall> FlushPendingSeatFalls()
    {
        if (_pendingSeatFalls.Count == 0)
        {
            LastSeatFalls = Array.Empty<SeatFall>();
            return LastSeatFalls;
        }

        var flushed = _pendingSeatFalls.ToArray();
        _pendingSeatFalls.Clear();
        LastSeatFalls = flushed;
        return flushed;
    }

    /// <summary>
    /// T138: records one human seat's fall, once per call — <see cref="SeatFall"/>'s own remarks for what
    /// it carries and why. <paramref name="fallen"/> must be the nation exactly as it stands at the fall
    /// (see each caller): the pre-<c>FUN_0044C8F0</c> nation for a deposition, the post-capture one for a
    /// conquest, because <c>TPremierForm_HumanLeaderFalls</c> opens the window before the deposition's own
    /// writes. A nation already recorded this call is a no-op, so the same fall reported by more than one
    /// of the session's own paths (a <c>--seat</c> seat caught by both
    /// <see cref="AppendFallMessagesForNewlyLostHumanSeats"/> and
    /// <see cref="AnnounceAndAdoptWatchModeIfSeatIsLost"/>) yields one <see cref="SeatFall"/>.
    /// </summary>
    private void RecordSeatFall(NationState fallen)
    {
        foreach (var already in _pendingSeatFalls)
        {
            if (string.Equals(already.NationId, fallen.Id, StringComparison.Ordinal))
            {
                return;
            }
        }

        var (reason, conqueror) = FallReasonOf(fallen);
        var endCityCount = State.Cities.Count(
            c => string.Equals(c.Owner, fallen.Id, StringComparison.Ordinal));
        _pendingSeatFalls.Add(new SeatFall(
            fallen.Id,
            reason,
            conqueror,
            FallMessageFor(reason, conqueror),
            fallen.Population,
            endCityCount,
            fallen.Treasury));
    }

    /// <summary>
    /// T138: which of the original's five branches a fall takes, and the conqueror when there is one —
    /// the one classification <see cref="HumanLeaderFallsMessage"/> and <see cref="RecordSeatFall"/> both
    /// read, in the original's own priority order (total conquest, the hard end year, then — only when the
    /// seat was <em>not</em> conquered — unpopularity or unpaid upkeep, conquered otherwise).
    /// <c>THumanFalls_InitializeForm</c> :56391–56404; see <see cref="HumanLeaderFallsMessage"/>'s own
    /// remarks for the full citation.
    /// </summary>
    private (SeatFallReason Reason, string? ConquerorNationId) FallReasonOf(NationState fallen)
    {
        var totalCities = State.Cities.Count;
        var ownedByFallen = State.Cities.Count(c => string.Equals(c.Owner, fallen.Id, StringComparison.Ordinal));
        if (totalCities > 0 && ownedByFallen >= totalCities)
        {
            return (SeatFallReason.AllCities, null);
        }

        if (State.Calendar.YearBc <= Ruleset.Victory.HardEndYearBc)
        {
            return (SeatFallReason.HardEndYear, null);
        }

        if (fallen.ConqueredBy is null)
        {
            return fallen.Unity < Ruleset.Economy.DebtUnityThreshold
                ? (SeatFallReason.Unpopularity, null)
                : (SeatFallReason.Unpaid, null);
        }

        return (SeatFallReason.Conquered, fallen.ConqueredBy);
    }

    /// <summary>
    /// Scans <paramref name="events"/> for a <see cref="Battle.PeaceTreatyOffered"/> this session should
    /// show, and records which human seat it is addressed to. Appends the dialog text and how to answer it
    /// to <paramref name="lines"/>, the same way every other line this call produced is appended. A no-op
    /// once an offer is already pending (see that field's own remarks).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Rework round 2, N-a: this used to also refuse an event where neither side is human, proven only by
    /// a reflection test reaching into a private method, since <see cref="Battle.InstantBattleResolver"/>'s
    /// own gate (<c>exactlyOneHuman</c>) already guarantees the real engine never publishes one — there was
    /// no path through <see cref="Submit"/> that could ever exercise that branch. Removed rather than kept
    /// under test by reflection: this method's job is now to pick out <em>which</em> side is human (needed
    /// for <see cref="PendingPeaceTreatyOffer.OfferedHumanNationId"/> regardless), not to re-prove an
    /// invariant its only caller already enforces. The reflection test
    /// (<c>AllAiPeaceTreatyOffered_IsIgnoredByTheSessionsOwnRecheck</c>) is deleted with it.
    /// </para>
    /// <para>
    /// Rework round 3, R3: a pending offer whose own <see cref="PendingPeaceTreatyOffer.OfferedHumanNationId"/>
    /// has since been eliminated is dropped here, before the early-return below — round 2's own fix (bind
    /// the answer and the expiry to that one human) left
    /// exactly this gap: an eliminated seat never gets another prompt, so neither <see cref="HandleEnd"/>'s
    /// own lapse nor <see cref="HandlePeaceTreatyAnswer"/> is ever reached for it again, and the early
    /// return just below then silently drops every later human's own qualifying offer for the rest of the
    /// game — round 0's B3(b), reached by a new path. No new flag: this reads the same
    /// <see cref="Model.NationState.Eliminated"/>/<see cref="Model.NationState.Control"/> the rest of the
    /// engine already keeps current (elimination and deposition both write through the ordinary state, not
    /// a side channel this session would otherwise need to track). Silent here (no "has lapsed" line): this
    /// runs on every command and every seat's turn regardless of whether today's own events hold anything
    /// worth showing, and a new qualifying offer's own dialog (below) is the visible result when there is
    /// one; printing "lapsed" here as well, on a call that most of the time raises nothing at all, would be
    /// a line with no offer and no dialog to explain it.
    /// </para>
    /// </remarks>
    private void CapturePeaceTreatyOfferIfAny(List<string> lines, IEnumerable<DomainEvent> events)
    {
        // T87, N-g: drop any entry whose own human is gone, one per seat, before scanning for anything
        // new -- was a single nullable field's own early check; now a dictionary can hold more than one
        // stale entry at once (one per fallen human seat), so every one of them is dropped, not just one.
        foreach (var staleHumanId in _pendingPeaceTreatyOffers
                     .Where(entry => IsOfferedHumanGone(entry.Value))
                     .Select(entry => entry.Key)
                     .ToList())
        {
            _pendingPeaceTreatyOffers.Remove(staleHumanId);
        }

        foreach (var offered in events.OfType<PeaceTreatyOffered>())
        {
            var winner = State.NationById(offered.WinnerNationId);
            var loser = State.NationById(offered.LoserNationId);
            if (winner is null || loser is null)
            {
                continue;
            }

            var offeredHuman = winner.Control == SeatControl.Human ? winner : loser;

            // N-g: one slot per human seat -- a second qualifying offer for a human who already has one
            // pending is still dropped (the original's dialog is modal per human), but it no longer
            // competes with a different human's own slot.
            if (_pendingPeaceTreatyOffers.ContainsKey(offeredHuman.Id))
            {
                continue;
            }

            _pendingPeaceTreatyOffers[offeredHuman.Id] = new PendingPeaceTreatyOffer(
                offered.WinnerNationId, offered.LoserNationId, offeredHuman.Id);
            lines.AddRange(PeaceTreatyOfferDialogLines(winner, loser));
            lines.Add("Type 'peace-yes' to accept or 'peace-no' to decline.");
        }
    }

    /// <summary>
    /// Rework round 3, R3: whether <paramref name="pending"/>'s own
    /// <see cref="PendingPeaceTreatyOffer.OfferedHumanNationId"/> can no longer answer or have their own
    /// <c>end</c> lapse it — eliminated, or no longer found at all (defensive; never expected in practice).
    /// Called only from <see cref="CapturePeaceTreatyOfferIfAny"/> — see <see cref="HandleEnd"/>'s own
    /// remarks for why it does not call this too.
    /// </summary>
    /// <remarks>
    /// <strong>T87 added the "no longer <see cref="SeatControl.Human"/>" branch the round-2 review found
    /// deliberately absent.</strong> That review's own reasoning no longer holds: it rested on a human
    /// seat's <c>SeatStart</c> (and so its own deposition check) never running until that same seat itself
    /// submitted <c>end</c>, which meant <see cref="HandleEnd"/>'s own lapse always ran first. T87 (bug
    /// #380) moved the human deposition check to <em>before</em> a seat's own prompt — the moment rotation
    /// reaches it, in <see cref="PlayUntilOneFullLapOrRepeat"/> and <see cref="AdvanceToHumanSeat"/> — so a
    /// human with a pending offer can now be deposed for debt, unity, the hard end year or total conquest
    /// <em>without ever reaching another prompt</em>, exactly the gap this method's own R3 fix already
    /// closed for elimination. Reading <see cref="Model.NationState.Control"/> here closes it for
    /// deposition the same way <see cref="Model.NationState.Eliminated"/> already closes it for elimination
    /// — both leave the seat with no further prompt to answer or lapse the offer at.
    /// </remarks>
    private bool IsOfferedHumanGone(PendingPeaceTreatyOffer pending)
    {
        var human = State.NationById(pending.OfferedHumanNationId);
        return human is null || human.Eliminated || human.Control != SeatControl.Human;
    }

    /// <summary>
    /// The offer's text as the original's whole "Offer of peace" box, as ordered lines (bug #746): the offer
    /// sentence addressed to the human's side either way, the terms line, then the two click prompts. The
    /// box's reparation lines are always empty in the original and are not supplied; its title is the
    /// window's. Each line is read from the box's painted text in the original's screenshots
    /// <strong>[Wine candidate: research report 2026-10-05-battle-peace-offer.md, "For the clone: the facts
    /// to copy", item 1]</strong>. The terms line ends with the full word "penalties" where the original's
    /// box clips it (the user's decision of 2026-10-05; design-audit.md §1.7 quotes
    /// <c>TBattlePols</c>'s line in full). The CLI prints each line; the Offer of peace window reads them as
    /// typed data and displays them unchanged.
    /// </summary>
    /// <param name="winner">The battle's winner.</param>
    /// <param name="loser">The battle's loser.</param>
    /// <returns>The box's lines, in order.</returns>
    public static IReadOnlyList<string> PeaceTreatyOfferDialogLines(NationState winner, NationState loser) =>
        Array.AsReadOnly(new[]
        {
            winner.Control == SeatControl.Human
                ? $"After losing to you in battle {loser.Name} are willing to end their war with you, if you agree to the terms below."
                : $"After defeating you in battle {winner.Name} are willing to end their war with you, if you agree to the terms below.",
            "An honourable peace with no reparations or penalties",
            "If the peace terms are acceptable click YES.",
            "Otherwise to continue the war click NO.",
        });

    /// <summary>Builds a session over a resolved world/ruleset/scenario, optionally overriding the seed.</summary>
    /// <param name="world">The loaded world.</param>
    /// <param name="ruleset">The loaded ruleset — the source of every number this session prints.</param>
    /// <param name="scenario">The scenario to start from.</param>
    /// <param name="seedOverride">
    /// When given, replaces <see cref="Scenario.RandomSeed"/> in the starting state — the CLI's
    /// <c>--seed</c> option. <see langword="null"/> keeps the scenario's own seed.
    /// </param>
    /// <param name="humanSeatNationId">
    /// The CLI's <c>--seat</c> option (<c>docs/tasks/T83.md</c> Done-when 1): the nation id this session
    /// plays interactively. Marked <see cref="Model.SeatControl.Human"/> in the starting state, and —
    /// review round 1, N8, the user's decision of 2026-09-25 on PR #375's review, replacing round 1's own
    /// "additively" choice — every <em>other</em> seat is marked <see cref="Model.SeatControl.Ai"/>, even
    /// one the scenario itself seats human (such as <c>toy-3city</c>'s <c>north</c>): <c>--seat</c> makes
    /// its nation the CLI's <em>only</em> human seat, never a second one alongside whatever the scenario
    /// already assigned. <see langword="null"/> keeps every seat exactly as the scenario assigns it.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="humanSeatNationId"/> names a nation <paramref name="scenario"/> assigns no seat to.
    /// </exception>
    public GameSession(
        World world, Ruleset ruleset, Scenario scenario, ulong? seedOverride = null,
        string? humanSeatNationId = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(ruleset);
        ArgumentNullException.ThrowIfNull(scenario);

        if (humanSeatNationId is not null && scenario.SeatFor(humanSeatNationId) is null)
        {
            throw new ArgumentException(
                $"Scenario '{scenario.Id}' assigns no seat to nation '{humanSeatNationId}'.",
                nameof(humanSeatNationId));
        }

        _humanSeatNationId = humanSeatNationId;
        _isWatchMode = humanSeatNationId is null && !scenario.Seats.Any(s => s.Control == SeatControl.Human);

        if (humanSeatNationId is not null)
        {
            scenario = scenario with
            {
                Seats = ValueList.From(scenario.Seats.Select(seat => seat with
                {
                    Control = string.Equals(seat.Nation, humanSeatNationId, StringComparison.Ordinal)
                        ? SeatControl.Human
                        : SeatControl.Ai,
                })),
            };
        }

        World = world;
        Ruleset = ruleset;
        Scenario = scenario;

        _registry = SystemRegistry.FromEngineAssembly();
        _dispatcher = new CommandDispatcher(_registry, ruleset, world, NullEventSink.Instance);
        _coordinator = new TurnCoordinator(_registry, ruleset, world, NullEventSink.Instance, _dispatcher);

        var initial = GameStateFactory.CreateInitial(world, ruleset, scenario);
        State = seedOverride.HasValue ? initial with { RandomSeed = seedOverride.Value } : initial;

        // T87 rework round 1 (bug #380, review B4): this used to run only for a --seat session
        // (_humanSeatNationId is not null), so a plain hotseat session's own first turn-order seat was
        // never checked at the true start of its own turn -- reachable the instant that seat starts
        // already in debt, past the hard end year, or otherwise fallen. AdvanceToHumanSeat's own logic
        // (the turn-start check, then PausesHere-gated advancing) already generalizes to hotseat without
        // any change, since PausesHere already branches on _humanSeatNationId itself; only pure watch
        // mode (no human seat anywhere) is excluded, matching HandleEndWatchMode's own "one round per
        // end" contract, which construction must not pre-empt.
        if (!_isWatchMode)
        {
            _pendingNewsBaseline = State.NewsLog.Slots;
            _pendingPrelude = AdvanceToHumanSeat();
        }
    }

    /// <summary>
    /// Builds a session already in progress from <paramref name="save"/> — <c>docs/tasks/T95.md</c>
    /// (#467), Done-when 2: continues the same game (same state, same seats, same active seat and phase)
    /// rather than starting a new one through <see cref="Model.GameStateFactory.CreateInitial"/>.
    /// </summary>
    /// <param name="world">
    /// The world the caller has already resolved and checked <paramref name="save"/> against (typically
    /// <see cref="Persistence.SaveManager.LoadFile"/>'s own <c>expectedWorld</c>).
    /// </param>
    /// <param name="ruleset">The ruleset the caller has already resolved and checked <paramref name="save"/> against.</param>
    /// <param name="scenario">
    /// The scenario the caller resolved from <paramref name="save"/>'s own <see cref="Model.SaveGame.ScenarioId"/>
    /// (<see cref="Persistence.SaveManager.LoadFile"/> does not itself check it, only World/Ruleset — see
    /// <c>game-design.md</c> §"Original-save compatibility"). Kept for this session's own public
    /// <see cref="Scenario"/> exposure (its seat list, its map size); every seat's actual control comes
    /// from <paramref name="save"/>'s state, never from this parameter's own <c>Seats</c> — see
    /// <see cref="ResumeFrom"/>.
    /// </param>
    /// <param name="save">
    /// The save to resume, already loaded and validated by the caller (typically
    /// <see cref="Persistence.SaveManager.LoadFile"/>, which throws a typed
    /// <see cref="Serialization.GameDataException"/> for a missing file, a malformed save, or a save
    /// recorded against a different world or ruleset — Done-when 5).
    /// </param>
    public GameSession(World world, Ruleset ruleset, Scenario scenario, SaveGame save)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(ruleset);
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(save);

        World = world;
        Ruleset = ruleset;
        Scenario = scenario;

        _registry = SystemRegistry.FromEngineAssembly();
        _dispatcher = new CommandDispatcher(_registry, ruleset, world, NullEventSink.Instance);
        _coordinator = new TurnCoordinator(_registry, ruleset, world, NullEventSink.Instance, _dispatcher);

        ResumeFrom(save);
    }

    /// <summary>
    /// Restores this session's own bookkeeping from <paramref name="save"/> — shared by the resume
    /// constructor just above and the <c>load &lt;path&gt;</c> command
    /// (<c>GameSession.Commands.cs</c>'s own <c>HandleLoad</c>), so a mid-session <c>load</c> and a
    /// cold-start resume behave identically.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Done-when 4 (#382's hazard): the human seat comes from the saved state, never from the
    /// scenario or a <c>--seat</c> override.</strong> Exactly one <see cref="Model.SeatControl.Human"/>
    /// nation among <paramref name="save"/>'s own <see cref="Model.GameState.Nations"/> is treated the
    /// same way a <c>--seat</c> construction treats its one named nation — <see cref="_humanSeatNationId"/>
    /// set, watch mode adopted if that one seat is later lost (<see cref="AnnounceAndAdoptWatchModeIfSeatIsLost"/>)
    /// rather than the whole session ending. Two or more is hotseat (<see cref="_humanSeatNationId"/> stays
    /// <see langword="null"/>, exactly like a scenario-driven hotseat session — <see cref="PausesHere"/>
    /// and <see cref="AnnounceGameOverIfNoHumanSeatRemains"/> already fall back to
    /// <see cref="Model.NationState.Control"/> for that case). Zero is watch mode. This reads the state's
    /// own <em>current</em> control, unlike the ordinary constructor's own <see cref="_isWatchMode"/>
    /// (fixed once, from the scenario, at construction) — deliberately: a resumed session has no
    /// scenario-time seat list left that still means anything once the game has been played forward.
    /// </para>
    /// <para>
    /// <strong>No fast-forward.</strong> The ordinary constructor's own <see cref="AdvanceToHumanSeat"/>
    /// exists to reach the human seat from a freshly built scenario's own turn-order start; a save's own
    /// <see cref="Model.GameState.ActiveSeatIndex"/> already records exactly where play stopped
    /// (Done-when 2's "the same active seat and phase"), so nothing here plays a single turn — every
    /// prelude field is simply cleared.
    /// </para>
    /// <para>
    /// <strong>Documented gap — rework round 2, R2-B1 (the user's decision on
    /// <see href="https://github.com/diegoami/imperial_conquest_2/issues/469">#469</see>, 2026-09-28):
    /// a save made before the saving session's own first <c>end</c> resumes with a different first
    /// <c>end</c> footer.</strong> <see cref="_pendingNewsBaseline"/> is cleared here rather than
    /// restored, same as <see cref="_pendingPrelude"/> just above — but unlike the prelude's own
    /// narration (which the <em>saving</em> session already flushed on its own first <see cref="Submit"/>
    /// call, whichever command that was, so it is never lost), the baseline itself is what T87 N4's
    /// <see cref="HandleEndSeated"/> reads for its own <em>first-ever</em> call's "News:" section — the
    /// pre-seat AI turns' own news, snapshotted at construction, shown bundled into whichever <c>end</c>
    /// happens to be the human's first. A save taken before that first <c>end</c> has no such snapshot to
    /// give a resumed session (nothing in <see cref="Model.SaveGame"/> records it), so the resumed
    /// session's own first <c>end</c> shows only that round's own news, while the uninterrupted session's
    /// first <c>end</c> would have shown the pre-seat news too — the independent reviewer's own probe: 13
    /// of classical-mediterranean's 16 <c>--seat</c> choices lose 4–5 News lines this way, including a
    /// player's own city falling. <strong>Not a state divergence</strong>: <see cref="State"/> is
    /// identical either way, every <c>end</c> from the second one on matches, and every entry is still in
    /// <see cref="Model.GameState.NewsLog"/> — the <c>news</c> command shows them regardless. No in-Owns
    /// fix is clean (nothing in a save records whether the baseline was already consumed, and a heuristic
    /// keyed on the turn index is wrong for a hotseat game saved on a later human's own first turn), so
    /// this is accepted as a documented gap rather than fixed — see
    /// <see href="https://github.com/diegoami/imperial_conquest_2/issues/487">#487</see> (item 1) for the
    /// follow-up, and <see href="https://github.com/diegoami/imperial_conquest_2/issues/486">#486</see>
    /// for the pre-existing T87 inconsistency (the same baseline is also never restored for a session's
    /// own <em>later</em> commands issued before its first <c>end</c>, uninterrupted or not) that produces
    /// the toy-world half of this same symptom.
    /// </para>
    /// <para>
    /// <strong>Not restored, and now refused rather than silently dropped — rework round 1, B1 (the
    /// user's decision, 2026-09-28).</strong> <see cref="_pendingPeaceTreatyOffers"/> has no counterpart
    /// anywhere in <see cref="Model.SaveGame"/> or <see cref="Model.GameState"/> (only
    /// <see cref="Model.GameState.PendingOffer"/>, a single, different alliance/trade slot); persisting it
    /// would mean changing one of those two types, both outside this task's Owns. The first round of this
    /// PR treated this as a documented gap (a save taken mid-offer would silently drop the dialog on
    /// resume — the war continuing as if the offer had never been raised, a different game from the one
    /// that was saved). The independent reviewer's own probe showed that is not an acceptable gap: it
    /// breaks Done-when 2 ("the same state, the same seats, and the same active seat and phase") and the
    /// wording of Done-when 3 for a game saved at that turn. The fix instead lives in
    /// <c>GameSession.Commands.cs</c>'s own <c>HandleSave</c>: <c>save &lt;path&gt;</c> is refused outright
    /// while <see cref="_pendingPeaceTreatyOffers"/> is non-empty, so no save can ever capture the state
    /// this field's own loss would corrupt.
    /// </para>
    /// <para>
    /// <strong>N1 (rework round 1, non-blocking; round 2, R2-N1): the seat mode is not fully recoverable
    /// from <see cref="Model.NationState.Control"/> alone, so some sessions resume in a different
    /// <em>mode</em> than they ran in — proved by the independent reviewer's own probes, not fixed (no
    /// in-Owns fix is clean; the follow-up is
    /// <see href="https://github.com/diegoami/imperial_conquest_2/issues/487">#487</see>, item 2).</strong>
    /// Three cases:
    /// </para>
    /// <list type="number">
    /// <item><description>
    /// <strong>A scenario-driven session with exactly one human seat and no <c>--seat</c></strong> (for
    /// example <c>toy-3city</c>, run with no <c>--seat</c> flag at all). The uninterrupted session has
    /// <see cref="_humanSeatNationId"/> <see langword="null"/> (plain hotseat, one human seat by scenario
    /// design); a save resumed from it has <see cref="_humanSeatNationId"/> set to that one nation
    /// (<c>--seat</c>-style), because <see cref="ResumeFrom"/> cannot tell the two apart from
    /// <see cref="Model.NationState.Control"/> alone. Observable: <c>help</c>'s own compact-view lines
    /// (<c>status mine</c>/<c>armies</c>/<c>cities</c>, gated on <c>_humanSeatNationId is not null ||
    /// _isWatchMode</c>) appear after resume but not before; and if that seat is later lost, the
    /// uninterrupted session ends entirely ("No human seat remains. The game is over.",
    /// <see cref="AnnounceGameOverIfNoHumanSeatRemains"/>) while the resumed one falls back to watch mode
    /// (<see cref="AnnounceAndAdoptWatchModeIfSeatIsLost"/>) and keeps running. The same applies to a
    /// hotseat game saved after one of its two humans was already deposed, leaving exactly one.
    /// </description></item>
    /// <item><description>
    /// <strong>A <c>--seat</c> session saved after its own seat was already lost.</strong> The saved state
    /// has zero <see cref="Model.SeatControl.Human"/> nations, so the resumed session is watch mode from
    /// construction (<see cref="_isWatchMode"/> true, <see cref="_humanSeatNationId"/> <see langword="null"/>)
    /// rather than "watch mode because <em>this</em> seat fell" (<see cref="_seatLost"/> true with
    /// <see cref="_humanSeatNationId"/> still naming the fallen seat, the uninterrupted session's own
    /// shape). Observable: <see cref="DefaultViewNationId"/> falls back to whichever seat is currently
    /// active rather than naming the fallen seat by id — <c>armies</c>/<c>status mine</c> show a different
    /// nation's own view. <c>end</c> itself still matches (both paths reach
    /// <see cref="HandleEndWatchMode"/>).
    /// </description></item>
    /// <item><description>
    /// Both cases are the same root cause: <see cref="Model.NationState.Control"/> records <em>who is
    /// human now</em>, not <em>how this session was started</em> (a single designated seat versus plain
    /// hotseat), and a fallen seat's own identity is gone once no nation is <see cref="Model.SeatControl.Human"/>
    /// at all. The <c>--seat</c>-style reading <see cref="ResumeFrom"/> picks for "exactly one human
    /// nation" is right for Godot (which always resumes with a single seat in mind), so this is left as
    /// designed rather than changed; whether "the same seats" (Done-when 2) is read to cover session
    /// *mode* as well as seat *control* is the user's call, folded into B1's own decision if so.
    /// </description></item>
    /// </list>
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(State))]
    private void ResumeFrom(SaveGame save)
    {
        ArgumentNullException.ThrowIfNull(save);

        State = save.State;

        var humanNationIds = State.Nations
            .Where(n => n.Control == SeatControl.Human)
            .Select(n => n.Id)
            .ToList();
        _humanSeatNationId = humanNationIds.Count == 1 ? humanNationIds[0] : null;
        _isWatchMode = humanNationIds.Count == 0;
        _seatLost = false;
        _gameOver = false;
        _pendingPrelude = null;
        _pendingNewsBaseline = null;
        _pendingPeaceTreatyOffers.Clear();
        _pendingBattleResults.Clear();
        LastBattles = Array.Empty<Battle.BattleResult>();
        _pendingSeatFalls.Clear();
        LastSeatFalls = Array.Empty<SeatFall>();
    }

    /// <summary>
    /// Plays every AI seat that comes before <see cref="_humanSeatNationId"/> in the starting turn
    /// order, so the session is already paused on it before the first command is accepted — the same
    /// per-seat "takes its turn" line <see cref="HandleEnd"/> prints later in the same round, produced
    /// here because this round's first seats go before any line has been submitted to render them
    /// against. <see cref="_humanSeatNationId"/> itself is never played here (Done-when 2: "Carthage is
    /// never played by the AI").
    /// </summary>
    /// <remarks>
    /// T87 (bug #380): the starting seat gets the same turn-start fall check every later seat gets from
    /// <see cref="PlayUntilOneFullLapOrRepeat"/>'s own loop, before this method's own <see cref="PausesHere"/>
    /// gate — a freshly constructed scenario is never expected to start a human seat already in debt or
    /// past the hard end year, but nothing here assumes that; it is simply checked, the same way every
    /// later turn boundary is.
    /// </remarks>
    private List<string>? AdvanceToHumanSeat()
    {
        var lines = new List<string>();
        DepositActiveHumanSeatIfItShouldFallAtTurnStart(lines);
        if (AnnounceAndAdoptWatchModeIfSeatIsLost(lines) || AnnounceGameOverIfNoHumanSeatRemains(lines))
        {
            return lines.Count > 0 ? lines : null;
        }

        if (!PausesHere())
        {
            // Shares PlayUntilOneFullLapOrRepeat with HandleEndSeated's own AI loop and HandleEndWatchMode
            // -- the same "stop the instant a seat would repeat" rule this task's review asked for is
            // exactly as correct here as it is mid-game.
            PlayUntilOneFullLapOrRepeat(lines, new HashSet<string>(StringComparer.Ordinal), PausesHere);
        }

        return lines.Count > 0 ? lines : null;
    }

    /// <summary>
    /// <strong>T87, bug #380: the human deposition check, moved to the true start of a seat's own turn —
    /// before it can issue any order</strong>, not the seat's own <c>end</c>
    /// (<see cref="HumanDepositionSystem"/>'s own remarks explain why that system alone could not be the
    /// fix). Called at the one place a seat becomes the active one and has not yet been given any order:
    /// the top of <see cref="PlayUntilOneFullLapOrRepeat"/>'s own loop, and <see cref="AdvanceToHumanSeat"/>
    /// for the very first seat of the session. A no-op unless the <em>active</em> seat is human — the
    /// AI's own debt check is <see cref="AiDepositionHandler"/>'s, at the quarter boundary, untouched.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Mutates <see cref="State"/> directly with the same three calls
    /// <see cref="HumanDepositionSystem.Execute"/> makes — <see cref="Deposition.ShouldFallAtHumanTurnStart"/>,
    /// <see cref="Deposition.ApplyEffects"/>, <see cref="Deposition.ResetRelations"/> — rather than going
    /// through <see cref="TurnCoordinator.RunTurn"/>: running the <em>whole</em> seat-scoped pipeline this
    /// early would also run <see cref="TurnPhase.Orders"/> and <see cref="TurnPhase.SeatEnd"/> (which
    /// rotates to the next seat) before this seat had a chance to act at all, which is not what "moved to
    /// the start of the turn" means.
    /// </para>
    /// <para>
    /// Appends the same fall message <see cref="HumanLeaderFallsMessage"/> gives any human seat's fall,
    /// keyed by the reason (year, total conquest, unpopularity or unpaid upkeep) exactly as
    /// <c>THumanFalls_InitializeForm</c> reads it — see that method's own remarks for the citations. Does
    /// <em>not</em> itself call <see cref="AnnounceAndAdoptWatchModeIfSeatIsLost"/> or
    /// <see cref="AnnounceGameOverIfNoHumanSeatRemains"/>: both callers check those immediately afterwards,
    /// before deciding whether to still stop here or play the now-AI seat's turn this same pass.
    /// </para>
    /// </remarks>
    private void DepositActiveHumanSeatIfItShouldFallAtTurnStart(List<string> lines)
    {
        var nation = State.NationById(State.ActiveNationId);
        if (nation is null
            || nation.Control != SeatControl.Human
            || nation.Eliminated
            || !Deposition.ShouldFallAtHumanTurnStart(nation, State, Ruleset))
        {
            return;
        }

        // T87 rework round 1 (review B1): the message is built from the pre-fall nation, not the
        // post-fall one. THumanFalls_InitializeForm reads the fall's own reason (chiefly unity) before
        // FUN_0044C8F0 ever writes its own +150 -- TPremierForm_HumanLeaderFalls (dump :50787) runs
        // before the unity/treasury/relation writes (:50795-50798) -- so a nation at unity 399 (just
        // under DebtUnityThreshold) must still read as "unpopularity", not the wrong branch a
        // post-fall unity of 549 would select.
        var message = HumanLeaderFallsMessage(nation);

        // T138: the fall is recorded from the pre-fall nation, the same one the message above is built
        // from -- TPremierForm_HumanLeaderFalls opens the window before FUN_0044C8F0's own writes, so the
        // window's end figures are the treasury and unity the nation actually held (see SeatFall's own
        // remarks). A no-op if some other path already recorded this same seat this call.
        RecordSeatFall(nation);

        var deposed = Deposition.ApplyEffects(nation, Ruleset) with { Control = SeatControl.Ai };
        var relations = Deposition.ResetRelations(State.Relations, nation.Id, Ruleset);
        var updatedNations = State.Nations.Select(n =>
            string.Equals(n.Id, nation.Id, StringComparison.Ordinal) ? deposed : n);
        State = State with { Nations = ValueList.From(updatedNations), Relations = relations };

        lines.Add(message);
    }

    /// <summary>
    /// The <c>THumanFalls</c> screen's own reason text (<c>THumanFalls_InitializeForm</c>, :56391–56404),
    /// read in the original's own priority order: total conquest, the hard end year, then — only when
    /// <paramref name="fallen"/> was <em>not</em> conquered — unpopularity or unpaid upkeep; conquered
    /// otherwise. DoD 3's own two-way split ("the conquered text when conquered-by is set, and a
    /// deposition text otherwise") is this order's own last two branches; the other two apply equally to
    /// an elimination (a nation that happens to reach either while falling) and to this task's own
    /// deposition-at-turn-start check.
    /// </summary>
    /// <remarks>
    /// <strong>[confirmed: decompile]</strong>, read directly from the dump
    /// (<c>%LOCALAPPDATA%\ReTools\all_app_functions.txt</c>, <c>THumanFalls_InitializeForm</c> @
    /// <c>0x00455e38</c>, :56391–56404 — the same function <c>decompiled-elimination-cleanup.md</c> §3
    /// already cites for the conquered-by text): <c>if cityCount &gt;= 0x14e (334): "You have conquerred
    /// the Mediterranean, a unique achievement."; elif year==0xfa (250): "You have reached the end of your
    /// allotted 20 years."; elif conqueredBy &lt; 0 (not conquered): unity&lt;400 ? "Your unpopularity has
    /// forced the army to overthrow you." : "Your army have deposed you because they have not been paid.";
    /// else: "Your nation has been conquerred by &lt;X&gt;."</c>. The first and third of these four strings
    /// are already in <c>tests/fixtures/corpus.json</c> as <c>gameOverForm.victoryAllCities</c> and
    /// <c>gameOverForm.conqueredByNation</c> (T42, cited to a different report reaching the same lines);
    /// the "20 years" and "unpopularity"/"unpaid" strings are not yet in that corpus (outside this task's
    /// Owns list to add), but the latter two are already quoted verbatim in
    /// <c>upkeep-payment-and-desertion.md</c> (<see cref="Deposition"/>'s own top-level citation), and the
    /// "334 cities"/"250 BC" thresholds are the same ones <see cref="Deposition.ShouldFallAtHumanTurnStart"/>
    /// already generalizes off <see cref="GameState.Cities"/>'s own count and <see cref="VictoryRules.HardEndYearBc"/>
    /// — never a re-invented literal.
    /// </remarks>
    private string HumanLeaderFallsMessage(NationState fallen)
    {
        var (reason, conqueror) = FallReasonOf(fallen);
        return FallMessageFor(reason, conqueror);
    }

    /// <summary>
    /// The text for one already-classified fall — <see cref="HumanLeaderFallsMessage"/>'s five strings,
    /// character for character (the two corpus literals included), kept in one place so
    /// <see cref="RecordSeatFall"/>'s own <see cref="SeatFall.Text"/> is the very line the session printed.
    /// </summary>
    private string FallMessageFor(SeatFallReason reason, string? conqueror) => reason switch
    {
        SeatFallReason.AllCities => "You have conquerred the Mediterranean, a unique achievement.",
        SeatFallReason.HardEndYear => "You have reached the end of your allotted 20 years.",
        SeatFallReason.Unpopularity => "Your unpopularity has forced the army to overthrow you.",
        SeatFallReason.Unpaid => "Your army have deposed you because they have not been paid.",
        SeatFallReason.Conquered => $"Your nation has been conquerred by {NationDisplay(conqueror!)}.",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown seat fall reason."),
    };

    /// <summary>
    /// T87, DoD 3: shows the fall message for any nation in <paramref name="before"/> that was
    /// <see cref="SeatControl.Human"/> and is not any more (eliminated, or deposed) — the general form of
    /// what <see cref="AnnounceAndAdoptWatchModeIfSeatIsLost"/> already does for the CLI's own <c>--seat</c>
    /// nation specifically. Called from <see cref="IssueCommand"/>, the one path that can eliminate a
    /// human seat other than the active one (a human-issued capture against another human, hotseat only —
    /// the CLI's own <c>--seat</c> mode never seats a second human). Iterates <paramref name="before"/> in
    /// its own list order (<see cref="GameState.Nations"/>'s own, deterministic), never a
    /// <see cref="Dictionary{TKey,TValue}"/>'s.
    /// </summary>
    private void AppendFallMessagesForNewlyLostHumanSeats(List<string> lines, IReadOnlyList<NationState> before)
    {
        foreach (var previously in before)
        {
            if (previously.Control != SeatControl.Human || previously.Eliminated)
            {
                continue;
            }

            var now = State.NationById(previously.Id);
            if (now is not null && (now.Eliminated || now.Control != SeatControl.Human))
            {
                RecordSeatFall(now);
                lines.Add(HumanLeaderFallsMessage(now));
            }
        }
    }

    /// <summary>
    /// DoD 3's third bullet: "When no human seat remains, the session reports that the game is over."
    /// Checked wherever a seat's fall could have just removed the last one — this task's own
    /// <see cref="DepositActiveHumanSeatIfItShouldFallAtTurnStart"/>, every place
    /// <see cref="AnnounceAndAdoptWatchModeIfSeatIsLost"/> is already checked, and
    /// <see cref="IssueCommand"/> (a human-issued capture eliminating a different human seat,
    /// hotseat only), since only those paths can change any nation's <see cref="Model.NationState.Control"/>
    /// or <see cref="Model.NationState.Eliminated"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>[designed]</strong>: the original closes the whole application once
    /// <c>FUN_00449078</c> finds no human seat left (<c>decompiled-elimination-cleanup.md</c> §3, "If no
    /// human seat is left, it closes the forms and the game is over") rather than printing anything — a
    /// CLI has no window to close, so this reports it as a line instead and asks <see cref="Submit"/> to
    /// end the session (<see cref="SessionOutput.ShouldExit"/>), the same signal the <c>quit</c> command
    /// already uses.
    /// </para>
    /// <para>
    /// <strong>Never for a <c>--seat</c> session.</strong> Such a session names exactly one human seat
    /// (Done-when 2's own "<c>--seat</c> makes its nation the CLI's <em>only</em> human seat"), so that
    /// seat falling is <em>always</em> "no human seat remains" in the literal sense — but T83's own
    /// <see cref="AnnounceAndAdoptWatchModeIfSeatIsLost"/> already exists precisely to answer that case
    /// with watch mode, not a stopped session ("T83's lost-seat fallback to watch mode still holds",
    /// this task's own Done-when 3). This method's own "no human seat remains" is instead about hotseat,
    /// where more than one seat can be human and there is no single CLI-designated seat to fall back to
    /// watching once every one of them has fallen.
    /// </para>
    /// </remarks>
    private bool AnnounceGameOverIfNoHumanSeatRemains(List<string> lines)
    {
        if (_gameOver)
        {
            return true;
        }

        // A session that started in watch mode never had a human seat to lose in the first place -- this
        // is "no human seat remains [any more]", not "there never was one" (Done-when 3's own wording,
        // "a human seat is handed over", presupposes one existed).
        if (_isWatchMode || _humanSeatNationId is not null || State.Nations.Any(n => n.Control == SeatControl.Human))
        {
            return false;
        }

        _gameOver = true;
        lines.Add("No human seat remains. The game is over.");
        return true;
    }

    /// <summary>The world this session is playing on.</summary>
    public World World { get; }

    /// <summary>The ruleset this session is playing under — never a C# literal for any number printed.</summary>
    public Ruleset Ruleset { get; }

    /// <summary>The scenario this session started from.</summary>
    public Scenario Scenario { get; }

    /// <summary>The session's current state. Advances only through <see cref="Submit"/>.</summary>
    public GameState State { get; private set; }

    /// <summary>
    /// T25 (plan #474): the same <see cref="Battle.BattleResult"/> list the most recent <see cref="Submit"/>
    /// call returned as <see cref="SessionOutput.Battles"/> — mirrored here so a caller that only sees a
    /// command's rendered lines (<see cref="GameMapView"/>'s and <see cref="ContextPanel"/>'s own
    /// <c>CommandIssued</c> events carry <c>IReadOnlyList&lt;string&gt;</c>, not the whole
    /// <see cref="SessionOutput"/>) can still ask "did that just resolve a battle" without every event in
    /// the UI layer having to be widened to carry a <see cref="SessionOutput"/> instead. Empty before the
    /// first <see cref="Submit"/> call, and again whenever that call resolved no battle.
    /// </summary>
    public IReadOnlyList<Battle.BattleResult> LastBattles { get; private set; } = Array.Empty<Battle.BattleResult>();

    /// <summary>
    /// T138: the same <see cref="SeatFall"/> list the most recent <see cref="Submit"/> call returned as
    /// <see cref="SessionOutput.SeatFalls"/> — mirrored here for exactly the reason <see cref="LastBattles"/>
    /// is (a UI that only sees a command's rendered lines can still ask "what fell on that call"), and for
    /// the game-end screen <c>MainGameScreen.OnCommandIssued</c> reads. Empty before the first
    /// <see cref="Submit"/> call, and again whenever that call recorded no fall.
    /// </summary>
    public IReadOnlyList<SeatFall> LastSeatFalls { get; private set; } = Array.Empty<SeatFall>();

    /// <summary>
    /// T138: whether no human seat can give orders any more — the game is over, as
    /// <see cref="AnnounceGameOverIfNoHumanSeatRemains"/> sets it, or a <c>--seat</c>-style session's own
    /// seat is lost and the session is in watch mode (<see cref="_seatLost"/>). The game-end window's own
    /// "The game is over." line and its Main menu / View map buttons read this; it changes no rule and
    /// stops no turn the engine was not already stopping.
    /// </summary>
    public bool IsGameOver => _gameOver || _seatLost;

    /// <summary>
    /// Parses and runs one command line, returning what to print and whether the session should stop.
    /// </summary>
    /// <param name="rawLine">One line of input, exactly as read from the script or the console.</param>
    public SessionOutput Submit(string rawLine)
    {
        ArgumentNullException.ThrowIfNull(rawLine);

        var lines = new List<string>();
        if (_pendingPrelude is { Count: > 0 } prelude)
        {
            lines.AddRange(prelude);
            lines.Add(string.Empty);
        }

        _pendingPrelude = null;

        lines.Add("> " + rawLine);
        var trimmed = rawLine.Trim();
        var shouldExit = false;

        if (trimmed.Length == 0)
        {
            lines.Add(string.Empty);
            return new SessionOutput(lines, shouldExit, FlushPendingBattleResults(), FlushPendingSeatFalls(), IsGameOver);
        }

        // T87 rework round 2, R2: DoD 2's own "before it can issue an order" also covers the moment
        // AFTER the game is already over -- _gameOver can already be true here, set by this same call's
        // own flushed prelude above (AdvanceToHumanSeat, at construction) or by an earlier Submit call,
        // and nothing past this point should still run: not a mutating command (IsWatchModeActive's own
        // gate below never runs once no seat exists to be "active" in the first place), and not a
        // read-only one either -- once no human seat remains anywhere, there is no seat left whose view
        // "status" or "news" would even be showing. ShouldExit was already true on whichever earlier call
        // first set _gameOver (Submit's own final "shouldExit || _gameOver"); this is only reached at all
        // if the caller submits again anyway.
        if (_gameOver)
        {
            lines.Add("The game is over. No further commands are accepted.");
            lines.Add(string.Empty);
            return new SessionOutput(lines, true, FlushPendingBattleResults(), FlushPendingSeatFalls(), IsGameOver);
        }

        var tokens = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var verb = tokens[0].ToLowerInvariant();

        // Done-when 3 (watch mode) and Done-when 7 (the seat has fallen) both mean "no seat to command":
        // review round 1, N5. Gating a fixed verb list here could not tell a real mutating command from a
        // typo, so an unrecognised verb like "stauts" was rejected as "watch mode" instead of "Unknown
        // command". The gate now lives where every mutating command actually funnels through instead --
        // IssueCommand (src/IC2.Engine/Presentation/GameSession.Commands.cs) for every verb below except
        // move and buy, which gate themselves the same way -- so an unrecognised verb still falls straight
        // through to this switch's own "default" case, in watch mode or not.
        switch (verb)
        {
            case "status":
                lines.AddRange(RenderStatusCommand(tokens));
                break;
            case "armies":
                lines.AddRange(RenderArmiesCommand(tokens));
                break;
            case "cities":
                lines.AddRange(RenderCitiesCommand(tokens));
                break;
            case "map":
                lines.AddRange(RenderMap());
                break;
            case "move":
                lines.AddRange(HandleMove(tokens));
                break;
            case "buy":
                lines.AddRange(HandleBuy(tokens));
                break;
            case "attack-army":
                lines.AddRange(HandleAttackArmy(tokens));
                break;
            case "besiege-city":
                lines.AddRange(HandleBesiegeCity(tokens));
                break;
            case "attack-fleet":
                lines.AddRange(HandleAttackFleet(tokens));
                break;
            case "disband-army":
                lines.AddRange(HandleDisbandArmy(tokens));
                break;
            case "join-armies":
                lines.AddRange(HandleJoinArmies(tokens));
                break;
            case "join-units":
                lines.AddRange(HandleJoinUnits(tokens));
                break;
            case "split-army":
                lines.AddRange(HandleSplitArmy(tokens));
                break;
            case "army-transfer":
                lines.AddRange(HandleArmyTransfer(tokens));
                break;
            case "split-unit":
                lines.AddRange(HandleSplitUnit(tokens));
                break;
            case "rename-unit":
                lines.AddRange(HandleRenameUnit(tokens));
                break;
            case "disband-unit":
                lines.AddRange(HandleDisbandUnit(tokens));
                break;
            case "order-city":
                lines.AddRange(HandleOrderCity(tokens));
                break;
            case "declare-war":
                lines.AddRange(HandleDeclareWar(tokens));
                break;
            case "make-peace":
                lines.AddRange(HandleMakePeace(tokens));
                break;
            case "propose-alliance":
                lines.AddRange(HandleProposeAlliance(tokens));
                break;
            case "propose-trade":
                lines.AddRange(HandleProposeTrade(tokens));
                break;
            case "accept-offer":
                lines.AddRange(HandleAcceptOffer(tokens));
                break;
            case "peace-yes":
                lines.AddRange(HandlePeaceTreatyAnswer(tokens, accept: true));
                break;
            case "peace-no":
                lines.AddRange(HandlePeaceTreatyAnswer(tokens, accept: false));
                break;
            case "mobilize":
                lines.AddRange(HandleMobilize(tokens));
                break;
            case "hire-mercenary":
                lines.AddRange(HandleHireMercenary(tokens));
                break;
            case "recruit-standing":
                lines.AddRange(HandleRecruitStanding(tokens));
                break;
            case "move-fleet":
                lines.AddRange(HandleMoveFleet(tokens));
                break;
            case "order-fleet":
                lines.AddRange(HandleOrderFleet(tokens));
                break;
            case "repair-fleet":
                lines.AddRange(HandleRepairFleet(tokens));
                break;
            case "scuttle-fleet":
                lines.AddRange(HandleScuttleFleet(tokens));
                break;
            case "split-fleet":
                lines.AddRange(HandleSplitFleet(tokens));
                break;
            case "join-fleets":
                lines.AddRange(HandleJoinFleets(tokens));
                break;
            case "embark-army":
                lines.AddRange(HandleEmbarkArmy(tokens));
                break;
            case "disembark-army":
                lines.AddRange(HandleDisembarkArmy(tokens));
                break;
            case "buy-fleet-supply":
                lines.AddRange(HandleBuyFleetSupply(tokens));
                break;
            case "fleet-transfer":
                lines.AddRange(HandleFleetTransfer(tokens));
                break;
            case "end":
                lines.AddRange(HandleEnd());
                break;
            case "save":
                lines.AddRange(HandleSave(trimmed));
                break;
            case "load":
                lines.AddRange(HandleLoad(trimmed));
                break;
            case "news":
                lines.AddRange(RenderNews());
                break;
            case "balance":
                lines.AddRange(RenderBalance());
                break;
            case "set-tax":
                lines.AddRange(HandleSetTax(tokens));
                break;
            case "transfer-money":
                lines.AddRange(HandleTransferMoney(tokens));
                break;
            case "help":
                lines.AddRange(RenderHelp());
                break;
            case "quit":
                lines.Add("Goodbye.");
                shouldExit = true;
                break;
            default:
                lines.Add($"Unknown command '{tokens[0]}'. Type 'help' for a list of commands.");
                break;
        }

        lines.Add(string.Empty);

        // T87, DoD 3: whichever command's own processing first found no human seat left (an "end" that
        // played the last human seat's fall, or a human-issued capture that eliminated the last other
        // human seat) also ends the session -- the same signal `quit` already uses.
        return new SessionOutput(lines, shouldExit || _gameOver, FlushPendingBattleResults(), FlushPendingSeatFalls(), IsGameOver);
    }

    private IReadOnlyList<string> HandleMove(string[] tokens)
    {
        if (IsWatchModeActive)
        {
            return new[] { WatchModeRejectionLine("Move") };
        }

        if (tokens.Length != 4
            || !int.TryParse(tokens[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var x)
            || !int.TryParse(tokens[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var y))
        {
            return new[] { "Usage: move <army> <x> <y>" };
        }

        var armyId = tokens[1];
        var beforeMoves = State.ArmyById(armyId)?.Moves;

        var command = new MoveArmyCommand(State.ActiveNationId, armyId, x, y);
        var result = _dispatcher.Dispatch(State, command);

        if (result.IsRejected)
        {
            return new[] { $"Move rejected ({result.Code}): {result.Rejection!.Message}" };
        }

        State = NewsLogWriter.Append(result.State, result.Events, Ruleset.NewsLog);

        var moved = result.Events.OfType<ArmyMoved>().First();

        // docs/task-catalogue.md T23 Done-when 2: a human army's move that ends against a non-hostile
        // city resupplies it automatically, through T38's AutomaticResupply.ForArmy -- deferred to this
        // task by T38's own remarks ("wiring the trigger... is T23's"). T22's own AI resupply pass
        // (Ai/AiResupplyPass.cs) is the AI's separate trigger for the same pure function; this is the
        // human seat's, composed here rather than inside MoveArmyCommandHandler, which is outside this
        // task's Owns list beyond the #226 terrain fix.
        ApplyAutomaticResupplyIfAgainstANonHostileCity(armyId);

        return new[]
        {
            $"{armyId} moved from ({moved.FromX},{moved.FromY}) to ({moved.ToX},{moved.ToY}), "
            + $"spending {moved.MovesSpent} of {beforeMoves} moves.",
        };
    }

    /// <summary>
    /// Done-when 2's automatic resupply, composed at the CLI boundary rather than inside
    /// <see cref="Movement.Commands.MoveArmyCommandHandler"/> (outside Owns). Finds the first city (list
    /// order, the same tie-break <see cref="Armies.Commands.DisbandArmyCommandHandler"/> uses) adjacent to
    /// the army's post-move position whose owner is not at war with the army's own nation -- the army's
    /// own city, or any nation still at peace -- and runs <see cref="AutomaticResupply.ForArmy"/> against
    /// it. A no-op when no such city adjoins the army's final tile.
    /// </summary>
    private void ApplyAutomaticResupplyIfAgainstANonHostileCity(string armyId)
    {
        var army = State.ArmyById(armyId);
        if (army is null || army.IsEmbarked)
        {
            return;
        }

        foreach (var city in State.Cities)
        {
            if (!AttackLegality.AreAdjacent(army.X, army.Y, city.X, city.Y) || IsAtWar(army.Nation, city.Owner))
            {
                continue;
            }

            var armyNation = State.NationById(army.Nation);
            var cityNation = State.NationById(city.Owner);
            if (armyNation is null || cityNation is null)
            {
                return;
            }

            var resupply = AutomaticResupply.ForArmy(army, city, armyNation, cityNation, Ruleset);
            State = State with
            {
                Armies = ValueList.From(State.Armies.Select(a =>
                    string.Equals(a.Id, resupply.Army.Id, StringComparison.Ordinal) ? resupply.Army : a)),
                Cities = ValueList.From(State.Cities.Select(c =>
                    string.Equals(c.Id, resupply.City.Id, StringComparison.Ordinal) ? resupply.City : c)),
                Nations = ValueList.From(State.Nations.Select(n =>
                    string.Equals(n.Id, resupply.ArmyNation.Id, StringComparison.Ordinal) ? resupply.ArmyNation
                    : string.Equals(n.Id, resupply.CityNation.Id, StringComparison.Ordinal) ? resupply.CityNation
                    : n)),
            };
            return;
        }
    }

    /// <summary>Whether <paramref name="nationA"/> and <paramref name="nationB"/> are at war, failing closed (not hostile) for an unknown nation or a nation compared against itself.</summary>
    private bool IsAtWar(string nationA, string nationB)
    {
        if (string.Equals(nationA, nationB, StringComparison.Ordinal))
        {
            return false;
        }

        var relations = State.Relations;
        if (relations.IndexOf(nationA) < 0 || relations.IndexOf(nationB) < 0)
        {
            return false;
        }

        return relations.Get(nationA, nationB) == Ruleset.Diplomacy.StateCodes.War;
    }

    private IReadOnlyList<string> HandleBuy(string[] tokens)
    {
        if (IsWatchModeActive)
        {
            return new[] { WatchModeRejectionLine("Purchase") };
        }

        // The 4-token city form is unchanged.
        if (tokens.Length == 4
            && int.TryParse(tokens[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var tons))
        {
            return BuyAtCity(tokens[1], tokens[2], tons);
        }

        // T134: the 5-token fleet form -- buy <army> fleet <fleet> <tons> -- reaches the
        // BuySupplyCommand.ProviderFleetId direction #147 folded in (T46 Done-when 11) but which this
        // session never built. A fleet provider is free (BuySupplyCommand's own [open] payment leg).
        if (tokens.Length == 5
            && string.Equals(tokens[2], "fleet", StringComparison.Ordinal)
            && int.TryParse(tokens[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var fleetTons))
        {
            return BuyFromFleet(tokens[1], tokens[3], fleetTons);
        }

        return new[] { "Usage: buy <army> <city> <tons>" };
    }

    /// <summary>The 4-token <c>buy &lt;army&gt; &lt;city&gt; &lt;tons&gt;</c> form — unchanged since T41.</summary>
    private IReadOnlyList<string> BuyAtCity(string armyId, string cityId, int tons)
    {
        var command = new BuySupplyCommand(State.ActiveNationId, armyId, cityId, tons);
        var result = _dispatcher.Dispatch(State, command);

        if (result.IsRejected)
        {
            return new[] { $"Purchase rejected ({result.Code}): {result.Rejection!.Message}" };
        }

        State = NewsLogWriter.Append(result.State, result.Events, Ruleset.NewsLog);

        var purchased = result.Events.OfType<ArmySupplyPurchased>().First();
        var costText = purchased.WasFreeOwnCity
            ? "free at your own city"
            : $"costing {purchased.TalentsPaid} talents";
        return new[]
        {
            $"{armyId} bought {purchased.AdmittedTons} tons of supply at {cityId}, {costText}.",
        };
    }

    /// <summary>
    /// T134: the 5-token <c>buy &lt;army&gt; fleet &lt;fleet&gt; &lt;tons&gt;</c> form — the Army menu's
    /// Supply army dialog reaching a fleet provider. The command's own handler moves tons only and pays
    /// nobody, so the reply line says <c>free</c>.
    /// </summary>
    private IReadOnlyList<string> BuyFromFleet(string armyId, string fleetId, int tons)
    {
        var command = new BuySupplyCommand(State.ActiveNationId, armyId, CityId: null, tons, fleetId);
        var result = _dispatcher.Dispatch(State, command);

        if (result.IsRejected)
        {
            return new[] { $"Purchase rejected ({result.Code}): {result.Rejection!.Message}" };
        }

        State = NewsLogWriter.Append(result.State, result.Events, Ruleset.NewsLog);

        var purchased = result.Events.OfType<ArmySupplyPurchasedFromFleet>().First();
        return new[]
        {
            $"{armyId} bought {purchased.AdmittedTons} tons of supply from fleet {fleetId}, free.",
        };
    }

    /// <summary>
    /// Whether the session has no seat left to pause on: watch mode from the start (Done-when 3), or a
    /// <c>--seat</c> session whose nation has since fallen (Done-when 7). Every mutating command's own
    /// gate reads this — <see cref="IssueCommand"/>, <see cref="HandleMove"/> and <see cref="HandleBuy"/>
    /// — rather than a verb allowlist in <see cref="Submit"/>, so an unrecognised verb still reaches the
    /// switch's own "Unknown command" case instead of being misreported as a watch-mode rejection (review
    /// round 1, N5).
    /// </summary>
    private bool IsWatchModeActive => _isWatchMode || _seatLost;

    /// <summary>
    /// The message every gated mutating command returns instead of dispatching, naming <c>--seat</c> as
    /// Done-when 3 requires. Review round 1, N11: one style throughout, and it is the same label each
    /// caller's own <em>real</em> rejection already uses — <see cref="IssueCommand"/> passes
    /// <see cref="ICommand.Kind"/> (its own "<c>{Kind} rejected (code): message</c>" convention), and
    /// <see cref="HandleMove"/>/<see cref="HandleBuy"/> pass <c>"Move"</c>/<c>"Purchase"</c> (their own
    /// "<c>Move rejected (code): message</c>"/"<c>Purchase rejected (code): message</c>" convention) —
    /// never a bare lowercase verb that convention does not otherwise use.
    /// </summary>
    private static string WatchModeRejectionLine(string label) =>
        $"{label} rejected: no seat to command (watch mode, or the --seat nation has fallen). "
        + "Pass --seat <nation> to play one.";

    /// <summary>
    /// <c>end</c> (rework round 1, B3(a)/(b)/(d)): a peace treaty offer still pending when the human ends
    /// their turn without answering it lapses -- Hazard 1's own "ignoring the offer leaves the war in
    /// place" (d), which this makes true regardless of how many turns pass, closing (a): before this, the
    /// offer never expired, so an answer given weeks later still took the honourable branch even though
    /// nothing about the original's own reasoning for "always honourable" (report §2.3, <c>[derived]</c>:
    /// the dialog is modal, so nothing can move between the gate and the human's Yes) survives that much
    /// play happening in between. Expiring on <c>end</c> also closes (b) -- a dropped offer no longer
    /// suppresses every later one for the rest of the game, since the field is clear again by the next
    /// battle -- and half of (c): in hotseat, a second human's own <c>end</c> now clears an offer neither
    /// human answered, rather than leaving it pending for whoever is active next to accept (the other half
    /// of (c), an issuer who is not this treaty's own human party, is closed at the command layer instead
    /// -- see <see cref="Diplomacy.Commands.AcceptPeaceTreatyRejections.IssuerNotPartyToTreaty"/> and
    /// <see cref="Diplomacy.Commands.AcceptPeaceTreatyRejections.IssuerNotHuman"/>).
    /// </summary>
    private IReadOnlyList<string> HandleEnd()
    {
        var lines = new List<string>();

        // Rework round 2, R1: only the offered human's own end lapses their offer -- State.ActiveNationId
        // here is whoever is submitting this "end" (the seat about to end its turn), which round 1 wrongly
        // treated as always being the offer's own human. In hotseat that let a second human's end lapse
        // the first human's still-unanswered offer before it ever reached their own prompt.
        //
        // Rework round 3, R3: an offer whose own human is gone for good (eliminated) is not re-checked
        // here as well -- deliberately. Every "end" reaches CapturePeaceTreatyOfferIfAny before this call
        // returns (HandleEndSeated's own N-f capture for the ending seat's RunTurn, or
        // PlayUntilOneFullLapOrRepeat's per-seat capture for every AI seat it plays). Move, buy and the
        // composed declare-war dispatch directly, bypassing IssueCommand's own capture call, but none of
        // them can eliminate a nation (only Cities/Capture/* calls NationElimination.ApplyIfLastCityLost),
        // so whatever eliminated the offered human still ran through a command or a played turn that did
        // reach a capture call (see IsOfferedHumanGone's own remarks, including why deposition needs no
        // check here at all). Adding the same check here as well was tried and proven redundant: removing
        // it failed no test in the whole suite, because nothing can reach this line with a stale offer
        // that a prior capture call has not already dropped. Kept out rather than kept as untested
        // belt-and-suspenders.
        // T87, N-g: each human's own end lapses only their own slot, never another human's.
        if (_pendingPeaceTreatyOffers.Remove(State.ActiveNationId))
        {
            lines.Add("The peace treaty offer has lapsed.");
        }

        lines.AddRange(IsWatchModeActive ? HandleEndWatchMode() : HandleEndSeated());
        return lines;
    }

    /// <summary>
    /// Plays <see cref="_coordinator"/>'s currently active seat, over and over, until the seat about to
    /// play next has <em>already</em> played this call — the general form of "one full lap of the turn
    /// order" that both <see cref="HandleEndWatchMode"/> and <see cref="HandleEndSeated"/>'s own AI loop
    /// build on. Tracking who has already played (rather than either "count up to
    /// <see cref="GameState.TurnOrder"/>'s length" or "wait to return to the seat this call started on")
    /// is what review round 1's N2 asked for: a nation eliminated <em>mid</em>-round is skipped by
    /// <c>SeatRotationSystem</c> from then on, so "return to the starting seat" can never fire again once
    /// that starting seat is the one eliminated — the old code's only remaining bound (a raw seat count)
    /// was one too high for <see cref="HandleEndSeated"/> specifically, because it did not account for the
    /// seat's own turn already having been played once, outside the loop, before the count started (bug
    /// #361's reappearance, N2's own probe: Seleucid played twice). Stopping the instant the next seat
    /// would be a repeat is correct regardless of how many nations are eliminated, when, or which one
    /// the round started on — see this class's own remarks in the PR for a worked trace.
    /// </summary>
    /// <param name="lines">Lines are appended here, one per seat played, in <see cref="AppendPerSeatLine"/>'s wording.</param>
    /// <param name="playedThisRound">Seeded with whichever seat(s) already played before this call — the round's own starting seat for <see cref="HandleEndWatchMode"/>, or that plus the CLI's own ended seat for <see cref="HandleEndSeated"/>.</param>
    /// <param name="stopEarly">
    /// Checked after every seat played, in addition to the "already played" rule — <see cref="PausesHere"/>
    /// for the seated path, or <see langword="null"/> for pure watch mode (which has no seat to pause on,
    /// only a lap to complete).
    /// </param>
    /// <remarks>
    /// <strong>T87 (bug #380):</strong> every iteration starts by giving whichever seat is now active a
    /// chance to fall at the true start of its own turn (<see cref="DepositActiveHumanSeatIfItShouldFallAtTurnStart"/>),
    /// <em>before</em> asking <paramref name="stopEarly"/> whether to pause here — <see cref="PausesHere"/>
    /// keys a <c>--seat</c> session purely on nation id, not <see cref="Model.NationState.Control"/>, so
    /// without this a seat this same check just deposed would still be paused on as if it were human.
    /// <see cref="AnnounceAndAdoptWatchModeIfSeatIsLost"/> is what actually notices the control change and
    /// stops the loop instead of pausing on it; both stop-checks are repeated after playing a seat too,
    /// for the ordinary elimination case that check already covered before this task.
    /// </remarks>
    private void PlayUntilOneFullLapOrRepeat(List<string> lines, HashSet<string> playedThisRound, Func<bool>? stopEarly)
    {
        while (true)
        {
            DepositActiveHumanSeatIfItShouldFallAtTurnStart(lines);
            if (AnnounceAndAdoptWatchModeIfSeatIsLost(lines) || AnnounceGameOverIfNoHumanSeatRemains(lines))
            {
                return;
            }

            if ((stopEarly is not null && stopEarly())
                || playedThisRound.Contains(State.ActiveNationId)
                || playedThisRound.Count >= State.TurnOrder.Count)
            {
                return;
            }

            var seat = State.ActiveNationId;
            playedThisRound.Add(seat);
            var nationsBeforeThisSeatsTurn = State.Nations;
            var result = _coordinator.RunTurn(State);
            State = result.State;
            AppendPerSeatLine(lines, seat, result.Events);

            // T88 (DoD 3, Hazard 1): an AI seat's own turn can resolve a battle that raises a
            // post-battle treaty for the human, who is not "at the prompt" here -- see
            // _pendingPeaceTreatyOffers's own remarks for why this cannot block the AI's turn loop.
            CapturePeaceTreatyOfferIfAny(lines, result.Events);
            CaptureBattleResultsIfAny(result.Events);

            // T87 rework round 1 (review B3): an AI seat's own turn can eliminate a *different* human
            // seat outright (a capture taking that human's last city) -- the only path
            // AnnounceAndAdoptWatchModeIfSeatIsLost (below, --seat-only) and
            // DepositActiveHumanSeatIfItShouldFallAtTurnStart (above, the *active* seat only) do not
            // already cover.
            AppendFallMessagesForNewlyLostHumanSeats(lines, nationsBeforeThisSeatsTurn);

            if (AnnounceAndAdoptWatchModeIfSeatIsLost(lines) || AnnounceGameOverIfNoHumanSeatRemains(lines))
            {
                return;
            }
        }
    }

    private void AppendPerSeatLine(List<string> lines, string seatId, IEnumerable<DomainEvent> events)
    {
        var ordersIssued = events.OfType<Ai.AiTurnDecided>().Sum(e => e.CommandsIssued);
        // Every seat played this way is AI-controlled: watch mode's whole lap is (by definition of
        // _isWatchMode), and HandleEndSeated's own loop only ever reaches a seat PausesHere() has not
        // already stopped it on -- which, since round 1's N8, means every OTHER seat, human-in-the-
        // scenario or not (--seat now hands every seat but its own to the AI).
        lines.Add(
            $"{NationDisplay(seatId)} takes its turn: "
            + $"{ordersIssued} order{(ordersIssued == 1 ? string.Empty : "s")} issued.");
        AppendWeatherLines(lines, events);
    }

    /// <summary>
    /// Done-when 7, the user's decision on PR #375's review (N2/N3): checked after every seat played this
    /// round, seated or watch mode alike. If the CLI's own <c>--seat</c> nation has just been found
    /// eliminated or deposed (<see cref="SeatControl.Ai"/>), announces it once, adopts watch mode
    /// permanently (<see cref="_seatLost"/> — neither elimination nor deposition reverses), and tells the
    /// caller to stop playing further seats this call: there is nothing left to pause on, and the round
    /// this call started (seated or not) has already correctly played every seat up to this point without
    /// a repeat, which is all Done-when 7 asks for ("no seat played twice").
    /// </summary>
    /// <remarks>
    /// T87, DoD 3: a deposition-caused fall (year, total conquest, unity or debt) already printed its own
    /// specific <see cref="HumanLeaderFallsMessage"/> the moment it happened, in
    /// <see cref="DepositActiveHumanSeatIfItShouldFallAtTurnStart"/> — this method's own generic "has been
    /// deposed" line is only the watch-mode-adoption notice for that case, not a second copy of the reason.
    /// An elimination (conquest or defection, outside this task's Owns) has no earlier message at all, so
    /// this is the first and only place that shows its own specific reason text (almost always "conquered
    /// by", per <see cref="HumanLeaderFallsMessage"/>'s own priority order).
    /// </remarks>
    private bool AnnounceAndAdoptWatchModeIfSeatIsLost(List<string> lines)
    {
        if (_seatLost || _humanSeatNationId is null)
        {
            return false;
        }

        var nation = State.NationById(_humanSeatNationId);
        if (nation is null || (!nation.Eliminated && nation.Control != SeatControl.Ai))
        {
            return false;
        }

        _seatLost = true;

        // T138: the CLI's own seat is a fall site too -- the same once-per-call record the other paths
        // make, so a fall this method alone notices is still on the game-end screen. An elimination or
        // deposition already recorded by AppendFallMessagesForNewlyLostHumanSeats /
        // DepositActiveHumanSeatIfItShouldFallAtTurnStart is a no-op here.
        RecordSeatFall(nation);

        lines.Add(
            nation.Eliminated
                ? $"{NationDisplay(_humanSeatNationId)} has fallen: {HumanLeaderFallsMessage(nation)} "
                  + "Watch mode from here on: one round per end, no orders."
                : $"{NationDisplay(_humanSeatNationId)} has been deposed and handed to the AI. "
                  + "Watch mode from here on: one round per end, no orders.");
        return true;
    }

    /// <summary>
    /// Watch mode's <c>end</c> (<c>docs/tasks/T83.md</c> Done-when 3, and Done-when 7 once a <c>--seat</c>
    /// session's own seat has fallen): plays exactly one full lap of <see cref="GameState.TurnOrder"/> from
    /// whichever seat happens to be active, robust to any seat (including the one this lap started on)
    /// being eliminated partway through — <see cref="PlayUntilOneFullLapOrRepeat"/>'s own remarks.
    /// </summary>
    private IReadOnlyList<string> HandleEndWatchMode()
    {
        var lines = new List<string>();
        var newsBeforeSlots = State.NewsLog.Slots;

        PlayUntilOneFullLapOrRepeat(lines, new HashSet<string>(StringComparer.Ordinal), stopEarly: null);

        AppendRoundFooter(lines, newsBeforeSlots);
        return lines;
    }

    /// <summary>
    /// <c>end</c> when there is a seat to pause on: either the CLI's own <c>--seat</c> nation
    /// (<c>docs/tasks/T83.md</c> Done-when 2 — <see cref="_humanSeatNationId"/> is never played by the
    /// AI), or, unchanged from before this task, whichever seat the scenario itself marks
    /// <see cref="SeatControl.Human"/> next (hotseat's "pass the device" case, exercised with no
    /// <c>--seat</c> flag at all by <c>toy-3city</c>'s own <c>north</c> seat — this is exactly the pre-T83
    /// loop, with its stopping condition named instead of re-derived).
    /// </summary>
    private IReadOnlyList<string> HandleEndSeated()
    {
        var lines = new List<string>();

        var endingSeat = State.ActiveNationId;

        // T23 hazard / bug #98: this used to infer how many news lines a round produced by counting
        // news-worthy EVENTS (result.Events.Count(e => e.IsNewsWorthy)). T42's round header appends two
        // log entries backed by no event at all (a blank line and the week header), and a dash-wrapped
        // elimination banner appends three entries for one event -- so that count and the log's own
        // growth disagree, and a round-ending turn could print the header while TakeLast under-counted
        // and silently dropped the real news line. Asking the log what it actually appended -- comparing
        // its slots before and after -- answers the only question that matters: how many entries to show,
        // whatever produced them.
        //
        // Review round 1, N4: NewsLog.Slots.Count alone stops answering that question correctly once the
        // 40-slot ring buffer is full (bug #376) -- CountNewsAppendedSince compares the slots themselves,
        // by reference, not their count. And the baseline is not always "this call's own starting slots":
        // the very first HandleEndSeated call after construction uses _pendingNewsBaseline instead, taken
        // before the construction-time prelude ran, so news the prelude itself produced (an alliance
        // formed against the player before their first turn) is not silently dropped from the very first
        // end's own summary.
        //
        // DoD 3 (#98 follow-up), moved here from PR #248's body per review round 1 (build-process.md T50
        // hazard: "a PR body does not survive the merge"): the catalogue's Done-when line asks for
        // coverage of "a round containing a header, a dash-delimited conquest line and an ordinary line".
        // GameSessionCommandsTests.HandleEnd_prints_every_entry_the_round_actually_appended_not_just_the_header
        // covers the general undercounting bug (a header plus more than one ordinary news-worthy entry).
        // GameSessionCommandsTests.HandleEnd_prints_the_dash_wrapped_elimination_from_an_ai_seats_own_turn
        // (T65, follow-up #256) covers the dash-wrapped sub-case specifically: a human-issued besiege-city
        // win is flushed by IssueCommand's own NewsLogWriter.Append call before this method's newsBefore
        // line ever runs, so a human capture never lands inside this round's newsBefore/newsAfter window --
        // reaching it needs an AI seat to besiege-and-capture a city within its own turn AND have that
        // capture eliminate the loser (NewsMessageCatalog.IsWrappedInDashLines only wraps an elimination,
        // not every conquest).
        var newsBeforeSlots = _pendingNewsBaseline ?? State.NewsLog.Slots;
        _pendingNewsBaseline = null;

        // T87 rework round 2, R3: this seat's own RunTurn is not only its own Orders/SeatEnd phases --
        // when endingSeat is also the round's own last seat, the same call runs every round-scoped phase
        // too (CityTick among them), including the quarterly economic tick's rebellion and rebirth. Either
        // one can defect away any nation's last city, human or not, endingSeat's own or another seat's
        // entirely -- see nationsBeforeThisSeatsTurn's own use in PlayUntilOneFullLapOrRepeat for the
        // identical shape. Snapshotting before the call, not after, is what lets the sweep below tell
        // "was human and now is not" from "was already AI".
        var nationsBeforeThisSeatsTurn = State.Nations;

        var result = _coordinator.RunTurn(State);
        State = result.State;
        lines.Add($"{NationDisplay(endingSeat)} ends its turn.");
        AppendWeatherLines(lines, result.Events);

        // T87 rework round 1 (bug #380, review B2/B5): there used to be a second check here, for
        // HumanDepositionSystem depositing endingSeat inside this very RunTurn call (its own SeatStart
        // phase). That system is deleted: the turn-start check now runs only from
        // DepositActiveHumanSeatIfItShouldFallAtTurnStart, strictly before a seat's own RunTurn is ever
        // called for it, and endingSeat's own Orders phase is a no-op for a Human-controlled active seat
        // (AiTurn.Run's own gate) -- neither of those two phases can still change Control or Eliminated
        // here. Review round 2, R3: an earlier revision of this remark generalized from that to "this call
        // can eliminate or depose neither endingSeat nor anyone else", which is false -- the round-scoped
        // phases this same RunTurn call also runs, when endingSeat ends the round, are a third path this
        // remark had not accounted for (see nationsBeforeThisSeatsTurn's own remark above). The old check
        // this replaced printed a fall message for the ending seat whether or not it had ever been human
        // before this same call (review B2's own probe), which its removal still correctly closes --
        // nationsBeforeThisSeatsTurn's own live comparison below only ever fires for a seat that
        // demonstrably was human just before this call, never unconditionally.
        AppendFallMessagesForNewlyLostHumanSeats(lines, nationsBeforeThisSeatsTurn);
        CapturePeaceTreatyOfferIfAny(lines, result.Events);
        CaptureBattleResultsIfAny(result.Events);

        if (!AnnounceAndAdoptWatchModeIfSeatIsLost(lines) && !AnnounceGameOverIfNoHumanSeatRemains(lines))
        {
            var playedThisRound = new HashSet<string>(StringComparer.Ordinal) { endingSeat };
            PlayUntilOneFullLapOrRepeat(lines, playedThisRound, PausesHere);
        }

        AppendRoundFooter(lines, newsBeforeSlots);
        return lines;
    }

    /// <summary>
    /// Whether the currently active seat is where <see cref="HandleEndSeated"/>'s AI loop should stop —
    /// the CLI's own <c>--seat</c> nation when one was given, otherwise (unchanged from before this task)
    /// whichever seat the scenario itself marks <see cref="SeatControl.Human"/>. Never called in watch
    /// mode, which has no seat to pause on at all (<see cref="HandleEndWatchMode"/> uses a different
    /// stopping rule: one full lap of the turn order).
    /// </summary>
    private bool PausesHere() =>
        _humanSeatNationId is not null
            ? string.Equals(State.ActiveNationId, _humanSeatNationId, StringComparison.Ordinal)
            : ActiveControl() == SeatControl.Human;

    /// <summary>
    /// The "Now: Week..." line and the round's news, shared verbatim between <see cref="HandleEndSeated"/>
    /// and <see cref="HandleEndWatchMode"/> — see <see cref="HandleEndSeated"/>'s own remarks (bug #98 and
    /// bug #376) for why <paramref name="newsBeforeSlots"/> is a snapshot of the log's own slots, not a
    /// count of anything.
    /// </summary>
    private void AppendRoundFooter(List<string> lines, IReadOnlyList<NewsEntry> newsBeforeSlots)
    {
        var newsAdded = CountNewsAppendedSince(newsBeforeSlots, State.NewsLog.Slots);

        var cal = State.Calendar;
        lines.Add(
            $"Now: Week {cal.Week}, {SeasonName(cal.SeasonIndex)} {cal.YearBc} BC. "
            + $"Active seat: {NationDisplay(State.ActiveNationId)}.");

        if (newsAdded > 0)
        {
            lines.Add("News:");
            foreach (var entry in State.NewsLog.Slots.TakeLast(newsAdded))
            {
                lines.Add("  " + entry.Text);
            }
        }
    }

    /// <summary>
    /// How many of <paramref name="after"/>'s entries were appended since <paramref name="before"/> was
    /// captured — bug #376, found in PR #375's review (N4): <c>NewsLog.Slots.Count</c> alone cannot answer
    /// this once the ruleset's 40-slot ring buffer (<see cref="Model.NewsLogRules.RingBufferSlots"/>) is
    /// full, because eviction keeps the count pinned at capacity even as new entries keep landing, so a
    /// plain subtraction silently reads zero. A surviving (not yet evicted) entry is the exact same
    /// <see cref="NewsEntry"/> <em>object</em> <see cref="Model.NewsLog.Append"/> carries forward by
    /// reference (its own <c>Slots.ToList()</c> never re-constructs an entry, only the log's outer list) --
    /// so walking <paramref name="after"/> from its newest slot backward and stopping at the first entry
    /// <paramref name="before"/> already held, compared <em>by reference</em> rather than by
    /// <see cref="NewsEntry"/>'s own value equality (duplicate text — the same weather effect two weeks
    /// running — is not a duplicate <em>entry</em>), finds exactly the new ones <em>among what
    /// <paramref name="after"/> still holds</em> — with no dependency on the buffer having had room left
    /// at the start of this call.
    /// </summary>
    /// <remarks>
    /// <strong>Review round 2, N9: this is not "how many entries this round wrote", full stop.</strong> If
    /// a single round appends more than <see cref="Model.NewsLogRules.RingBufferSlots"/> entries, the
    /// oldest of that round's own appends are evicted before this method is ever called — faithful to the
    /// original's own ring buffer, which this task was never asked to widen — so the count returned here
    /// is capped at the ring's own capacity, the same cap <c>news</c> itself is subject to. Bug #376 was
    /// specifically that the old count-subtraction silently returned <em>zero</em> whenever the buffer
    /// started full, not that it under-reported inside a single oversized round; this method fixes exactly
    /// that.
    /// </remarks>
    private static int CountNewsAppendedSince(IReadOnlyList<NewsEntry> before, IReadOnlyList<NewsEntry> after)
    {
        var beforeByReference = new HashSet<NewsEntry>(before, ReferenceEqualityComparer.Instance);
        var newCount = 0;
        for (var i = after.Count - 1; i >= 0 && !beforeByReference.Contains(after[i]); i--)
        {
            newCount++;
        }

        return newCount;
    }

    private static void AppendWeatherLines(List<string> lines, IEnumerable<DomainEvent> events)
    {
        foreach (var weather in events.OfType<Economy.WeatherEventFired>())
        {
            lines.Add($"  Weather: {weather.EffectId} (week {weather.Week}).");
        }
    }

    private SeatControl ActiveControl() => State.NationById(State.ActiveNationId)!.Control;

    /// <summary>
    /// The nation the compact views (<c>docs/tasks/T83.md</c> Done-when 4) default to when no explicit
    /// <c>[nation]</c> argument is given: the CLI's own <c>--seat</c> nation when one was given, otherwise
    /// whichever seat currently has the turn — the same seat a bare <c>status</c>/<c>end</c> already acts
    /// on, so "mine" means the same thing everywhere in one session.
    /// </summary>
    private string DefaultViewNationId => _humanSeatNationId ?? State.ActiveNationId;
}
