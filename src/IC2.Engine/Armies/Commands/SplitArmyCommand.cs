using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Armies.Commands;

/// <summary>
/// Splits <paramref name="UnitIndicesToNewArmy"/> off an existing army into a new one —
/// <c>docs/task-catalogue.md</c> "T15 Army and unit management", Done-when 2: needs at least
/// <see cref="Model.ArmyManagementRules.SplitMinUnits"/> (2) units in the parent army before splitting,
/// enforces the 198-army cap, and gives the new army morale 59 and — <c>seatAsymmetry</c>-gated
/// (<c>design-audit.md</c> Q6) — 0 moves for a human seat / 1 move for an AI seat under
/// <c>classical-faithful</c>, the same starting moves for every seat under <c>improved</c>. Troops and
/// units conserve exactly across the two resulting armies.
/// </summary>
/// <remarks>
/// <para>
/// <c>TUnitMap_SplitArmy</c> (<c>0x0044755C</c>) → <c>FUN_00449F08</c>
/// <strong>[confirmed: decompiled-unit-map-orders-and-record-fields.md]</strong>: "Needs ≥ 2 units...
/// cap 198 armies total (&lt; 0xC6), moves 0 for a human nation and 1 for an AI one, supplies/money 0,
/// morale 59." Troop/unit conservation and morale 59 are cross-checked against a real observed split —
/// 75,536 troops / 16 units → 37,081 + 38,455 and 9 + 7 — in
/// <c>pending-offer-block-army-split-and-naupactus.md</c>.
/// </para>
/// <para>
/// <strong>Money and supplies are a requested allocation, not the constant <c>FUN_00449F08</c> writes.</strong>
/// <strong>[confirmed: pending-offer-block-army-split-and-naupactus.md]</strong> — those <c>0</c>s are
/// what the original's <c>TSplitArmyUnit</c> dialog <em>opens</em> with, a two-pane transfer screen with
/// money and supply spinners the player can move before committing; a real observed split moved 256
/// talents to 156 (parent) / 100 (new army). <see cref="MoneyToNewArmy"/> and
/// <see cref="SupplyTonsToNewArmy"/> model exactly that: they default to 0 (the dialog's opening state),
/// are validated against what the parent actually holds, and are subtracted from the parent and credited
/// to the new army exactly — the two totals conserve, they are never a constant.
/// </para>
/// <para>
/// <strong>Embarked armies are refused (not evidenced either way).</strong> No report states what
/// happens when a <c>TUnitMap_SplitArmy</c> target is aboard a fleet, and a fleet's
/// <see cref="Model.FleetState.CarriedArmyId"/> can name only one army — splitting one in two while
/// embarked would leave the new army with no map cell and no carrying fleet, an unreachable, dangling
/// entity of exactly the shape <c>docs/build-process.md</c> §4.2 gate 5's delete-then-dangle sweep looks
/// for. Refused defensively, the same direction <c>JoinArmiesCommand</c> and <c>EmbarkArmyCommand</c>
/// already take for an embarked army, <c>[designed, no confirmed evidence either way]</c>.
/// </para>
/// <para>
/// <strong>The <c>improved</c> starting-moves value.</strong> <c>docs/game-design.md</c>'s
/// <c>seatAsymmetry</c> row states only that <c>improved</c> "applies the same rule to every seat",
/// without naming which of the two confirmed values. This task picks
/// <see cref="Model.ArmyManagementRules.NewArmyMovesAiSeat"/> (1) for every seat — the same direction
/// <c>MovementAbortRule</c> (T09) took for its own <c>seatAsymmetry</c> case, generalising the value that
/// was previously seat-specific rather than picking a side arbitrarily. Unlike <c>EmbarkArmyCommand</c>'s
/// opposite call (T14, which generalises toward refusal because the AI-only behaviour there destroys
/// troops), giving every seat 1 move instead of 0 is strictly neutral-to-beneficial — nothing is lost —
/// so there is no reason to prefer the harsher value. <c>[designed, decided independently per
/// docs/design-audit.md</c> Q6's own "decided per-task, on its own merits" framing.
/// </para>
/// </remarks>
/// <param name="ArmyId">The army being split. The rest of its units stay here.</param>
/// <param name="NewArmyId">The new army's id.</param>
/// <param name="UnitIndicesToNewArmy">
/// Indices into <see cref="Model.ArmyState.Units"/> (as it stands before the split) naming the units that
/// move to the new army. Must name between 1 and <c>Units.Count − 1</c> distinct, in-range indices, so
/// both the parent and the new army end up with at least one unit.
/// </param>
/// <param name="MoneyToNewArmy">Talents moved from the parent's purse to the new army's. Defaults to 0.</param>
/// <param name="SupplyTonsToNewArmy">Supply tons moved from the parent's stock to the new army's. Defaults to 0.</param>
public sealed record SplitArmyCommand(
    string IssuingNationId,
    string ArmyId,
    string NewArmyId,
    ValueList<int> UnitIndicesToNewArmy,
    int MoneyToNewArmy = 0,
    int SupplyTonsToNewArmy = 0) : ICommand
{
    /// <inheritdoc/>
    public string Kind => "armies.split-army";
}

/// <summary>Rejection codes this command's handler declares, in its own directory — see <see cref="RejectionCode"/>.</summary>
public static class SplitArmyRejections
{
    /// <summary>The command names an army id that does not exist.</summary>
    public static readonly RejectionCode UnknownArmy = new("armies.unknown-army");

    /// <summary>The named army belongs to a nation other than the one issuing the command.</summary>
    public static readonly RejectionCode NotYourArmy = new("armies.not-your-army");

    /// <summary>The army is aboard a fleet — see <see cref="SplitArmyCommand"/>'s remarks.</summary>
    public static readonly RejectionCode ArmyEmbarked = new("armies.army-embarked");

    /// <summary>The army has fewer than <see cref="Model.ArmyManagementRules.SplitMinUnits"/> units.</summary>
    public static readonly RejectionCode TooFewUnitsToSplit = new("armies.too-few-units-to-split");

    /// <summary>
    /// <see cref="SplitArmyCommand.UnitIndicesToNewArmy"/> is empty, has a duplicate or out-of-range
    /// index, or would leave the parent army with no units.
    /// </summary>
    public static readonly RejectionCode InvalidUnitSelection = new("armies.invalid-unit-selection");

    /// <summary>The requested new army id already names an existing army.</summary>
    public static readonly RejectionCode DuplicateArmyId = new("armies.duplicate-army-id");

    /// <summary>The nation already has <see cref="Model.ArmyManagementRules.MaxArmies"/> (198) armies.</summary>
    public static readonly RejectionCode TooManyArmies = new("armies.too-many-armies");

    /// <summary><see cref="SplitArmyCommand.MoneyToNewArmy"/> is negative or more than the parent holds.</summary>
    public static readonly RejectionCode InvalidMoneyAllocation = new("armies.invalid-money-allocation");

    /// <summary><see cref="SplitArmyCommand.SupplyTonsToNewArmy"/> is negative or more than the parent holds.</summary>
    public static readonly RejectionCode InvalidSupplyAllocation = new("armies.invalid-supply-allocation");
}
