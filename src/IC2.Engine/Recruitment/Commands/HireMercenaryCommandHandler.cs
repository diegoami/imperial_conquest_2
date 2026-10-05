using IC2.Engine.Core;
using IC2.Engine.Economy;
using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Naval;

namespace IC2.Engine.Recruitment.Commands;

/// <summary>
/// Wires <see cref="MercenaryHireCost.Compute"/> behind the command seam as a <strong>minimum-purse
/// gate</strong> — <c>docs/task-catalogue.md</c> "T13 Recruitment and mercenaries", Done-when 2 and 4.
/// </summary>
/// <remarks>
/// <para>
/// <strong>T143 (bug #755): the price is a gate, not a charge.</strong> An accepted hire appends the
/// hired unit and consumes the pool slot, and changes no purse and no treasury. The value
/// <see cref="MercenaryHireCost.Compute"/> returns is only the minimum the army's own purse must hold;
/// the first money a mercenary actually costs is its quarterly <see cref="Economy.ArmyUpkeep"/> pay,
/// which this handler does not touch
/// <strong>[derived: code, <c>TRecruitMercs_RecruitMercUnit</c> @ <c>00441360</c> refuses when
/// <c>purse &lt; (troops × price[type] div 1000) × quality</c> (:43633-43639), and nothing in the
/// function writes the purse or the treasury (:43618-43701); Wine candidate: two hires left army 1's
/// purse at 30 and the treasury at 2,270 both times, <c>Q4_03</c> → <c>Q4_04</c> → <c>Q4_06</c>,
/// release <c>run-exp-v050-rules</c>]</strong>. Both presets charge nothing
/// (<c>flags.economyPurses</c> is not read here).
/// </para>
/// <para>
/// <strong>Bug #769: the original's low-supply refusal.</strong> Between the enemy-city refusal and the
/// embarked fleet's capacity check the order tests the army's supplies:
/// </para>
/// <code>
/// if (army.supplies * 10000 / total &lt; 15) "No mercenaries will join an army with so few supplies.";
/// </code>
/// <para>
/// where <c>total</c> is the army's troops <em>before</em> the hire, in integer arithmetic
/// <strong>[confirmed: code, <c>TUnitMap_RecruitMercenaries</c> @ <c>0x00446FF4</c>, :46858–46925]</strong>.
/// The order's own sequence is 20 units, more than 100,000 troops, an enemy city, the supplies, then the
/// fleet; the clone's order already differs elsewhere (its purse, troop and unit caps come before the
/// enemy city), so the new refusal is placed only to stay after the enemy city and before the fleet
/// <strong>[derived: the report's order, mapped onto the clone's]</strong>. An army with no troops is
/// refused without dividing — the original's <c>div</c> by zero is no rule to copy
/// <strong>[designed: a hand-built or imported state can reach it, and the engine's own paths cannot]</strong>.
/// </para>
/// </remarks>
[CommandHandler]
public sealed class HireMercenaryCommandHandler : ICommandHandler<HireMercenaryCommand>
{
    /// <summary>
    /// The original's mercenary supply floor: a hire is refused when the army's supply percentage,
    /// <c>supplies × 10000 / troops</c>, is below this
    /// <strong>[confirmed: code, decompiled-mercenary-offer-list-and-position.md §1,
    /// <c>TUnitMap_RecruitMercenaries</c> @ <c>0x00446FF4</c>: <c>army.supplies*10000/total &lt; 15</c>]</strong>.
    /// The <c>10000</c> is <see cref="EconomyRules.SupplyPercentNumerator"/> through
    /// <see cref="SupplyCapacity.PercentFull"/>; the <c>15</c> has no ruleset field and this task's Owns
    /// forbids adding one, so it is carried here with its provenance rather than invented.
    /// </summary>
    private const int MinimumSupplyPercentForHire = 15;

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

        // Bug #755/T143: this is the minimum purse the hire must pass, not a charge. The hire below
        // writes no Money at all; the value travels on the event as HireGate so no reader mistakes it
        // for talents paid.
        var hireGate = MercenaryHireCost.Compute(slot.Troops, slot.UnitTypeId, slot.Quality, context.Ruleset);
        if (army.Money < hireGate)
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

        // Bug #769: the original's supply floor, tested after the enemy city and before the fleet. The
        // percentage is the one SupplyCapacity.PercentFull computes (supplies × the ruleset's numerator
        // / troops, the panel's own readout); 15 is the constant the original compares against and has
        // no ruleset field, which this task's Owns forbids adding
        // [confirmed: decompiled-mercenary-offer-list-and-position.md §1,
        // TUnitMap_RecruitMercenaries @ 0x00446FF4, :46858-46925]. total is read before the hire.
        var totalTroopsBeforeHire = army.TotalTroops;
        if (totalTroopsBeforeHire == 0
            || SupplyCapacity.PercentFull(army.SupplyTons, totalTroopsBeforeHire, context.Ruleset)
               < MinimumSupplyPercentForHire)
        {
            return CommandOutcome.Reject(
                HireMercenaryRejections.TooFewSupplies,
                "No mercenaries will join an army with so few supplies.");
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

        // Bug #755/T143: the hire takes nothing from the purse or the treasury. The army changes only
        // by its new unit.
        var updatedArmy = army with
        {
            Units = ValueList.From(army.Units.Append(hiredUnit)),
        };

        var updatedPool = state.MercenaryPool.Where(s => s.SlotIndex != slot.SlotIndex);

        context.Events.Publish(new MercenaryHired(
            army.Nation, army.Id, slot.SlotIndex, slot.UnitTypeId, slot.Troops, slot.Quality, hireGate));

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
