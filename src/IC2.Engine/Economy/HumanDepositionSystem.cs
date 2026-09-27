using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Economy;

/// <summary>
/// The human half of leader-falls deposition — <c>docs/task-catalogue.md</c> "T39 Quarterly upkeep: who
/// pays, mercenary desertion, and deposition for debt", Done-when 6's last clause: "a human nation in debt
/// at the start of its turn is deposed and its seat passes to the AI"; widened by T87 (<c>#389</c>,
/// bug #380) to the full four-way test <c>FUN_00452034</c> actually runs, not debt alone.
/// </summary>
/// <remarks>
/// <para>
/// <strong>[confirmed: decompiled-elimination-cleanup.md §3 / upkeep-payment-and-desertion.md]</strong>:
/// <c>FUN_00452034</c> runs at the start of every human nation's turn, not only at a quarter boundary, and
/// calls the same <c>FUN_0044c8f0</c> deposition <see cref="AiDepositionHandler"/> calls, but on a wider
/// OR: 250 BC, 334 cities (owning every one on the map), unity below 400, or debt — see
/// <see cref="Deposition.ShouldFallAtHumanTurnStart"/> for the exact four-way test and its citations.
/// Deterministic throughout, no random draw. Registered into <see cref="TurnPhase.SeatStart"/>
/// (<c>docs/task-catalogue.md</c>'s own phase for "the active seat's turn begins"), which runs once per
/// seat's turn -- exactly "the start of its turn", <em>as the pipeline models it</em>.
/// </para>
/// <para>
/// <strong>Bug #380: this alone is not "the start of its turn" for the CLI.</strong>
/// <see cref="Presentation.GameSession"/> only ever calls <see cref="TurnCoordinator.RunTurn"/> for a
/// human seat once, exactly when that seat's own <c>end</c> is submitted -- so this system's own
/// <see cref="TurnPhase.SeatStart"/> execution, reached through that same call, actually lands at the
/// <em>end</em> of the seat's turn, after every order it gave, not before any of them. This class is
/// still correct and still exercised directly through <see cref="TurnCoordinator.RunTurn"/> by its own
/// tests (<c>HumanDepositionSystemTests</c>) -- the pipeline's "start of turn" is a true predicate no
/// matter when it is asked -- but the CLI's own fix for #380 could not live here: it needed to ask the
/// same question <em>before</em> presenting a prompt, which needs no <see cref="SystemContext"/> and
/// so cannot go through this class's own <see cref="Execute"/>.
/// <see cref="Presentation.GameSession"/> (Owned by T87 for exactly this) calls
/// <see cref="Deposition.ShouldFallAtHumanTurnStart"/>/<see cref="Deposition.ApplyEffects"/>/
/// <see cref="Deposition.ResetRelations"/> directly, the same three calls this method makes, at the one
/// point in its own rotation loop where a seat becomes active and has not yet been given any order — see
/// <c>GameSession.DepositeActiveHumanSeatIfItShouldFallAtTurnStart</c>. The two call sites necessarily
/// duplicate this same short sequence (there is no shared surface between "a system driven by
/// <see cref="SystemContext"/>" and "a plain call before the pipeline runs at all") rather than one
/// calling the other.
/// </para>
/// <para>
/// <strong>What is code-only, per this task's own hazard note.</strong> The effects
/// (<see cref="Deposition.ApplyEffects"/>, <see cref="Deposition.ResetRelations"/>) and the seat handoff
/// itself (<c>FUN_00449078</c>, "turns the nation AI" -- here, flipping
/// <see cref="Model.NationState.Control"/> to <see cref="Model.SeatControl.Ai"/>) are all
/// <c>[derived]</c>: Rome's deepest sampled debt, −904, never approached its own −5,856 line, so no
/// human deposition was ever observed to confirm or refute against. <see cref="Model.NationState.LeaderName"/>
/// is left unchanged, per <c>docs/task-catalogue.md</c> Done-when 7 (open data gap, re-checked and still
/// open — see <see cref="Deposition"/>'s own remarks) -- the rename is not invented here. No event is
/// published on this path: the original's human equivalent opens a game-over dialog, which is not
/// news-log content — <see cref="Presentation.GameSession"/> is where that dialog's text now lives (T87),
/// not an event this system would publish.
/// </para>
/// <para>
/// This runs for every seat's <see cref="TurnPhase.SeatStart"/>, human or AI, and is a no-op unless the
/// <em>active</em> seat is both human-controlled and matches the four-way test -- the AI's own check is
/// <see cref="AiDepositionHandler"/>'s, at the quarter boundary, debt-only, not here.
/// </para>
/// </remarks>
[GameSystem(TurnPhase.SeatStart, "economy.human-deposition")]
public sealed class HumanDepositionSystem : IGameSystem
{
    /// <inheritdoc/>
    public GameState Execute(SystemContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var ruleset = context.Ruleset;
        var nation = context.ActiveNation;

        if (nation is null
            || nation.Control != SeatControl.Human
            || !Deposition.ShouldFallAtHumanTurnStart(nation, context.State, ruleset))
        {
            return context.State;
        }

        var deposed = Deposition.ApplyEffects(nation, ruleset) with { Control = SeatControl.Ai };
        var relations = Deposition.ResetRelations(context.State.Relations, nation.Id, ruleset);

        var updatedNations = context.State.Nations.Select(n =>
            string.Equals(n.Id, nation.Id, StringComparison.Ordinal) ? deposed : n);

        return context.State with { Nations = ValueList.From(updatedNations), Relations = relations };
    }
}
