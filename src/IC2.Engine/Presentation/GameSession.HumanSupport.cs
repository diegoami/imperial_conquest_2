using IC2.Engine.Core;
using IC2.Engine.Economy;
using IC2.Engine.Model;
using IC2.Engine.News;
using IC2.Engine.Movement.Commands;
using IC2.Engine.Naval.Commands;

namespace IC2.Engine.Presentation;

public sealed partial class GameSession
{
    private string? _lastHumanSupportNation;
    private int _lastHumanSupportTurn = -1;

    /// <summary>Opt-in human support, supplied by the app rather than saved campaign data.</summary>
    public HumanSupportOptions? HumanSupport { get; private set; }

    private HumanSupportOptions? EffectiveArmySupport(string id) =>
        RetreatSupplyPlanFor(id, isFleet: false)?.NationId == State.ActiveNationId
            ? (HumanSupport ?? new()) with { SupplyArmies = true, FundArmies = true }
            : HumanSupport;

    private HumanSupportOptions? ResolveHumanSupportForCommand(ICommand command)
    {
        if (command is MoveArmyCommand army) return EffectiveArmySupport(army.ArmyId);
        if (command is not MoveFleetCommand fleet) return HumanSupport;
        var options = HumanSupport;
        if (RetreatSupplyPlanFor(fleet.FleetId, isFleet: true)?.NationId == State.ActiveNationId)
            options = (options ?? new()) with { SupplyFleets = true, FundFleets = true };
        if (State.FleetById(fleet.FleetId)?.CarriedArmyId is { } cargo
            && RetreatSupplyPlanFor(cargo, isFleet: false)?.NationId == State.ActiveNationId)
            options = (options ?? new()) with { SupplyArmies = true, FundArmies = true };
        return options;
    }

    /// <summary>
    /// Apply a changed runtime policy to the current strategic human view. Identical
    /// policies (including visibility/font-only settings commits) are inert.
    /// </summary>
    public HumanSupportResult ConfigureHumanSupport(HumanSupportOptions? options)
    {
        options = options?.Enabled == true ? options : null;
        if (Equals(HumanSupport, options)) return EmptyHumanSupport();
        HumanSupport = options;
        _dispatcher.HumanSupport = options;
        ResetHumanSupportTurn();
        var result = ApplyHumanSupportAtTurnStart();
        if (result.Events.Count > 0) EventsPublished?.Invoke(result.Events);
        return result;
    }

    private bool HumanSupportAppliedThisTurn => HumanSupport is not null
        && _lastHumanSupportNation == State.ActiveNationId
        && _lastHumanSupportTurn == State.Calendar.TurnIndex;

    private void ResetHumanSupportTurn()
    {
        _lastHumanSupportNation = null;
        _lastHumanSupportTurn = -1;
    }

    private HumanSupportResult EmptyHumanSupport() => new(State,
        ValueList<DomainEvent>.Empty, ValueList<HumanSupportFailure>.Empty);

    private HumanSupportResult ApplyHumanSupportAtTurnStart(List<string>? lines = null)
    {
        if (HumanSupport is null || HumanSupportAppliedThisTurn || _gameOver || _battle is not null
            || State.NationById(State.ActiveNationId) is not { Control: SeatControl.Human, Eliminated: false })
            return EmptyHumanSupport();
        var result = HumanAutoSupport.ForNation(State, Ruleset, World, HumanSupport,
            SplitMix64Rng.ForStream(State.RandomSeed, "human.turn-support"));
        State = NewsLogWriter.Append(result.State, result.Events, Ruleset.NewsLog);
        _lastHumanSupportNation = State.ActiveNationId;
        _lastHumanSupportTurn = State.Calendar.TurnIndex;
        if (lines is not null)
        {
            AccumulateEvents(result.Events);
            lines.AddRange(result.Failures.Select(f => HumanSupportRefusalLine(f.CommandKind, f.Rejection.Code, f.Rejection.Message)));
        }
        return result with { State = State };
    }

    private static string HumanSupportRefusalLine(string kind, RejectionCode code, string message) =>
        $"{kind} rejected ({code}): {message}";

    private static IEnumerable<string> HumanSupportRefusalLines(IEnumerable<DomainEvent> events) =>
        events.OfType<HumanSupportRefused>().Select(f => HumanSupportRefusalLine(f.CommandKind, f.Code, f.Message));
}
