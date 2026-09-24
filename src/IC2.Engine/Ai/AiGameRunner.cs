using System.Globalization;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Victory;
using static IC2.Engine.Ai.AiFormat;

namespace IC2.Engine.Ai;

/// <summary>How a run of <see cref="AiGameRunner"/> finished.</summary>
public enum AiGameEnding
{
    /// <summary>A nation met the victory condition.</summary>
    Won,

    /// <summary>The condition's own hard limit was reached with no winner (<see cref="VictoryStatus.Expired"/>).</summary>
    Expired,

    /// <summary>
    /// The turn cap was reached with the game still undecided. <strong>This is a pass, not a failure</strong>
    /// — <c>docs/task-catalogue.md</c> T22 Done-when 2: "<em>a seat that hits the turn cap without a
    /// victory passes… the soak proves the engine survives 50 divergent games, not that the AI wins
    /// them.</em>"
    /// </summary>
    TurnCapReached,
}

/// <summary>Everything one seeded game produced.</summary>
/// <param name="Seed">The seed the game was run under. A failing run is reproduced from this alone.</param>
/// <param name="Ending">How it finished.</param>
/// <param name="WinningNationId">The winner, when <see cref="Ending"/> is <see cref="AiGameEnding.Won"/>.</param>
/// <param name="TurnsPlayed">How many seat turns ran.</param>
/// <param name="CommandsIssued">Commands dispatched by AI seats across the whole game.</param>
/// <param name="CommandsRejected">How many of them were refused. Required to be zero.</param>
/// <param name="ProjectionMismatches">Attacks abandoned after their declaration landed. Required to be zero.</param>
/// <param name="TurnsHittingActionCap">AI turns that stopped at <see cref="AiWeights.MaxActionsPerTurn"/>.</param>
/// <param name="CommandlessTurns">AI turns that issued nothing at all.</param>
/// <param name="LongestStallRun">
/// The longest run of consecutive turns that both issued no command and left
/// <see cref="AiSubstantiveState"/> unchanged. Two or more is the Done-when 1 stall failure.
/// </param>
/// <param name="FinalState">The state the game ended in.</param>
/// <param name="Transcript">The per-seed log — the Done-when 4 artifact.</param>
public sealed record AiGameResult(
    ulong Seed,
    AiGameEnding Ending,
    string? WinningNationId,
    int TurnsPlayed,
    int CommandsIssued,
    int CommandsRejected,
    int ProjectionMismatches,
    int TurnsHittingActionCap,
    int CommandlessTurns,
    int LongestStallRun,
    GameState FinalState,
    IReadOnlyList<string> Transcript)
{
    /// <summary>A one-line summary, for a soak's own report.</summary>
    public string Summary() => string.Format(
        CultureInfo.InvariantCulture,
        "seed {0}: {1}{2} after {3} turns, {4} commands, {5} rejected, {6} mismatches, "
        + "{7} commandless turns, longest stall run {8}",
        Seed,
        Ending,
        WinningNationId is null ? string.Empty : " by " + WinningNationId,
        TurnsPlayed,
        CommandsIssued,
        CommandsRejected,
        ProjectionMismatches,
        CommandlessTurns,
        LongestStallRun);
}

/// <summary>
/// Plays one all-AI game from a scenario and a seed, to a victory condition or to a turn cap, and hands
/// back the whole transcript.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The turn cap is a required argument, not a default and not a safety net.</strong>
/// <c>docs/task-catalogue.md</c>'s T22 entry names non-termination as this task's hazard — "<em>the
/// highest risk of a non-terminating soak, because the original's victory condition is total conquest.
/// The turn cap is mandatory, not optional.</em>" There is deliberately no overload without it and no
/// default value, so no caller anywhere can start a game that has no end.
/// </para>
/// <para>
/// <strong>How the game is judged to be over.</strong> Not by this class's own reading of the state, but
/// by <see cref="VictoryCheckSystem"/>, which T43 registered into <see cref="TurnPhase.RoundEnd"/> and
/// which publishes <see cref="GameWon"/> or <see cref="GameExpired"/>. This runner watches the event
/// stream for those two and stops. It never evaluates a victory condition itself: a second opinion about
/// who won is a second rule.
/// </para>
/// <para>
/// <strong>The stall rule, exactly as the Done-when line states it.</strong> A turn counts toward a stall
/// when it both issues no command <em>and</em> leaves <see cref="AiSubstantiveState"/> unchanged;
/// <see cref="AiGameResult.LongestStallRun"/> is the longest consecutive run of such turns, and the soak
/// fails at two. Bookkeeping that moves whether or not anything happened — the seat index, the calendar,
/// the persisted random value, the news log's round header — is excluded by construction, for the reason
/// <see cref="AiSubstantiveState"/> gives: a comparison that included them could never report a stall at
/// all.
/// </para>
/// <para>
/// <strong>What it does not do.</strong> It does not touch a clock, a file or the console. Timing and
/// artifact-writing belong to the test that calls it (<c>docs/task-catalogue.md</c> T22 Done-when 2 and
/// 4), and a wall-clock read inside <c>src/IC2.Engine</c> would fail this repository's own determinism
/// guard.
/// </para>
/// </remarks>
public static class AiGameRunner
{
    /// <summary>Plays one game.</summary>
    /// <param name="world">The loaded world.</param>
    /// <param name="ruleset">The loaded ruleset.</param>
    /// <param name="scenario">The scenario, whose seats decide which nations the AI plays.</param>
    /// <param name="seed">
    /// The seed this game runs under, replacing <see cref="Scenario.RandomSeed"/> — the whole of what
    /// makes one of the fifty soak runs differ from another.
    /// </param>
    /// <param name="turnCap">The maximum number of seat turns to play. Must be positive.</param>
    /// <exception cref="ArgumentNullException">Any reference argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="turnCap"/> is not positive.</exception>
    public static AiGameResult Run(World world, Ruleset ruleset, Scenario scenario, ulong seed, int turnCap)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(ruleset);
        ArgumentNullException.ThrowIfNull(scenario);
        if (turnCap <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(turnCap),
                turnCap,
                "A soak run needs a positive turn cap: the shipped victory condition is total conquest, "
                + "so an uncapped all-AI game is not guaranteed to terminate.");
        }

        var registry = SystemRegistry.FromEngineAssembly();
        var dispatcher = new CommandDispatcher(registry, ruleset, world, NullEventSink.Instance);
        var coordinator = new TurnCoordinator(registry, ruleset, world, NullEventSink.Instance, dispatcher);

        var state = GameStateFactory.CreateInitial(world, ruleset, scenario) with { RandomSeed = seed };

        var transcript = new List<string>
        {
            Inv(
                "seed {0} | scenario {1} | world {2} | ruleset {3} | turn cap {4} | victory {5}",
                seed, scenario.Id, world.Id, ruleset.Id, turnCap, scenario.Victory.Type),
        };

        var ending = AiGameEnding.TurnCapReached;
        string? winner = null;
        var turnsPlayed = 0;
        var issued = 0;
        var rejected = 0;
        var mismatches = 0;
        var cappedTurns = 0;
        var commandless = 0;
        var stallRun = 0;
        var longestStallRun = 0;

        while (turnsPlayed < turnCap)
        {
            var seatId = state.ActiveNationId;
            var before = state;
            var result = coordinator.RunTurn(state);
            state = result.State;
            turnsPlayed++;

            var turnCommands = 0;
            foreach (var published in result.Events)
            {
                switch (published)
                {
                    case AiTurnDecided decided:
                        turnCommands += decided.CommandsIssued;
                        issued += decided.CommandsIssued;
                        rejected += decided.CommandsRejected;
                        mismatches += decided.ProjectionMismatches;
                        if (decided.HitActionCap)
                        {
                            cappedTurns++;
                        }

                        transcript.Add(Inv("turn {0} -- {1}", turnsPlayed, seatId));
                        foreach (var line in decided.Lines)
                        {
                            transcript.Add("    " + line);
                        }

                        break;
                    case GameWon won:
                        ending = AiGameEnding.Won;
                        winner = won.WinningNationId;
                        break;
                    case GameExpired:
                        ending = AiGameEnding.Expired;
                        break;
                    default:
                        break;
                }
            }

            if (turnCommands == 0)
            {
                commandless++;
                if (AiSubstantiveState.AreEquivalent(before, state))
                {
                    stallRun++;
                    longestStallRun = Math.Max(longestStallRun, stallRun);
                    transcript.Add(Inv(
                        "turn {0} -- {1}: no command issued and no substantive change (stall run {2})",
                        turnsPlayed, seatId, stallRun));
                }
                else
                {
                    stallRun = 0;
                }
            }
            else
            {
                stallRun = 0;
            }

            if (ending != AiGameEnding.TurnCapReached)
            {
                transcript.Add(Inv(
                    "game over on turn {0}: {1}{2}",
                    turnsPlayed, ending, winner is null ? string.Empty : " by " + winner));
                break;
            }
        }

        if (ending == AiGameEnding.TurnCapReached)
        {
            transcript.Add(Inv(
                "turn cap {0} reached with the game undecided -- a pass, per T22 Done-when 2", turnCap));
        }

        var outcome = new AiGameResult(
            seed,
            ending,
            winner,
            turnsPlayed,
            issued,
            rejected,
            mismatches,
            cappedTurns,
            commandless,
            longestStallRun,
            state,
            transcript);

        transcript.Add(outcome.Summary());
        return outcome;
    }
}
