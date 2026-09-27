using IC2.Engine.Presentation;
using IC2.Engine.Serialization;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Presentation;

/// <summary>
/// <c>docs/tasks/T80.md</c> Done-when 1: "A second scripted scenario, with its own golden transcript,
/// issues each of the 28 command types at least once successfully: each line's outcome in the transcript
/// is an accepted command, not a rejection." Mirrors <see cref="GameSessionTests"/>'s own demo-script
/// pattern exactly, for both new scripts.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Two scripts, not one</strong> (Done-when 2b, added 2026-09-27 after PR #452's review): no
/// single shipped scenario can host every reachable command. <c>success.txt</c> carries every command
/// except <c>attack-fleet</c>; <c>success-fleet.txt</c> is a short, separate script on the shipped
/// <c>toy-3city</c> scenario carrying only <c>attack-fleet</c> (composing its own <c>declare-war</c> ahead
/// of the attack, exactly as <c>GameSession.Commands</c>'s remarks describe). Each is checked against its
/// own golden, independently, by its own pair of tests below; <see cref="CommandCoverageTests"/> reads
/// both goldens together when it checks that every non-excluded command type has an accepted line
/// <em>somewhere</em>.
/// </para>
/// <para>
/// <strong><c>success.txt</c>: scenario and seed.</strong> <c>classical-mediterranean</c>, seat
/// <c>macedonia</c>, <c>--seed 3</c> (an explicit override, not the scenario's own committed 270 — see
/// "Why seed 3" below). A two-nation toy world (<c>toy-3city</c>) cannot host the diplomacy half of this
/// script: <see cref="Diplomacy.PendingOfferSystem"/>'s own decision rule (<c>DecideOfferType</c>) can
/// only ever propose an <em>alliance</em> to a human seat with a single other nation in the world (the
/// "candidate has a trade partner poorer than the human" test is structurally false with no third nation
/// to be that poorer partner), so accepting a natural offer there forecloses <c>propose-trade</c> ever
/// succeeding afterward (and vice versa) — the two can never both succeed against the same, sole other
/// nation. <c>classical-mediterranean</c>'s sixteen nations give <c>propose-trade</c>,
/// <c>propose-alliance</c>, <c>declare-war</c> and the naturally-arriving <c>accept-offer</c> each a
/// distinct target, so all four succeed in the one script.
/// </para>
/// <para>
/// <strong>Why <c>macedonia</c>.</strong> It starts at peace with every other nation (no pre-existing
/// war), which <c>propose-alliance</c> requires of the issuer against an AI target
/// (<see cref="Diplomacy.Commands.ProposeAllianceCommandHandler"/>: "the proposer is itself at war with
/// anyone" refuses unconditionally, regardless of which nation is named). <c>carthage</c> and
/// <c>ptolemaic</c> are the only two nations the world ships a starting fleet for, and both start
/// permanently at war (<c>carthage</c> with <c>celtiberia</c>, <c>ptolemaic</c> with <c>seleucid</c>) —
/// static <c>startingRelations</c> data, unaffected by <c>--seed</c> — which an AI target's war can never
/// be talked down from (<c>MakePeaceCommandHandler</c> refuses a make-peace against an AI still committed
/// to war, unconditionally). A fleet-owning seat can therefore never also succeed at
/// <c>propose-alliance</c>; this is exactly why <c>attack-fleet</c> needed its own, separate script instead
/// (see <c>success-fleet.txt</c>'s own remarks below), rather than trying to fold a fleet-owning seat into
/// this one.
/// </para>
/// <para>
/// <strong>Why seed 3, not the scenario's own 270 (Hazards: "Determinism. Say which seed and why").</strong>
/// <c>peace-yes</c> (<see cref="Diplomacy.Commands.AcceptPeaceTreatyCommand"/>) only ever becomes
/// answerable when <see cref="Battle.InstantBattleResolver"/> raises the post-battle treaty offer, which
/// needs the battle's own loser to clear <c>unity &gt; 500 &amp;&amp; cities &gt; 7</c> (macedonia's own
/// sixteen cities and 800+ starting unity clear this easily whenever macedonia itself is the one who
/// loses) <em>and</em> a further, independent <c>Random(5) == 0</c> roll. At the scenario's own seed
/// (270), seven separate under-strength sacrifices against <c>army-14</c> (Thracia) never landed that
/// roll (PR #452's own finding). <c>--seed 3</c> was found by running this exact script's own setup (every
/// line up to and including the first <c>attack-army mac-detached army-14</c>) against seeds 1 through
/// 250 and keeping the first one whose second sacrificial attack (<c>mac-sac-1</c>) draws the offer —
/// <c>attacksNeeded == 2</c> at every one of the 218 seeds (of 250) that landed it at all inside three
/// sacrifices, never <c>1</c>, so this script's own two attacks are not a seed-3 idiosyncrasy but the
/// shape the mechanic actually takes here. Changing the seed reshuffles every other random-driven line
/// too (weather, city loyalty, casualty counts), which is why the whole golden was regenerated through the
/// CLI under <c>--seed 3</c> rather than hand-patched.
/// </para>
/// <para>
/// <strong>Random consumers.</strong> <see cref="Battle.BattleCasualties"/>'s casualty divisor
/// (<c>attack-army</c>, <c>besiege-city</c>), <see cref="Economy.WeatherEventSystem"/>, and
/// <see cref="Economy.CityLoyaltyDraws"/> are all reachable from this script (unlike the demo's own
/// no-combat toy world), so this is not re-asserted seed-insensitive line by line the way
/// <see cref="GameSessionTests"/> does for the demo; <see cref="The_success_script_gives_an_identical_transcript_twice"/>
/// is the determinism claim this task's Hazards note asks for.
/// </para>
/// <para>
/// <strong><c>success-fleet.txt</c>: scenario and seed.</strong> The shipped <c>toy-3city</c> scenario, no
/// <c>--seat</c> override (<c>north</c> is already its own default human seat) and no <c>--seed</c>
/// override (the scenario's own committed 20250913) — preferred over authoring a new
/// <c>data/scenarios/example-cli-*.json</c> (Owns, added 2026-09-27) because <c>toy-3city</c> already has
/// exactly the starting state <c>attack-fleet</c> needs and no other shipped scenario does: two fleets
/// (<c>north-fleet-1</c>, <c>south-fleet-1</c>) close enough to close the distance in a couple of turns,
/// owned by two nations that start at peace (<c>toy-3city</c>'s own world data carries no
/// <c>startingRelations</c> at all, unlike <c>classical-mediterranean</c>'s permanently-warring fleet
/// owners). <c>north-fleet-1</c>'s own straight-line move toward <c>south-fleet-1</c> is blocked
/// immediately (the direct path crosses land the movement walker will not detour around); routing it via
/// row <c>y=5</c>, the one all-sea row on this 8x6 map, is what actually closes the distance. The battle
/// itself is lost (<c>"Southern League sinks fleet of Northern League"</c>) — irrelevant to Done-when 1,
/// which asks only that the <em>command</em> is accepted, not that the battle is won.
/// </para>
/// <para>
/// <strong>Confirmed unreachable, and excluded rather than forced (docs/tasks/T80.md's own Hazards:
/// "STOP and report which command, and why"; Done-when 2b: "keyed exceptions, and nothing else").</strong>
/// Three command types have no accepted line in either script, each keyed to the issue that will remove
/// the exception — see <see cref="CommandCoverageTests"/>'s own <c>ConfirmedUnreachable</c> set and its
/// remarks for the full account:
/// </para>
/// <list type="bullet">
/// <item><description><c>hire-mercenary</c> (<see cref="Recruitment.Commands.HireMercenaryCommand"/>),
/// keyed to T56/#229.</description></item>
/// <item><description><c>embark-army</c> and <c>disembark-army</c> (disembark can only ever follow a
/// successful embark), keyed to T93/#453.</description></item>
/// </list>
/// <para>
/// The three <c>Ai*</c>-prefixed command types (<see cref="Diplomacy.Commands.AiFormAllianceCommand"/>,
/// <see cref="Diplomacy.Commands.AiFormTradeCommand"/>,
/// <see cref="Diplomacy.Commands.AiSwapTradePartnerCommand"/>) are a different case again, covered by
/// <see cref="CommandCoverageTests"/>'s own remarks rather than repeated here: they are the AI's own
/// direct, no-consent writes and were never meant to have a CLI verb at all.
/// </para>
/// </remarks>
public sealed class SuccessScriptTests
{
    private static readonly string SuccessScriptPath =
        Path.Combine(ModelTestPaths.RepositoryRoot, "tests", "fixtures", "cli", "success.txt");

    private static readonly string SuccessGoldenPath =
        Path.Combine(ModelTestPaths.RepositoryRoot, "tests", "fixtures", "cli", "success.golden.txt");

    private static readonly string SuccessFleetScriptPath =
        Path.Combine(ModelTestPaths.RepositoryRoot, "tests", "fixtures", "cli", "success-fleet.txt");

    private static readonly string SuccessFleetGoldenPath =
        Path.Combine(ModelTestPaths.RepositoryRoot, "tests", "fixtures", "cli", "success-fleet.golden.txt");

    private static readonly Lazy<ResolvedScenario> LazyClassical = new(
        () => GameDataRepository.Load(ModelTestPaths.DataRoot).Resolve("classical-mediterranean"));

    private static readonly Lazy<ResolvedScenario> LazyToy = new(
        () => GameDataRepository.Load(ModelTestPaths.DataRoot).Resolve("toy-3city"));

    private static ResolvedScenario Classical => LazyClassical.Value;

    private static ResolvedScenario Toy => LazyToy.Value;

    /// <summary>seed 3 — see this class's own remarks for why, not the scenario's committed 270.</summary>
    private static GameSession NewSuccessSession() =>
        new(Classical.World, Classical.Ruleset, Classical.Scenario, seedOverride: 3, humanSeatNationId: "macedonia");

    /// <summary>The scenario's own committed seed (20250913); north is already its own default human seat.</summary>
    private static GameSession NewFleetSession() =>
        new(Toy.World, Toy.Ruleset, Toy.Scenario, seedOverride: null);

    /// <summary>Runs every line through <paramref name="session"/> and renders exactly what the CLI would print.</summary>
    private static string RunTranscript(GameSession session, IEnumerable<string> scriptLines)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var line in scriptLines)
        {
            var output = session.Submit(line);
            foreach (var text in output.Lines)
            {
                builder.Append(text).Append('\n');
            }

            if (output.ShouldExit)
            {
                break;
            }
        }

        return builder.ToString();
    }

    // ---- success.txt ----

    /// <summary>
    /// Done-when 1: "<c>dotnet run --project src/IC2.Cli -- --scenario classical-mediterranean --seat
    /// macedonia --seed 3 --script tests/fixtures/cli/success.txt</c> exits 0, and its output equals the
    /// committed <c>tests/fixtures/cli/success.golden.txt</c> byte for byte. A test runs the same session
    /// in-process, through the Presentation session, and asserts the same."
    /// </summary>
    [Fact]
    public void The_success_script_run_in_process_matches_the_committed_golden_transcript()
    {
        var scriptLines = File.ReadAllLines(SuccessScriptPath);
        var session = NewSuccessSession();

        var transcript = RunTranscript(session, scriptLines);

        var golden = File.ReadAllText(SuccessGoldenPath);
        Assert.Equal(golden, transcript);
    }

    /// <summary>
    /// This task's own Hazards note: "Determinism. The golden must reproduce from a fixed seed." Seed 3
    /// is what both the golden and this test use, fixed — so two fresh sessions over the same script must
    /// agree exactly.
    /// </summary>
    [Fact]
    public void The_success_script_gives_an_identical_transcript_twice()
    {
        var scriptLines = File.ReadAllLines(SuccessScriptPath);

        var first = RunTranscript(NewSuccessSession(), scriptLines);
        var second = RunTranscript(NewSuccessSession(), scriptLines);

        Assert.Equal(first, second);
    }

    /// <summary>
    /// Done-when 1, read literally: "each line's outcome in the transcript is an accepted command, not a
    /// rejection." Every mutating line's own outcome is exactly a <c>"{kind} accepted."</c> line
    /// (<see cref="GameSession.Commands"/>'s generic renderer), a composed
    /// <c>diplomacy.declare-war accepted (composed ahead of the attack).</c> line, or one of the two
    /// hand-worded successes T41 pins (<c>move</c> and <c>buy</c>) — never a <c>"... rejected (...)"</c>
    /// line, anywhere in the whole transcript.
    /// </summary>
    [Fact]
    public void No_line_in_the_success_golden_transcript_is_a_rejection()
    {
        var golden = File.ReadAllText(SuccessGoldenPath);

        Assert.DoesNotContain("rejected (", golden, StringComparison.Ordinal);
    }

    // ---- success-fleet.txt ----

    /// <summary>
    /// Done-when 2b's own <c>attack-fleet</c> requirement: <c>dotnet run --project src/IC2.Cli --
    /// --scenario toy-3city --script tests/fixtures/cli/success-fleet.txt</c> exits 0, and its output
    /// equals the committed <c>tests/fixtures/cli/success-fleet.golden.txt</c> byte for byte.
    /// </summary>
    [Fact]
    public void The_fleet_script_run_in_process_matches_the_committed_golden_transcript()
    {
        var scriptLines = File.ReadAllLines(SuccessFleetScriptPath);
        var session = NewFleetSession();

        var transcript = RunTranscript(session, scriptLines);

        var golden = File.ReadAllText(SuccessFleetGoldenPath);
        Assert.Equal(golden, transcript);
    }

    /// <summary>Determinism, for the second script's own fixed (committed) seed.</summary>
    [Fact]
    public void The_fleet_script_gives_an_identical_transcript_twice()
    {
        var scriptLines = File.ReadAllLines(SuccessFleetScriptPath);

        var first = RunTranscript(NewFleetSession(), scriptLines);
        var second = RunTranscript(NewFleetSession(), scriptLines);

        Assert.Equal(first, second);
    }

    /// <summary>No line in the fleet golden is a rejection either — see the class remarks above.</summary>
    [Fact]
    public void No_line_in_the_fleet_golden_transcript_is_a_rejection()
    {
        var golden = File.ReadAllText(SuccessFleetGoldenPath);

        Assert.DoesNotContain("rejected (", golden, StringComparison.Ordinal);
    }
}
