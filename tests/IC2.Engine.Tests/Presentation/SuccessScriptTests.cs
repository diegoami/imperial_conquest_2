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
/// <strong>Four independent cases.</strong> T192 retains a corrected new-game Macedonia/seed3
/// prefix in <c>success.txt</c>; B (<c>success-trade-offer.txt</c>) accepts its actual Numidia
/// trade immediately after original end22 and replays every later command. The peace answer and the
/// marine group live in <c>success-treaty-load.txt</c>, resumed from one native port save taken on the
/// corrected A path (#1012; see below). Fleet remains
/// a fresh <c>toy-3city</c> case. Every case has nonempty golden equality, fresh-session/load
/// determinism and per-line own-verb acceptance; <see cref="CommandCoverageTests"/> aggregates all four.
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
/// distinct target across the two independent Macedonia scripts.
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
/// <strong>T80 rework round 1, B2 (blocking): the previous version of this remark misdescribed the gate,
/// misdiagnosed what fails at 270, and asserted an unverified "218 of 250, never 1" sweep. All three are
/// corrected here, against the engine as read, not as assumed.</strong>
/// </para>
/// <para>
/// <c>peace-yes</c> (<see cref="Diplomacy.Commands.AcceptPeaceTreatyCommand"/>) only ever becomes
/// answerable when <see cref="Battle.InstantBattleResolver"/> (l.302–314) raises the post-battle treaty
/// offer, gated on <c>winnerArmyPower &lt; loserArmyPower &amp;&amp; loserNation.Unity &gt;
/// combat.AutoPeaceLoserUnityThreshold &amp;&amp; loserCityCount &gt; combat.AutoPeaceLoserCityThreshold</c>
/// — the battle's winner having <em>less</em> total army power than its loser, not merely "macedonia
/// loses" — and then, only once that gate passes, an independent
/// <c>rng.NextInt(combat.AutoPeaceChanceDenominator) &lt; combat.AutoPeaceChanceNumerator</c> roll.
/// <c>classical-faithful.json</c>'s own <c>combat</c> block reads
/// <c>autoPeaceLoserUnityThreshold: 500</c>, <c>autoPeaceLoserCityThreshold: 7</c>,
/// <c>autoPeaceChanceNumerator: 2</c>, <c>autoPeaceChanceDenominator: 5</c> — a <strong>2-in-5</strong>
/// chance once gated, not the 1-in-5 an earlier draft of this remark claimed.
/// </para>
/// <para>
/// <strong>Historical seed selection.</strong> T80 selected seed 3 from its then-current CLI sweep
/// of seeds 1 through 40. Those seed-specific outcomes predate the T171 faithful mobilisation
/// correction and are not claims about the current script. The scenario, seed 3 and fixed turn order
/// remain unchanged; no new seed search was used for T171.
/// </para>
/// <para>
/// <strong>T192 boundaries and provenance.</strong> Command numbers refer to A at baseline
/// <c>a3dbf161b3b40bb02717845e933754ef08623854</c>. Corrected A keeps original1–78 with the three
/// specified preparation moves and, since #1012, one move that brings <c>mac-detached</c> beside
/// <c>army-14</c> before its attack.
/// </para>
/// <para>
/// <strong>#1012 re-tune (owner decision, 2026-10-22).</strong> The lateral step changed where Thracia's
/// <c>army-14</c> marches. The sacrificial <c>mac-sac-1</c> attack could no longer reach it, so it was
/// dropped. Without that attack, Thracia's recruits lift its army power above Macedonia's by original78,
/// so Thracia's win there fails the offer gate (winner power below loser power). The baseline save no
/// longer reached an offer. With the owner's approval, the treaty save was rebuilt by ordinary current-engine
/// play and save on corrected A, with the <c>mac-detached</c> move and attack removed, through the
/// <c>end</c> after <c>split-fleet mac-fleet-1 mac-fleet-keep 13</c>.
/// The loaded case attacks <c>army-14</c> with <c>mac-detached</c> (the battle is lost and the
/// resolver offers peace), answers <c>peace-yes</c>, then runs original79–87's marine group, with
/// <c>move mac-marines 142 42</c> and its <c>end</c> before it. The fleet waits at <c>(149, 41)</c>,
/// because <c>(148, 41)</c> is no longer reached in one turn. The boundary was found by trying each
/// earlier player turn; the provenance file records it. No offer or state was injected and no reseeding
/// was used.
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
/// owners). <c>north-fleet-1</c> moves to <c>(0, 5)</c> on row <c>y=5</c>, the one all-sea row on this 8x6
/// map, and waits there: since #1012 the AI's <c>south-fleet-1</c> steps around land and comes west along
/// that row itself, ending the second turn beside <c>north-fleet-1</c> and away from its own city (a fleet
/// docked at <c>meridia</c> cannot be attacked), so the attack follows two <c>end</c>s. The battle
/// itself is lost (<c>"Southern League sinks fleet of Northern League"</c>) — irrelevant to Done-when 1,
/// which asks only that the <em>command</em> is accepted, not that the battle is won.
/// </para>
/// <para>
/// All exposed mutating verbs now have accepted lines across these scripts, including hiring,
/// embarkation and disembarkation; <see cref="CommandCoverageTests"/>'s current exclusion set is empty.
/// </para>
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

    private static readonly string SuccessTradeOfferScriptPath =
        Path.Combine(ModelTestPaths.RepositoryRoot, "tests", "fixtures", "cli", "success-trade-offer.txt");

    private static readonly string SuccessTradeOfferGoldenPath =
        Path.Combine(ModelTestPaths.RepositoryRoot, "tests", "fixtures", "cli", "success-trade-offer.golden.txt");

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
    /// <remarks>
    /// T119: the order is pinned fixed (<c>--turn-order fixed</c> on the CLI). T80 chose seed 3 on the world's
    /// own order, and the shuffled order (<c>classical-faithful</c>'s default) broke the then-current script's accepted lines
    /// for that seed and for every seed tried then (the <c>hire-mercenary</c> line needs an offer next to
    /// <c>mac-merc</c>).
    /// </remarks>
    private static GameSession NewSuccessSession() =>
        new(
            Classical.World,
            Classical.Ruleset with
            {
                Flags = Classical.Ruleset.Flags with { NewGameTurnOrder = IC2.Engine.Model.NewGameTurnOrderPolicy.Fixed },
            },
            Classical.Scenario,
            seedOverride: 3,
            humanSeatNationId: "macedonia");

    /// <summary>The scenario's own committed seed (20250913); north is already its own default human seat.</summary>
    private static GameSession NewFleetSession() =>
        new(Toy.World, Toy.Ruleset, Toy.Scenario, seedOverride: null);

    /// <summary>Verbs that print nothing to accept or reject — excluded from the per-line accepted check below.</summary>
    private static readonly HashSet<string> ReadOnlyVerbs = new(StringComparer.Ordinal)
    {
        "end", "status", "news", "quit", "help", "armies", "cities", "map",
    };

    /// <summary>
    /// The inverse of <see cref="CommandCoverageTests.VerbByKind"/> — one source of truth for which verb
    /// maps to which <see cref="Core.ICommand.Kind"/>, shared rather than kept as a second, driftable copy.
    /// </summary>
    private static readonly Dictionary<string, string> KindByVerb = CommandCoverageTests.VerbByKind
        .ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);

    /// <summary>
    /// Whether <paramref name="block"/>'s own outcome reads as an accepted command for <em>its own
    /// verb</em> — the generic renderer's exact <c>"{kind} accepted."</c>, where <c>kind</c> is the kind
    /// <see cref="KindByVerb"/> says this block's own verb maps to (never any other kind's line, composed
    /// or not), or <c>move</c>/<c>buy</c>/<c>transfer-money</c>'s own bespoke wording. Rejected outright,
    /// regardless of verb, if
    /// the outcome contains <c>" rejected"</c> anywhere — the composed
    /// <c>diplomacy.declare-war accepted (composed ahead of the attack).</c> line
    /// <c>attack-army</c>/<c>besiege-city</c>/<c>attack-fleet</c> print ahead of their own outcome must
    /// never let a rejection of the primary command through.
    /// </summary>
    /// <remarks>
    /// T80 rework round 2, B3 (blocking): the previous version accepted a block if its outcome contained
    /// <em>any</em> <c>" accepted."</c> or <c>" accepted ("</c> substring, anywhere — so a composed
    /// declare-war line ahead of a <em>rejected</em> primary command (e.g. <c>besiege-city</c> not
    /// adjacent) still counted as accepted, exactly the same shape of bug B1 fixed for coverage but never
    /// applied here. Proved by mutation: appending <c>besiege-city mac-marines volubilis</c> (not
    /// adjacent, so its own <c>battle.besiege-city</c> is rejected after the declare-war composes) after
    /// <c>accept-offer</c> in <c>success.txt</c>, and regenerating the golden through the CLI, used to
    /// leave all ten tests in this class and <see cref="CommandCoverageTests"/> green; this fixed version
    /// fails, naming that exact line. Reverted afterward — see the PR for the before/after.
    /// </remarks>
    private static bool BlockLooksAccepted(GoldenTranscriptBlocks.Block block)
    {
        if (block.Outcome.Contains(" rejected", StringComparison.Ordinal))
        {
            return false;
        }

        if (block.Verb == "move")
        {
            return block.Outcome.Contains("moved from (", StringComparison.Ordinal);
        }

        if (block.Verb == "buy")
        {
            return block.Outcome.Contains("bought ", StringComparison.Ordinal)
                && block.Outcome.Contains(" tons of supply", StringComparison.Ordinal);
        }

        // Fix #710: transfer-money's bespoke reply names what moved ("... received N talents from X.").
        if (block.Verb == "transfer-money")
        {
            return block.Outcome.Contains(" talents from ", StringComparison.Ordinal);
        }

        return KindByVerb.TryGetValue(block.Verb, out var kind)
            && block.Outcome.Contains($"{kind} accepted.", StringComparison.Ordinal);
    }

    /// <summary>
    /// Done-when 1, read literally: "each line's outcome in the transcript is an accepted command, not a
    /// rejection." Checked per script line (<see cref="GoldenTranscriptBlocks"/>), not by scanning the
    /// whole transcript for one fixed substring — <see langword="rejected"/> is only one of several
    /// non-acceptance shapes the session prints (<c>Usage: ...</c>, <c>Unknown command '...'</c>,
    /// <c>There is no pending peace treaty offer.</c>, a peace-treaty offer "addressed to" someone else,
    /// the watch-mode "no seat to command" line, which carries no parenthesis at all), and a golden could
    /// pass a bare <c>"rejected ("</c> search while still containing one of those.
    /// </summary>
    /// <remarks>
    /// <para>
    /// T80 rework round 1, N2: the previous version of both "no rejection" tests below asserted only
    /// <c>Assert.DoesNotContain("rejected (", golden)</c>, which is silent on every shape named above.
    /// Proved by mutation: inserting a repeated <c>peace-yes</c> (prints "There is no pending peace treaty
    /// offer."), a malformed <c>join-armies army-8</c> (prints its own <c>Usage:</c> line) and an
    /// <c>Unknown command 'frobnicate'</c> into <c>success.txt</c>, then regenerating the golden through
    /// the CLI, left the old assertion green while this one fails, naming all three lines — reverted
    /// afterward.
    /// </para>
    /// <para>
    /// T80 rework round 2, N6: <c>GoldenTranscriptBlocks.Parse</c> returns an empty list for a transcript
    /// with no <c>"&gt; "</c> prompts at all (an empty file, or one whose echo format changed out from
    /// under this parser), and a <c>foreach</c>/<c>Where</c> over an empty sequence finds nothing bad —
    /// this assertion would have passed vacuously. <see cref="Assert.NotEmpty{T}"/> below closes that.
    /// Proved by mutation: truncating a copy of <c>success.golden.txt</c> to empty now fails this
    /// assertion (before the fix, only the byte-compare and coverage tests caught it) — reverted
    /// afterward.
    /// </para>
    /// </remarks>
    private static void AssertEveryMutatingLineProducedAnAcceptedOutcome(string golden)
    {
        var blocks = GoldenTranscriptBlocks.Parse(golden);
        Assert.NotEmpty(blocks);

        var bad = blocks
            .Where(b => !ReadOnlyVerbs.Contains(b.Verb) && !BlockLooksAccepted(b))
            .Select(b => b.Line)
            .ToList();

        Assert.True(
            bad.Count == 0,
            "The following script line(s) did not produce an accepted outcome: " + string.Join(", ", bad));
    }

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
    /// macedonia --seed 3 --turn-order fixed --script tests/fixtures/cli/success.txt</c> exits 0, and its output equals the
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

    /// <summary>See <see cref="AssertEveryMutatingLineProducedAnAcceptedOutcome"/>.</summary>
    [Fact]
    public void Every_mutating_line_in_the_success_golden_produced_an_accepted_outcome()
    {
        AssertEveryMutatingLineProducedAnAcceptedOutcome(File.ReadAllText(SuccessGoldenPath));
    }

    // ---- success-trade-offer.txt: an independent fresh Macedonia session ----

    [Fact]
    public void The_trade_offer_script_run_in_process_matches_the_committed_golden_transcript()
    {
        var transcript = RunTranscript(NewSuccessSession(), File.ReadAllLines(SuccessTradeOfferScriptPath));
        Assert.Equal(File.ReadAllText(SuccessTradeOfferGoldenPath), transcript);
    }

    [Fact]
    public void The_trade_offer_script_gives_an_identical_transcript_twice()
    {
        var lines = File.ReadAllLines(SuccessTradeOfferScriptPath);
        Assert.Equal(RunTranscript(NewSuccessSession(), lines), RunTranscript(NewSuccessSession(), lines));
    }

    [Fact]
    public void Every_mutating_line_in_the_trade_offer_golden_produced_an_accepted_outcome()
    {
        AssertEveryMutatingLineProducedAnAcceptedOutcome(File.ReadAllText(SuccessTradeOfferGoldenPath));
    }

    // #1012: native port snapshot from ordinary current-engine play; no field/seed/offer injection.
    private static string CliFixture(string name) => Path.Combine(ModelTestPaths.RepositoryRoot, "tests", "fixtures", "cli", name);
    private static string TreatySavePath => Path.Combine(ModelTestPaths.RepositoryRoot, "tests", "fixtures", "saves", "success-treaty-before-attack.json");
    private static GameSession NewTreatySession() => new(Classical.World, Classical.Ruleset, Classical.Scenario,
        IC2.Engine.Persistence.SaveManager.LoadFile(TreatySavePath, Classical.World, Classical.Ruleset));

    [Fact]
    public void The_loaded_treaty_case_has_pinned_native_context_and_exact_roundtrip()
    {
        var file = System.Text.Encoding.UTF8.GetBytes(File.ReadAllText(TreatySavePath).Replace("\r\n", "\n", StringComparison.Ordinal));
        Assert.Equal("4803fc30a60dc67c56c6206942ac93bd08043494c70db5f433e57aeb340da357",
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(file)));
        var save = IC2.Engine.Persistence.SaveManager.LoadFile(TreatySavePath, Classical.World, Classical.Ruleset);
        var text = IC2.Engine.Persistence.SaveManager.Serialize(save);
        var copy = IC2.Engine.Persistence.SaveManager.Load("treaty-roundtrip", text, Classical.World, Classical.Ruleset);
        Assert.Equal(File.ReadAllText(TreatySavePath).Replace("\r\n", "\n", StringComparison.Ordinal), text.Replace("\r\n", "\n", StringComparison.Ordinal));
        Assert.Equal(text, IC2.Engine.Persistence.SaveManager.Serialize(copy));
        Assert.Equal(save, copy);
        Assert.Equal(save.State, copy.State);
        Assert.Equal("4b17202ae07488627ab40a1bdd4b035bed4b32a4ede64dcbe37574c86d45cf91", IC2.Engine.Core.GameStateHash.Compute(copy.State));
        Assert.Equal(17524961124136824322UL, copy.State.RandomSeed);
        Assert.Equal(13, copy.State.Calendar.TurnIndex);
        Assert.Equal("macedonia", copy.State.ActiveNationId);
        Assert.Equal("macedonia", Assert.Single(copy.State.Nations, n => n.Control == IC2.Engine.Model.SeatControl.Human).Id);
        Assert.Equal("classical-mediterranean", copy.ScenarioId);
        Assert.Equal("classical-mediterranean", copy.WorldId);
        Assert.Equal("classical-faithful", copy.RulesetId);
        Assert.Equal(new[] { "rome", "carthage", "seleucid", "ptolemaic", "macedonia", "numidia", "gaul", "greece", "celtiberia", "illyria", "dacia", "bithynia", "galatia", "armenia", "media", "thracia" }, copy.State.TurnOrder);
        Assert.Null(copy.Battle);
    }

    [Fact]
    public void The_loaded_treaty_case_matches_its_golden_and_accepts_a_real_offer_and_every_own_command()
    {
        var session = NewTreatySession();
        var offers = new List<IC2.Engine.Battle.PeaceTreatyOffered>();
        var battles = new List<IC2.Engine.Battle.BattleResolved>();
        session.EventsPublished += events =>
        {
            offers.AddRange(events.OfType<IC2.Engine.Battle.PeaceTreatyOffered>());
            battles.AddRange(events.OfType<IC2.Engine.Battle.BattleResolved>());
        };
        var transcript = RunTranscript(session, File.ReadAllLines(CliFixture("success-treaty-load.txt")));
        AssertEveryMutatingLineProducedAnAcceptedOutcome(transcript);
        Assert.Contains(offers, e => e.WinnerNationId == "thracia" && e.LoserNationId == "macedonia");
        Assert.Contains(battles, e => e.Result.AttackerId == "mac-detached" && e.Result.DefenderId == "army-14" && e.Result.PeaceTreatyOffered);
        Assert.Equal(File.ReadAllText(CliFixture("success-treaty-load.golden.txt")), transcript);
        AssertEveryMutatingLineProducedAnAcceptedOutcome(File.ReadAllText(CliFixture("success-treaty-load.golden.txt")));
    }

    [Fact]
    public void The_loaded_treaty_case_is_deterministic_from_two_fresh_loads()
    {
        var lines = File.ReadAllLines(CliFixture("success-treaty-load.txt"));
        Assert.Equal(RunTranscript(NewTreatySession(), lines), RunTranscript(NewTreatySession(), lines));
    }

    // Immutable baseline input, not a regenerated current-state expectation. The mapping below
    // pins every original command/argument and the only approved additions, relocations and split.
    private const string OriginalA = """
split-army army-8 mac-detached 0
split-army army-8 mac-split 0
join-armies army-8 mac-split
split-army army-8 mac-marines 1
split-army army-8 mac-spare 1
order-city pella fortify 1
recruit-standing pella light_infantry 200
recruit-standing pella light_infantry 200
order-fleet amphipolis 20 mac-fleet-1
propose-trade illyria
propose-alliance dacia
declare-war armenia
end
make-peace illyria
move mac-marines 144 42
buy mac-marines pella 5
move mac-spare 147 40
move mac-spare 147 41
disband-army mac-spare
move mac-detached 157 31
move army-8 135 52
end
move mac-detached 155 29
move army-8 135 52
end
move army-8 135 52
end
move mac-detached 155 26
end
besiege-city army-8 phoenice
split-army army-8 mac-sac-1 0
end
move mac-sac-1 151 31
end
move mac-sac-1 151 31
end
mobilize 2 mac-recruit-0
mobilize 2 mac-recruit-1
join-units mac-marines 1 2
transfer-money army-8 100
transfer-money army-8 -50
move army-8 136 42
buy mac-marines pella 5
buy mac-marines pella 5000
transfer-money mac-marines 100
move mac-marines 137 42
army-transfer mac-marines army-8 supply=2 money=20
split-unit mac-marines 0 1000
rename-unit mac-marines 0 LegioX
disband-unit mac-marines 1
split-unit mac-marines 1 200
split-army mac-marines mac-merc 2 supply=45 money=80
end
move mac-merc 140 47
move mac-merc 141 48
hire-mercenary mac-merc 25
end
attack-army mac-detached army-14
end
end
split-fleet mac-fleet-1 mac-fleet-2 10
fleet-transfer mac-fleet-1 mac-fleet-2 1 1 0
join-fleets mac-fleet-1 mac-fleet-2
buy-fleet-supply mac-fleet-1 amphipolis 5
repair-fleet mac-fleet-1 1
split-fleet mac-fleet-1 mac-fleet-keep 13
end
attack-army mac-sac-1 army-14
end
end
status
news
set-tax 20
transfer-money mac-fleet-1 100
transfer-money mac-fleet-1 -50 via mac-fleet-keep
scuttle-fleet mac-fleet-1
move mac-marines 142 42
end
peace-yes
move mac-marines 148 40
move-fleet mac-fleet-keep 148 41
embark-army mac-marines mac-fleet-keep
disembark-army mac-marines 148 40
recruit-standing pella light_infantry 200
disband-slot 0
end
quit
""";

    [Fact]
    public void The_four_case_split_preserves_every_original_command_and_the_complete_marine_group()
    {
        var original = OriginalA.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).ToArray();
        Assert.Equal(87, original.Length);
        var prefix = new List<string>();
        for (var i = 1; i <= 78; i++)
        {
            // #1012: army-14 marches elsewhere now, so mac-detached first steps beside it, and the
            // sacrificial mac-sac-1 attack (original68), which can no longer reach it, is dropped.
            if (i == 58) prefix.Add("move mac-detached 156 24");
            if (i == 60) prefix.Add("move mac-sac-1 148 40");
            if (i != 68) prefix.Add(original[i - 1]);
            if (i == 70) prefix.Add("move mac-merc 139 43");
        }
        var trade = original.Take(67).Append("end").ToList();
        trade.Insert(22, "accept-offer");
        trade.Insert(58, "move mac-detached 156 24");
        // #1012: the loaded case resumes before the mac-detached attack (original58), whose lost battle
        // is offered peace, then runs original79's answer and the marine group (original77, 78, 80-87).
        var treaty = new[] { "move mac-detached 155 26", original[57], original[78], original[76], original[77] }
            .Concat(original.Skip(79).Select(l => l.StartsWith("move-fleet mac-fleet-keep ", StringComparison.Ordinal) ? "move-fleet mac-fleet-keep 149 41" : l));
        Assert.Equal(prefix, File.ReadAllLines(SuccessScriptPath));
        Assert.Equal(trade, File.ReadAllLines(SuccessTradeOfferScriptPath));
        Assert.Equal(treaty, File.ReadAllLines(CliFixture("success-treaty-load.txt")));
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

    /// <summary>See <see cref="AssertEveryMutatingLineProducedAnAcceptedOutcome"/>.</summary>
    [Fact]
    public void Every_mutating_line_in_the_fleet_golden_produced_an_accepted_outcome()
    {
        AssertEveryMutatingLineProducedAnAcceptedOutcome(File.ReadAllText(SuccessFleetGoldenPath));
    }
}
