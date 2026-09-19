using System.Globalization;
using IC2.Engine.Core;
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

    /// <summary>Builds a session over a resolved world/ruleset/scenario, optionally overriding the seed.</summary>
    /// <param name="world">The loaded world.</param>
    /// <param name="ruleset">The loaded ruleset — the source of every number this session prints.</param>
    /// <param name="scenario">The scenario to start from.</param>
    /// <param name="seedOverride">
    /// When given, replaces <see cref="Scenario.RandomSeed"/> in the starting state — the CLI's
    /// <c>--seed</c> option. <see langword="null"/> keeps the scenario's own seed.
    /// </param>
    public GameSession(World world, Ruleset ruleset, Scenario scenario, ulong? seedOverride = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(ruleset);
        ArgumentNullException.ThrowIfNull(scenario);

        World = world;
        Ruleset = ruleset;
        Scenario = scenario;

        _registry = SystemRegistry.FromEngineAssembly();
        _dispatcher = new CommandDispatcher(_registry, ruleset, world, NullEventSink.Instance);
        _coordinator = new TurnCoordinator(_registry, ruleset, world, NullEventSink.Instance, _dispatcher);

        var initial = GameStateFactory.CreateInitial(world, ruleset, scenario);
        State = seedOverride.HasValue ? initial with { RandomSeed = seedOverride.Value } : initial;
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

        var lines = new List<string> { "> " + rawLine };
        var trimmed = rawLine.Trim();
        var shouldExit = false;

        if (trimmed.Length == 0)
        {
            lines.Add(string.Empty);
            return new SessionOutput(lines, shouldExit);
        }

        var tokens = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var verb = tokens[0].ToLowerInvariant();

        switch (verb)
        {
            case "status":
                lines.AddRange(RenderStatus());
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
        return new[]
        {
            $"{armyId} moved from ({moved.FromX},{moved.FromY}) to ({moved.ToX},{moved.ToY}), "
            + $"spending {moved.MovesSpent} of {beforeMoves} moves.",
        };
    }

    private IReadOnlyList<string> HandleBuy(string[] tokens)
    {
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

    private IReadOnlyList<string> HandleEnd()
    {
        var lines = new List<string>();

        var endingSeat = State.ActiveNationId;
        var result = _coordinator.RunTurn(State);
        State = result.State;
        var newsAdded = result.Events.Count(e => e.IsNewsWorthy);
        lines.Add($"{NationDisplay(endingSeat)} ends its turn.");
        AppendWeatherLines(lines, result.Events);

        var guard = 0;
        while (ActiveControl() == SeatControl.Ai && guard < State.TurnOrder.Count)
        {
            var aiSeat = State.ActiveNationId;
            var aiResult = _coordinator.RunTurn(State);
            State = aiResult.State;
            newsAdded += aiResult.Events.Count(e => e.IsNewsWorthy);
            var aiOrders = aiResult.Events.OfType<Ai.AiTurnDecided>().Sum(e => e.CommandsIssued);
            lines.Add(
                $"{NationDisplay(aiSeat)} takes its turn: "
                + $"{aiOrders} order{(aiOrders == 1 ? string.Empty : "s")} issued.");
            AppendWeatherLines(lines, aiResult.Events);
            guard++;
        }

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

        return lines;
    }

    private static void AppendWeatherLines(List<string> lines, IEnumerable<DomainEvent> events)
    {
        foreach (var weather in events.OfType<Economy.WeatherEventFired>())
        {
            lines.Add($"  Weather: {weather.EffectId} (week {weather.Week}).");
        }
    }

    private SeatControl ActiveControl() => State.NationById(State.ActiveNationId)!.Control;
}
