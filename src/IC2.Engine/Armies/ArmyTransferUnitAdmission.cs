using IC2.Engine.Armies.Commands;
using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Armies;

/// <summary>A stable unit identity from the two rosters when the dialog opened.</summary>
public sealed record ArmyTransferUnitIdentity(bool SelectedOrigin, int Index, UnitSlot Unit);

/// <summary>Move an original unit to one working side; identities survive compaction and return moves.</summary>
public sealed record ArmyTransferUnitStep(bool SelectedOrigin, int Index, bool ToPartner);

public sealed record ArmyTransferUnitFailure(ArmyTransferUnitStep Step, RejectionCode Code);

public sealed record ArmyTransferUnitPlan(
    ValueList<ArmyTransferUnitIdentity> Selected,
    ValueList<ArmyTransferUnitIdentity> Partner,
    ValueList<ArmyTransferUnitStep> AcceptedSteps,
    ValueList<ArmyTransferUnitFailure> NotMoved,
    bool SelectedReceived,
    bool PartnerReceived);

/// <summary>Research85: pure working-copy admission, one whole unit at a time, continuing after refusals.</summary>
public static class ArmyTransferUnitAdmission
{
    public static ArmyTransferUnitPlan Plan(ArmyState selected, ArmyState partner, Ruleset ruleset,
        GameState? state, IEnumerable<ArmyTransferUnitStep> steps)
    {
        var a = selected.Units.Select((u, i) => new ArmyTransferUnitIdentity(true, i, u)).ToList();
        var b = partner.Units.Select((u, i) => new ArmyTransferUnitIdentity(false, i, u)).ToList();
        var accepted = new List<ArmyTransferUnitStep>();
        var refused = new List<ArmyTransferUnitFailure>();
        var aReceived = false;
        var bReceived = false;
        foreach (var step in steps)
        {
            var receiver = step.ToPartner ? b : a;
            var giver = step.ToPartner ? a : b;
            var identity = giver.FindIndex(u => u.SelectedOrigin == step.SelectedOrigin && u.Index == step.Index);
            if (identity < 0)
            {
                // Repeating a move to its current side is inert. Invalid original
                // indexes are rejected atomically by the command boundary.
                continue;
            }
            var unit = giver[identity];
            RejectionCode? failure = null;
            var troops = receiver.Sum(u => (long)u.Unit.Troops) + unit.Unit.Troops;
            if (receiver.Count >= ruleset.ArmyManagement.MaxUnitsPerArmy)
                failure = ArmyTransferRejections.CombinedUnitsTooLarge;
            else if (troops > ruleset.ArmyManagement.MaxTroopsPerArmy)
                failure = ArmyTransferRejections.CombinedTroopsTooLarge;
            else
            {
                var army = step.ToPartner ? partner : selected;
                if (army.IsEmbarked && state?.FleetById(army.AboardFleetId!) is { } fleet
                    && troops / ruleset.Naval.TransportTroopsPerShip > fleet.Ships)
                    failure = ArmyTransferRejections.FleetCannotCarry;
            }
            if (failure is { } code)
            {
                refused.Add(new ArmyTransferUnitFailure(step, code));
                continue;
            }
            giver.RemoveAt(identity);
            receiver.Add(unit);
            accepted.Add(step);
            if (step.ToPartner) bReceived = true; else aReceived = true;
        }
        return new(ValueList.From(a), ValueList.From(b), ValueList.From(accepted),
            ValueList.From(refused), aReceived, bReceived);
    }

    /// <summary>The measured summary order: unit count else troop count, then independently fleet space.</summary>
    public static IReadOnlyList<string> RefusalMessages(IEnumerable<ArmyTransferUnitFailure> failures, Ruleset ruleset)
    {
        var codes = failures.Select(f => f.Code).ToHashSet();
        var messages = new List<string>();
        if (codes.Contains(ArmyTransferRejections.CombinedUnitsTooLarge))
            messages.Add($"This army already has {ruleset.ArmyManagement.MaxUnitsPerArmy} units.");
        else if (codes.Contains(ArmyTransferRejections.CombinedTroopsTooLarge))
            messages.Add($"An army can not hold more than {News.NewsLogWriter.FormatGroupedAmount(ruleset.ArmyManagement.MaxTroopsPerArmy)} troops.");
        if (codes.Contains(ArmyTransferRejections.FleetCannotCarry))
            messages.Add("This fleet can not carry any more troops.");
        return messages;
    }
}

/// <summary>Accepted command feedback, including units that stayed put; never a total command rejection.</summary>
[DomainEvent("armies.transfer-units-resolved")]
public sealed record ArmyTransferUnitsResolved(string SelectedArmyId, string PartnerArmyId,
    int Moved, int Requested, ValueList<ArmyTransferUnitFailure> NotMoved) : DomainEvent;
