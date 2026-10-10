using System.Text.RegularExpressions;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Scenarios;

/// <summary>
/// Reflection-driven test that asserts <c>docs/scenario-authoring.md</c> documents every JSON field of
/// <see cref="World"/>, <see cref="Ruleset"/> and <see cref="Scenario"/> — including, recursively, every
/// nested model type they serialize (a record, or the element type of a <see cref="ValueList{T}"/> of
/// one) — so the doc cannot silently go stale as new fields are added.
/// </summary>
/// <remarks>
/// Field names are asserted in their JSON (camelCase, or an explicit <c>JsonPropertyName</c> override
/// such as <c>"_provenance"</c>) spelling, via <see cref="JsonContract"/> — the same reflection-derived
/// contract <c>GameDataLoader</c>/<c>SchemaValidator</c> validate a loaded document against, so this test
/// and the loader can never disagree about what a field is called. <see cref="JsonContract.For"/>
/// already returns <see langword="null"/> for anything outside the model (primitives, <see cref="string"/>,
/// enums, and framework types), which is exactly where this walk needs to stop.
/// </remarks>
public class ScenarioAuthoringDocumentationTest
{
    /// <summary>
    /// Fields the model exposes but <c>docs/scenario-authoring.md</c> does not yet name. Empty by
    /// default; each entry is the responsibility of the PR that added the field to keep it small.
    /// T156 (issue #925) carries the 28 AI tree keys here because its brief told the implementer to
    /// list the rows under "Docs affected" instead of editing the doc, and the main session applies
    /// them post-merge.
    /// </summary>
    private static readonly IReadOnlyCollection<string> PendingDocumentation = new[]
    {
        "armyScoreCap",
        "armyScoreThreshold",
        "armyScoreWeakerDistanceThreshold",
        "armyScoreWeakerWithinBonus",
        "armyTargetStrengthNumerator",
        "cityAttackCapitalDefenseRatioDenominator",
        "cityAttackCapitalDefenseRatioNumerator",
        "cityAttackDistanceThreshold",
        "cityId",
        "cityRegionById",
        "cityScoreRegionHalvingDenominator",
        "cityScoreThreshold",
        "defendResupplyArmyScoreThreshold",
        "defendResupplyCityScoreThreshold",
        "demoralisedArmyDistanceThreshold",
        "demoralisedMoraleThreshold",
        "demoralisedSuppliesThreshold",
        "garrisonFallbackCapitalDistance",
        "mercenaryRunCityDistanceFar",
        "mercenaryRunOfferRange",
        "mercenaryRunTroopsDivisor",
        "region",
        "resupplyCapitalPenalty",
        "resupplyForeignBonus",
        "resupplyForeignMoneyDivisor",
        "resupplyForeignSupplyMargin",
        "resupplyMaxForeignDistance",
        "resupplyStrengthTroopsDivisor",
    };

    [Fact]
    public void ScenarioAuthoringDocNamesEveryModelField()
    {
        var docPath = Path.Combine(FindRepositoryRoot(), "docs", "scenario-authoring.md");
        var docContent = File.ReadAllText(docPath);

        var fieldNames = new SortedSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<Type>();
        CollectFieldNames(typeof(World), fieldNames, visited);
        CollectFieldNames(typeof(Ruleset), fieldNames, visited);
        CollectFieldNames(typeof(Scenario), fieldNames, visited);

        // The walk must be doing real recursive work, not silently finding nothing -- this is exactly
        // the failure mode PR #434's review found: GetProperties(BindingFlags.Public |
        // BindingFlags.IgnoreCase), missing Instance/Static, matched zero members, so the old test's
        // "assert every found property is named in the doc" passed unconditionally regardless of what
        // the doc said. A field several nested levels deep in Ruleset's economy block, only reachable if
        // the walk actually recurses through EconomyRules -> SupplyConsumptionRules, is a canary for that:
        // if this line ever fails, the walk stopped finding fields again, not that the doc regressed.
        Assert.Contains("citySupplyCapTonsPerPopulationThousand", fieldNames);
        Assert.True(
            fieldNames.Count > 250,
            "Expected well over 250 distinct JSON field names across World/Ruleset/Scenario and their "
            + $"nested model types; found {fieldNames.Count}. The reflection walk may have stopped early "
            + "(e.g. a type moved out of the IC2.Engine.Model namespace, or JsonContract.For changed shape).");

        var missing = new List<string>();
        foreach (var fieldName in fieldNames)
        {
            if (PendingDocumentation.Contains(fieldName))
            {
                continue;
            }

            // Matches the field name in a markdown code span (`fieldName`), a table cell
            // (| fieldName |), or plain prose -- any context where the doc names the field at all.
            if (!Regex.IsMatch(docContent, @"[`|\s]" + Regex.Escape(fieldName) + @"[`|\s]"))
            {
                missing.Add(fieldName);
            }
        }

        if (missing.Count > 0)
        {
            Assert.Fail(
                "The following JSON field names (from World, Ruleset, Scenario, and every nested model "
                + "type they serialize) are missing from docs/scenario-authoring.md:\n"
                + string.Join("\n", missing));
        }
    }

    /// <summary>
    /// Recursively collects the JSON field names of <paramref name="type"/>'s <see cref="JsonContract"/>
    /// into <paramref name="names"/>, and recurses into every member whose type -- or, for a
    /// <see cref="ValueList{T}"/> member, whose element type -- is itself a model type with its own
    /// contract. Stops where <see cref="JsonContract.For"/> returns <see langword="null"/> (a primitive,
    /// <see cref="string"/>, an enum, or a type outside <c>IC2.Engine.Model</c> -- including
    /// <see cref="ProvenanceMap"/>, which carries its own <see cref="System.Text.Json.Serialization.JsonConverterAttribute"/>
    /// and is therefore opaque to the walk, exactly as it is to schema validation). <paramref name="visited"/>
    /// guards against a cycle (none exists in the model today, but nothing here assumes that stays true).
    /// </summary>
    private static void CollectFieldNames(Type type, ISet<string> names, ISet<Type> visited)
    {
        var contract = JsonContract.For(type);
        if (contract is null || !visited.Add(type))
        {
            return;
        }

        foreach (var member in contract.Members)
        {
            names.Add(member.JsonName);

            var elementType = JsonContract.ValueListElementType(member.Type);
            CollectFieldNames(elementType ?? member.Type, names, visited);
        }
    }

    /// <summary>Finds the repository root by looking for the <c>IC2.sln</c> file.</summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IC2.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find the repository root (IC2.sln not found).");
    }
}
