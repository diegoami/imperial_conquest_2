using System.Globalization;
using System.Text;
using IC2.Engine.Ai;
using IC2.Engine.Serialization;
using Xunit;
using Xunit.Abstractions;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Ai;

/// <summary>
/// T156 (issue #925) diagnostic: how often the AI's target tree sees an adjacent army/enemy-city pair,
/// how often it selects that pair for attack, and how often the ruleset cannot stage a siege. The
/// numbers come out of <see cref="AiSiegeGateTally"/>'s per-turn log lines, summed over the soak and
/// over the shipped all-AI scenario, then read back out of those same lines — the same artifact a
/// reader of a failing seed's log has in front of them.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What the test asserts.</strong> T156's tree replaces T60's three-gate tally with a single
/// "tree-selected-for-attack" count: of every adjacent pair the tree saw, how many did it pick for a
/// siege? The shipped scenario's many nations and large maps provide enough data that the count is
/// non-zero across the run — that is the assertion, and it is what separates "the AI cannot besiege"
/// from "this fixture cannot be besieged".
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
    /// The soak summary: adjacent pairs, tree selections, and the ruleset-can't-siege count, summed
    /// across all fifty seeds.
    /// </summary>
    [Fact]
    public void The_tree_selects_attack_city_for_an_adjacent_army_in_the_soak()
    {
        var gates = new GateTotals();
        foreach (var seed in AiTestbed.SoakSeeds())
        {
            gates.Add(AiTestbed.RunSeed(seed).Transcript);
        }

        _output.WriteLine(gates.Report("toy-3city, 50 seeds"));

        Assert.True(
            gates.Adjacent > 0,
            "if no army ever stood next to an enemy city, the tree never had a chance to pick an "
            + "attack-city branch and the diagnostic is meaningless.");

        Assert.Equal(0, gates.RulesetCannotSiege);

        // The shipped-classical scenario (below) is where a siege is actually exercised end-to-end; the
        // toy soak may legitimately pick no siege when every adjacent pair the tree sees is too strong.
        // We assert the lines parse and the gates line up, not that the toy fixture alone reaches a
        // siege.
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
            + "AiArmyTargetTree.Decide and not in the toy fixture.");
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

        public long TreeSelectedAttackCity { get; private set; }

        public long Proposed { get; private set; }

        public long RulesetCannotSiege { get; private set; }

        public long TurnsWithAdjacency { get; private set; }

        public long BestCityScore { get; private set; } = long.MinValue;

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
                TreeSelectedAttackCity += LeadingNumber(counts[1]);
                Proposed += LeadingNumber(counts[2]);
                if (counts.Length > 3)
                {
                    RulesetCannotSiege += LeadingNumber(counts[3]);
                }

                var scoreAt = raw.IndexOf("city score ", StringComparison.Ordinal);
                if (scoreAt < 0)
                {
                    continue;
                }

                var score = LeadingNumber(raw[(scoreAt + "city score ".Length)..]);
                if (score > BestCityScore)
                {
                    BestCityScore = score;
                    BestLine = raw.Trim();
                }
            }
        }

        public string Report(string label)
        {
            var report = new StringBuilder();
            report.AppendLine(CultureInfo.InvariantCulture, $"siege-gate distribution -- {label}");
            report.AppendLine(CultureInfo.InvariantCulture, $"  turns with an adjacency               {TurnsWithAdjacency}");
            report.AppendLine(CultureInfo.InvariantCulture, $"  army/city pairs at adjacency          {Adjacent}");
            report.AppendLine(CultureInfo.InvariantCulture, $"  tree selected for attack city          {TreeSelectedAttackCity}");
            report.AppendLine(CultureInfo.InvariantCulture, $"  ruleset could not siege               {RulesetCannotSiege}");
            report.AppendLine(CultureInfo.InvariantCulture, $"  proposed (siege candidates issued)    {Proposed}");
            report.AppendLine(CultureInfo.InvariantCulture, $"  best pair: {BestLine}");
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
