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
        var target = (int)Math.Ceiling((double)capacity * settings.AutoSupplyTargetPercent / 100);
        var needed = Math.Max(0, target - army.SupplyTons);
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
        var target = Math.Min(settings.AutoPurseTargetTalents, ruleset.Economy.PurseCapPerUnit);
        var needed = Math.Max(0, target - army.Money);
        var available = Math.Max(0, nation.Treasury - settings.AutoTreasuryReserveTalents);
        var amount = Math.Min(Math.Min(needed, available), ruleset.Economy.PurseCapPerUnit);
        return amount == 0 ? null : new(army.Id,
            $"transfer-money {army.Id} {amount.ToString(CultureInfo.InvariantCulture)}", "money", amount);
    }
}
