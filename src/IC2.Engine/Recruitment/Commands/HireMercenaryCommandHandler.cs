using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Naval;

namespace IC2.Engine.Recruitment.Commands;

/// <summary>
/// Wires <see cref="MercenaryHireCost.Compute"/> behind the command seam — <c>docs/task-catalogue.md</c>
/// "T13 Recruitment and mercenaries", Done-when 2 and 4.
/// </summary>
[CommandHandler]
public sealed class HireMercenaryCommandHandler : ICommandHandler<HireMercenaryCommand>
{
    /// <inheritdoc/>
    public CommandOutcome Handle(HireMercenaryCommand command, CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        var state = context.State;
        var army = state.ArmyById(command.ArmyId);
        if (army is null)
        {
            return CommandOutcome.Reject(
                HireMercenaryRejections.UnknownArmy, $"'{command.ArmyId}' is not a known army.");
        }

        if (!string.Equals(army.Nation, command.IssuingNationId, StringComparison.Ordinal))
        {
            return CommandOutcome.Reject(
                HireMercenaryRejections.NotYourArmy,
                $"Army '{army.Id}' belongs to '{army.Nation}', not '{command.IssuingNationId}'.");
        }

        var slot = FindPoolSlot(state, command.PoolSlotIndex);
        if (slot is null)
        {
            return CommandOutcome.Reject(
                HireMercenaryRejections.UnknownPoolSlot,
                $"Mercenary pool slot {command.PoolSlotIndex} has no offer to hire.");
        }

        // T76: the player's position gate. FUN_00449D08 takes the first live offer, in slot order, at
        // Chebyshev distance exactly the ruleset's human-seat range from the army and uses its city;
        // TRecruitMercs_InitializeForm then lists only the live offers on that city's tile. With no
        // adjacent offer the order is a silent no-op.
        // [confirmed: decompiled-mercenary-offer-list-and-position.md §1-§2]
        var chosenCity = FindChosenCity(state, army, context.Ruleset.Recruitment.MercenaryHireRangeHumanSeat);
        if (chosenCity is null)
        {
            return CommandOutcome.Reject(
                HireMercenaryRejections.NoAdjacentOffer,
                $"Army '{army.Id}' at ({army.X},{army.Y}) is not adjacent to any mercenary offer's city.");
        }

        if (slot.X != chosenCity.X || slot.Y != chosenCity.Y)
        {
            return CommandOutcome.Reject(
                HireMercenaryRejections.OfferNotAdjacentCity,
                $"Mercenary pool slot {slot.SlotIndex} sits on ({slot.X},{slot.Y}), not on the chosen "
                + $"city '{chosenCity.Id}' at ({chosenCity.X},{chosenCity.Y}).");
        }

        var cost = MercenaryHireCost.Compute(slot.Troops, slot.UnitTypeId, slot.Quality, context.Ruleset);
        if (army.Money < cost)
        {
            return CommandOutcome.Reject(
                HireMercenaryRejections.InsufficientMoney,
                "Your army has too little money to pay these mercenaries.");
        }

        var troopsAfterHire = army.TotalTroops + slot.Troops;
        if (troopsAfterHire > context.Ruleset.ArmyManagement.MaxTroopsPerArmy)
        {
            return CommandOutcome.Reject(
                HireMercenaryRejections.OverArmyTroopCap,
                "An army can not contain more than 100,000 troops.");
        }

        // T15 Done-when 6 (issue #181): the hire always appends exactly one unit slot, so the check is
        // the same shape as JoinArmiesCommandHandler's own MaxUnitsPerArmy cap -- inclusive, so an army
        // at 19 units accepting a hire (ending at 20) is still allowed.
        if (army.Units.Count + 1 > context.Ruleset.ArmyManagement.MaxUnitsPerArmy)
        {
            return CommandOutcome.Reject(
                HireMercenaryRejections.OverArmyUnitCap,
                $"Army '{army.Id}' already has {army.Units.Count} units; hiring one more would exceed the "
                + $"{context.Ruleset.ArmyManagement.MaxUnitsPerArmy}-unit cap.");
        }

        // The original's own refusal "You cannot recruit from an enemy city.": relation[city.owner][me]
        // == war. It sits here, after the troop and unit caps, because TUnitMap_RecruitMercenaries checks
        // "20 units" and "100,000" before the enemy city, and because every refusal that existed before
        // T76 must keep returning its own code
        // [confirmed: decompiled-mercenary-offer-list-and-position.md §1].
        if (state.Relations.IndexOf(command.IssuingNationId) >= 0
            && state.Relations.IndexOf(chosenCity.Owner) >= 0
            && state.Relations.Get(command.IssuingNationId, chosenCity.Owner)
               == context.Ruleset.Diplomacy.StateCodes.War)
        {
            return CommandOutcome.Reject(
                HireMercenaryRejections.EnemyCity,
                $"'{chosenCity.Id}' belongs to '{chosenCity.Owner}', at war with '{command.IssuingNationId}'.");
        }

        if (army.IsEmbarked)
        {
            var fleet = state.FleetById(army.AboardFleetId!);
            var capacity = fleet is null ? 0 : fleet.Ships * context.Ruleset.Naval.TransportTroopsPerShip;
            if (fleet is null || troopsAfterHire > capacity)
            {
                return CommandOutcome.Reject(
                    HireMercenaryRejections.FleetNoSpace,
                    "This fleet has too little space for these mercenaries.");
            }
        }

        var hiredUnit = new UnitSlot(
            MercenaryLabel: slot.NameLabel,
            UnitTypeId: slot.UnitTypeId,
            Troops: slot.Troops,
            Quality: slot.Quality,
            Name: $"Mercenary unit (label {slot.NameLabel})");

        var updatedArmy = army with
        {
            Money = army.Money - cost,
            Units = ValueList.From(army.Units.Append(hiredUnit)),
        };

        var updatedPool = state.MercenaryPool.Where(s => s.SlotIndex != slot.SlotIndex);

        context.Events.Publish(new MercenaryHired(
            army.Nation, army.Id, slot.SlotIndex, slot.UnitTypeId, slot.Troops, slot.Quality, cost));

        var updatedArmies = state.Armies.Select(a =>
            string.Equals(a.Id, army.Id, StringComparison.Ordinal) ? updatedArmy : a);

        return CommandOutcome.Accept(state with
        {
            Armies = ValueList.From(updatedArmies),
            MercenaryPool = ValueList.From(updatedPool),
        });
    }

    private static MercenaryPoolSlot? FindPoolSlot(GameState state, int slotIndex)
    {
        foreach (var slot in state.MercenaryPool)
        {
            if (slot.SlotIndex == slotIndex)
            {
                return slot;
            }
        }

        return null;
    }

    /// <summary>
    /// The city of the first live offer, in slot order, at Chebyshev distance exactly
    /// <paramref name="range"/> from the army — the original's <c>FUN_00449D08</c> / <c>FUN_004498D8</c>
    /// pair, which skips an adjacent offer whose tile holds no city and keeps looking. The range is the
    /// ruleset's, never a literal, so a changed ruleset actually changes the gate
    /// <strong>[confirmed: decompiled-mercenary-offer-list-and-position.md §1]</strong>.
    /// </summary>
    private static CityState? FindChosenCity(GameState state, ArmyState army, int range)
    {
        MercenaryPoolSlot? chosen = null;
        foreach (var slot in state.MercenaryPool)
        {
            if (chosen is not null && slot.SlotIndex >= chosen.SlotIndex)
            {
                continue;
            }

            if (!IsAdjacent(army, slot, range))
            {
                continue;
            }

            if (CityAt(state, slot.X, slot.Y) is not null)
            {
                chosen = slot;
            }
        }

        return chosen is null ? null : CityAt(state, chosen.X, chosen.Y);
    }

    private static bool IsAdjacent(ArmyState army, MercenaryPoolSlot slot, int range) =>
        LandingTile.ChebyshevDistance(new GridPoint(army.X, army.Y), new GridPoint(slot.X, slot.Y)) == range;

    private static CityState? CityAt(GameState state, int x, int y)
    {
        foreach (var city in state.Cities)
        {
            if (city.X == x && city.Y == y)
            {
                return city;
            }
        }

        return null;
    }
}
