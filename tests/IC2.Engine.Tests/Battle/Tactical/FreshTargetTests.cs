using System.Text.Json;
using IC2.Engine.Battle.Tactical;
using IC2.Engine.Import;
using IC2.Engine.Model;
using IC2.Engine.Tests.Import;
using Xunit;
using Xunit.Abstractions;

namespace IC2.Engine.Tests.Battle.Tactical.GoldenMaster;

/// <summary>
/// T173: original FUN_00437DE4 leaves word 9 untouched; FUN_00439968 clears word 9
/// for all 20 slots of the moving side, including unused slots. Pinned listing SHA256:
/// 2018c205a4e4ea4a4079f2d166623fb571062832d76e19b72e11d1f0edac87d8.
/// Fresh-process BATTLE01 blocks supply the initial zero on the idle attacker side.
/// </summary>
public sealed class FreshTargetTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> FreshSeries() => GoldenData.Rows(
        BattleFixtures.SkipReason.Length == 0
            ? GoldenData.FixedSeries.Concat(SeriesCatalogue.HookedTrials().Select(t => t.Series.Name))
            : Array.Empty<string>());

    private static SeriesSpec Spec(string name) => GoldenData.FixedSeries.Contains(name)
        ? GoldenData.Named(name) : GoldenData.Trial(name).Series;

    [SkippableTheory]
    [MemberData(nameof(FreshSeries))]
    public void Fresh_targets_match_recorded_occupied_and_unused_slots_then_first_attacker_setup_clears_them(string name)
    {
        Skip.IfNot(BattleFixtures.SkipReason.Length == 0, BattleFixtures.SkipReason);
        var spec = Spec(name);
        var original = BattleFixtures.Block(spec.Files[0]);
        var game = BattleFixtures.Import(spec.Start).Save.State;
        var context = TacticalContext.From(RealGameData.Ruleset);
        var draws = new RecordingDelphiDraws(spec.Seed);
        var fresh = TacticalCopyIn.Start(game,
            OriginalBattleBlockImport.ArmyIdOf(original.AttackerArmy),
            OriginalBattleBlockImport.ArmyIdOf(original.DefenderArmy), context, draws).Battle;

        // The first file is after defender placement, which does not change these targets.
        for (var slot = 0; slot < original.Slots.Count; slot++)
        {
            Assert.Equal(slot < context.SlotsPerSide ? 0 : TacticalSlot.NoTarget, original.Slots[slot].Target);
            Assert.Equal(original.Slots[slot].Target, fresh.Slots[slot].Target);
            Assert.Equal(original.Slots[slot].Troops == 0, fresh.Slots[slot].Troops == 0);
        }
        var drawCount = draws.Draws.Count;
        var attackerSetup = TacticalHalfRound.Setup(fresh with { SideToMove = TacticalBattleState.AttackerSide }, context);
        Assert.All(attackerSetup.Slots, slot => Assert.Equal(TacticalSlot.NoTarget, slot.Target));
        Assert.Equal(drawCount, draws.Draws.Count);

        // Resume runs only setup: preserve arbitrary live AND unused enemy targets, never copy-in zeros.
        var saved = fresh with
        {
            Slots = ValueList.From(fresh.Slots.Select((slot, index) =>
                slot with { Target = index < context.SlotsPerSide ? 7 : 8 })),
        };
        var resumed = TacticalHalfRound.Resume(saved, context);
        Assert.Equal(saved.Counter + 1, resumed.Counter);
        Assert.All(resumed.Slots.Take(context.SlotsPerSide), slot => Assert.Equal(7, slot.Target));
        Assert.All(resumed.Slots.Skip(context.SlotsPerSide), slot => Assert.Equal(TacticalSlot.NoTarget, slot.Target));
        Assert.Equal(drawCount, draws.Draws.Count);
    }

    [SkippableTheory]
    [MemberData(nameof(FreshSeries))]
    public void Fresh_series_first_file_targets_match_and_report_later_first_discrepancy(string name)
    {
        Skip.IfNot(BattleFixtures.SkipReason.Length == 0, BattleFixtures.SkipReason);
        var spec = Spec(name);
        var replay = GoldenReplay.Run(spec);
        Assert.NotEmpty(replay.Snapshots);
        var targetDifferences = BlockComparison.All(replay.Snapshots[0], BattleFixtures.Block(spec.Files[0]))
            .Where(difference => difference.Field == "target").ToArray();
        Assert.Empty(targetDifferences);
        output.WriteLine("T173-SERIES " + JsonSerializer.Serialize(new
        {
            Series = name, Matched = replay.FilesMatched, Total = spec.Files.Count,
            Passed = replay.Passed, Diagnostic = replay.Describe(spec),
        }));
    }
}
