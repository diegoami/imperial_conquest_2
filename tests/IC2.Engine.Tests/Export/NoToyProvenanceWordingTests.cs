using System.Text.Json;
using Xunit;

namespace IC2.Engine.Tests.Export;

/// <summary>
/// Bug #299's guard: no <c>_provenance</c> string in either shipped preset may be written from
/// <c>toy-ruleset.json</c>'s own point of view. <c>scripts/export-classical-world.cs</c> builds
/// <c>classical-faithful.json</c> by loading <c>toy-ruleset.json</c> whole and copying every
/// <c>_provenance</c> string it doesn't specifically re-annotate verbatim, so a toy-ruleset-specific
/// note (e.g. "this toy ruleset is set the way classical-faithful is set") ships inside the faithful
/// preset unchanged. The rule this repository follows is that <c>toy-ruleset.json</c>'s own
/// provenance wording is written preset-neutrally -- never naming a specific ruleset file -- so that
/// copying it verbatim can never say something false about the file it lands in. This test is that
/// rule's safety net over the two shipped, committed presets; the export script carries the matching
/// safety net over its own output (throws before writing, mirroring its other DoD checks).
/// </summary>
public class NoToyProvenanceWordingTests
{
    [Fact]
    public void Classical_faithful_has_no_toy_provenance_wording()
    {
        AssertNoToyProvenanceWording(ExportedDataPaths.RulesetFile);
    }

    [Fact]
    public void Improved_has_no_toy_provenance_wording()
    {
        AssertNoToyProvenanceWording(ExportedDataPaths.ImprovedRulesetFile);
    }

    /// <summary>
    /// Regression proof: the exact text bug #299 reported at <c>classical-faithful.json:870</c> on
    /// `main` at 7e18d83 -- reintroduced here as an in-memory document, never written to a committed
    /// file -- must still fail this guard. If a future edit to the detector narrows it so this passes,
    /// that is the same defect #299 reported, back again.
    /// </summary>
    [Fact]
    public void The_original_299_text_fails_the_guard()
    {
        const string regressed = """
            {
              "flags": {
                "_provenance": {
                  "diplomacyModel": "docs/game-design.md §'Two shipped presets' — this toy ruleset is set the way classical-faithful is set, so tests exercise the confirmed behaviour by default (audit Q3)."
                }
              }
            }
            """;

        var hits = FindToyProvenanceMentions(JsonDocument.Parse(regressed).RootElement);

        Assert.Single(hits);
        Assert.Contains("flags._provenance.diplomacyModel", hits[0]);
    }

    /// <summary>
    /// The guard scans only strings inside an object named <c>_provenance</c>; a "toy" mention
    /// elsewhere in the document (here, the top-level <c>id</c>, exactly like <c>toy-ruleset.json</c>'s
    /// own <c>"id": "toy-ruleset"</c>) must not trip it, or the guard would reject every field the
    /// exporter copies from a file legitimately named that.
    /// </summary>
    [Fact]
    public void A_toy_mention_outside_provenance_produces_no_hits()
    {
        const string clean = """
            {
              "flags": {
                "_provenance": {
                  "diplomacyModel": "docs/game-design.md §'Two shipped presets' — the confirmed state machine only, with no AI opinion-score layer (audit Q3)."
                }
              },
              "id": "toy-ruleset"
            }
            """;

        var hits = FindToyProvenanceMentions(JsonDocument.Parse(clean).RootElement);

        Assert.Empty(hits);
    }

    private static void AssertNoToyProvenanceWording(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var hits = FindToyProvenanceMentions(doc.RootElement);

        Assert.True(hits.Count == 0,
            $"{Path.GetFileName(path)} carries a _provenance string written from toy-ruleset.json's own " +
            $"point of view (bug #299) -- fix the wording in toy-ruleset.json and regenerate through the " +
            $"exporter, never hand-edit the committed file:\n  " + string.Join("\n  ", hits));
    }

    /// <summary>
    /// Recursively finds every string value inside a JSON object literally named
    /// <c>_provenance</c> (anywhere in the document) that mentions "toy" case-insensitively. Mirrors
    /// <c>export-classical-world.cs</c>'s own <c>FindToyProvenanceMentions</c> local function -- kept
    /// as a separate implementation deliberately, the same way <c>ExportScriptReproducibilityTests</c>
    /// re-runs the script as a subprocess rather than sharing its internals, so this test proves the
    /// committed file is clean independently of whatever the script's own guard did or didn't catch.
    /// </summary>
    private static List<string> FindToyProvenanceMentions(JsonElement element, string path = "")
    {
        var hits = new List<string>();

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
                            if (provenanceProperty.Value.ValueKind == JsonValueKind.String)
                            {
                                var text = provenanceProperty.Value.GetString() ?? "";
                                if (text.Contains("toy", StringComparison.OrdinalIgnoreCase))
                                {
                                    hits.Add($"{childPath}.{provenanceProperty.Name}: \"{text}\"");
                                }
                            }
                        }
                    }
                    else
                    {
                        hits.AddRange(FindToyProvenanceMentions(property.Value, childPath));
                    }
                }
                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    hits.AddRange(FindToyProvenanceMentions(item, $"{path}[{index}]"));
                    index++;
                }
                break;
        }

        return hits;
    }
}
