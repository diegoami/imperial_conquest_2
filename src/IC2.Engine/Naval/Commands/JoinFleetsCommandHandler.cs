using IC2.Engine.Core;
using IC2.Engine.Economy;
using IC2.Engine.Model;

namespace IC2.Engine.Naval.Commands;

/// <summary><c>TUnitMap_JoinFleets</c>.</summary>
/// <remarks>
/// <strong>The pooled purse is capped (T50 Done-when 4, issue #165 item 3).</strong> <c>[derived]</c>: "T08
/// Economy, supply, and purses" Done-when 6 already establishes the cap's scope as confirmed fact — "the
/// purse cap of 1,000 is enforced on every path that credits a purse" — and this merge (T14) is exactly
/// such a path, so leaving it uncapped is the actual defect, not a free stylistic choice between two
/// otherwise-equal options. The decision made here is <em>enforce</em>, not <em>leave alone</em>, to match
/// that already-merged contract, the same way <see cref="Economy.TreasuryPurseTransfer"/> and
/// <see cref="Economy.AutomaticResupply"/> already do. Any excess over
/// <see cref="EconomyRules.PurseCapPerUnit"/> moves to the issuing nation's treasury — the same
/// "excess over the cap moves to the treasury" hygiene <see cref="Economy.AutomaticResupply"/> already
/// applies — so the join conserves money exactly rather than discarding it: two 900-talent purses still
/// sum to 1,800 total, now split 1,000 aboard the survivor and 800 credited to the treasury, instead of
/// letting the survivor alone hold all 1,800.
/// </remarks>
[CommandHandler]
public sealed class JoinFleetsCommandHandler : ICommandHandler<JoinFleetsCommand>
{
    /// <inheritdoc/>
    public CommandOutcome Handle(JoinFleetsCommand command, CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        if (string.Equals(command.SurvivingFleetId, command.AbsorbedFleetId, StringComparison.Ordinal))
        {
            return CommandOutcome.Reject(
                JoinFleetsRejections.SameFleet, "A fleet cannot join itself.");
        }

        var state = context.State;
        var survivor = state.FleetById(command.SurvivingFleetId);
        if (survivor is null)
        {
            return CommandOutcome.Reject(
                JoinFleetsRejections.UnknownFleet, $"'{command.SurvivingFleetId}' is not a known fleet.");
        }

        var absorbed = state.FleetById(command.AbsorbedFleetId);
        if (absorbed is null)
        {
            return CommandOutcome.Reject(
                JoinFleetsRejections.UnknownFleet, $"'{command.AbsorbedFleetId}' is not a known fleet.");
        }

        if (!string.Equals(survivor.Nation, command.IssuingNationId, StringComparison.Ordinal)
            || !string.Equals(absorbed.Nation, command.IssuingNationId, StringComparison.Ordinal))
        {
            return CommandOutcome.Reject(
                JoinFleetsRejections.NotYourFleet, "Both fleets must belong to the issuing nation.");
        }

        if (survivor.IsUnderConstruction || absorbed.IsUnderConstruction)
        {
            return CommandOutcome.Reject(
                JoinFleetsRejections.UnderConstruction, "Neither fleet may still be under construction.");
        }

        if (survivor.X != absorbed.X || survivor.Y != absorbed.Y)
        {
            return CommandOutcome.Reject(
                JoinFleetsRejections.NotCoLocated, "Both fleets must be on the same tile to join.");
        }

        if (survivor.IsCarryingArmy || absorbed.IsCarryingArmy)
        {
            return CommandOutcome.Reject(
                JoinFleetsRejections.CarryingArmy, "Neither fleet may carry an army to join.");
        }

        var rules = context.Ruleset.Naval;
        var combinedShips = survivor.Ships + absorbed.Ships;
        if (combinedShips > rules.JoinMaxShips)
        {
            return CommandOutcome.Reject(
                JoinFleetsRejections.CombinedShipsTooLarge,
                $"There are more than {rules.JoinMaxShips} ships in these fleets combined.");
        }

        // The purse cap (see this type's remarks): pool both fleets' money, then clamp to
        // EconomyRules.PurseCapPerUnit exactly as PurseAccounting.Credit does everywhere else a purse is
        // credited, sending anything the cap turns away to the issuing nation's own treasury so it is
        // moved, never destroyed.
        var pooledMoney = survivor.Money + absorbed.Money;
        var cappedMoney = PurseAccounting.Credit(survivor.Money, absorbed.Money, context.Ruleset);
        var excessToTreasury = pooledMoney - cappedMoney;

        var joined = survivor with
        {
            Ships = combinedShips,
            SupplyTons = survivor.SupplyTons + absorbed.SupplyTons,
            Money = cappedMoney,
            Moves = 0,
        };

        var updatedFleets = state.Fleets
            .Where(f => !string.Equals(f.Id, absorbed.Id, StringComparison.Ordinal))
            .Select(f => string.Equals(f.Id, survivor.Id, StringComparison.Ordinal) ? joined : f);

        if (excessToTreasury == 0)
        {
            return CommandOutcome.Accept(state with { Fleets = ValueList.From(updatedFleets) });
        }

        var updatedNation = context.IssuingNation with { Treasury = context.IssuingNation.Treasury + excessToTreasury };
        var updatedNations = state.Nations.Select(n =>
            string.Equals(n.Id, updatedNation.Id, StringComparison.Ordinal) ? updatedNation : n);

        return CommandOutcome.Accept(state with
        {
            Fleets = ValueList.From(updatedFleets),
            Nations = ValueList.From(updatedNations),
        });
    }
}
