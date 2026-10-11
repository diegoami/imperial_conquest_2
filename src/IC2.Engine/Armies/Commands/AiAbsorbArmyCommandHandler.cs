using IC2.Engine.Core;
using IC2.Engine.Economy;
using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Naval;

namespace IC2.Engine.Armies.Commands;

/// <summary>
/// <c>FUN_004509f0</c>'s merge step. See <see cref="AiAbsorbArmyCommand"/>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Gates [confirmed: decompile, A2/A3/A5].</strong> Donor: troops strictly below
/// <see cref="AiWeightsRules.FaithfulMergeArmiesDonorTroopsBelow"/>, not aboard. Partner: Chebyshev distance
/// strictly below <see cref="AiWeightsRules.FaithfulMergeArmiesDistanceBelow"/>, the two slot extents summing
/// strictly below <see cref="AiWeightsRules.FaithfulMergeArmiesExtentBelow"/>, its own troops strictly below
/// <see cref="AiWeightsRules.FaithfulMergeArmiesPartnerTroopsBelow"/>, not aboard. No orders or moves gate; the
/// sizes are never compared. The <em>extent</em> is the highest slot holding troops above 0, plus 1 (the user's
/// decision 3), not <c>Units.Count</c>.
/// </para>
/// <para>
/// <strong>Carry-over [A6, A7, A8, A10].</strong> Money through <see cref="PurseAccounting.Credit"/>
/// (saturating at 32,767, the excess discarded: the user's decision 1, a [designed] divergence from the
/// original's 16-bit wrap; no treasury is touched); supplies added whole. The donor's slots are appended
/// whole after the partner's highest occupied slot, in the order slot 0, then the last occupied slot, then
/// the next last, and so on (A8, [derived] for more than three). When the donor's moves are 0 the partner's
/// become 0; otherwise they stay (A7). The partner keeps every other field. A donor with no occupied slot
/// moves nothing and leaves the moves alone (A12's skipped loop; the donor is still deleted, decision 4).
/// </para>
/// </remarks>
[CommandHandler]
public sealed class AiAbsorbArmyCommandHandler : ICommandHandler<AiAbsorbArmyCommand>
{
    /// <inheritdoc/>
    public CommandOutcome Handle(AiAbsorbArmyCommand command, CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        var state = context.State;
        var nation = state.NationById(command.IssuingNationId);
        if (nation is null || nation.Eliminated || nation.Control != SeatControl.Ai)
        {
            return CommandOutcome.Reject(
                AiAbsorbArmyRejections.NotAnAiSeat, "Only an AI-controlled seat merges armies at a distance.");
        }

        if (context.Ruleset.Flags.AiEconomy != AiEconomyModel.Original)
        {
            return CommandOutcome.Reject(
                AiAbsorbArmyRejections.NotOriginalAi, "This ruleset's AI does not merge armies at a distance.");
        }

        if (string.Equals(command.SurvivingArmyId, command.AbsorbedArmyId, StringComparison.Ordinal))
        {
            return CommandOutcome.Reject(AiAbsorbArmyRejections.SameArmy, "An army cannot be absorbed by itself.");
        }

        var partner = state.ArmyById(command.SurvivingArmyId);
        var donor = state.ArmyById(command.AbsorbedArmyId);
        if (partner is null || donor is null)
        {
            var missing = partner is null ? command.SurvivingArmyId : command.AbsorbedArmyId;
            return CommandOutcome.Reject(AiAbsorbArmyRejections.UnknownArmy, $"'{missing}' is not a known army.");
        }

        if (!string.Equals(partner.Nation, command.IssuingNationId, StringComparison.Ordinal)
            || !string.Equals(donor.Nation, command.IssuingNationId, StringComparison.Ordinal))
        {
            return CommandOutcome.Reject(
                AiAbsorbArmyRejections.NotYourArmy, "Both armies must belong to the issuing nation.");
        }

        var weights = context.Ruleset.Ai;
        if (!IsValidDonor(donor, weights) || !IsValidPartner(donor, partner, weights))
        {
            return CommandOutcome.Reject(
                AiAbsorbArmyRejections.GateFailed,
                $"Armies '{donor.Id}' and '{partner.Id}' fail the merge gates.");
        }

        var moved = ArrivalOrder(donor);
        var units = partner.Units.Take(Extent(partner)).ToList();
        units.AddRange(moved);

        var merged = partner with
        {
            Units = ValueList.From(units),
            Money = PurseAccounting.Credit(partner.Money, donor.Money),
            SupplyTons = partner.SupplyTons + donor.SupplyTons,
            Moves = moved.Count > 0 && donor.Moves == 0 ? 0 : partner.Moves,
        };

        var updated = state.Armies
            .Where(a => !string.Equals(a.Id, donor.Id, StringComparison.Ordinal))
            .Select(a => string.Equals(a.Id, partner.Id, StringComparison.Ordinal) ? merged : a);
        return CommandOutcome.Accept(state with { Armies = ValueList.From(updated) });
    }

    /// <summary>The donor's own gates (A2) beyond ownership: troops strictly below the bound, not aboard.</summary>
    public static bool IsValidDonor(ArmyState donor, AiWeightsRules weights) =>
        donor.TotalTroops < weights.FaithfulMergeArmiesDonorTroopsBelow && donor.AboardFleetId is null;

    /// <summary>
    /// The partner's gates (A3) beyond ownership and being another army: distance, combined extent, its own
    /// troops, not aboard.
    /// </summary>
    public static bool IsValidPartner(ArmyState donor, ArmyState partner, AiWeightsRules weights) =>
        LandingTile.ChebyshevDistance(new GridPoint(donor.X, donor.Y), new GridPoint(partner.X, partner.Y))
            < weights.FaithfulMergeArmiesDistanceBelow
        && Extent(donor) + Extent(partner) < weights.FaithfulMergeArmiesExtentBelow
        && partner.TotalTroops < weights.FaithfulMergeArmiesPartnerTroopsBelow
        && partner.AboardFleetId is null;

    /// <summary>The slot extent (A5): the highest slot index holding troops above 0, plus 1; 0 with none.</summary>
    public static int Extent(ArmyState army)
    {
        var extent = 0;
        for (var i = 0; i < army.Units.Count; i++)
        {
            if (army.Units[i].Troops > 0)
            {
                extent = i + 1;
            }
        }

        return extent;
    }

    /// <summary>
    /// The donor's occupied slots in the order they arrive at the partner (A8): slot 0, then the last
    /// occupied slot, then the next last, and so on down to slot 1. Empty slots (troops 0) are not moved.
    /// </summary>
    private static List<UnitSlot> ArrivalOrder(ArmyState donor)
    {
        var occupied = donor.Units.Where(u => u.Troops > 0).ToList();
        var order = new List<UnitSlot>(occupied.Count);
        if (occupied.Count > 0)
        {
            order.Add(occupied[0]);
            for (var i = occupied.Count - 1; i >= 1; i--)
            {
                order.Add(occupied[i]);
            }
        }

        return order;
    }
}
