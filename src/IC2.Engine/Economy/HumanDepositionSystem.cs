using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Economy;

/// <summary>
/// The human half of debt deposition — <c>docs/task-catalogue.md</c> "T39 Quarterly upkeep: who pays,
/// mercenary desertion, and deposition for debt", Done-when 6's last clause: "a human nation in debt at
/// the start of its turn is deposed and its seat passes to the AI, with <c>_provenance</c>
/// <c>[derived]</c> (code only)."
/// </summary>
/// <remarks>
/// <para>
/// <strong>[confirmed: upkeep-payment-and-desertion.md]</strong>: <c>FUN_00452034</c> runs at the start
/// of every human nation's turn, not only at a quarter boundary, and calls the same
/// <c>FUN_0044c8f0</c> deposition <see cref="AiDepositionHandler"/> calls when a nation is in debt —
/// deterministically, with no random draw. Registered into <see cref="TurnPhase.SeatStart"/>
/// (<c>docs/task-catalogue.md</c>'s own phase for "the active seat's turn begins"), which runs once per
/// seat's turn -- exactly "the start of its turn".
/// </para>
/// <para>
/// <strong>What is code-only, per this task's own hazard note.</strong> The effects
/// (<see cref="Deposition.ApplyEffects"/>, <see cref="Deposition.ResetRelations"/>) and the seat handoff
/// itself (<c>FUN_00449078</c>, "turns the nation AI" -- here, flipping
/// <see cref="Model.NationState.Control"/> to <see cref="Model.SeatControl.Ai"/>) are all
/// <c>[derived]</c>: Rome's deepest sampled debt, −904, never approached its own −5,856 line, so no
/// human deposition was ever observed to confirm or refute against. <see cref="Model.NationState.LeaderName"/>
/// is left unchanged, per <c>docs/task-catalogue.md</c> Done-when 7 -- the rename is a known-open data
/// gap, not invented here. No event is published on this path either: the original's human equivalent
/// opens a game-over dialog ("Your army have deposed you because they have not been paid."), which is
/// not news-log content, and no report gives this task grounds to invent a UI-facing message shape for
/// it.
/// </para>
/// <para>
/// This runs for every seat's <see cref="TurnPhase.SeatStart"/>, human or AI, and is a no-op unless the
/// <em>active</em> seat is both human-controlled and in debt -- the AI's own check is
/// <see cref="AiDepositionHandler"/>'s, at the quarter boundary, not here.
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

        if (nation is null || nation.Control != SeatControl.Human || !Deposition.InDebt(nation, ruleset))
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
