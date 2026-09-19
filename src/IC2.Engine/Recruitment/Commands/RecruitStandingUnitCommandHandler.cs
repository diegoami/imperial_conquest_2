using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Recruitment.Commands;

/// <summary>
/// Wires <see cref="StandingRecruitmentCost.InitialCost"/> behind the command seam and creates the
/// resulting <see cref="RecruitmentSlot"/> — <c>docs/task-catalogue.md</c> "T13 Recruitment and
/// mercenaries".
/// </summary>
[CommandHandler]
public sealed class RecruitStandingUnitCommandHandler : ICommandHandler<RecruitStandingUnitCommand>
{
    /// <inheritdoc/>
    public CommandOutcome Handle(RecruitStandingUnitCommand command, CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        var state = context.State;
        var city = state.CityById(command.CityId);
        if (city is null)
        {
            return CommandOutcome.Reject(
                RecruitStandingUnitRejections.UnknownCity, $"'{command.CityId}' is not a known city.");
        }

        if (!string.Equals(city.Owner, command.IssuingNationId, StringComparison.Ordinal))
        {
            return CommandOutcome.Reject(
                RecruitStandingUnitRejections.NotYourCity,
                $"City '{city.Id}' belongs to '{city.Owner}', not '{command.IssuingNationId}'.");
        }

        var type = context.Ruleset.UnitTypeById(command.UnitTypeId);
        if (type is null)
        {
            return CommandOutcome.Reject(
                RecruitStandingUnitRejections.UnknownUnitType,
                $"'{command.UnitTypeId}' is not a unit type in ruleset '{context.Ruleset.Id}'.");
        }

        if (command.Troops <= 0)
        {
            return CommandOutcome.Reject(
                RecruitStandingUnitRejections.InvalidTroops, "Must recruit a positive number of troops.");
        }

        var cost = StandingRecruitmentCost.InitialCost(command.Troops, command.UnitTypeId, context.Ruleset);
        var nation = context.IssuingNation;
        if (nation.Treasury < cost)
        {
            return CommandOutcome.Reject(
                RecruitStandingUnitRejections.InsufficientTreasury,
                $"Nation '{nation.Id}' has {nation.Treasury} talents, which is less than the order's cost of {cost}.");
        }

        var slot = new RecruitmentSlot(
            TargetCityId: city.Id,
            UnitTypeId: command.UnitTypeId,
            Troops: command.Troops,
            StateCode: 0);

        var updatedNation = nation with
        {
            Treasury = nation.Treasury - cost,
            RecruitmentSlots = ValueList.From(nation.RecruitmentSlots.Append(slot)),
        };

        context.Events.Publish(new RecruitmentOrdered(
            nation.Id, city.Id, command.UnitTypeId, command.Troops, cost));

        var updatedNations = state.Nations.Select(n =>
            string.Equals(n.Id, updatedNation.Id, StringComparison.Ordinal) ? updatedNation : n);

        return CommandOutcome.Accept(state with { Nations = ValueList.From(updatedNations) });
    }
}
