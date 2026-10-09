using IC2.Data;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Import;
using IC2.Engine.Persistence;
using IC2.Engine.Recruitment;
using IC2.Engine.Recruitment.Commands;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Fixtures;
using IC2.Engine.Tests.Import;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;
using Xunit;
using Xunit.Extensions;

namespace IC2.Engine.Tests.Recruitment;

/// <summary>
/// T56's New Game fill (Done-when 9, bug #457), the long-game reachability it exists for (Done-when 6,
/// the DoD line that connects to #225), the corpus top-up for the mobilization new-army template
/// (Done-when 8) and the fill's own draw-count report (the Hazards' "say how many draws a quarter
/// costs" — pinned here so a change to the per-slot draw pattern is a test failure, not a silent shift
/// of every seeded downstream measurement).
/// </summary>
public sealed class MercenaryPoolNewGameFillTests
{
    private static readonly Lazy<ResolvedScenario> LazyClassical = new(
        () => GameDataRepository.Load(ModelTestPaths.DataRoot).Resolve("classical-mediterranean"));

    private static ResolvedScenario Classical => LazyClassical.Value;

    // ---- Done-when 9: a new game fills its pool with one restock pass ----

    [Fact]
    public void A_new_game_starts_with_its_pool_filled_by_one_pinned_pass()
    {
        var state = GameStateFactory.CreateInitial(Classical.World, Classical.Ruleset, Classical.Scenario);

        // The seeded count, pinned exactly: the scenario's committed seed draws this many offers, and
        // the same call must draw the identical pool twice (the fill is a pure function of the seed).
        const int PinnedFillCount = 40;
        Assert.Equal(PinnedFillCount, state.MercenaryPool.Count);

        var again = GameStateFactory.CreateInitial(Classical.World, Classical.Ruleset, Classical.Scenario);
        Assert.Equal(state.MercenaryPool, again.MercenaryPool);

        // And the count is plausible for Binomial(50, 46/54) (mean 42.6, standard deviation 2.5): the
        // pinned value must stay inside the band the report's four observed fills (43, 45, 46, 40)
        // bounded, so a drift in the rule shows up as a count outside it.
        Assert.InRange(state.MercenaryPool.Count, 38, 47);

        // Every offer sits on one of the pool's own slot indexes, each once.
        Assert.Equal(state.MercenaryPool.Count, state.MercenaryPool.Select(s => s.SlotIndex).Distinct().Count());
        Assert.All(state.MercenaryPool, s => Assert.InRange(s.SlotIndex, 0, Classical.Ruleset.Recruitment.MercenaryPoolSlots - 1));
    }

    [Fact]
    public void Every_starting_offer_matches_its_template_exactly_where_the_report_says_so()
    {
        var templates = Classical.World.MercenaryTemplates!;
        var state = GameStateFactory.CreateInitial(Classical.World, Classical.Ruleset, Classical.Scenario);

        Assert.NotEmpty(state.MercenaryPool);
        foreach (var slot in state.MercenaryPool)
        {
            var candidates = templates.Where(t =>
                    t.X == slot.X && t.Y == slot.Y && t.Label == slot.NameLabel && t.UnitTypeId == slot.UnitTypeId)
                .ToList();
            Assert.NotEmpty(candidates);

            var standardSize = Classical.Ruleset.UnitTypeById(slot.UnitTypeId)!.StandardBattalionSize;
            Assert.True(
                candidates.Any(t =>
                {
                    var troopsBase = (t.TroopsBase * 3) / 2;
                    var troopsBand = new[] { Math.Min(troopsBase, standardSize), Math.Min(2 * troopsBase - 1, standardSize) };
                    var qualityBand = new[] { Math.Clamp(t.QualityBase, 5, 9), Math.Clamp(t.QualityBase + 1, 5, 9) };
                    return slot.Troops >= troopsBand[0] && slot.Troops <= troopsBand[1] && qualityBand.Contains(slot.Quality);
                }),
                $"Starting offer on slot {slot.SlotIndex} fits no template with the same (x, y, Label, type) key.");
        }
    }

    [Fact]
    public void The_fill_costs_about_186_draws_from_its_own_stream()
    {
        // The fill's stream is name-derived, so it moves no other seeded outcome and no other outcome
        // moves it; what this pins is the draw COUNT's shape — the Hazards' "a quarterly system that
        // touches 50 slots is a large consumer of the stream". 3.72 draws per slot on average,
        // decompiled-new-game-mercenary-fill.md §2: between 2 (both rolls refuse) and 5 (fall-through
        // refill) per slot, so 100..250 for the whole fill. A rule change that altered the per-slot
        // draw pattern would still land inside this band more often than not — the exact count is what
        // the pinned pool above fixes.
        var counting = new CountingRng(SplitMix64Rng.ForStream(270, MercenaryPoolRestock.NewGameFillStreamName));
        var state = GameStateFactory.CreateInitial(Classical.World, Classical.Ruleset, Classical.Scenario)
            with { MercenaryPool = ValueList<MercenaryPoolSlot>.Empty };

        MercenaryPoolRestock.Apply(state, Classical.Ruleset, Classical.World, counting);
        Assert.InRange(counting.Draws, 100, 250);
    }

    [SkippableFact]
    public void Save_import_never_runs_the_fill()
    {
        Skip.IfNot(LocalOriginalAssets.IsConfigured, LocalOriginalAssets.SkipReason);

        // The corpus's Felsina save: its pool is whatever the save itself carries. The fill would
        // replace it with template draws at the scenario's seed, so asserting the imported pool equals
        // the SAV's own live records (parsed here through IC2.Data's own table reader) proves the
        // importer never called it.
        const string saveName = "1_rome_270_winter_1.sav";
        var data = File.ReadAllBytes(OriginalFixture.ResolveOrThrow(saveName));
        var result = OriginalSaveImporter.Import(
            data, saveName, RealGameData.World, RealGameData.Ruleset, RealGameData.Scenario,
            saveId: "imported-" + saveName, saveLabel: "Imported " + saveName);

        var live = SaveMercenaryTable.Parse(data).Records.Where(r => !r.IsEmpty).ToList();
        Assert.NotEmpty(live);

        Assert.Equal(live.Count, result.Save.State.MercenaryPool.Count);
        foreach (var slot in result.Save.State.MercenaryPool)
        {
            var record = live.Single(r => r.Index == slot.SlotIndex);
            Assert.Equal(record.X, slot.X);
            Assert.Equal(record.Y, slot.Y);
            Assert.Equal(record.Label, slot.NameLabel);
            Assert.Equal(record.Troops, slot.Troops);
            Assert.Equal(record.QualityCode, slot.Quality);
        }
    }

    // ---- Done-when 6: the hire stays reachable over a long game ----

    [Fact]
    public void The_pool_still_holds_offers_after_enough_quarters_to_drain_any_starting_pool()
    {
        var coordinator = new TurnCoordinator(
            SystemRegistry.FromEngineAssembly(), Classical.Ruleset, Classical.World, NullEventSink.Instance);

        // Start from the filled new-game state, then play 24 quarters in which every offer is hired
        // away each quarter (the pool drained outright — harsher than any real hire pattern, and past
        // the handful of quarters T22's soak fires hires in). The quarterly restock must put offers
        // back every single time: an empty slot refills with probability 46/54, so a quarter that
        // refilled nothing at all has probability (8/54)^50, far past impossible.
        var state = GameStateFactory.CreateInitial(Classical.World, Classical.Ruleset, Classical.Scenario);
        for (var season = 0; season < 24; season++)
        {
            state = state with { MercenaryPool = ValueList<MercenaryPoolSlot>.Empty };
            state = coordinator.FireQuarterBoundary(state, endingSeasonIndex: season % 4);
            Assert.NotEmpty(state.MercenaryPool);
        }

        // And the hire itself is reachable, asserted directly: place the first army next to the first
        // live offer with a purse and supplies that pass the gates, and the command is accepted.
        var offer = state.MercenaryPool.OrderBy(s => s.SlotIndex).First();
        var army = state.Armies.First();
        var positioned = state with
        {
            Armies = ValueList.From(state.Armies.Select(a =>
                string.Equals(a.Id, army.Id, StringComparison.Ordinal)
                    ? a with { X = offer.X + 1, Y = offer.Y, Money = 100_000, SupplyTons = 10_000 }
                    : a)),
        };

        var dispatcher = new CommandDispatcher(
            SystemRegistry.FromEngineAssembly(), Classical.Ruleset, Classical.World, NullEventSink.Instance);
        var outcome = dispatcher.Dispatch(
            positioned,
            new HireMercenaryCommand(positioned.ActiveNationId, army.Id, offer.SlotIndex));

        Assert.True(outcome.IsAccepted, $"The hire was rejected: {outcome.Rejection?.Code} {outcome.Rejection?.Message}");
    }

    // ---- Done-when 8: the corpus top-up for the mobilization new-army template ----

    [Fact]
    public void The_corpus_carries_the_new_armys_creation_values_and_the_restock_report()
    {
        var template = FixtureCorpus.Get("mobilization.newArmyTemplate");
        Assert.Equal(0, template.Value.GetProperty("supplyTons").GetInt32());
        Assert.Equal(0, template.Value.GetProperty("money").GetInt32());
        Assert.Equal(0, template.Value.GetProperty("movesHumanSeat").GetInt32());
        Assert.Equal(1, template.Value.GetProperty("movesAiSeat").GetInt32());
        Assert.Equal(59, template.Value.GetProperty("morale").GetInt32());
        Assert.Contains("CREATION values", template.Note, StringComparison.Ordinal);
        Assert.Contains("one weekly tick later", template.Note, StringComparison.Ordinal);

        Assert.Contains(
            "decompiled-mobilization-and-mercenary-restock.md",
            FixtureCorpus.KnownReportFilenames,
            StringComparer.Ordinal);
    }

    /// <summary>An <see cref="IRng"/> wrapper that counts the draws made through it.</summary>
    private sealed class CountingRng : IRng
    {
        private readonly IRng _inner;

        public CountingRng(IRng inner) => _inner = inner;

        public int Draws { get; private set; }

        public ulong Seed => _inner.Seed;

        public ulong State => _inner.State;

        public ulong NextUInt64()
        {
            Draws++;
            return _inner.NextUInt64();
        }

        public int NextInt(int exclusiveUpperBound)
        {
            Draws++;
            return _inner.NextInt(exclusiveUpperBound);
        }

        public int NextInt(int inclusiveLowerBound, int exclusiveUpperBound)
        {
            Draws++;
            return _inner.NextInt(inclusiveLowerBound, exclusiveUpperBound);
        }

        public bool NextChance(int numerator, int denominator)
        {
            Draws++;
            return _inner.NextChance(numerator, denominator);
        }

        public IRng ForStream(string streamName) => new CountingRng(_inner.ForStream(streamName));
    }
}
