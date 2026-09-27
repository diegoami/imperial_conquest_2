using IC2.Engine.Cities.Capture;
using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Economy;

/// <summary>
/// The quarterly tick's city loop, wired against real <see cref="GameState"/> —
/// <c>docs/task-catalogue.md</c> "T35 Model: nation tax base, recruitment slots, and the pending
/// diplomatic offer", Done-when 5, 9 and 11; T89 (<c>#397</c>) wires the rebellion itself onto this
/// loop's own loyalty pass.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Runs after T08's upkeep billing</strong> (<see cref="QuarterlyEconomySystem"/>, registered at
/// this hook's default order 0): <c>city-population-growth.md</c>'s step order has upkeep billed first,
/// then wealth and tax base zeroed, then this city loop (growth, the rebuild, the loyalty draws), then
/// the nation loop (<see cref="QuarterlyNationEconomySystem"/>, ordered after this one). Neither this
/// system's growth nor its loyalty draws read anything T08's upkeep billing writes, so the only ordering
/// requirement between the two is that this one not run first — an explicit <see cref="QuarterBoundaryHandlerAttribute.Order"/>
/// past T08's default 0 states that rather than leaving it to registration order.
/// </para>
/// <para>
/// <strong>T87 rework round 1 (review B10): one interleaved pass, not growth-then-draws.</strong> A single
/// loop over <see cref="GameState.Cities"/>' own list order computes each city's grown population (reading
/// its owner's <em>current</em>, pre-decay tax rate and mobilization — <see cref="QuarterlyNationEconomySystem"/>
/// decays mobilization later), folds that growth back into the working <see cref="GameState"/>, then
/// immediately applies that same city's own loyalty draws over one shared
/// <see cref="QuarterBoundaryContext.Rng"/> stream and, right after those draws — <c>decompiled-quarterly-rebellion.md</c>'s
/// own code order — runs <see cref="Rebellion.Run"/> for any city whose draws leave it under
/// <see cref="EconomyRules.RebellionLoyaltyThreshold"/> and not a capital, before moving to the next city
/// in that same list order. T89: this replaces the old, unconsumed <c>RebellionRiskDetected</c>
/// publication.
/// </para>
/// <para>
/// <strong>T87 rework round 2 (review R6): wealth and tax base — the reasoning below this remark
/// previously carried was wrong, and this system's own shape is an intentionally named, still-open
/// divergence, not a fix.</strong> <see cref="NationTaxBaseRebuild.Rebuild"/> runs once, after the whole
/// loop, over the <em>final</em> city list — a claim that this "gives the same result as running it after
/// every city" is false whenever a city changes owner mid-loop: the original (<c>FUN_00451b40</c>, dump
/// :54832–54836) credits each city's own <c>population×3000</c> and <c>contribution×4</c> to whichever
/// nation owns it <em>at the moment its own growth runs</em>, right before that same city's own loyalty
/// draws — not to whichever nation ends up owning it once the whole quarter has settled.
/// </para>
/// <para>
/// <strong>Why this is not simply fixed by crediting each city once, at its own growth.</strong> Doing so
/// (with every nation's <c>Wealth</c>/<c>TaxBase</c> zeroed once before the loop, matching the report's own
/// "zeroed" step) was tried and reverted: <see cref="Rebirth.Run"/> defects every qualifying city — not
/// only the one whose own rebellion triggered it — in one call, so a city later in
/// <see cref="GameState.Cities"/>' own list order than the triggering one is moved to the reborn nation
/// <em>before this loop's own iteration ever reaches it</em>, at whatever population it held at that
/// moment (not yet grown this quarter). <see cref="CityOwnershipTaxTransfer.Transfer"/> — the same
/// full-value move <see cref="CityCaptureResolver.Defect"/> already makes for any ownership change, reused
/// here as everywhere else, never a second implementation — moves that (pre-growth) value from the old
/// owner to the reborn one immediately. When this loop's own iteration later reaches that same city and
/// grows it, a second, <em>full</em> credit under the new owner double-counts the city's own contribution;
/// crediting only <em>growth's own delta</em> instead avoids the double count only if every nation's
/// <c>Wealth</c>/<c>TaxBase</c> already correctly reflects its cities' <em>current</em> contribution
/// before this quarter began — true only if some earlier mechanism (a previous quarter's own full rebuild)
/// already established it, which a freshly-loaded <see cref="Model.World"/>'s own author-provided
/// <see cref="Model.NationDefinition.Wealth"/>/<see cref="Model.NationDefinition.TaxBase"/> fields are not
/// guaranteed to (<see cref="Model.GameStateFactory"/> copies them as given, never rebuilding them against
/// city ownership) — exactly the gap a full, zero-then-sum rebuild exists to close. Reconciling a
/// per-city, growth-time credit against <see cref="Rebirth.Run"/>'s own already-merged, multi-city sweep
/// needs either a decompile-level check of how <c>FUN_00451b40</c> and <c>FUN_0044bed8</c>/<c>FUN_0044bb18</c>
/// actually interact when a rebirth moves a city ahead of its own turn (this task's own Owns list does not
/// reach the research repository), or a design decision on which of the two known-imperfect shapes to
/// keep. Left as this system's own pre-existing shape (the final rebuild) rather than a confidently wrong
/// replacement — the user's decision, not invented here.
/// </para>
/// <para>
/// <strong>Why interleaved, not split.</strong> An earlier revision of this system ran two passes — every
/// city's growth, then every city's loyalty draws and rebellions — reasoning that neither draw reads a
/// city's own or any other city's grown population or rebuilt tax base, only its own prior loyalty and its
/// owner's (growth-unaffected) tax rate, so the split looked safe (except for review round 1, N5's own
/// capital-liveness edge, fixed separately by reading each capital live rather than from a set built once
/// before the loop). That reasoning missed one case, found by review round 1, B10: a rebellion or rebirth
/// that eliminates its own old owner disposes of that nation's armies mid-loop
/// (<c>EliminationForces</c>, via <c>CityCaptureResolver.Defect</c>), and in the original a <em>later</em>
/// city's own growth in the same quarter sees those armies already gone from
/// <see cref="HostileArmyAdjacent.IsThreatened"/>'s own check (<c>decompiled-quarterly-rebellion.md</c> §2's
/// own last paragraph, §5 item 5) — a two-pass split grows every city <em>before</em> any rebellion in that
/// same quarter can dispose of an army, so a later city stayed suppressed by a threat that would not exist
/// once the quarter was over. Interleaving growth and draws per city, in list order, matches the original's
/// own per-city loop and removes the divergence: whichever city's own rebellion or rebirth disposes of an
/// army, every city after it in list order sees that army already gone when its own growth runs.
/// <c>QuarterlyCityEconomySystemTests.AnOwnerRebellionEliminatesMidQuarter_ALaterCitysGrowthSeesItsArmyAlreadyGone</c>
/// proves it (through the mechanism already merged before this task, ordinary rebellion-elimination, T89) —
/// proved by mutation: reverting this loop back to the old two-pass split makes that test fail, verified
/// locally and reverted.
/// </para>
/// <para>
/// <strong>Rebirth (T87, <c>#421</c>) introduces the identical divergence, not a new one</strong> — a city
/// defecting to a reborn nation can leave its former still-live owner with zero cities, running
/// <c>EliminationForces</c> mid-loop exactly as an ordinary rebellion-elimination already could before this
/// task — and the interleaved restructuring above fixes it the same way for both. The live capital read
/// itself — the other half of §5 item 5 — is proven directly by
/// <c>QuarterlyCityEconomySystemTests.ARebirthMidQuarter_SetsANewCapital_AndThatCapitalIsNotTreatedAsARebelCandidate</c>,
/// a real rebirth setting a new capital mid-quarter whose own city a stale, pre-rebirth snapshot would
/// have (wrongly) let rebel again in the same quarter (proved by mutation: reverting this method's own
/// capital test to a set built once before the loop makes that test fail, verified locally and reverted).
/// </para>
/// </remarks>
[QuarterBoundaryHandler("economy.quarterly-city-tick", Order = 100)]
public sealed class QuarterlyCityEconomySystem : IQuarterBoundaryHandler
{
    /// <inheritdoc/>
    public GameState OnQuarterBoundary(QuarterBoundaryContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var state = context.State;
        var ruleset = context.Ruleset;
        var economy = ruleset.Economy;

        // T87 rework round 1 (review B10): growth and the loyalty draws are interleaved per city, in
        // city (list) index order, matching the report's own per-city loop (§2's own last paragraph and
        // §5 item 5: "a rebirth moves later cities before their growth and draws, so it breaks the
        // equivalence" the old two-pass split assumed). Snapshotting the loop's own city order up front,
        // before any city in it can be touched: a rebellion/rebirth below only ever changes a city's
        // Owner/Loyalty in place (CityCaptureResolver.Defect never adds, removes or reorders
        // GameState.Cities), so re-fetching each id from the working `state` inside the loop always finds
        // it, and this list stays the correct index order for the whole pass.
        var cityIdsInOrder = new List<string>(state.Cities.Count);
        foreach (var snapshotCity in state.Cities)
        {
            cityIdsInOrder.Add(snapshotCity.Id);
        }

        foreach (var cityId in cityIdsInOrder)
        {
            var city = state.CityById(cityId)!;
            var owner = state.NationById(city.Owner);
            if (owner is null)
            {
                continue;
            }

            var threatened = HostileArmyAdjacent.IsThreatened(city, state, ruleset);
            var grownPopulation = CityPopulationGrowth.Grow(
                city.PopulationThousands,
                city.MaxPopulationThousands,
                owner.TaxRatePercent,
                owner.MobilizedPercent,
                threatened,
                economy);
            var grownCity = city with { PopulationThousands = grownPopulation };
            state = state with { Cities = CityCaptureResolver.ReplaceCity(state.Cities, grownCity) };

            // T87 (#421, N9): read live, not from a set built once before this loop. Rebirth (T87's own
            // Rebellion.cs call) can set a nation's capital partway through this same quarter's loop --
            // decompiled-quarterly-rebellion.md §5, item 5: "the capital test must see a capital that a
            // rebirth set mid-loop", exactly as the original re-reads it every iteration (§1's own "(d):
            // cities(n) and unity(n) are read live"). A set built once before the loop -- this system's
            // own pre-T87 shape -- would miss a capital rebirth had just assigned to a still-live nation,
            // and would keep treating a capital a rebirth just took away from its old owner as one.
            // Review round 1, N3: reuses CapitalOwnership.IsAnyNationsCapital rather than a second,
            // duplicate scan of GameState.Nations.
            var isCapital = CapitalOwnership.IsAnyNationsCapital(state, grownCity.Id);
            var result = CityLoyaltyDraws.Apply(grownCity, owner.TaxRatePercent, isCapital, economy, context.Rng);
            var updatedCity = grownCity with { Loyalty = result.Loyalty };
            state = state with { Cities = CityCaptureResolver.ReplaceCity(state.Cities, updatedCity) };

            if (result.RebellionRisk)
            {
                // Review round 1, N1: Rebellion.Run's own signature grew an IRng parameter for Rebirth's
                // one consumed-and-discarded leader-name draw; this same shared stream, unchanged.
                state = Rebellion.Run(state, context.World, ruleset, updatedCity, context.Events, context.Rng);
            }
        }

        // T87 rework round 2 (review R6): kept as this system's own pre-existing shape, a named divergence
        // -- see this class's own remarks on why a per-city, growth-time credit was tried and reverted.
        // NationTaxBaseRebuild.Rebuild is pure (zero-then-sum over state.Cities' own current owner and
        // population), run once after the loop against the final city list.
        state = NationTaxBaseRebuild.Rebuild(state, ruleset);

        return state;
    }
}
