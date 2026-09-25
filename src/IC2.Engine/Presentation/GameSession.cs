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
    private readonly string? _humanSeatNationId;

    /// <summary>
    /// Whether this session has no seat to command at all — no <c>--seat</c> flag, and the scenario's own
    /// seats are every one <see cref="Model.SeatControl.Ai"/> — <c>docs/tasks/T83.md</c> Done-when 3
    /// ("watch mode"). Computed once, from the scenario's own seat assignments as loaded, not from
    /// <see cref="Model.GameState.Nations"/>'s live <c>Control</c> (which a system such as
    /// <c>HumanDepositionSystem</c> can flip mid-game): the CLI's own mode is a property of how the
    /// session was started, not of anything a turn can later change.
    /// </summary>
    private readonly bool _isWatchMode;

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
    /// own <c>peace-yes</c>/<c>peace-no</c> answer (T88, DoD 3, Hazard 1). At most one at a time, the same
    /// convention <see cref="Model.GameState.PendingOffer"/> uses for a trade/alliance offer — but this
    /// cannot live on <see cref="Model.GameState"/> itself, which is outside this task's Owns list, so it
    /// lives here instead: a session-scoped field, exactly like <see cref="_pendingPrelude"/> above. A
    /// battle raises this offer whether the human is "at the prompt" or not — it can happen inside an AI
    /// seat's own turn, when the human's army is the one that lost — so <see cref="_dispatcher"/>'s own
    /// per-command <see cref="Battle.PeaceTreatyOffered"/> (via <see cref="IssueCommand"/>) and every AI
    /// seat played through <see cref="PlayUntilOneFullLapOrRepeat"/> both feed
    /// <see cref="CapturePeaceTreatyOfferIfAny"/>, so the decision survives to be shown and answered later
    /// without blocking the AI's own turn loop — the hazard's own "smallest design" choice. A second offer
    /// raised while one is already pending is dropped rather than replacing it: the original's own dialog
    /// is modal (one battle's treaty at a time), and this build has no queue for a second one either.
    /// </summary>
    private PendingPeaceTreatyOffer? _pendingPeaceTreatyOffer;

    /// <summary>See <see cref="_pendingPeaceTreatyOffer"/>.</summary>
    private sealed record PendingPeaceTreatyOffer(string WinnerNationId, string LoserNationId);

    /// <summary>
    /// Scans <paramref name="events"/> for a <see cref="Battle.PeaceTreatyOffered"/> this session should
    /// show — one whose winner or loser is currently human-controlled, i.e. worth a human's own answer
    /// (a battle between two AI seats never raises this event at all;
    /// <see cref="Battle.InstantBattleResolver"/>'s own gate already restricts it to exactly one human
    /// side, but this session checks again rather than trusting that invariant blindly). Appends the
    /// dialog text and how to answer it to <paramref name="lines"/>, the same way every other line this
    /// call produced is appended. A no-op once an offer is already pending (see that field's own remarks).
    /// </summary>
    private void CapturePeaceTreatyOfferIfAny(List<string> lines, IEnumerable<DomainEvent> events)
    {
        if (_pendingPeaceTreatyOffer is not null)
        {
            return;
        }

        foreach (var offered in events.OfType<PeaceTreatyOffered>())
        {
            var winner = State.NationById(offered.WinnerNationId);
            var loser = State.NationById(offered.LoserNationId);
            if (winner is null || loser is null
                || (winner.Control != SeatControl.Human && loser.Control != SeatControl.Human))
            {
                continue;
            }

            _pendingPeaceTreatyOffer = new PendingPeaceTreatyOffer(offered.WinnerNationId, offered.LoserNationId);
            lines.Add(PeaceTreatyOfferDialogText(winner, loser));
            lines.Add("Type 'peace-yes' to accept or 'peace-no' to decline.");
            return;
        }
    }

    /// <summary>
    /// The offer's own wording, addressed to the human's side either way — the confirmed prefixes
    /// <strong>[confirmed: decompiled-war-cascade-and-peace-paths.md §2.3]</strong>,
    /// <c>TBattlePols_InitializeForm</c>'s "*After defeating you in battle &lt;W&gt; are willing to end
    /// …*" (winner AI) or "*After losing to you in battle &lt;L&gt; are willing to end …*" (winner human).
    /// The report's own ellipsis is exactly that — the words after "willing to end" are not read from the
    /// decompile — and the human-consent treaty never previews reparations (it is always honourable), so
    /// "the war" completes the sentence here rather than guessing at unconfirmed reparations wording.
    /// </summary>
    private static string PeaceTreatyOfferDialogText(NationState winner, NationState loser) =>
        winner.Control == SeatControl.Human
            ? $"After losing to you in battle, {loser.Name} are willing to end the war."
            : $"After defeating you in battle, {winner.Name} are willing to end the war.";

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

        if (_humanSeatNationId is not null)
        {
            _pendingNewsBaseline = State.NewsLog.Slots;
            _pendingPrelude = AdvanceToHumanSeat();
        }
    }

    /// <summary>
    /// Plays every AI seat that comes before <see cref="_humanSeatNationId"/> in the starting turn
    /// order, so the session is already paused on it before the first command is accepted — the same
    /// per-seat "takes its turn" line <see cref="HandleEnd"/> prints later in the same round, produced
    /// here because this round's first seats go before any line has been submitted to render them
    /// against. <see cref="_humanSeatNationId"/> itself is never played here (Done-when 2: "Carthage is
    /// never played by the AI").
    /// </summary>
    private List<string>? AdvanceToHumanSeat()
    {
        if (PausesHere())
        {
            return null;
        }

        // Shares PlayUntilOneFullLapOrRepeat with HandleEndSeated's own AI loop and HandleEndWatchMode --
        // the same "stop the instant a seat would repeat" rule this task's review asked for is exactly as
        // correct here as it is mid-game, even though a freshly created scenario can never actually start
        // with an eliminated or deposed seat, so AnnounceAndAdoptWatchModeIfSeatIsLost is not expected to
        // fire from inside this call in practice.
        var lines = new List<string>();
        PlayUntilOneFullLapOrRepeat(lines, new HashSet<string>(StringComparer.Ordinal), PausesHere);
        return lines.Count > 0 ? lines : null;
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
            return new SessionOutput(lines, shouldExit);
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
            case "news":
                lines.AddRange(RenderNews());
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
        return new SessionOutput(lines, shouldExit);
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

        if (tokens.Length != 4
            || !int.TryParse(tokens[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var tons))
        {
            return new[] { "Usage: buy <army> <city> <tons>" };
        }

        var armyId = tokens[1];
        var cityId = tokens[2];

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

    private IReadOnlyList<string> HandleEnd() => IsWatchModeActive ? HandleEndWatchMode() : HandleEndSeated();

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
    private void PlayUntilOneFullLapOrRepeat(List<string> lines, HashSet<string> playedThisRound, Func<bool>? stopEarly)
    {
        while ((stopEarly is null || !stopEarly())
               && !playedThisRound.Contains(State.ActiveNationId)
               && playedThisRound.Count < State.TurnOrder.Count)
        {
            var seat = State.ActiveNationId;
            playedThisRound.Add(seat);
            var result = _coordinator.RunTurn(State);
            State = result.State;
            AppendPerSeatLine(lines, seat, result.Events);

            // T88 (DoD 3, Hazard 1): an AI seat's own turn can resolve a battle that raises a
            // post-battle treaty for the human, who is not "at the prompt" here -- see
            // _pendingPeaceTreatyOffer's own remarks for why this cannot block the AI's turn loop.
            CapturePeaceTreatyOfferIfAny(lines, result.Events);

            if (AnnounceAndAdoptWatchModeIfSeatIsLost(lines))
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
        lines.Add(
            nation.Eliminated
                ? $"{NationDisplay(_humanSeatNationId)} has fallen. Watch mode from here on: "
                  + "one round per end, no orders."
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

        var result = _coordinator.RunTurn(State);
        State = result.State;
        lines.Add($"{NationDisplay(endingSeat)} ends its turn.");
        AppendWeatherLines(lines, result.Events);

        if (!AnnounceAndAdoptWatchModeIfSeatIsLost(lines))
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
