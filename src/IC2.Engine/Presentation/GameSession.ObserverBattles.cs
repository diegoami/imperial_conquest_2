using IC2.Engine.Battle;
using IC2.Engine.Battle.Commands;
using IC2.Engine.Battle.Tactical;
using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Presentation;

/// <summary>Immutable display evidence for an already concluded observer encounter; never saved.</summary>
public sealed record ObserverBattleReplay(string BattleId, GameState AtAttack, GameState After,
    TacticalBattleReport Report, ValueList<TacticalBattleFrame> Frames, ValueList<DomainEvent> Events);

public sealed partial class GameSession
{
    // Explicit session policy. Legacy headless/watch and normal human play stay Instant.
    public bool ObserverTacticalBattles { get; set; }
    public Func<string, string, bool>? CaptureObserverBattle { get; set; }
    /// <summary>A computation budget only: reaching it aborts the uncommitted seat, never decides a winner.</summary>
    public int ObserverAutomaticHalfRoundLimit { get; set; } = 1000;
    public ValueList<ObserverBattleReplay> LastObserverBattles { get; private set; } = ValueList<ObserverBattleReplay>.Empty;
    private readonly List<ObserverBattleReplay> _observerBattlesThisCall = new();

    private sealed class ObserverBattleDispatch(GameSession owner, ICommandDispatch inner) : ICommandDispatch
    {
        public CommandResult Dispatch(GameState state, ICommand command) => Dispatch(state, command, NullEventSink.Instance);
        public CommandResult Dispatch(GameState state, ICommand command, IEventSink events)
        {
            if (command is not AttackArmyCommand attack
                || state.ArmyById(attack.AttackerArmyId) is not { } attacker
                || state.ArmyById(attack.TargetArmyId) is not { } defender
                || state.NationById(attacker.Nation)?.Control != SeatControl.Ai
                || state.NationById(defender.Nation)?.Control != SeatControl.Ai
                || AttackLegality.Check(state, owner.Ruleset, attack) is not null
                || !TacticalBattleSession.CanBeFought(state, attacker.Id, defender.Id, owner.Ruleset))
                return inner.Dispatch(state, command, events);

            var identity = owner.NextTacticalBattleIdentity();
            var capture = false;
            try { capture = owner.CaptureObserverBattle?.Invoke(attacker.Nation, defender.Nation) == true; }
            catch { /* A failed view filter cannot change the simulation. */ }
            var battle = TacticalBattleSession.Open(state, attacker.Id, defender.Id, owner.Ruleset, out var opened,
                identity, captureTimeline: capture, automaticHalfRoundLimit: Math.Max(1, owner.ObserverAutomaticHalfRoundLimit));
            var done = battle.Conclude(opened, owner.Ruleset, world: owner.World);
            if (done.State is null || done.Report is null)
                throw new InvalidOperationException($"Observer battle could not conclude: {done.Rejection}");
            var evidence = ValueList.From(done.Events);
            foreach (var item in evidence) events.Publish(item);
            owner._observerBattlesThisCall.Add(new(identity, state, done.State, done.Report, battle.DrainTimeline(), evidence));
            owner._strategicTimeline.Battle(attack, state, done.State, completed: true, evidence, "observer:" + identity);
            // Conclude already advances the command seed once. No second dispatch,
            // human tactical gate, pending battle or replay of this AI seat is involved.
            return CommandResult.Accepted(done.State, command.Kind, evidence);
        }
    }
}
