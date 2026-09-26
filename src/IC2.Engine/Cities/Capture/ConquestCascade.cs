using IC2.Engine.Core;
using IC2.Engine.Diplomacy;
using IC2.Engine.Economy;
using IC2.Engine.Model;

namespace IC2.Engine.Cities.Capture;

/// <summary>
/// <c>FUN_0044C528(loser, winner)</c>, :50602 — <c>decompiled-elimination-cleanup.md</c> §4 (research
/// <c>43a44a1</c>), with the neighbour merge from <c>dat-neighbour-mask.md</c> §4. Applies the mass
/// conquest transfer once <see cref="ConquestTrigger.Evaluate"/> says it fires: every city the loser
/// still owns moves to the winner, the winner gains +50 unity and the loser's positive treasury, the
/// relation reset and the neighbour merge both run, the loser's forces are disposed of, the loser is
/// eliminated with its recruitment slots wiped, and the "X conquers Y." news banner is written.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The loyalty draw's own stream.</strong> Neither <see cref="CityCaptureResolver.Capture"/> nor
/// this type receives an <c>IRng</c> — the production call chain
/// (<c>src/IC2.Engine/Battle/Commands/BesiegeCityCommandHandler.cs</c>) is outside this task's Owns list,
/// so threading one through would need an edit there. Every conquest loyalty draw instead derives its own
/// independent stream directly from <see cref="GameState.RandomSeed"/> — the same persisted root value
/// <c>context.Rng</c> was itself derived from for this same command, just under a different stream name
/// (<c>"capture.conquestLoyalty"</c>, further split per city id) — exactly the pattern
/// <see cref="Core.Pipeline.TurnCoordinator"/> and the battle tournament harness already use to get a
/// generator straight from a seed rather than through dependency injection. Two different stream names
/// under the same root seed are independent by construction (<see cref="IRng.ForStream"/>'s own
/// contract), so this cannot perturb or be perturbed by the siege's own battle-resolution draws in the
/// same command. <see cref="RandomSeed"/> itself is never advanced by this — only
/// <c>TurnCoordinator</c>/<c>CommandDispatcher</c> ever touch the persisted root value, so this remains a
/// pure, replayable function of the state it is given.
/// </para>
/// <para>
/// <strong>Two quirks reproduced deliberately, not fixed.</strong> The original copies the loser's
/// treasury to the winner instead of moving it (the loser's own balance is untouched), which creates
/// money out of nothing — reproduced here exactly, not "fixed", per this task's Hazards. The original also
/// never decrements the loser's own city count, wealth or tax base; <c>NationState.Wealth</c> and
/// <c>NationState.TaxBase</c> both exist on this reimplementation's own loser and are left exactly as
/// they stood before conquest by this method — review round 1, N2: an earlier revision of this remark
/// claimed there was "no separate wealth/tax-base field" left stale to reproduce the quirk with, which
/// was false; the fields are real and <see cref="Apply"/> genuinely never writes either one on
/// <paramref name="loserId"/>'s own record, reproducing the original's own quirk rather than merely
/// having nothing to act on. (The engine additionally derives live city <em>counts</em> from ownership
/// rather than a stored counter, so that part of the original's quirk has no analogue here — that half
/// of the original remark stands.)
/// </para>
/// </remarks>
public static class ConquestCascade
{
    private const string LoyaltyRandomStreamName = "capture.conquestLoyalty";

    /// <summary>Applies every effect of a conquest (Scope's numbered list, in order) to <paramref name="state"/>.</summary>
    /// <param name="state">The state after <see cref="ConquestTrigger.Evaluate"/> decided conquest fires.</param>
    /// <param name="ruleset">Every constant this cascade uses.</param>
    /// <param name="loserId">The nation being conquered.</param>
    /// <param name="winnerId">The nation conquering it.</param>
    /// <param name="events">Where this publishes <see cref="NationConquered"/>.</param>
    public static GameState Apply(GameState state, Ruleset ruleset, string loserId, string winnerId, IEventSink events)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ruleset);
        ArgumentException.ThrowIfNullOrWhiteSpace(loserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(winnerId);
        ArgumentNullException.ThrowIfNull(events);

        var rules = ruleset.Capture;
        var economy = ruleset.Economy;
        var loser = state.NationById(loserId)
                    ?? throw new ArgumentException($"'{loserId}' is not a known nation.", nameof(loserId));
        var winner = state.NationById(winnerId)
                     ?? throw new ArgumentException($"'{winnerId}' is not a known nation.", nameof(winnerId));

        // 1. Every loser city -> winner: the loyalty formula (+ one independent Random(6) draw per
        // city) and the winner's own per-city credits. The loser's own city count/wealth/tax base are
        // never decremented -- see this type's own remarks.
        var updatedCities = new List<CityState>(state.Cities.Count);
        foreach (var city in state.Cities)
        {
            if (!string.Equals(city.Owner, loserId, StringComparison.Ordinal))
            {
                updatedCities.Add(city);
                continue;
            }

            var cityRng = SplitMix64Rng.ForStream(state.RandomSeed, LoyaltyRandomStreamName).ForStream(city.Id);
            var bonus = cityRng.NextInt(rules.ConquestLoyaltyRandomBonusMax);
            var loyalty = ConquestLoyalty(city, winnerId, ruleset) + bonus;

            var contribution = CityTaxContribution.Compute(city);
            winner = winner with
            {
                Wealth = winner.Wealth + (city.PopulationThousands * economy.WealthPerPopulationThousand),
                TaxBase = winner.TaxBase + (contribution * economy.TaxBaseContributionMultiplier),
                Treasury = winner.Treasury + (contribution * rules.ConquestTreasuryCreditMultiplier),
            };

            updatedCities.Add(city with { Owner = winnerId, Loyalty = loyalty });
        }

        // 2. Winner unity += 50, capped.
        winner = winner with { Unity = Math.Min(economy.UnityCap, winner.Unity + rules.ConquestWinnerUnityGain) };

        // 3. If the loser's treasury is positive, the winner gains it -- copied, not moved (this type's
        // own remarks on the quirk).
        if (loser.Treasury > 0)
        {
            winner = winner with { Treasury = winner.Treasury + loser.Treasury };
        }

        var stateAfterCitiesAndWinner = state with
        {
            Cities = ValueList.From(updatedCities),
            Nations = CityCaptureResolver.ReplaceNation(state.Nations, winner),
        };

        // 4. Relation reset (T69's helper) and the neighbour merge.
        //
        // Review round 1, N1: this method runs 4, then 7/8, then 5, then 6 -- not the Scope list's own
        // 4, 5, 6, 7/8 order. Left as is rather than reordered, because the reviewer's own read confirms
        // the RESULT is identical either way: steps 4 (relations/neighbours), 5 (forces), 6 (news) and
        // 7/8 (the loser's own final fields) each read and write disjoint pieces of state -- none of the
        // four reads anything the others write -- so this is four independent updates whose relative
        // order cannot be observed from the outside, only their union at the end. Reordering purely for
        // cosmetic Scope-list fidelity would touch working, tested code for no behavioral gain.
        var stateAfterRelations = RelationTransitions.ResetAllOnElimination(stateAfterCitiesAndWinner, ruleset, loserId);
        var stateAfterNeighbours = MergeNeighbours(stateAfterRelations, loserId, winnerId);

        // 7. Loser: conquered-by, unity reset, capital sentinel, eliminated. 8. Every recruitment slot's
        // troops zeroed -- the sparse ValueList<RecruitmentSlot> model's own way of saying "every slot
        // now reads 0 troops" is to hold none at all (NationState.RecruitmentSlots' own doc comment:
        // "Empty slots are simply absent").
        var finalLoser = stateAfterNeighbours.NationById(loserId)!;
        finalLoser = finalLoser with
        {
            Eliminated = true,
            ConqueredBy = winnerId,
            Unity = rules.EliminationUnityReset,
            CapitalCityId = null,
            RecruitmentSlots = ValueList<RecruitmentSlot>.Empty,
        };
        var stateWithFinalLoser = stateAfterNeighbours with
        {
            Nations = CityCaptureResolver.ReplaceNation(stateAfterNeighbours.Nations, finalLoser),
        };

        // 5. Forces disposed (T84's helper); the winner receives any fleet still under construction.
        var stateAfterForces = EliminationForces.Dispose(stateWithFinalLoser, loserId, winnerId);

        // 6. News banner: NewsMessageCatalog.IsWrappedInDashLines("nation.conquered") already wraps this
        // between two dashed lines -- the same catalog entry a plain last-city capture used before T86.
        events.Publish(new NationConquered(winner.Name, loser.Name));

        return stateAfterForces;
    }

    /// <summary>
    /// The conquest's own mass-transfer loyalty formula (<c>decompiled-elimination-cleanup.md</c> §4) --
    /// textually distinct from <see cref="CityCaptureResolver"/>'s single-city capture/defection
    /// formulas, with its own clamp bounds (<see cref="LoyaltyRules.ConquestAllegiantCap"/>/
    /// <see cref="LoyaltyRules.ConquestAllegiantBase"/>/<see cref="LoyaltyRules.ConquestNonAllegiantCap"/>/
    /// <see cref="LoyaltyRules.ConquestNonAllegiantFloor"/>), before the caller adds the per-city
    /// <c>+ Random(6)</c> this method does not itself draw.
    /// </summary>
    private static int ConquestLoyalty(CityState city, string winnerId, Ruleset ruleset)
    {
        var loyalty = ruleset.Loyalty;
        if (string.Equals(city.Allegiance, winnerId, StringComparison.Ordinal))
        {
            return Math.Min(loyalty.ConquestAllegiantCap, loyalty.ConquestAllegiantBase - city.Loyalty);
        }

        var target = loyalty.NonAllegiantTransferBase - city.Loyalty;
        return Math.Min(loyalty.ConquestNonAllegiantCap, Math.Max(loyalty.ConquestNonAllegiantFloor, target));
    }

    /// <summary>
    /// <c>dat-neighbour-mask.md</c> §4: every nation that bordered the loser, except the winner, becomes
    /// a neighbour of the winner, both ways. The loser's own entry is never touched (it still lists its
    /// old neighbours; a later rebirth, T87, would see them again), and nobody ever <em>loses</em> a
    /// neighbour here — the merge only adds bits, in pairs, exactly as the original's own <c>BTS</c>
    /// instructions do.
    /// </summary>
    /// <remarks>
    /// A no-op when <see cref="GameState.Neighbours"/> is <see langword="null"/> — reachable only for a
    /// <see cref="GameState"/> built directly, bypassing every production path, since
    /// <see cref="GameStateFactory"/>, <see cref="IC2.Engine.Import.OriginalSaveImporter"/> and
    /// <see cref="IC2.Engine.Persistence.SaveManager.Load"/> all populate the field for real
    /// (<see cref="GameState.Neighbours"/>'s own remarks); such a state carries no baseline to merge
    /// into, and <see cref="Diplomacy.NeighbourGeography"/>'s own world fallback answers every query
    /// against it instead, for as long as it stays <see langword="null"/> — nothing here or elsewhere
    /// later fills it in on its own. Review round 1, B3 item 3: an earlier revision of this remark
    /// described the pre-T86 old-save case, since fixed by <c>SaveMigrations.MigrateV2ToV3</c>, and
    /// separately claimed "the very next migrated save" would pick up this run's merge, which was never
    /// true of a hand-built state with no save to migrate at all.
    /// </remarks>
    private static GameState MergeNeighbours(GameState state, string loserId, string winnerId)
    {
        if (state.Neighbours is not { } neighbours)
        {
            return state;
        }

        var loserNeighbourIds = FindNeighbourIds(neighbours, loserId);
        var toMerge = new HashSet<string>(StringComparer.Ordinal);
        foreach (var k in loserNeighbourIds)
        {
            if (!string.Equals(k, winnerId, StringComparison.Ordinal))
            {
                toMerge.Add(k);
            }
        }

        if (toMerge.Count == 0)
        {
            return state;
        }

        var updated = new List<NationNeighbours>(neighbours.Count);
        var winnerHasOwnEntry = false;
        foreach (var entry in neighbours)
        {
            if (string.Equals(entry.NationId, winnerId, StringComparison.Ordinal))
            {
                winnerHasOwnEntry = true;
                updated.Add(entry with { NeighbourIds = UnionOrdered(neighbours, entry.NeighbourIds, toMerge) });
            }
            else if (toMerge.Contains(entry.NationId))
            {
                updated.Add(entry with
                {
                    NeighbourIds = UnionOrdered(neighbours, entry.NeighbourIds, SingleSet(winnerId)),
                });
            }
            else
            {
                updated.Add(entry);
            }
        }

        if (!winnerHasOwnEntry)
        {
            updated.Add(new NationNeighbours(winnerId, UnionOrdered(neighbours, ValueList<string>.Empty, toMerge)));
        }

        return state with { Neighbours = ValueList.From(updated) };
    }

    private static ValueList<string> FindNeighbourIds(ValueList<NationNeighbours> list, string nationId)
    {
        foreach (var entry in list)
        {
            if (string.Equals(entry.NationId, nationId, StringComparison.Ordinal))
            {
                return entry.NeighbourIds;
            }
        }

        return ValueList<string>.Empty;
    }

    private static HashSet<string> SingleSet(string value) => new(StringComparer.Ordinal) { value };

    /// <summary>
    /// Orders the union of <paramref name="existing"/> and <paramref name="extra"/> by each id's own
    /// position among <paramref name="list"/>'s entries — the same stable order
    /// <see cref="Diplomacy.NeighbourGeography.NeighboursOf(GameState, Model.World, string)"/> already
    /// uses, never a <see cref="HashSet{T}"/>'s own enumeration order.
    /// </summary>
    private static ValueList<string> UnionOrdered(
        ValueList<NationNeighbours> list, ValueList<string> existing, IReadOnlyCollection<string> extra)
    {
        var union = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in existing)
        {
            union.Add(id);
        }

        foreach (var id in extra)
        {
            union.Add(id);
        }

        var ordered = new List<string>(union.Count);
        foreach (var entry in list)
        {
            if (union.Remove(entry.NationId))
            {
                ordered.Add(entry.NationId);
            }
        }

        // Defensive: an id in the union with no entry of its own in `list` at all -- should not happen,
        // since GameStateFactory/the SAV importer both seed one entry per world nation, but appended in
        // a stable (ordinal) order rather than left to whatever HashSet enumeration order remained.
        if (union.Count > 0)
        {
            var leftovers = new List<string>(union);
            leftovers.Sort(StringComparer.Ordinal);
            ordered.AddRange(leftovers);
        }

        return ValueList.From(ordered);
    }
}
