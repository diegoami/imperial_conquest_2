using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using IC2.Engine.Tests.TestInfrastructure;
using Xunit;

namespace IC2.Engine.Tests.Export;

/// <summary>
/// Bug #795: <c>scripts/export-classical-world.cs</c>'s <c>AnnotateProvenance</c> appended the
/// "T04 fixtures corpus id" citation to a key's <c>_provenance</c> even when the source text copied
/// from <c>toy-ruleset.json</c> already carried it, so the regenerated
/// <c>classical-faithful.json</c> repeated it. Seen on T146's three new <c>RulesetCorpusMap</c> rows
/// (<c>recruitment.mercenaryHireRangeHumanSeat</c>, <c>recruitment.mercenaryHireRangeAiSeat</c> and
/// <c>recruitment.mercenaryAiHireMinMoney</c>).
/// </summary>
/// <remarks>
/// The reproduction runs against the committed export output -- the artifact the exporter writes.
/// Before the fix the three strings carried the citation twice, so
/// <see cref="Classical_faithful_never_repeats_a_corpus_id_citation"/> and the theory below failed;
/// after the fix and a regeneration they carry it once and both pass. The general invariant is "no
/// <c>_provenance</c> string names the same T04 fixtures corpus id twice"; the theory pins the
/// exact three keys the bug reported.
/// </remarks>
public class ProvenanceCitationTests
{
    private static readonly Regex CorpusIdCitation =
        new(@"T04 fixtures corpus id: '(?<id>[^']+)'", RegexOptions.Compiled);

    /// <summary>
    /// Bug #795's reproduction: every <c>T04 fixtures corpus id: '...'</c> citation inside a single
    /// <c>_provenance</c> string must be unique. On the unmodified tree this fails on the three T146
    /// keys named above, each citing its id twice.
    /// </summary>
    [Fact]
    public void Classical_faithful_never_repeats_a_corpus_id_citation()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(ExportedDataPaths.RulesetFile));
        var repeats = FindRepeatedCitations(doc.RootElement, path: "");

        Assert.True(repeats.Count == 0,
            "classical-faithful.json repeats a T04 fixtures corpus-id citation inside one _provenance " +
            "string (bug #795: AnnotateProvenance appended a citation the toy-ruleset.json source " +
            "already carried). Fix scripts/export-classical-world.cs and regenerate, never hand-edit " +
            "the committed file:\n  " + string.Join("\n  ", repeats));
    }

    /// <summary>
    /// Bug #795, spot check: the three T146 <c>RulesetCorpusMap</c> rows named in the report each
    /// cite their corpus id exactly once. This is the general invariant above narrowed to the keys
    /// the bug was actually seen on, so the regression cannot hide behind a coincidental duplicate
    /// elsewhere.
    /// </summary>
    [Theory]
    [InlineData("mercenaryHireRangeHumanSeat", "mercenary.playerHireRange")]
    [InlineData("mercenaryHireRangeAiSeat", "mercenary.aiHireRadius")]
    [InlineData("mercenaryAiHireMinMoney", "mercenary.aiHireMoneyThreshold")]
    public void The_three_T146_mercenary_keys_cite_their_corpus_id_exactly_once(string key, string corpusId)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(ExportedDataPaths.RulesetFile));
        var text = doc.RootElement.GetProperty("recruitment").GetProperty("_provenance").GetProperty(key).GetString()!;
        var count = CorpusIdCitation.Matches(text)
            .Count(match => string.Equals(match.Groups["id"].Value, corpusId, StringComparison.Ordinal));

        Assert.True(count == 1,
            $"recruitment._provenance.{key} cites corpus id '{corpusId}' {count} times, expected exactly 1:\n{text}");
    }

    /// <summary>
    /// Recursively collects, per <c>_provenance</c> string, every corpus id it cites more than once,
    /// as a <c>&lt;json-path&gt;: '&lt;id&gt;' cited N times</c> line (mirrors the shape
    /// <c>NoToyProvenanceWordingTests.FindToyProvenanceMentions</c> already uses for its own scan).
    /// </summary>
    private static List<string> FindRepeatedCitations(JsonElement element, string path)
    {
        var repeats = new List<string>();

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var childPath = path.Length == 0 ? property.Name : $"{path}.{property.Name}";
                    if (property.Name == "_provenance" && property.Value.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var provenanceProperty in property.Value.EnumerateObject())
                        {
                            if (provenanceProperty.Value.ValueKind != JsonValueKind.String)
                            {
                                continue;
                            }

                            var text = provenanceProperty.Value.GetString()!;
                            foreach (var sameId in CorpusIdCitation.Matches(text)
                                         .GroupBy(match => match.Groups["id"].Value, StringComparer.Ordinal))
                            {
                                var count = sameId.Count();
                                if (count > 1)
                                {
                                    repeats.Add($"{childPath}.{provenanceProperty.Name}: '{sameId.Key}' cited {count} times");
                                }
                            }
                        }
                    }
                    else
                    {
                        repeats.AddRange(FindRepeatedCitations(property.Value, childPath));
                    }
                }
                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    repeats.AddRange(FindRepeatedCitations(item, $"{path}[{index}]"));
                    index++;
                }
                break;
        }

        return repeats;
    }
}

/// <summary>
/// Bug #795 R2: the exporter-behaviour guard the committed-JSON tests above cannot give. Those tests
/// reproduce the bad <em>artifact</em>; deleting the exporter's deduplication behaviour while leaving
/// <c>classical-faithful.json</c> unchanged would still pass them. This test drives the real
/// <c>AnnotateProvenance</c> (through the script's DAT-free <c>--provenance-probe</c> entry point)
/// on toy inputs, so it fails if the dedup is removed or weakened. It needs no original game files.
/// </summary>
/// <remarks>
/// Joins <see cref="DotnetRunScriptCollection"/> because it starts a real
/// <c>dotnet run scripts/export-classical-world.cs</c>, through the shared
/// <see cref="DotnetRunScriptRunner"/>, reusing the same per-script build-output subdirectory
/// <see cref="ExportScriptReproducibilityTests"/> uses (the collection runs every dotnet-run test
/// sequentially, so sharing one subdirectory for one script is safe and avoids a second cold build).
/// </remarks>
[Collection(DotnetRunScriptCollection.Name)]
public class ProvenanceCitationExporterTests
{
    public ProvenanceCitationExporterTests(DotnetRunArtifactsFixture artifacts)
    {
        _artifacts = artifacts;
    }

    private readonly DotnetRunArtifactsFixture _artifacts;

    private const string Citation = "T04 fixtures corpus id: 'mercenary.playerHireRange'.";
    private const string Suffix = " " + Citation;

    /// <summary>
    /// The four toy inputs: a note already carrying the complete citation (must be left untouched), a
    /// note without it (appended once), a note carrying the citation text inside a longer run of
    /// characters -- bug #795 R1's substring case (still appended), and an empty note (set to the
    /// citation). A plain <c>Contains</c> dedup fails the third; deleting the dedup fails the first.
    /// </summary>
    [Fact]
    public void AnnotateProvenance_dedupes_only_a_complete_citation_on_a_boundary()
    {
        var already = "confirmed: the order does nothing. " + Citation;
        var absent = "confirmed: the order does nothing.";
        // R1: the note contains the citation's text, but immediately followed by further characters --
        // it is the start of a longer citation, not a complete citation occurrence of its own.
        var longer = "confirmed: the order does nothing. " + Citation + "FollowedByMoreCharacters";
        // R1, other boundary: the citation's text is preceded by a non-whitespace character.
        var joined = "confirmed: no whitespace before the citation." + Citation;

        var cases = new JsonObject
        {
            ["already"] = ProbeCase(already),
            ["absent"] = ProbeCase(absent),
            ["longer"] = ProbeCase(longer),
            ["joined"] = ProbeCase(joined),
        };

        var casesPath = Path.Combine(
            Path.GetTempPath(), "ic2-provenance-probe-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(casesPath, cases.ToJsonString());

            var result = DotnetRunScriptRunner.Run(
                ExportedDataPaths.ExportScript,
                Path.Combine(_artifacts.ArtifactsPath, "export-classical-world"),
                ExportedDataPaths.RepositoryRoot,
                "--provenance-probe",
                casesPath);

            Assert.True(result.ExitCode == 0,
                $"export-classical-world.cs --provenance-probe exited {result.ExitCode}.\nstdout:\n{result.Stdout}\nstderr:\n{result.Stderr}");

            var actual = ParseProbeOutput(result.Stdout);
            Assert.Equal(already, actual["already"]!.GetValue<string>());
            Assert.Equal(absent + Suffix, actual["absent"]!.GetValue<string>());
            Assert.Equal(longer + Suffix, actual["longer"]!.GetValue<string>());
            Assert.Equal(joined + Suffix, actual["joined"]!.GetValue<string>());
        }
        finally
        {
            if (File.Exists(casesPath))
            {
                File.Delete(casesPath);
            }
        }
    }

    private static JsonObject ProbeCase(string existing) =>
        new() { ["existing"] = existing, ["suffix"] = Suffix };

    /// <summary>
    /// Reads the probe's own JSON object out of <paramref name="stdout"/>, ignoring any
    /// <c>dotnet run</c> build output around it: the last line that parses as a JSON object wins.
    /// </summary>
    private static JsonObject ParseProbeOutput(string stdout)
    {
        JsonObject? last = null;
        foreach (var raw in stdout.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith('{') || !line.EndsWith('}'))
            {
                continue;
            }

            try
            {
                last = JsonNode.Parse(line)?.AsObject() ?? last;
            }
            catch (JsonException)
            {
                // Not the probe's line (or truncated build output) -- keep looking.
            }
        }

        Assert.NotNull(last);
        return last!;
    }
}
