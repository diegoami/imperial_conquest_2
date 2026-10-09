using IC2.Data;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Import;
using IC2.Engine.Persistence;
using IC2.Engine.Presentation;
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

    /// <summary>
    /// Rework round 1 (R1): the fill is no longer inside <see cref="GameStateFactory.CreateInitial"/>
    /// (which commits only the scenario's seed), but one call after the game's effective seed is set —
    /// the same placement the New Game callers use. This helper mirrors that: commit the seed, then fill.
    /// </summary>
    private static GameState NewGame(ulong seed) =>
        MercenaryPoolRestock.FillNewGamePool(
            GameStateFactory.CreateInitial(Classical.World, Classical.Ruleset, Classical.Scenario)
                with { RandomSeed = seed },
            Classical.Ruleset,
            Classical.World);

    // ---- Done-when 9: a new game fills its pool with one restock pass ----

    [Fact]
    public void A_new_game_starts_with_its_pool_filled_by_one_pinned_pass()
    {
        var state = NewGame(Classical.Scenario.RandomSeed);

        // The seeded count, pinned exactly: the scenario's committed seed draws this many offers, and
        // the same call must draw the identical pool twice (the fill is a pure function of the seed).
        const int PinnedFillCount = 40;
        Assert.Equal(PinnedFillCount, state.MercenaryPool.Count);

        var again = NewGame(Classical.Scenario.RandomSeed);
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
        var state = NewGame(Classical.Scenario.RandomSeed);

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
    public void The_fill_costs_exactly_180_draws_from_its_own_stream_at_the_scenario_seed()
    {
        // Rework round 1 (R2): the count is pinned EXACTLY, not banded. The previous 100..250 band was
        // 50x the per-slot minimum and maximum (2..5) and held for any per-slot draw pattern, so a
        // deleted draw left it green (proved by mutation in review). The pinned number is the fill's
        // own stream at the scenario's committed seed 270 over the 50 empty slots:
        // - a first-roll refill (roll <= 4): 1 roll + 3 refill draws = 4 draws;
        // - a fall-through refill (roll 5, replace roll >= 8): 2 rolls + 3 refill draws = 5 draws;
        // - a refused empty slot: 2 rolls (5, then a replace roll below 8).
        // Deleting or adding any single draw anywhere in the pass moves this count and fails here.
        const int PinnedDrawCount = 180;
        var counting = new CountingRng(
            SplitMix64Rng.ForStream(Classical.Scenario.RandomSeed, MercenaryPoolRestock.NewGameFillStreamName));
        var state = GameStateFactory.CreateInitial(Classical.World, Classical.Ruleset, Classical.Scenario);
        Assert.Empty(state.MercenaryPool);

        var filled = MercenaryPoolRestock.Apply(state, Classical.Ruleset, Classical.World, counting);
        Assert.Equal(PinnedDrawCount, counting.Draws);

        // And the pinned count is consistent with the pool it produced: 40 offers drawn and all 40
        // refills came on the first roll (no fall-through refills at this seed), so
        // 40 first-roll refills x 4 draws + 10 refused slots x 2 draws = 180 exactly.
        Assert.Equal(40, filled.MercenaryPool.Count);
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

    // ---- Rework round 1 (R1): the game's own seed reaches the fill ----

    [Fact]
    public void Two_different_seeds_draw_two_different_starting_pools_through_GameSession()
    {
        // R1: the fill used to run inside GameStateFactory.CreateInitial, which commits only the
        // scenario's seed, so every seedOverride got the scenario-seed pool. It now runs in the New
        // Game constructor after the override (next to NewGameLeaders.Apply), and must draw from the
        // seed the session was actually given.
        var at3 = new GameSession(
            Classical.World, Classical.Ruleset, Classical.Scenario, seedOverride: 3, humanSeatNationId: "macedonia");
        var at4 = new GameSession(
            Classical.World, Classical.Ruleset, Classical.Scenario, seedOverride: 4, humanSeatNationId: "macedonia");

        Assert.NotEmpty(at3.State.MercenaryPool);
        Assert.NotEmpty(at4.State.MercenaryPool);

        // Two seeds, two different pools — and neither is the scenario's committed-seed pool. (The
        // session's pool is not byte-equal to a raw FillNewGamePool at the same seed: constructing a
        // session with a human seat advances the game to that seat's first turn, which can itself
        // cross a quarter boundary and restock once more, so the comparison here is between two
        // sessions constructed identically but for the seed — the only thing that can differ is what
        // the fill drew.)
        Assert.NotEqual(at3.State.MercenaryPool, at4.State.MercenaryPool);
        var at270 = NewGame(Classical.Scenario.RandomSeed).MercenaryPool;
        Assert.NotEqual(at270, at3.State.MercenaryPool);
        Assert.NotEqual(at270, at4.State.MercenaryPool);
    }

    [Fact]
    public void The_CLIs_seed_option_reaches_the_fill()
    {
        // R1: the CLI's --seed is the observable the review named. This runs the real CLI (the same
        // dotnet run --project src/IC2.Cli the goldens are regenerated with) twice on a one-command
        // script: hire from pool slot 11 with macedonia's starting army. Slot 11 is filled by the
        // fill at seed 4 and left empty at seed 3 (both pinned by the pool computation above), so the
        // transcripts' hire line differs between the two seeds — "has no offer to hire" at 3 and the
        // position-gate rejection at 4 — which is only possible if --seed reached the fill.
        const ulong FilledSeed = 4;
        const ulong EmptySeed = 3;
        const int Slot = 11;

        var cliDll = Path.Combine(
            ModelTestPaths.RepositoryRoot, "src", "IC2.Cli", "bin", "Debug", "net10.0", "IC2.Cli.dll");
        if (!File.Exists(cliDll))
        {
            // The same skip CliProcessTests uses: a narrow test invocation that never built IC2.Cli
            // cannot spawn it, and dotnet test IC2.sln always builds it first.
            return;
        }

        var scriptPath = Path.Combine(Path.GetTempPath(), "ic2-t56-seed-reaches-fill.txt");
        File.WriteAllText(scriptPath, $"hire-mercenary army-8 {Slot}{Environment.NewLine}quit{Environment.NewLine}");

        var atFilled = RunCli(cliDll, scriptPath, FilledSeed);
        var atEmpty = RunCli(cliDll, scriptPath, EmptySeed);

        var noOffer = $"Mercenary pool slot {Slot} has no offer to hire.";
        Assert.DoesNotContain(noOffer, atFilled, StringComparison.Ordinal);
        Assert.Contains(noOffer, atEmpty, StringComparison.Ordinal);
    }

    /// <summary>
    /// Spawns the real CLI on <paramref name="scriptPath"/> with <paramref name="seed"/> — the built
    /// <c>IC2.Cli.dll</c> the solution build already produced, the same seam
    /// <c>CliProcessTests</c> spawns (Program.cs's own handling runs before a GameSession exists, so
    /// only the real executable proves the CLI's --seed reached the fill).
    /// </summary>
    private static string RunCli(string cliDll, string scriptPath, ulong seed)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(
            "dotnet",
            string.Join(
                ' ',
                $"\"{cliDll}\"",
                "--scenario classical-mediterranean",
                $"--script \"{scriptPath}\"",
                "--seat macedonia",
                $"--seed {seed}"))
        {
            WorkingDirectory = ModelTestPaths.RepositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = System.Diagnostics.Process.Start(psi)
                             ?? throw new InvalidOperationException("Failed to start the CLI.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WaitAll(stdoutTask, stderrTask);

        Assert.Equal(0, process.ExitCode);
        return stdoutTask.Result;
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
        var state = NewGame(Classical.Scenario.RandomSeed);
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
