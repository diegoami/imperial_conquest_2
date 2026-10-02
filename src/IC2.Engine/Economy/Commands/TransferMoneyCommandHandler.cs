using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Naval;

namespace IC2.Engine.Economy.Commands;

/// <summary>
/// Wires <see cref="TreasuryPurseTransfer"/> behind the command seam — <c>docs/tasks/T105.md</c> "A command
/// moves money between the treasury and a purse". The two sides are the national treasury (or, with a
/// <c>via</c> fleet, that fleet's own purse) and the named unit's purse.
/// </summary>
/// <remarks>
/// <para>
/// Every rejection here is a command-layer concern — is the named id an army or fleet, is it yours, is the
/// <c>via</c> fleet known, yours, distinct and co-located, is the request well-formed. The money rule
/// itself — the per-move 1,000 ceiling, the source-balance clamp and the receiving purse's cap — stays
/// entirely inside <see cref="TreasuryPurseTransfer"/>, reused for the fleet side rather than copied.
/// </para>
/// <para>
/// <strong>The per-move ceiling is <see cref="EconomyRules.PurseCapPerUnit"/>.</strong> The report's one
/// sentence ("capped at 1,000 money per army or fleet",
/// <c>docs/investigations/original-ui-command-audit.md</c> §1.6) is the source of both the purse cap and
/// this command's per-move ceiling (T105's first Hazard, which keeps both), and the ruleset holds that
/// 1,000 once. This handler therefore compares against that field, never a second literal.
/// </para>
/// <para>
/// <strong>Clamping, not rejection, once inside the ceiling.</strong> A move that asks for more than the
/// source holds, or that would push the receiving purse past its cap, is accepted at the amount actually
/// applied — the same shape T38 gave every other purse credit (see <see cref="PurseAccounting.Credit"/>).
/// </para>
/// </remarks>
[CommandHandler]
public sealed class TransferMoneyCommandHandler : ICommandHandler<TransferMoneyCommand>
{
    /// <inheritdoc/>
    public CommandOutcome Handle(TransferMoneyCommand command, CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        var state = context.State;
        var army = state.ArmyById(command.UnitId);
        var fleet = state.FleetById(command.UnitId);
        if (army is null && fleet is null)
        {
            return CommandOutcome.Reject(
                TransferMoneyRejections.UnknownUnit, $"'{command.UnitId}' is not a known army or fleet.");
        }

        var unitNation = army?.Nation ?? fleet!.Nation;
        if (!string.Equals(unitNation, command.IssuingNationId, StringComparison.Ordinal))
        {
            return CommandOutcome.Reject(
                TransferMoneyRejections.NotYourUnit,
                $"Unit '{command.UnitId}' belongs to '{unitNation}', not '{command.IssuingNationId}'.");
        }

        if (command.Amount == 0)
        {
            return CommandOutcome.Reject(
                TransferMoneyRejections.InvalidAmount, "Must move a non-zero amount of talents.");
        }

        // The report's "capped at 1,000 money per army or fleet" sentence is both the purse cap
        // (PurseCapPerUnit) and this move's ceiling; T105's first Hazard keeps both. Read once, here.
        var moveLimit = context.Ruleset.Economy.PurseCapPerUnit;
        if (command.Amount > moveLimit || command.Amount < -moveLimit)
        {
            return CommandOutcome.Reject(
                TransferMoneyRejections.AmountExceedsMoveLimit,
                $"One move is at most {moveLimit} talents, not {command.Amount}.");
        }

        return command.ViaFleetId is null
            ? TransferWithTreasury(command, context, state, army, fleet)
            : TransferWithViaFleet(command, context, state, army, fleet);
    }

    /// <summary>No <c>via</c>: the other side is the issuing nation's own treasury.</summary>
    private static CommandOutcome TransferWithTreasury(
        TransferMoneyCommand command, CommandContext context, GameState state, ArmyState? army, FleetState? fleet)
    {
        if (army is not null)
        {
            var result = TreasuryPurseTransfer.TransferWithArmy(
                context.IssuingNation, army, command.Amount, context.Ruleset);

            var armies = state.Armies.Select(a =>
                string.Equals(a.Id, army.Id, StringComparison.Ordinal) ? result.Army : a);
            var nations = state.Nations.Select(n =>
                string.Equals(n.Id, result.Nation.Id, StringComparison.Ordinal) ? result.Nation : n);

            return CommandOutcome.Accept(state with
            {
                Armies = ValueList.From(armies),
                Nations = ValueList.From(nations),
            });
        }

        var fleetResult = TreasuryPurseTransfer.TransferWithFleet(
            context.IssuingNation, fleet!, command.Amount, context.Ruleset);

        var fleets = state.Fleets.Select(f =>
            string.Equals(f.Id, fleet!.Id, StringComparison.Ordinal) ? fleetResult.Fleet : f);
        var updatedNations = state.Nations.Select(n =>
            string.Equals(n.Id, fleetResult.Nation.Id, StringComparison.Ordinal) ? fleetResult.Nation : n);

        return CommandOutcome.Accept(state with
        {
            Fleets = ValueList.From(fleets),
            Nations = ValueList.From(updatedNations),
        });
    }

    /// <summary>
    /// With <c>via</c>: the other side is that fleet's own purse, so both accounts being moved between are
    /// purses — the dialog's "treasury, or a co-located fleet" second source.
    /// </summary>
    private static CommandOutcome TransferWithViaFleet(
        TransferMoneyCommand command, CommandContext context, GameState state, ArmyState? army, FleetState? fleet)
    {
        var viaFleetId = command.ViaFleetId!;
        var viaFleet = state.FleetById(viaFleetId);
        if (viaFleet is null)
        {
            return CommandOutcome.Reject(
                TransferMoneyRejections.UnknownViaFleet, $"'{viaFleetId}' is not a known fleet.");
        }

        if (!string.Equals(viaFleet.Nation, command.IssuingNationId, StringComparison.Ordinal))
        {
            return CommandOutcome.Reject(
                TransferMoneyRejections.ViaFleetNotYours,
                $"Fleet '{viaFleet.Id}' belongs to '{viaFleet.Nation}', not '{command.IssuingNationId}'.");
        }

        if (fleet is not null && string.Equals(fleet.Id, viaFleet.Id, StringComparison.Ordinal))
        {
            return CommandOutcome.Reject(
                TransferMoneyRejections.ViaFleetIsTheUnit, "The via fleet cannot be the named unit itself.");
        }

        // The fleet carrying the named army always counts: an embarked army's X/Y follow its carrier in
        // this engine (EmbarkArmyCommandHandler, MoveFleetCommandHandler), so this branch is belt-and-
        // braces against a hand-built state; every other candidate is the one-tile provider radius.
        var carriedByVia = army is not null
            && string.Equals(army.AboardFleetId, viaFleet.Id, StringComparison.Ordinal);
        if (!carriedByVia)
        {
            var unitX = army?.X ?? fleet!.X;
            var unitY = army?.Y ?? fleet!.Y;
            var distance = LandingTile.ChebyshevDistance(
                new GridPoint(unitX, unitY), new GridPoint(viaFleet.X, viaFleet.Y));
            if (distance > context.Ruleset.Economy.CommandAdjacencyRadiusTiles)
            {
                return CommandOutcome.Reject(
                    TransferMoneyRejections.ViaFleetNotWithinRange,
                    $"Fleet '{viaFleet.Id}' is not within the provider radius of unit '{command.UnitId}'.");
            }
        }

        var unitMoney = army?.Money ?? fleet!.Money;

        // Positive moves the via fleet's purse into the named unit's; negative moves it back. Both are
        // purses, so the shared rule clamps whichever funds and caps whichever receives; on a negative
        // move it swaps the roles internally, so the receiving via purse is the one capped (B1).
        var (updatedViaMoney, updatedUnitMoney, _) =
            TreasuryPurseTransfer.TransferBetweenPurses(viaFleet.Money, unitMoney, command.Amount, context.Ruleset);

        var updatedVia = viaFleet with { Money = updatedViaMoney };

        if (army is not null)
        {
            var updatedArmy = army with { Money = updatedUnitMoney };
            var armies = state.Armies.Select(a =>
                string.Equals(a.Id, army.Id, StringComparison.Ordinal) ? updatedArmy : a);
            var fleets = state.Fleets.Select(f =>
                string.Equals(f.Id, viaFleet.Id, StringComparison.Ordinal) ? updatedVia : f);

            return CommandOutcome.Accept(state with
            {
                Armies = ValueList.From(armies),
                Fleets = ValueList.From(fleets),
            });
        }

        var updatedFleet = fleet! with { Money = updatedUnitMoney };
        var updatedFleets = state.Fleets.Select(f =>
            string.Equals(f.Id, fleet!.Id, StringComparison.Ordinal) ? updatedFleet :
            string.Equals(f.Id, viaFleet.Id, StringComparison.Ordinal) ? updatedVia : f);

        return CommandOutcome.Accept(state with { Fleets = ValueList.From(updatedFleets) });
    }
}
