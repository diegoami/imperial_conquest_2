using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Economy.Commands;

/// <summary>
/// Writes <see cref="SetTaxRateCommand.TaxRatePercent"/> onto the issuing nation's
/// <see cref="Model.NationState.TaxRatePercent"/> — <c>docs/tasks/T103.md</c> DoD 1 and 3.
/// </summary>
/// <remarks>
/// The dispatcher has already resolved the issuing nation and checked it is the active seat
/// (<see cref="CommandDispatcher"/>), so the only rule this handler adds is the inclusive range from the
/// ruleset (<see cref="Model.EconomyRules.TaxRateMinPercent"/> through
/// <see cref="Model.EconomyRules.TaxRateMaxPercent"/>). A rejection returns before any state is built, so
/// the state is untouched — <see cref="CommandOutcome"/> structurally cannot hand back a rejection and a
/// modified state at once.
/// </remarks>
[CommandHandler]
public sealed class SetTaxRateCommandHandler : ICommandHandler<SetTaxRateCommand>
{
    /// <inheritdoc/>
    public CommandOutcome Handle(SetTaxRateCommand command, CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        var economy = context.Ruleset.Economy;
        if (command.TaxRatePercent < economy.TaxRateMinPercent
            || command.TaxRatePercent > economy.TaxRateMaxPercent)
        {
            return CommandOutcome.Reject(
                SetTaxRateRejections.OutOfRange,
                $"Tax rate must be between {economy.TaxRateMinPercent} and {economy.TaxRateMaxPercent} "
                + $"whole percent inclusive, not {command.TaxRatePercent}.");
        }

        var state = context.State;
        var updatedNations = state.Nations.Select(nation =>
            string.Equals(nation.Id, command.IssuingNationId, StringComparison.Ordinal)
                ? nation with { TaxRatePercent = command.TaxRatePercent }
                : nation);

        return CommandOutcome.Accept(state with { Nations = ValueList.From(updatedNations) });
    }
}
