using IC2.Engine.Model;

namespace IC2.Engine.Economy;

/// <summary>
/// Automatic resupply — <c>docs/task-catalogue.md</c> "T38 Supply dialog follow-ups, treasury ↔ purse
/// transfers, and automatic resupply" (issue #78), Done-when 7. Pure functions only: wiring the trigger
/// (a human army's move ending against a non-hostile city is T23's; the AI's per-turn pass is T22's) is
/// explicitly out of this task's scope.
/// </summary>
/// <remarks>
/// <para>
/// <strong><c>[confirmed]</c>, transcribed from <c>supply-capacity-rounding.md</c></strong>:
/// <c>FUN_0044F6D8(city, army)</c> (lines 53077-53115) and its fleet twin <c>FUN_0044F7E4(city, fleet)</c>
/// (lines 53120-53158). Unlike the supply dialog (<see cref="SupplyPurchase"/>), this path takes no
/// request — it always tries to move exactly <c>capacity − currentSupplyTons</c> tons, capped by the
/// provider's own stock, with <strong>no dialog <c>+1</c> bonus</strong>
/// (<see cref="SupplyCapacity.ArmyCapacityTons"/> / <see cref="SupplyCapacity.FleetCapacityTons"/>, not
/// the dialog variants) and, exactly as the dialog, <strong>the room term is not floored at 0</strong>: an
/// army or fleet already over its cap gives the surplus back to the provider.
/// </para>
/// <para>
/// <strong>Own city: free, then purse hygiene.</strong> The ton transfer costs nothing. Afterward, if the
/// purse is over <see cref="EconomyRules.PurseCapPerUnit"/>, the excess moves to the treasury; if the
/// purse is under <see cref="EconomyRules.AutoResupplyPurseTopUpThreshold"/> and the treasury is positive,
/// the purse gains a flat <see cref="EconomyRules.AutoResupplyPurseTopUpAmount"/> from the treasury. Both
/// are real transfers (the treasury's side moves by the same amount), not invented money. Gated on
/// <see cref="EconomyPurseModel.PerUnitPurses"/>: under <see cref="EconomyPurseModel.CentralTreasury"/>
/// there is no per-unit purse for this hygiene rule to apply to.
/// </para>
/// <para>
/// <strong>Foreign city: paid, no purse hygiene.</strong> The tons are additionally capped at
/// <c>money / SupplyTonsPerTalent</c> — <em>not</em> <c>money × SupplyTonsPerTalent</c> as in the dialog —
/// and the cost is again <c>tons / SupplyTonsPerTalent</c>, credited to the selling city's owner's
/// treasury exactly as <see cref="SupplyPurchase"/>'s foreign path credits it (<c>[derived]</c>: code
/// only, no save has a foreign automatic resupply yet either). Under <see cref="EconomyPurseModel.CentralTreasury"/>
/// there is no per-unit purse to cap the tons by, so (exactly as <see cref="SupplyPurchase"/>) no cap is
/// invented for the treasury side.
/// </para>
/// </remarks>
public static class AutomaticResupply
{
    /// <summary>The result of one automatic army resupply.</summary>
    /// <param name="Army">The resupplied army, its supply stock changed and (own-city, per-unit purses) its purse hygiene applied.</param>
    /// <param name="City">The provider city, its supply stock changed oppositely to <see cref="Army"/>.</param>
    /// <param name="ArmyNation">The army's own nation, its treasury moved by purse hygiene (own city) or the debit (foreign, centralized).</param>
    /// <param name="CityNation">The city's own nation, its treasury credited on a foreign purchase.</param>
    /// <param name="AdmittedTons">The tons actually transferred; negative when an over-capacity army gives supply back.</param>
    /// <param name="TalentsPaid">Talents debited on the foreign path; 0 at an own city.</param>
    public sealed record ArmyResult(
        ArmyState Army, CityState City, NationState ArmyNation, NationState CityNation, int AdmittedTons, int TalentsPaid);

    /// <summary>Runs one automatic resupply for <paramref name="army"/> against <paramref name="city"/>.</summary>
    /// <param name="army">The army being resupplied.</param>
    /// <param name="city">The city (within range, non-hostile — the caller's responsibility to check) supplying it.</param>
    /// <param name="armyNation">The army's own nation; its <see cref="NationState.Id"/> must equal <paramref name="army"/>'s <see cref="ArmyState.Nation"/>.</param>
    /// <param name="cityNation">The city's own nation; its <see cref="NationState.Id"/> must equal <paramref name="city"/>'s <see cref="CityState.Owner"/>.</param>
    /// <param name="ruleset">Supplies every constant and the <see cref="RulesetFlags.EconomyPurses"/> flag — never a C# literal.</param>
    /// <exception cref="ArgumentException"><paramref name="armyNation"/> or <paramref name="cityNation"/> is not the expected owner.</exception>
    public static ArmyResult ForArmy(ArmyState army, CityState city, NationState armyNation, NationState cityNation, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(army);
        ArgumentNullException.ThrowIfNull(city);
        ArgumentNullException.ThrowIfNull(armyNation);
        ArgumentNullException.ThrowIfNull(cityNation);
        ArgumentNullException.ThrowIfNull(ruleset);

        if (!string.Equals(armyNation.Id, army.Nation, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Nation '{armyNation.Id}' is not army '{army.Id}''s own nation ('{army.Nation}').",
                nameof(armyNation));
        }

        if (!string.Equals(cityNation.Id, city.Owner, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Nation '{cityNation.Id}' does not own city '{city.Id}' ('{city.Owner}' does).",
                nameof(cityNation));
        }

        var isOwnCity = string.Equals(city.Owner, army.Nation, StringComparison.Ordinal);
        var capacity = SupplyCapacity.ArmyCapacityTons(army.TotalTroops, ruleset); // general cap, no dialog bonus.
        var room = capacity - army.SupplyTons; // not floored at 0.
        var tons = Math.Min(room, city.SupplyTons);

        var updatedArmyNation = armyNation;
        var updatedCityNation = cityNation;
        ArmyState updatedArmy;
        CityState updatedCity;
        var talents = 0;

        if (isOwnCity)
        {
            updatedArmy = army with { SupplyTons = army.SupplyTons + tons };
            updatedCity = city with { SupplyTons = city.SupplyTons - tons };

            if (ruleset.Flags.EconomyPurses == EconomyPurseModel.PerUnitPurses)
            {
                (updatedArmy, updatedArmyNation) = ApplyPurseHygiene(updatedArmy.Money, updatedArmy, updatedArmyNation, ruleset,
                    (a, money) => a with { Money = money });
            }

            return new ArmyResult(updatedArmy, updatedCity, updatedArmyNation, updatedCityNation, tons, 0);
        }

        var foreignTons = tons;
        if (ruleset.Flags.EconomyPurses == EconomyPurseModel.PerUnitPurses)
        {
            foreignTons = Math.Min(foreignTons, army.Money / ruleset.Economy.SupplyTonsPerTalent);
        }

        talents = foreignTons / ruleset.Economy.SupplyTonsPerTalent;
        updatedCity = city with { SupplyTons = city.SupplyTons - foreignTons };
        updatedArmy = army with { SupplyTons = army.SupplyTons + foreignTons };

        if (talents != 0)
        {
            if (ruleset.Flags.EconomyPurses == EconomyPurseModel.CentralTreasury)
            {
                updatedArmyNation = armyNation with { Treasury = armyNation.Treasury - talents };
            }
            else
            {
                updatedArmy = updatedArmy with { Money = PurseAccounting.Credit(updatedArmy.Money, -talents, ruleset) };
            }

            updatedCityNation = cityNation with { Treasury = cityNation.Treasury + talents };
        }

        return new ArmyResult(updatedArmy, updatedCity, updatedArmyNation, updatedCityNation, foreignTons, talents);
    }

    /// <summary>The result of one automatic fleet resupply — the naval twin of <see cref="ArmyResult"/>.</summary>
    public sealed record FleetResult(
        FleetState Fleet, CityState City, NationState FleetNation, NationState CityNation, int AdmittedTons, int TalentsPaid);

    /// <summary>Runs one automatic resupply for <paramref name="fleet"/> against <paramref name="city"/>. See <see cref="ForArmy"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="fleetNation"/> or <paramref name="cityNation"/> is not the expected owner.</exception>
    public static FleetResult ForFleet(FleetState fleet, CityState city, NationState fleetNation, NationState cityNation, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(fleet);
        ArgumentNullException.ThrowIfNull(city);
        ArgumentNullException.ThrowIfNull(fleetNation);
        ArgumentNullException.ThrowIfNull(cityNation);
        ArgumentNullException.ThrowIfNull(ruleset);

        if (!string.Equals(fleetNation.Id, fleet.Nation, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Nation '{fleetNation.Id}' is not fleet '{fleet.Id}''s own nation ('{fleet.Nation}').",
                nameof(fleetNation));
        }

        if (!string.Equals(cityNation.Id, city.Owner, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Nation '{cityNation.Id}' does not own city '{city.Id}' ('{city.Owner}' does).",
                nameof(cityNation));
        }

        var isOwnCity = string.Equals(city.Owner, fleet.Nation, StringComparison.Ordinal);
        var capacity = SupplyCapacity.FleetCapacityTons(fleet.Ships, ruleset);
        var room = capacity - fleet.SupplyTons; // not floored at 0.
        var tons = Math.Min(room, city.SupplyTons);

        var updatedFleetNation = fleetNation;
        var updatedCityNation = cityNation;
        FleetState updatedFleet;
        CityState updatedCity;

        if (isOwnCity)
        {
            updatedFleet = fleet with { SupplyTons = fleet.SupplyTons + tons };
            updatedCity = city with { SupplyTons = city.SupplyTons - tons };

            if (ruleset.Flags.EconomyPurses == EconomyPurseModel.PerUnitPurses)
            {
                (updatedFleet, updatedFleetNation) = ApplyPurseHygiene(updatedFleet.Money, updatedFleet, updatedFleetNation, ruleset,
                    (f, money) => f with { Money = money });
            }

            return new FleetResult(updatedFleet, updatedCity, updatedFleetNation, updatedCityNation, tons, 0);
        }

        var foreignTons = tons;
        if (ruleset.Flags.EconomyPurses == EconomyPurseModel.PerUnitPurses)
        {
            foreignTons = Math.Min(foreignTons, fleet.Money / ruleset.Economy.SupplyTonsPerTalent);
        }

        var talents = foreignTons / ruleset.Economy.SupplyTonsPerTalent;
        updatedCity = city with { SupplyTons = city.SupplyTons - foreignTons };
        updatedFleet = fleet with { SupplyTons = fleet.SupplyTons + foreignTons };

        if (talents != 0)
        {
            if (ruleset.Flags.EconomyPurses == EconomyPurseModel.CentralTreasury)
            {
                updatedFleetNation = fleetNation with { Treasury = fleetNation.Treasury - talents };
            }
            else
            {
                updatedFleet = updatedFleet with { Money = PurseAccounting.Credit(updatedFleet.Money, -talents, ruleset) };
            }

            updatedCityNation = cityNation with { Treasury = cityNation.Treasury + talents };
        }

        return new FleetResult(updatedFleet, updatedCity, updatedFleetNation, updatedCityNation, foreignTons, talents);
    }

    /// <summary>
    /// The own-city purse hygiene shared by <see cref="ForArmy"/> and <see cref="ForFleet"/>: excess over
    /// <see cref="EconomyRules.PurseCapPerUnit"/> moves to the treasury; a purse under
    /// <see cref="EconomyRules.AutoResupplyPurseTopUpThreshold"/>, with a positive treasury, gains a flat
    /// <see cref="EconomyRules.AutoResupplyPurseTopUpAmount"/> from it. Generic over <typeparamref name="TUnit"/>
    /// (<see cref="ArmyState"/> or <see cref="FleetState"/>) since the rule reads only the unit's own
    /// money, via <paramref name="withMoney"/>.
    /// </summary>
    private static (TUnit Unit, NationState Nation) ApplyPurseHygiene<TUnit>(
        int purse, TUnit unit, NationState nation, Ruleset ruleset, Func<TUnit, int, TUnit> withMoney)
    {
        if (purse > ruleset.Economy.PurseCapPerUnit)
        {
            var excess = purse - ruleset.Economy.PurseCapPerUnit;
            return (withMoney(unit, ruleset.Economy.PurseCapPerUnit), nation with { Treasury = nation.Treasury + excess });
        }

        if (purse < ruleset.Economy.AutoResupplyPurseTopUpThreshold && nation.Treasury > 0)
        {
            var grant = ruleset.Economy.AutoResupplyPurseTopUpAmount;
            return (withMoney(unit, PurseAccounting.Credit(purse, grant, ruleset)), nation with { Treasury = nation.Treasury - grant });
        }

        return (unit, nation);
    }
}
