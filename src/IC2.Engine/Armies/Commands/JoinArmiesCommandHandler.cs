using IC2.Engine.Core;
using IC2.Engine.Economy;
using IC2.Engine.Model;

namespace IC2.Engine.Armies.Commands;

/// <summary><c>TUnitMap_JoinArmies</c>. See <see cref="JoinArmiesCommand"/>'s remarks.</summary>
[CommandHandler]
public sealed class JoinArmiesCommandHandler : ICommandHandler<JoinArmiesCommand>
{
    /// <inheritdoc/>
    public CommandOutcome Handle(JoinArmiesCommand command, CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        if (string.Equals(command.SurvivingArmyId, command.AbsorbedArmyId, StringComparison.Ordinal))
        {
            return CommandOutcome.Reject(JoinArmiesRejections.SameArmy, "An army cannot join itself.");
        }

        var state = context.State;
        var survivor = state.ArmyById(command.SurvivingArmyId);
        if (survivor is null)
        {
            return CommandOutcome.Reject(
                JoinArmiesRejections.UnknownArmy, $"'{command.SurvivingArmyId}' is not a known army.");
        }

        var absorbed = state.ArmyById(command.AbsorbedArmyId);
        if (absorbed is null)
        {
            return CommandOutcome.Reject(
                JoinArmiesRejections.UnknownArmy, $"'{command.AbsorbedArmyId}' is not a known army.");
        }

        if (!string.Equals(survivor.Nation, command.IssuingNationId, StringComparison.Ordinal)
            || !string.Equals(absorbed.Nation, command.IssuingNationId, StringComparison.Ordinal))
        {
            return CommandOutcome.Reject(
                JoinArmiesRejections.NotYourArmy, "Both armies must belong to the issuing nation.");
        }

        if (survivor.IsEmbarked || absorbed.IsEmbarked)
        {
            return CommandOutcome.Reject(
                JoinArmiesRejections.ArmyEmbarked, "An army on a fleet cannot be combined with another.");
        }

        if (survivor.X != absorbed.X || survivor.Y != absorbed.Y)
        {
            return CommandOutcome.Reject(
                JoinArmiesRejections.NotCoLocated, "Both armies must be on the same tile to join.");
        }

        var rules = context.Ruleset.ArmyManagement;
        var combinedUnits = survivor.Units.Count + absorbed.Units.Count;
        if (combinedUnits > rules.MaxUnitsPerArmy)
        {
            return CommandOutcome.Reject(
                JoinArmiesRejections.CombinedUnitsTooLarge,
                $"There are more than {rules.MaxUnitsPerArmy} units in these armies combined.");
        }

        var combinedTroops = survivor.TotalTroops + absorbed.TotalTroops;
        if (combinedTroops > rules.MaxTroopsPerArmy)
        {
            return CommandOutcome.Reject(
                JoinArmiesRejections.CombinedTroopsTooLarge,
                $"There are more than {rules.MaxTroopsPerArmy} troops in these armies combined.");
        }

        // The purse cap (see JoinArmiesCommand's remarks): pool both armies' money, then clamp to
        // EconomyRules.PurseCapPerUnit exactly as PurseAccounting.Credit does everywhere else a purse is
        // credited, sending anything the cap turns away to the issuing nation's own treasury so it is
        // moved, never destroyed.
        var pooledMoney = survivor.Money + absorbed.Money;
        var cappedMoney = PurseAccounting.Credit(survivor.Money, absorbed.Money, context.Ruleset);
        var excessToTreasury = pooledMoney - cappedMoney;

        var joined = survivor with
        {
            Units = ValueList.From(survivor.Units.Concat(absorbed.Units)),
            SupplyTons = survivor.SupplyTons + absorbed.SupplyTons,
            Money = cappedMoney,
            Moves = 0,
        };

        var updatedArmies = state.Armies
            .Where(a => !string.Equals(a.Id, absorbed.Id, StringComparison.Ordinal))
            .Select(a => string.Equals(a.Id, survivor.Id, StringComparison.Ordinal) ? joined : a);

        if (excessToTreasury == 0)
        {
            return CommandOutcome.Accept(state with { Armies = ValueList.From(updatedArmies) });
        }

        var updatedNation = context.IssuingNation with { Treasury = context.IssuingNation.Treasury + excessToTreasury };
        var updatedNations = state.Nations.Select(n =>
            string.Equals(n.Id, updatedNation.Id, StringComparison.Ordinal) ? updatedNation : n);

        return CommandOutcome.Accept(state with
        {
            Armies = ValueList.From(updatedArmies),
            Nations = ValueList.From(updatedNations),
        });
    }
}
