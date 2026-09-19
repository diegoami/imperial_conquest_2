using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Recruitment.Commands;

/// <summary>
/// Wires <see cref="StandingRecruitmentCost.InitialCost"/> behind the command seam and creates the
/// resulting <see cref="RecruitmentSlot"/> — <c>docs/task-catalogue.md</c> "T13 Recruitment and
/// mercenaries", plus T55's Done-when 6 and 7.
/// </summary>
/// <remarks>
/// <para>
/// <strong>T55 Done-when 6 — the table is 40 slots, and refusing at 40 is the original's own
/// refusal.</strong> <c>TArmyRecruits_RecruitUnit</c> takes the first slot whose troops are <c>0</c> and
/// refuses outright when slot 39 is occupied, with the literal <em>"You have reached your limit of 40
/// units."</em> <strong>[confirmed: decompiled-mobilization-and-mercenary-restock.md §1]</strong>. The
/// table is a compacted list, not a sparse array — <c>FUN_0044a610</c> deletes a slot by shifting every
/// later slot down and zeroing slot 39 — and <see cref="NationState.RecruitmentSlots"/> already models
/// exactly that by holding only occupied slots, so "slot 39 occupied" is "the list is already
/// <see cref="RecruitmentRules.MaxSlots"/> long", and a free-slot scan over a sparse table would be a
/// different rule. Nothing in this engine enforced that cap before, because T13 had no evidence of one.
/// </para>
/// <para>
/// <strong>T55 Done-when 7 — placing an order is what raises the mobilization rate.</strong> See
/// <see cref="MobilizationRate"/> for the formula and its provenance. The original also refuses a
/// further order once the rate reaches its cap (<em>"Your mobilisation rate is already 100%."</em>, and
/// the AI gates its own orders on <c>mobilized &lt; 100</c>); that refusal is <c>[open]</c> here — this
/// handler applies the confirmed <c>min(cap, …)</c> clamp the report gives and adds no rejection T55's
/// Done-when does not name.
/// </para>
/// <para>
/// Where the slot-table refusal sits: before the treasury is read, because the original refuses at the
/// point it looks for a free slot and never reaches the purchase. Which of the two the original reports
/// first when a nation is both broke and full is not recorded anywhere, and no test here asserts an
/// ordering between them.
/// </para>
/// </remarks>
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

        var nation = context.IssuingNation;
        if (nation.RecruitmentSlots.Count >= context.Ruleset.Recruitment.MaxSlots)
        {
            return CommandOutcome.Reject(
                RecruitStandingUnitRejections.RecruitmentTableFull,
                $"Nation '{nation.Id}' already has {context.Ruleset.Recruitment.MaxSlots} units in training.");
        }

        var cost = StandingRecruitmentCost.InitialCost(command.Troops, command.UnitTypeId, context.Ruleset);
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
            MobilizedPercent = MobilizationRate.AfterOrderPlaced(
                nation.MobilizedPercent, command.Troops, nation.Wealth, context.Ruleset.Recruitment),
        };

        context.Events.Publish(new RecruitmentOrdered(
            nation.Id, city.Id, command.UnitTypeId, command.Troops, cost));

        var updatedNations = state.Nations.Select(n =>
            string.Equals(n.Id, updatedNation.Id, StringComparison.Ordinal) ? updatedNation : n);

        return CommandOutcome.Accept(state with { Nations = ValueList.From(updatedNations) });
    }
}
