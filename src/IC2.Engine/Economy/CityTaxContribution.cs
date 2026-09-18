using IC2.Engine.Model;

namespace IC2.Engine.Economy;

/// <summary>
/// One city's contribution to its owner's tax base — <c>FUN_004498b0</c>, shared by the quarterly rebuild
/// (<see cref="NationTaxBaseRebuild"/>) and the city-ownership-transfer adjustment
/// (<see cref="CityOwnershipTaxTransfer"/>) so the two never restate the formula differently
/// <strong>[confirmed: nation-tax-base-and-city-economy-fields.md]</strong>.
/// </summary>
public static class CityTaxContribution
{
    /// <summary><c>tribute × population / maxPopulation</c>, integer-truncating.</summary>
    public static int Compute(CityState city)
    {
        ArgumentNullException.ThrowIfNull(city);
        return city.Tribute * city.PopulationThousands / city.MaxPopulationThousands;
    }
}
