using IC2.Engine.Model;

namespace IC2.Engine.Economy;

/// <summary>
/// The quarterly tax-base and wealth rebuild — <c>FUN_00451b40</c>: every nation's tax base and wealth
/// are zeroed, then rebuilt as a sum over the cities it currently owns
/// <strong>[confirmed: nation-tax-base-and-city-economy-fields.md]</strong>.
/// </summary>
/// <remarks>
/// Summing from scratch over <paramref name="state"/>'s cities is equivalent to the original's
/// zero-then-accumulate loop and reads more directly as a test. This must run <em>after</em> that
/// quarter's population growth, so the rebuild's <c>population</c> term is the grown value, not the
/// pre-tick one <strong>[confirmed: city-population-growth.md's step order]</strong>.
/// </remarks>
public static class NationTaxBaseRebuild
{
    /// <summary>Returns <paramref name="state"/> with every nation's <see cref="NationState.TaxBase"/> and <see cref="NationState.Wealth"/> rebuilt from its currently-owned cities.</summary>
    public static GameState Rebuild(GameState state, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ruleset);

        var economy = ruleset.Economy;
        var taxBaseByNation = new Dictionary<string, int>(StringComparer.Ordinal);
        var wealthByNation = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var nation in state.Nations)
        {
            taxBaseByNation[nation.Id] = 0;
            wealthByNation[nation.Id] = 0;
        }

        foreach (var city in state.Cities)
        {
            if (!taxBaseByNation.ContainsKey(city.Owner))
            {
                // Defensive only: GameDataValidation already requires every city's owner to resolve to a
                // known nation, so this branch is unreachable for a state this engine loaded or produced.
                continue;
            }

            taxBaseByNation[city.Owner] += CityTaxContribution.Compute(city) * economy.TaxBaseContributionMultiplier;
            wealthByNation[city.Owner] += city.PopulationThousands * economy.WealthPerPopulationThousand;
        }

        var nations = state.Nations.Select(nation => nation with
        {
            TaxBase = taxBaseByNation[nation.Id],
            Wealth = wealthByNation[nation.Id],
        });

        return state with { Nations = ValueList.From(nations) };
    }
}
