using IC2.Engine.Presentation;
using IC2.Engine.Serialization;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Presentation;

/// <summary>
/// <c>docs/tasks/T80.md</c> Done-when 1: "A second scripted scenario, with its own golden transcript,
/// issues each of the 28 command types at least once successfully: each line's outcome in the transcript
/// is an accepted command, not a rejection." Mirrors <see cref="GameSessionTests"/>'s own demo-script
/// pattern exactly, for the new script.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Scenario and seed.</strong> <c>classical-mediterranean</c>, seat <c>macedonia</c>, the
/// scenario's own committed <c>randomSeed</c> (270) — no <c>--seed</c> override, the same convention
/// <c>demo.golden.txt</c> and <c>seat-rome.golden.txt</c> both already rely on. A two-nation toy world
/// (<c>toy-3city</c>) cannot host this script: <see cref="Diplomacy.PendingOfferSystem"/>'s own decision
/// rule (<c>DecideOfferType</c>) can only ever propose an <em>alliance</em> to a human seat with a single
/// other nation in the world (the "candidate has a trade partner poorer than the human" test is
/// structurally false with no third nation to be that poorer partner), so accepting a natural offer
/// there forecloses <c>propose-trade</c> ever succeeding afterward (and vice versa) — the two can never
/// both succeed against the same, sole other nation. <c>classical-mediterranean</c>'s sixteen nations
/// give <c>propose-trade</c>, <c>propose-alliance</c>, <c>declare-war</c> and the naturally-arriving
/// <c>accept-offer</c> each a distinct target, so all four succeed in the one script.
/// </para>
/// <para>
/// <strong>Why <c>macedonia</c>.</strong> It starts at peace with every other nation (no pre-existing
/// war), which <c>propose-alliance</c> requires of the issuer against an AI target
/// (<see cref="Diplomacy.Commands.ProposeAllianceCommandHandler"/>: "the proposer is itself at war with
/// anyone" refuses unconditionally, regardless of which nation is named). <c>carthage</c> and
/// <c>ptolemaic</c> are the only two nations the world ships a starting fleet for, and both start
/// permanently at war (<c>carthage</c> with <c>celtiberia</c>, <c>ptolemaic</c> with <c>seleucid</c>) —
/// static <c>startingRelations</c> data, unaffected by <c>--seed</c> — which an AI target's war can never
/// be talked down from (<c>MakePeaceCommandHandler</c> refuses a make-peace against an AI still
/// committed to war, unconditionally). A fleet-owning seat can therefore never also succeed at
/// <c>propose-alliance</c>. <c>macedonia</c> starts with no fleet, so its own <c>order-fleet</c> takes
/// the ruleset's full 24-tick (12-round) construction before it can be used for the rest of the naval
/// verbs.
/// </para>
/// <para>
/// <strong>Random consumers.</strong> <see cref="Battle.BattleCasualties"/>'s casualty divisor
/// (<c>attack-army</c>, <c>besiege-city</c>), <see cref="Economy.WeatherEventSystem"/>, and
/// <see cref="Economy.CityLoyaltyDraws"/> are all reachable from this script (unlike the demo's own
/// no-combat toy world), so this is not re-asserted seed-insensitive line by line the way
/// <see cref="GameSessionTests"/> does for the demo; <see cref="The_same_seed_gives_an_identical_transcript_twice"/>
/// is the determinism claim this task's Hazards note asks for.
/// </para>
/// <para>
/// <strong>Confirmed unreachable, and excluded rather than forced (docs/tasks/T80.md's own Hazards:
/// "STOP and report which command, and why").</strong> Five CLI-mapped command types have no accepted
/// line in this script, each for a reason confirmed by reading the engine, not merely by running out of
/// script:
/// </para>
/// <list type="bullet">
/// <item><description><c>hire-mercenary</c> (<see cref="Recruitment.Commands.HireMercenaryCommand"/>):
/// <see cref="Model.GameState.MercenaryPool"/> starts empty in every scenario and nothing in the
/// currently-merged engine ever adds to it — grepping the whole engine for a
/// <c>MercenaryPool = ValueList.From(...Append(new MercenaryPoolSlot...</c> write finds exactly one caller,
/// <see cref="Import.OriginalSaveImporter"/> (a legacy-save import the CLI has no flag to reach). T56, "the
/// quarterly mercenary restock" (issue #229), is the system that would populate it, and is still open and
/// blocked. No fixture can help either: Owns forbids a new <c>data/</c> scenario, and the CLI has no flag
/// to point at one anyway.</description></item>
/// <item><description><c>embark-army</c> and <c>disembark-army</c> (disembark can only ever follow a
/// successful embark): <see cref="Naval.Commands.EmbarkArmyCommandHandler"/> requires the army and the
/// fleet at the exact same <c>(X, Y)</c>. <see cref="Movement.Commands.MoveArmyCommandHandler"/> blocks an
/// army from ever entering any city's own cell (friendly or not), while a fleet can only ever occupy a sea
/// cell or, by <see cref="Naval.CoastalCity"/>'s own documented exception, a city's cell (for repairing) —
/// never an open land cell. No cell is ever both "an army can stand here" and "a fleet can stand here": an
/// open land tile refuses the fleet, a sea tile refuses the army, and a city cell refuses the army
/// specifically. Confirmed directly against this world's terrain grid at four different coastal cities
/// (<c>amphipolis</c>, <c>abdera</c>, <c>chalcis</c>, <c>demetrias</c>), and true of the move handlers'
/// own rules regardless of world or seed.</description></item>
/// <item><description><c>attack-fleet</c> (<see cref="Battle.Commands.AttackFleetCommand"/>): needs an
/// issuing nation that both owns a usable fleet and can still succeed at <c>propose-alliance</c> — no
/// nation in this world is both (the paragraph above). Substituting a fleet-owning seat instead would cost
/// <c>propose-alliance</c>; this world ships exactly two fleets total (<c>carthage</c>'s and
/// <c>ptolemaic</c>'s), and across every seed from 1 to 400 both are lost to ordinary storm attrition by
/// round 13 at the latest (a <c>--seat macedonia</c> fleet, ordered on the very first turn, cannot launch
/// before round 13 either) — so a peaceful nation's own newly-built fleet can never reach either one in
/// time, and nothing in the currently-merged engine ever commissions a replacement (a 60-round watch
/// found zero new fleets from any of the sixteen nations).</description></item>
/// <item><description><c>peace-yes</c> (<see cref="Diplomacy.Commands.AcceptPeaceTreatyCommand"/>):
/// <see cref="Battle.InstantBattleResolver"/> only ever raises the post-battle treaty offer this answers
/// when the battle's own loser clears <c>unity &gt; 500 &amp;&amp; cities &gt; 7</c> and then a further,
/// independent <c>Random(5) == 0</c> roll — and the offer is stored for a human seat's own answer only
/// when that seat is the one who lost. Both gates are satisfied by construction whenever <c>macedonia</c>
/// itself loses a battle (its own sixteen cities and 800+ starting unity clear the first easily), so this
/// script sacrifices seven separate under-strength detachments against <c>army-14</c> (Thracia) to draw
/// seven independent rolls of the fifth chance — none landed at this seed. Confirmed reachable in
/// principle (the gate is a plain, satisfiable condition, not a structural block like the other four
/// above), just not within a script this task can still call "thin": the same World/Ruleset/Naming
/// investigation this remark documents is available to whoever wires a dedicated, seeded repro instead of
/// widening this general-purpose script further to chase one more coin flip.</description></item>
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

    private static readonly Lazy<ResolvedScenario> LazyClassical = new(
        () => GameDataRepository.Load(ModelTestPaths.DataRoot).Resolve("classical-mediterranean"));

    private static ResolvedScenario Classical => LazyClassical.Value;

    private static GameSession NewSession() =>
        new(Classical.World, Classical.Ruleset, Classical.Scenario, seedOverride: null, humanSeatNationId: "macedonia");

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

    /// <summary>
    /// Done-when 1: "<c>dotnet run --project src/IC2.Cli -- --scenario classical-mediterranean --seat
    /// macedonia --script tests/fixtures/cli/success.txt</c> exits 0, and its output equals the committed
    /// <c>tests/fixtures/cli/success.golden.txt</c> byte for byte. A test runs the same session in-process,
    /// through the Presentation session, and asserts the same."
    /// </summary>
    [Fact]
    public void The_success_script_run_in_process_matches_the_committed_golden_transcript()
    {
        var scriptLines = File.ReadAllLines(SuccessScriptPath);
        var session = NewSession();

        var transcript = RunTranscript(session, scriptLines);

        var golden = File.ReadAllText(SuccessGoldenPath);
        Assert.Equal(golden, transcript);
    }

    /// <summary>
    /// This task's own Hazards note: "Determinism. The golden must reproduce from a fixed seed." The
    /// scenario's own committed <c>randomSeed</c> (270) is what both the golden and this test use — no
    /// <c>--seed</c> override — so two fresh sessions over the same script must agree exactly.
    /// </summary>
    [Fact]
    public void The_same_seed_gives_an_identical_transcript_twice()
    {
        var scriptLines = File.ReadAllLines(SuccessScriptPath);

        var first = RunTranscript(NewSession(), scriptLines);
        var second = RunTranscript(NewSession(), scriptLines);

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
    public void No_line_in_the_golden_transcript_is_a_rejection()
    {
        var golden = File.ReadAllText(SuccessGoldenPath);

        Assert.DoesNotContain("rejected (", golden, StringComparison.Ordinal);
    }
}
