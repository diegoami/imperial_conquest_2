using System.Globalization;
using IC2.Engine.Economy;
using IC2.Engine.Model;

namespace IC2.Slice.UI;

/// <summary>One proposed ordinary player command; planning itself never changes state or RNG.</summary>
public sealed record ArmyAutoSupportProposal(string ArmyId, string CommandLine, string Kind,
    int Requested, string? ProviderKey = null);

/// <summary>T207's pure next-command selection for global, opt-in human-army support.</summary>
public static class ArmyAutoSupportPlanner
{
    public static ArmyAutoSupportProposal? NextSupply(GameState state, ArmyState army, Ruleset ruleset,
        UserSettings settings, IReadOnlySet<string> attemptedProviders)
    {
        if (!settings.AutoSupplyArmies) return null;
        var capacity = SupplyCapacity.ArmyDialogCapacityTons(army.TotalTroops, ruleset);
        var needed = Math.Max(0, capacity - army.SupplyTons);
        if (needed == 0) return null;

        foreach (var provider in SupplyProviders.ForArmy(state, army.Id, ruleset))
        {
            var key = $"{provider.Kind}:{provider.Id}";
            if (!provider.IsFree || attemptedProviders.Contains(key)) continue;
            var stock = provider.Kind switch
            {
                SupplyProviderKind.City => state.CityById(provider.Id)?.SupplyTons ?? 0,
                SupplyProviderKind.Fleet => state.FleetById(provider.Id)?.SupplyTons ?? 0,
                SupplyProviderKind.Army => state.ArmyById(provider.Id)?.SupplyTons ?? 0,
                _ => 0,
            };
            var tons = Math.Min(needed, Math.Max(0, stock));
            if (tons == 0) continue;
            var token = provider.Kind switch
            {
                SupplyProviderKind.Fleet => $"fleet {provider.Id}",
                SupplyProviderKind.Army => $"army {provider.Id}",
                _ => provider.Id,
            };
            return new(army.Id, $"buy {army.Id} {token} {tons.ToString(CultureInfo.InvariantCulture)}",
                "supply", tons, key);
        }

        return null;
    }

    public static ArmyAutoSupportProposal? Fund(GameState state, ArmyState army, Ruleset ruleset,
        UserSettings settings)
    {
        if (!settings.AutoFundArmies) return null;
        var nation = state.NationById(army.Nation);
        if (nation is null) return null;
        var target = Math.Min(QuarterlyPurseTarget(army, ruleset), ruleset.Economy.PurseCapPerUnit);
        var needed = Math.Max(0, target - army.Money);
        // Never request a positive transfer into an over-cap purse: the manual
        // writer would pull it back to the cap. Funding is a top-up only.
        var amount = Math.Min(needed, ruleset.Economy.PurseCapPerUnit);
        if (ruleset.Flags.EconomyPurses != EconomyPurseModel.PerUnitPurses)
            amount = Math.Min(amount, Math.Max(0, nation.Treasury));
        return amount == 0 ? null : new(army.Id,
            $"transfer-money {army.Id} {amount.ToString(CultureInfo.InvariantCulture)}", "money", amount);
    }

    /// <summary>The same nominal regular+mercenary quarterly cost shown by the army information panel.</summary>
    public static int QuarterlyPurseTarget(ArmyState army, Ruleset ruleset) =>
        Math.Max(0, ArmyUpkeep.Compute(army.Units, ruleset));
}
