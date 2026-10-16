using System.Security.Cryptography;
using IC2.Data;
using IC2.Engine.Battle.Tactical;
using IC2.Engine.Import;
using IC2.Engine.Model;
using IC2.Engine.Persistence;
using IC2.Engine.Presentation;
using IC2.Engine.Tests.Import;
using Xunit;

namespace IC2.Engine.Tests.Battle.Tactical.GoldenMaster;

/// <summary>
/// T129 Done-when 3, the resume (check 11). <c>resume05_gate2_a_r1</c>'s ten files equal the port's resume of
/// <c>gate2_a_BATTLE05</c> under seed 1, and none equals the uninterrupted <c>gate2_a</c> tail, as the probe found. The same
/// holds through a clone save: the block, imported, saved by T125's mid-battle save, reloaded and resumed under seed 1,
/// gives the same ten files (the user's decision of 2026-10-04 on #496: a classical reload resumes like the original).
/// </summary>
public sealed class ResumeTests
{
    private const int TailFirst = 6;
    private const int TailLast = 15;

    [SkippableFact]
    public void The_resume_gives_the_ten_files_the_original_wrote()
    {
        Skip.IfNot(BattleFixtures.SkipReason.Length == 0, BattleFixtures.SkipReason);

        var spec = SeriesCatalogue.Resume05();
        Assert.Equal(10, spec.Files.Count);

        var outcome = GoldenReplay.Run(spec);

        KnownFailures.Check(nameof(The_resume_gives_the_ten_files_the_original_wrote), () =>
        {
            KnownFailures.Require(outcome.BlocksPassed, outcome.Describe(spec));
        });
    }

    [SkippableFact]
    public void No_resumed_file_equals_the_uninterrupted_tail()
    {
        Skip.IfNot(BattleFixtures.SkipReason.Length == 0, BattleFixtures.SkipReason);

        var spec = SeriesCatalogue.Resume05();
        var tail = Enumerable.Range(TailFirst, TailLast - TailFirst + 1).Select(n => $"probe/gate2_a_BATTLE{n:00}.SAV").ToList();

        // The original's own files: no resumed file is byte-equal to a file of the uninterrupted tail.
        var tailHashes = tail.Select(f => Convert.ToHexString(SHA256.HashData(BattleFixtures.Read(f)))).ToHashSet();
        foreach (var file in spec.Files)
        {
            Assert.DoesNotContain(Convert.ToHexString(SHA256.HashData(BattleFixtures.Read(file))), tailHashes);
        }

        // The port's resume: none of its blocks equals a block of the uninterrupted tail.
        var outcome = GoldenReplay.Run(spec);
        KnownFailures.Check(nameof(No_resumed_file_equals_the_uninterrupted_tail), () =>
        {
            KnownFailures.Require(outcome.BlocksPassed, outcome.Describe(spec));
            var tailBlocks = tail.Select(BattleFixtures.Block).ToList();
            for (var i = 0; i < outcome.Snapshots.Count; i++)
            {
                for (var t = 0; t < tailBlocks.Count; t++)
                {
                    KnownFailures.Require(
                        BlockComparison.First(outcome.Snapshots[i], tailBlocks[t]) is not null,
                        $"resumed file {i + 1} equals the uninterrupted file {tail[t]}");
                }
            }
        });
    }

    [SkippableFact]
    public void The_resume_through_a_clone_save_gives_the_same_ten_files()
    {
        Skip.IfNot(BattleFixtures.SkipReason.Length == 0, BattleFixtures.SkipReason);

        var spec = SeriesCatalogue.Resume05();
        var outcome = GoldenReplay.Run(spec, start: ThroughACloneSave(spec));

        KnownFailures.Check(nameof(The_resume_through_a_clone_save_gives_the_same_ten_files), () =>
        {
            KnownFailures.Require(outcome.BlocksPassed, outcome.Describe(spec));
        });
    }

    /// <summary>
    /// The block, imported, saved by T125's mid-battle save (<see cref="TacticalBattleSession.ToSaved"/> into a
    /// <see cref="SaveGame"/> written to disk), loaded again and rebuilt as the battle to resume.
    /// </summary>
    private static Func<SaveBattleBlock, GameState, TacticalBattleState> ThroughACloneSave(SeriesSpec spec) =>
        (block, game) =>
        {
            // Fresh-process replay input: serialize these stated off flags, not flags inferred from the original block.
            var state = OriginalBattleBlockImport.Import(block, game, new[] { false, false });
            var session = GoldenReplay.NewSession(TacticalContext.From(RealGameData.Ruleset), state, game);
            var save = BattleFixtures.Import(spec.Start).Save with { State = game, Battle = session.ToSaved() };

            var path = Path.Combine(Path.GetTempPath(), "ic2-t129-clone-" + Guid.NewGuid().ToString("N") + ".sav.json");
            try
            {
                SaveManager.WriteFile(path, save);
                var loaded = SaveManager.LoadFile(path, RealGameData.World, RealGameData.Ruleset);
                Assert.NotNull(loaded.Battle);
                Assert.Equal(game, loaded.State);
                return TacticalBattleSession.FromSaved(loaded.Battle!);
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        };
}
