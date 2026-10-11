using IC2.Engine.Core;
using IC2.Engine.Economy;
using IC2.Engine.Model;
using IC2.Engine.Movement;

namespace IC2.Engine.Naval.Commands;

/// <summary>
/// <c>FUN_00450b30</c>'s merge step. See <see cref="AiAbsorbFleetCommand"/>.
/// </summary>
/// <remarks>
/// <strong>Gates [confirmed: decompile, F2/F3].</strong> Survivor: ships strictly below
/// <see cref="AiWeightsRules.FaithfulMergeFleetsSurvivorShipsBelow"/>, no carried army, launched. Absorbed
/// fleet: Chebyshev distance strictly below <see cref="AiWeightsRules.FaithfulMergeFleetsDistanceBelow"/>, the
/// combined ships strictly below <see cref="AiWeightsRules.FaithfulMergeFleetsCombinedShipsBelow"/>, no
/// carried army, launched. The survivor's cap is not applied to the absorbed fleet and the sizes are not
/// compared. No destination gate: the original's "no destination" is the construction countdown.
/// Money goes through <see cref="PurseAccounting.Credit"/> (saturating at 32,767, the excess discarded: the
/// user's decision 1); supplies are added whole; no treasury is touched.
/// </remarks>
[CommandHandler]
public sealed class AiAbsorbFleetCommandHandler : ICommandHandler<AiAbsorbFleetCommand>
{
    /// <inheritdoc/>
    public CommandOutcome Handle(AiAbsorbFleetCommand command, CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        var state = context.State;
        var nation = state.NationById(command.IssuingNationId);
        if (nation is null || nation.Eliminated || nation.Control != SeatControl.Ai)
        {
            return CommandOutcome.Reject(
                AiAbsorbFleetRejections.NotAnAiSeat, "Only an AI-controlled seat merges fleets at a distance.");
        }

        if (context.Ruleset.Flags.AiEconomy != AiEconomyModel.Original)
        {
            return CommandOutcome.Reject(
                AiAbsorbFleetRejections.NotOriginalAi, "This ruleset's AI does not merge fleets at a distance.");
        }

        if (string.Equals(command.SurvivingFleetId, command.AbsorbedFleetId, StringComparison.Ordinal))
        {
            return CommandOutcome.Reject(AiAbsorbFleetRejections.SameFleet, "A fleet cannot be absorbed by itself.");
        }

        var survivor = state.FleetById(command.SurvivingFleetId);
        var absorbed = state.FleetById(command.AbsorbedFleetId);
        if (survivor is null || absorbed is null)
        {
            var missing = survivor is null ? command.SurvivingFleetId : command.AbsorbedFleetId;
            return CommandOutcome.Reject(AiAbsorbFleetRejections.UnknownFleet, $"'{missing}' is not a known fleet.");
        }

        if (!string.Equals(survivor.Nation, command.IssuingNationId, StringComparison.Ordinal)
            || !string.Equals(absorbed.Nation, command.IssuingNationId, StringComparison.Ordinal))
        {
            return CommandOutcome.Reject(
                AiAbsorbFleetRejections.NotYourFleet, "Both fleets must belong to the issuing nation.");
        }

        var weights = context.Ruleset.Ai;
        if (!IsValidSurvivor(survivor, weights) || !IsValidPartner(survivor, absorbed, weights))
        {
            return CommandOutcome.Reject(
                AiAbsorbFleetRejections.GateFailed,
                $"Fleets '{survivor.Id}' and '{absorbed.Id}' fail the merge gates.");
        }

        var merged = survivor with
        {
            Ships = survivor.Ships + absorbed.Ships,
            Money = PurseAccounting.Credit(survivor.Money, absorbed.Money),
            SupplyTons = survivor.SupplyTons + absorbed.SupplyTons,
        };

        var updated = state.Fleets
            .Where(f => !string.Equals(f.Id, absorbed.Id, StringComparison.Ordinal))
            .Select(f => string.Equals(f.Id, survivor.Id, StringComparison.Ordinal) ? merged : f);
        return CommandOutcome.Accept(state with { Fleets = ValueList.From(updated) });
    }

    /// <summary>The survivor's gates (F2) beyond ownership: ships strictly below the bound, no army, launched.</summary>
    public static bool IsValidSurvivor(FleetState survivor, AiWeightsRules weights) =>
        survivor.Ships < weights.FaithfulMergeFleetsSurvivorShipsBelow
        && survivor.CarriedArmyId is null
        && survivor.ConstructionTicksRemaining is null;

    /// <summary>
    /// The absorbed fleet's gates (F3) beyond ownership and being another fleet: distance, combined ships, no
    /// army, launched.
    /// </summary>
    public static bool IsValidPartner(FleetState survivor, FleetState partner, AiWeightsRules weights) =>
        LandingTile.ChebyshevDistance(new GridPoint(survivor.X, survivor.Y), new GridPoint(partner.X, partner.Y))
            < weights.FaithfulMergeFleetsDistanceBelow
        && survivor.Ships + partner.Ships < weights.FaithfulMergeFleetsCombinedShipsBelow
        && partner.CarriedArmyId is null
        && partner.ConstructionTicksRemaining is null;
}
