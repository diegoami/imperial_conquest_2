using System.Text.Json;
using Xunit;

namespace IC2.Engine.Tests.Export;

/// <summary>
/// Bug #299's guard: no <c>_provenance</c> string in either shipped preset may be written from
/// <c>toy-ruleset.json</c>'s own point of view. <c>scripts/export-classical-world.cs</c> builds
/// <c>classical-faithful.json</c> by loading <c>toy-ruleset.json</c> whole and copying every
/// <c>_provenance</c> string it doesn't specifically re-annotate verbatim, so a toy-ruleset-specific
/// note (e.g. "this toy ruleset is set the way classical-faithful is set") ships inside the faithful
/// preset unchanged.
/// </summary>
/// <remarks>
/// THE RULE (stated once in <c>export-classical-world.cs</c>'s own "#299 guard" comment -- keep the
/// two in sync): a <c>_provenance</c> string never describes the file it sits in, with exactly one
/// exception, <c>_provenance.id</c>, which the exporter owns explicitly (the same way it owns
/// <c>id</c>/<c>name</c>/<c>description</c>), precisely because describing the file it's in is that
/// key's entire job. Naming a preset ("classical-faithful" or "improved") is fine as long as the
/// statement is true of that flag's value in EITHER file -- five notes do this today
/// (<c>flags._provenance.{combatOnDefeat,faithfulThawColumnBug}</c>,
/// <c>combat.scatteredDefeat._provenance.survivorCasualtyNumerator</c>,
/// <c>victory._provenance.{defaultCondition,defaultTurnLimit}</c>) -- what the rule forbids is a
/// claim that is only true of the file it happens to be copied into (bug #299's <c>:870</c>, and
/// review round 1 B1's <c>_provenance.id</c>, both said something true of <c>toy-ruleset.json</c>
/// and false of <c>classical-faithful.json</c>).
/// <para>
/// This test is the rule's safety net over the two shipped, committed presets, scanning for the
/// literal word "toy" -- a narrower, mechanical check than the rule itself, since not every
/// violation has to use that word (see <c>_provenance.id</c>'s B1 instance, which the export
/// script owns explicitly rather than relying on this scan to catch). The export script carries
/// the matching safety net over its own output (throws before writing, mirroring its other DoD
/// checks) plus a second, specific check that <c>_provenance.id</c> round-trips to its own owned
/// text.
/// </para>
/// <para>
/// Round 2 R1-B1: the top-level <c>_provenance.id</c> the exporter writes legitimately names
/// <c>toy-ruleset.json</c> -- it is the one key whose job is to describe where the file's data
/// actually comes from, and for <c>classical-faithful.json</c> that is genuinely
/// <c>toy-ruleset.json</c> itself, cross-checked against the corpus wherever the corpus has a
/// matching id (not the DAT directly -- R1-B1's own finding). <see cref="FindToyProvenanceMentions"/>
/// therefore skips that one key, mirroring the export script's identical exemption in its own copy
/// of this function, for the identical reason. Round 3 R2-N2: "toy-ruleset.json's own confirmed
/// constants" overclaimed -- the exporter copies every value, designed placeholders included, and
/// only cross-checks the ones with a corpus id; corrected below and in the script's own text.
/// </para>
/// </remarks>
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

    /// <summary>
    /// Round 2 R1-B1: the root document's own <c>_provenance.id</c> is the exporter-owned
    /// exception (see the class remarks) and may legitimately name <c>toy-ruleset.json</c> --
    /// exactly what the real exporter now writes.
    /// </summary>
    [Fact]
    public void The_root_documents_own_provenance_id_is_exempt()
    {
        const string realExporterOutput = """
            {
              "_provenance": {
                "id": "One of the two shipped presets (docs/game-design.md §'Two shipped presets'). Generated by task T29's exporter from toy-ruleset.json (confirmed and designed values alike; see 'description' above) and cross-checked here against the T04 fixtures corpus wherever the corpus has a matching id; not the small, hand-authored ruleset kept for unit tests to load."
              }
            }
            """;

        var hits = FindToyProvenanceMentions(JsonDocument.Parse(realExporterOutput).RootElement);

        Assert.Empty(hits);
    }

    /// <summary>
    /// The exemption is scoped to the root document's own <c>_provenance.id</c> only -- a nested
    /// object's <c>_provenance.id</c> (none exist in the shipped rulesets today, but nothing stops
    /// one existing tomorrow) is still scanned normally, so this proves the exemption is not a
    /// blanket "any key named id" carve-out.
    /// </summary>
    [Fact]
    public void A_nested_provenance_id_is_not_exempt()
    {
        const string nested = """
            {
              "someNestedThing": {
                "_provenance": {
                  "id": "this toy identifier is not the root document's own _provenance.id"
                }
              }
            }
            """;

        var hits = FindToyProvenanceMentions(JsonDocument.Parse(nested).RootElement);

        Assert.Single(hits);
        Assert.Contains("someNestedThing._provenance.id", hits[0]);
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
    /// <c>_provenance</c> (anywhere in the document) that mentions "toy" case-insensitively,
    /// except the root document's own <c>_provenance.id</c> (see the class remarks). Mirrors
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
                            if (path.Length == 0 && provenanceProperty.Name == "id")
                            {
                                continue; // the exporter-owned exception; see the class remarks.
                            }

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
