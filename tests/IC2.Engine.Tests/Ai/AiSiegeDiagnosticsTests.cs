using System.Globalization;
using System.Text;
using IC2.Engine.Ai;
using IC2.Engine.Serialization;
using Xunit;
using Xunit.Abstractions;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Ai;

/// <summary>
/// <c>docs/task-catalogue.md</c> T60, issue <c>#259</c>: which of
/// <c>AiMilitaryPhase.ProposeSieges</c>' three gates past ownership rejects, how often, and on what
/// values — measured across T22's fifty-seed soak, and then across the shipped all-AI scenario.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The answer, and it is the same on every seed.</strong> Gate (c) — "the army never reaches
/// adjacency" — is <em>refuted</em>: armies stand next to an enemy city on roughly half of all turns.
/// Gate (a), <c>AttackLegality.IsLegal</c> on the projected post-declaration state, never rejects once.
/// Every rejection is gate (b), the strength ratio, and it is rejecting <strong>correctly</strong>: the
/// observed ratios run from 20 to a few hundred permille, and a siege is won only above 1,000
/// (<c>InstantBattleResolver</c> decides it on <c>defender &lt; attacker</c> exactly). The AI is not
/// declining a siege it could win; it is declining one it would certainly lose.
/// </para>
/// <para>
/// <strong>Why the bug report only ever saw "2 tiles away".</strong> <c>ProposeMarches</c> skips any
/// city at <c>distance &lt;= 1</c> — "<em>already in place: besieging it, or garrisoning it, is another
/// candidate's job</em>". So an army that reaches adjacency and is then refused a siege yields no
/// candidate at all for that city, and its best remaining move is the <em>other</em> city, which walks
/// it back out to distance 2. Next turn, the same in reverse. Adjacency is invisible in that log line by
/// construction, so its absence was never evidence that the army failed to arrive. The oscillation is
/// what a correctly-working march loop does when the siege is permanently declined, and
/// <c>ProposeMarches</c>' one-march-per-army ration — which is about oscillation <em>within</em> a turn
/// — is untouched by this task, because the argument it makes is still right.
/// </para>
/// <para>
/// <strong>What the two scenarios are for.</strong> The toy world's cities carry the largest
/// populations in the shipped corpus (220-300 thousand, against a 37-thousand median across
/// <c>classical-mediterranean</c>'s 334 cities) while its armies carry a fifth of that world's median
/// troop count, so its defender strengths sit an order of magnitude above anything its economy can
/// field. <see cref="The_shipped_all_ai_scenario_reaches_the_siege_gate_and_passes_it"/> plays the
/// committed 16-nation scenario instead and shows the same code path proposing and taking sieges, which
/// is what separates "the AI cannot besiege" from "this fixture cannot be besieged".
/// </para>
/// </remarks>
public sealed class AiSiegeDiagnosticsTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>The marker <c>AiSiegeGateTally.Describe</c> writes, and the only thing parsed here.</summary>
    private const string GateLinePrefix = "siege gates: ";

    public AiSiegeDiagnosticsTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// The Done-when 1 table: the gate distribution over all fifty soak seeds, asserted as a shape and
    /// printed in full so the PR can quote it.
    /// </summary>
    [Fact]
    public void The_gate_that_rejects_every_siege_in_the_soak_is_the_strength_ratio()
    {
        var gates = new GateTotals();
        foreach (var seed in AiTestbed.SoakSeeds())
        {
            gates.Add(AiTestbed.RunSeed(seed).Transcript);
        }

        _output.WriteLine(gates.Report("toy-3city, 50 seeds"));

        Assert.True(
            gates.Adjacent > 0,
            "gate (c) is refuted only if armies actually reach adjacency; none did, so the diagnosis "
            + "in docs/task-catalogue.md T60 Done-when 2 is wrong and this task is a movement problem.");

        Assert.Equal(0, gates.RejectedByLegality);
        Assert.Equal(0, gates.RulesetCannotSiege);
        Assert.Equal(gates.Adjacent - gates.Proposed, gates.RejectedByRatio);

        // The gate is declining correctly, not conservatively: a siege is won above 1000 permille, and
        // the best ratio any army reached in 50 games is far below that. If this ever fails because the
        // best ratio crossed 1000 while the AI still declined, the required-ratio constant is the
        // suspect and the diagnosis changes -- which is exactly the distinction T60 Done-when 3 draws.
        Assert.True(
            gates.BestRatioPermille < AiWeights.PermilleScale,
            $"the closest siege reached {gates.BestRatioPermille} permille, at or above the {AiWeights.PermilleScale} "
            + "a siege must clear to be won at all. The gate is then refusing a winnable siege, and the "
            + "threshold -- not the army's size -- is the defect.");
    }

    /// <summary>
    /// Done-when 4, on data that can carry it: the committed all-AI <c>classical-mediterranean</c>
    /// scenario, where <c>chose [Military/besiege]</c> is not zero.
    /// </summary>
    /// <remarks>
    /// Three seeds and a 400-turn cap, sized to stay inside a few seconds of the suite's budget while
    /// still being three genuinely divergent games. The turn cap is mandatory for the reason
    /// <see cref="AiGameRunner"/> gives — this scenario declares no <c>turnLimit</c> at all, so nothing
    /// else would stop it.
    /// </remarks>
    [Fact]
    public void The_shipped_all_ai_scenario_reaches_the_siege_gate_and_passes_it()
    {
        var shipped = GameDataRepository.Load(ModelTestPaths.DataRoot).Resolve("classical-mediterranean");

        var besieges = 0;
        var rejected = 0;
        var mismatches = 0;
        var gates = new GateTotals();
        foreach (var seed in new ulong[] { 1, 2, 3 })
        {
            var result = AiGameRunner.Run(shipped.World, shipped.Ruleset, shipped.Scenario, seed, 400);
            rejected += result.CommandsRejected;
            mismatches += result.ProjectionMismatches;
            gates.Add(result.Transcript);

            var chosen = CountChosen(result.Transcript, "Military/besiege");
            besieges += chosen;
            _output.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0} | besieges chosen {1}", result.Summary(), chosen));
        }

        _output.WriteLine(gates.Report("classical-mediterranean, 3 seeds x 400 turns"));

        Assert.True(
            besieges > 0,
            "the AI must be able to besiege on the shipped scenario; if this is zero the fault is in "
            + "AiMilitaryPhase.ProposeSieges and not in the toy fixture.");
        Assert.Equal(0, rejected);
        Assert.Equal(0, mismatches);
    }

    private static int CountChosen(IReadOnlyList<string> transcript, string kind)
    {
        var needle = "chose [" + kind + "]";
        var count = 0;
        foreach (var line in transcript)
        {
            if (line.Contains(needle, StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// The gate counts summed over however many transcripts are fed to it, parsed back out of the lines
    /// <c>AiSiegeGateTally.Describe</c> writes — the same artifact a reader of a failing seed's log has
    /// in front of them, rather than a second measurement taken a different way.
    /// </summary>
    private sealed class GateTotals
    {
        public long Adjacent { get; private set; }

        public long RejectedByLegality { get; private set; }

        public long RejectedByRatio { get; private set; }

        public long Proposed { get; private set; }

        public long RulesetCannotSiege { get; private set; }

        public long TurnsWithAdjacency { get; private set; }

        public long BestRatioPermille { get; private set; } = -1;

        public string BestLine { get; private set; } = "(none)";

        public void Add(IReadOnlyList<string> transcript)
        {
            foreach (var raw in transcript)
            {
                var start = raw.IndexOf(GateLinePrefix, StringComparison.Ordinal);
                if (start < 0)
                {
                    continue;
                }

                TurnsWithAdjacency++;
                var counts = raw[(start + GateLinePrefix.Length)..].Split('|')[0].Split(',');
                Adjacent += LeadingNumber(counts[0]);
                RejectedByLegality += LeadingNumber(counts[1]);
                RejectedByRatio += LeadingNumber(counts[2]);
                Proposed += LeadingNumber(counts[3]);
                if (counts.Length > 4)
                {
                    RulesetCannotSiege += LeadingNumber(counts[4]);
                }

                var ratioAt = raw.IndexOf(", ratio ", StringComparison.Ordinal);
                if (ratioAt < 0)
                {
                    continue;
                }

                var ratio = LeadingNumber(raw[(ratioAt + ", ratio ".Length)..]);
                if (ratio > BestRatioPermille)
                {
                    BestRatioPermille = ratio;
                    BestLine = raw.Trim();
                }
            }
        }

        public string Report(string label)
        {
            var report = new StringBuilder();
            report.AppendLine(CultureInfo.InvariantCulture, $"siege-gate distribution -- {label}");
            report.AppendLine(CultureInfo.InvariantCulture, $"  turns with an adjacency      {TurnsWithAdjacency}");
            report.AppendLine(CultureInfo.InvariantCulture, $"  army/city pairs at adjacency {Adjacent}");
            report.AppendLine(CultureInfo.InvariantCulture, $"  (a) rejected by legality     {RejectedByLegality}");
            report.AppendLine(CultureInfo.InvariantCulture, $"  (b) rejected by ratio        {RejectedByRatio}");
            report.AppendLine(CultureInfo.InvariantCulture, $"  ruleset could not siege      {RulesetCannotSiege}");
            report.AppendLine(CultureInfo.InvariantCulture, $"  proposed                     {Proposed}");
            report.AppendLine(CultureInfo.InvariantCulture, $"  closest attempt: {BestLine}");
            return report.ToString();
        }

        /// <summary>
        /// The first integer in a fragment such as <c>" 23900 below ratio"</c> — enough parsing for a
        /// line this file's own sibling wrote, and no more.
        /// </summary>
        private static long LeadingNumber(string fragment)
        {
            var text = fragment.TrimStart();
            var end = 0;
            while (end < text.Length && char.IsAsciiDigit(text[end]))
            {
                end++;
            }

            return end == 0 ? 0 : long.Parse(text[..end], CultureInfo.InvariantCulture);
        }
    }
}
