using System.Reflection;
using IC2.Data;
using IC2.Engine.Battle.Tactical;
using IC2.Engine.Battle.Tactical.General;
using IC2.Engine.Core;
using IC2.Engine.Import;
using IC2.Engine.Model;
using IC2.Engine.News;
using IC2.Engine.Presentation;
using IC2.Engine.Tests.Import;

namespace IC2.Engine.Tests.Battle.Tactical.GoldenMaster;

/// <summary>One draw the port took through its seam: the bound, the value, and the half-round counter it fell in.</summary>
internal sealed record PortDraw(int Bound, int Value, int Counter);

/// <summary>
/// The original's own generator (<see cref="DelphiBattleDraws"/>) with every draw recorded, so a series' draws can be
/// compared with the hook's log. The counter is the half-round the replay is in, set by the replay before each
/// operation; it is bookkeeping for the report, never read by a rule.
/// </summary>
internal sealed class RecordingDelphiDraws : IBattleDraws
{
    private readonly DelphiBattleDraws _inner;

    public RecordingDelphiDraws(uint seed) => _inner = new DelphiBattleDraws(seed);

    public List<PortDraw> Draws { get; } = new();

    public int Counter { get; set; }

    public int Random(int n)
    {
        var value = _inner.Random(n);
        Draws.Add(new PortDraw(n, value, Counter));
        return value;
    }
}

/// <summary>How a series starts.</summary>
internal enum ReplayStart
{
    /// <summary>The attack: copy-in from the strategic start save, then the defender's placement.</summary>
    Fresh,

    /// <summary>A resume: the start save's own block, reseeded, run through <see cref="TacticalHalfRound.Resume"/>.</summary>
    Resume,
}

/// <summary>One series to replay.</summary>
/// <param name="Name">The series' name for reports.</param>
/// <param name="Start">The start save, relative to <c>battles/</c>.</param>
/// <param name="Seed">The lab's baked <c>RandSeed</c>.</param>
/// <param name="Files">The <c>BATTLEnn</c> files in order, relative to <c>battles/</c>.</param>
/// <param name="Post">The post-battle save, or <see langword="null"/>.</param>
/// <param name="Mode">Fresh or resume.</param>
internal sealed record SeriesSpec(
    string Name, string Start, uint Seed, IReadOnlyList<string> Files, string? Post, ReplayStart Mode = ReplayStart.Fresh);

/// <summary>What replaying a series produced.</summary>
internal sealed class ReplayOutcome
{
    /// <summary>The first file whose block differs, with its difference, or <see langword="null"/>.</summary>
    public (int Index, string File, BlockDifference Difference)? FirstBlockDifference { get; set; }

    /// <summary>A structural failure (the port's battle ended early or late), or <see langword="null"/>.</summary>
    public string? Structural { get; set; }

    /// <summary>How many files matched.</summary>
    public int FilesMatched { get; set; }

    /// <summary>The port's state at each file's snapshot (after the side's moves, before its melee), as far as the replay got.</summary>
    public List<TacticalBattleState> Snapshots { get; } = new();

    /// <summary>The final battle state.</summary>
    public TacticalBattleState? Final { get; set; }

    /// <summary>The game after copy-in's writes (the strategic state the write-back applies to).</summary>
    public GameState? Game { get; set; }

    /// <summary>The draws taken through the whole replay.</summary>
    public RecordingDelphiDraws Draws { get; set; } = new(1);

    /// <summary>The port's battle context.</summary>
    public TacticalContext Context { get; set; } = TacticalContext.From(RealGameData.Ruleset);

    /// <summary>The port's conclusion (write-back, promotions, peace) once the replayed battle is over.</summary>
    public GoldenReplay.Conclusion? Conclusion { get; set; }

    /// <summary>The first difference between the port's post-battle strategic state and the post save, or <see langword="null"/>.</summary>
    public string? PostDifference { get; set; }

    /// <summary>Whether every block matched and the battle ended where the series does.</summary>
    public bool BlocksPassed => Structural is null && FirstBlockDifference is null;

    /// <summary>Whether the blocks matched and the post-battle fields equal the post save's.</summary>
    public bool Passed => BlocksPassed && PostDifference is null;

    /// <summary>The report line of a failure: the first differing field, or the structural reason.</summary>
    public string Describe(SeriesSpec spec) =>
        Structural is not null
            ? $"{spec.Name}: {Structural}"
            : FirstBlockDifference is { } f
                ? $"{spec.Name}: {f.File} (file {f.Index + 1} of {spec.Files.Count}), {f.Difference}"
                : PostDifference is not null
                    ? $"{spec.Name}: post-battle fields differ: {PostDifference}"
                    : $"{spec.Name}: replays exactly ({spec.Files.Count} files)";
}

/// <summary>
/// Replays an original series with the port: T123's copy-in (or <c>Resume</c>), T124's general on both sides and
/// T123's half-rounds under the original's generator, comparing the port's block with each file at the point where
/// the lab writes it ("after a side's moves and before its melee", the call to the half-round end).
/// </summary>
internal static class GoldenReplay
{
    /// <summary>
    /// Both sides are played by the computer general from the start: Rome is a human nation under
    /// <em>Computer general</em>, Gaul a computer nation (the task's "Who is computer-controlled"). Which side is a
    /// computer <em>nation</em> comes from the imported game, never from here.
    /// </summary>
    private static readonly bool[] BothGenerals = { true, true };

    /// <summary>
    /// Replays <paramref name="spec"/>. With <paramref name="compareBlocks"/> the port's block is compared with every
    /// file and the replay stops at the first difference; without it the port plays its battle to the end on its own
    /// (the files supply only the two armies' names), so the draws can be read before any block is.
    /// </summary>
    /// <param name="spec">The series.</param>
    /// <param name="compareBlocks">Whether to compare blocks.</param>
    /// <param name="start">For a resume, how to turn the start save's block and game into the battle to resume; by default the block imported as it is.</param>
    public static ReplayOutcome Run(
        SeriesSpec spec,
        bool compareBlocks = true,
        Func<SaveBattleBlock, GameState, TacticalBattleState>? start = null)
    {
        var outcome = new ReplayOutcome();
        var blocks = spec.Files.Select(BattleFixtures.Block).ToList();
        var imported = BattleFixtures.Import(spec.Start);
        var context = TacticalContext.From(RealGameData.Ruleset);
        outcome.Context = context;
        var draws = new RecordingDelphiDraws(spec.Seed);
        outcome.Draws = draws;

        TacticalBattleState state;
        GameState game;
        if (spec.Mode == ReplayStart.Fresh)
        {
            var first = blocks[0];
            var begun = TacticalCopyIn.Start(
                imported.Save.State,
                OriginalBattleBlockImport.ArmyIdOf(first.AttackerArmy),
                OriginalBattleBlockImport.ArmyIdOf(first.DefenderArmy),
                context,
                draws);
            game = begun.Game;
            state = begun.Battle with { ComputerGeneral = ValueList.From(BothGenerals) };
        }
        else
        {
            game = imported.Save.State;
            var block = BattleFixtures.Block(spec.Start);
            state = start is null
                ? OriginalBattleBlockImport.Import(block, game, BothGenerals)
                : start(block, game);
            draws.Counter = state.Counter;
            state = TacticalHalfRound.Resume(state, context);
        }

        outcome.Game = game;
        var general = new ComputerGeneral();
        var limit = compareBlocks ? spec.Files.Count : (spec.Files.Count * 4) + 100;
        for (var i = 0; i < limit; i++)
        {
            if (state.IsOver)
            {
                if (compareBlocks)
                {
                    outcome.Structural = $"the port's battle was over before {spec.Files[i]} (file {i + 1} of {spec.Files.Count})";
                }

                break;
            }

            draws.Counter = state.Counter;
            state = state.Placed
                ? general.Move(state, context, draws)
                : general.Place(state, context, draws);

            outcome.Snapshots.Add(state);
            if (compareBlocks)
            {
                var diff = BlockComparison.First(state, blocks[i]);
                if (diff is not null)
                {
                    outcome.FirstBlockDifference = (i, spec.Files[i], diff);
                    outcome.Final = state;
                    return outcome;
                }

                outcome.FilesMatched++;
            }

            state = TacticalHalfRound.End(state, context, draws);
        }

        if (!state.IsOver && outcome.Structural is null)
        {
            outcome.Structural = compareBlocks
                ? $"the port's battle is still running after the last file ({spec.Files.Count} files)"
                : $"the port's battle did not end within {limit} half-rounds";
        }

        outcome.Final = state;
        if (state.IsOver && outcome.Structural is null)
        {
            outcome.Conclusion = Conclude(outcome, draws);
            if (compareBlocks && spec.Post is not null && outcome.Conclusion.Result.State is not null)
            {
                outcome.PostDifference = PostBattle.FirstDifference(
                    outcome.Conclusion.GameWithNews, BattleFixtures.Import(spec.Post).Save.State);
            }
        }

        return outcome;
    }

    /// <summary>What the port's own conclusion produced after a replayed battle.</summary>
    internal sealed record Conclusion(TacticalBattleConclusion Result, GameState GameWithNews);

    /// <summary>
    /// Runs the port's real <see cref="TacticalBattleSession.Conclude"/> (the write-back, the promotions' draws and the
    /// peace dialog's gate and draw) on the replayed, ended battle, with the draws going through
    /// <paramref name="draws"/>, the replay's Delphi generator.
    /// </summary>
    /// <remarks>
    /// The session builds its own <see cref="IBattleDraws"/> from a <see cref="SplitMix64Rng"/> and offers no seam to
    /// replace it (a finding for the PR), so the test builds the session through its private constructor and sets its
    /// private draw field by reflection: the code under test is the production <c>Conclude</c>, unmodified.
    /// </remarks>
    public static Conclusion Conclude(ReplayOutcome replay, RecordingDelphiDraws draws)
    {
        var battle = replay.Final ?? throw new InvalidOperationException("The replay produced no final battle.");
        var game = replay.Game!;
        var session = NewSession(replay.Context, battle, game);
        typeof(TacticalBattleSession).GetField("_draws", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(session, draws);

        draws.Counter = battle.Counter;
        var result = session.Conclude(game, RealGameData.Ruleset);
        var withNews = result.State is null
            ? game
            : NewsLogWriter.Append(result.State, result.Events, RealGameData.Ruleset.NewsLog);
        return new Conclusion(result, withNews);
    }

    /// <summary>A <see cref="TacticalBattleSession"/> holding <paramref name="battle"/>, built through its private constructor.</summary>
    public static TacticalBattleSession NewSession(TacticalContext context, TacticalBattleState battle, GameState game)
    {
        var attackerNation = game.ArmyById(battle.AttackerArmyId)!.Nation;
        var defenderNation = game.ArmyById(battle.DefenderArmyId)!.Nation;
        var constructor = typeof(TacticalBattleSession)
            .GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Single();
        return (TacticalBattleSession)constructor.Invoke(
            new object[] { context, battle, new SplitMix64Rng(0), attackerNation, defenderNation });
    }
}
