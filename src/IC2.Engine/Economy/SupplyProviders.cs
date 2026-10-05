using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Naval;

namespace IC2.Engine.Economy;

/// <summary>
/// What kind of thing a supply purchase's provider is — a city (own or foreign) or one of the buying
/// army's own fleets. The original's <c>TAFSupply</c> window lists both together
/// <strong>[confirmed: code, <c>TAFSupply_FindProviders</c>; <c>supply-capacity-rounding.md</c> "Which
/// path a purchase takes"]</strong>.
/// </summary>
public enum SupplyProviderKind
{
    /// <summary>A city within one tile whose owner is not at war with the buying army's nation.</summary>
    City,

    /// <summary>One of the buying army's own fleets within one tile, not under construction.</summary>
    Fleet,
}

/// <summary>
/// One provider <see cref="SupplyProviders.ForArmy"/> offers an army — exactly the entities
/// <c>TAFSupply_FindProviders</c> hands the dialog.
/// </summary>
/// <param name="Kind">City or fleet, so the dialog knows which <c>buy</c> form to compose.</param>
/// <param name="Id">The city or fleet id.</param>
/// <param name="IsFree">
/// Whether a purchase here is free. An own city and an own fleet are free; a foreign city at peace is
/// paid. <c>TAFSupply_ChangeSupply</c> applies a free transfer at once and
/// <c>TAFSupply_ChangeBuyAmount</c>/<c>TAFSupply_TransferSupply</c> the paid one
/// <strong>[confirmed: code; <c>supply-capacity-rounding.md</c>]</strong>.
/// </param>
public sealed record SupplyProvider(SupplyProviderKind Kind, string Id, bool IsFree);

/// <summary>
/// The read-only provider query behind the Army menu's <strong>Supply army</strong> dialog — the
/// providers <c>TAFSupply_FindProviders</c> offers an army
/// <strong>[confirmed: code, <c>supply-capacity-rounding.md</c> "Which path a purchase takes"]</strong>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What it lists.</strong> Every city within
/// <see cref="EconomyRules.CommandAdjacencyRadiusTiles"/> tiles (Chebyshev) whose owner is not at war with
/// the army's nation, and every one of the army's own fleets within the same radius that is not under
/// construction. Cities come first, in the state's own order, then fleets in the state's own order — the
/// dialog's list order.
/// </para>
/// <para>
/// <strong>It re-implements no rule and no gate.</strong> Each condition here is the same condition
/// <see cref="Commands.BuySupplyCommandHandler"/> applies before it accepts a purchase, so
/// <c>SupplyProvidersTests</c> can assert the two agree entity by entity: a provider the query lists is
/// accepted by the dispatcher and one it leaves out is refused with its own rejection code. The
/// free/paid split is <c>army.Nation == city.Owner</c>, the same test <see cref="SupplyPurchase"/> uses.
/// </para>
/// <para>
/// An unknown army yields no providers rather than throwing: the dialog has already established its own
/// selection, and a helper that returned providers for an army that does not exist could only ever be a
/// bug.
/// </para>
/// </remarks>
public static class SupplyProviders
{
    /// <summary>The providers the dialog offers <paramref name="armyId"/>, cities first, in state order.</summary>
    /// <param name="state">The live state the dialog reads.</param>
    /// <param name="armyId">The buying army's id; an unknown id yields an empty list.</param>
    /// <param name="ruleset">Supplies <see cref="EconomyRules.CommandAdjacencyRadiusTiles"/> and the war state code — never a C# literal.</param>
    public static IReadOnlyList<SupplyProvider> ForArmy(GameState state, string armyId, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ruleset);

        var army = state.ArmyById(armyId);
        if (army is null)
        {
            return Array.Empty<SupplyProvider>();
        }

        var radius = ruleset.Economy.CommandAdjacencyRadiusTiles;
        var armyTile = new GridPoint(army.X, army.Y);
        var providers = new List<SupplyProvider>();

        // Cities first, in the state's own order (the handler looks each provider up by id, so this is
        // only the dialog's display order).
        foreach (var city in state.Cities)
        {
            if (LandingTile.ChebyshevDistance(armyTile, new GridPoint(city.X, city.Y)) > radius)
            {
                continue;
            }

            // Same unresolvable-owner gate the handler applies before it reads the owner's war state.
            if (state.NationById(city.Owner) is null)
            {
                continue;
            }

            var isOwnCity = string.Equals(city.Owner, army.Nation, StringComparison.Ordinal);
            if (!isOwnCity && state.Relations.Get(army.Nation, city.Owner) == ruleset.Diplomacy.StateCodes.War)
            {
                continue;
            }

            providers.Add(new SupplyProvider(SupplyProviderKind.City, city.Id, isOwnCity));
        }

        // Then the army's own fleets within one tile, in state order. A fleet provider is never foreign
        // and never under construction -- both gates live in the handler too.
        foreach (var fleet in state.Fleets)
        {
            if (!string.Equals(fleet.Nation, army.Nation, StringComparison.Ordinal))
            {
                continue;
            }

            if (fleet.IsUnderConstruction)
            {
                continue;
            }

            if (LandingTile.ChebyshevDistance(armyTile, new GridPoint(fleet.X, fleet.Y)) > radius)
            {
                continue;
            }

            providers.Add(new SupplyProvider(SupplyProviderKind.Fleet, fleet.Id, IsFree: true));
        }

        return providers;
    }
}
