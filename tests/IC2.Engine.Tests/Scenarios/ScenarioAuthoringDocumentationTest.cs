using System.Reflection;
using System.Text.RegularExpressions;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Scenarios;

/// <summary>
/// Reflection-driven test that asserts <c>docs/scenario-authoring.md</c> documents every public
/// property of <see cref="World"/>, <see cref="Ruleset"/>, and <see cref="Scenario"/>.
/// </summary>
public class ScenarioAuthoringDocumentationTest
{
    [Fact]
    public void ScenarioAuthoringDocNamesThenObjectsPublicProperties()
    {
        var docPath = FindRepositoryRoot();
        docPath = Path.Combine(docPath, "docs", "scenario-authoring.md");

        var docContent = File.ReadAllText(docPath);

        var worldProperties = GetPublicProperties(typeof(World));
        var rulesetProperties = GetPublicProperties(typeof(Ruleset));
        var scenarioProperties = GetPublicProperties(typeof(Scenario));

        var allProperties = worldProperties.Concat(rulesetProperties).Concat(scenarioProperties).Distinct().ToList();

        var missingProperties = new List<string>();
        foreach (var propertyName in allProperties)
        {
            // Properties in camelCase in JSON (by JsonPropertyName or default convention).
            var camelCaseName = ToCamelCase(propertyName);

            // Search for the property name in various contexts in the doc:
            // 1. In a markdown code block: `propertyName`
            // 2. In a table cell: | propertyName |
            // 3. In a backtick context
            if (!Regex.IsMatch(docContent, @"[`|\s]" + Regex.Escape(camelCaseName) + @"[`|\s]", RegexOptions.IgnoreCase))
            {
                missingProperties.Add(propertyName + " (camelCase: " + camelCaseName + ")");
            }
        }

        if (missingProperties.Any())
        {
            Assert.Fail($"The following properties are missing from docs/scenario-authoring.md:\n{string.Join("\n", missingProperties)}");
        }
    }

    /// <summary>
    /// Gets all public properties and fields of a type that would be serialized by System.Text.Json.
    /// Excludes indexer properties and properties marked with [JsonIgnore].
    /// </summary>
    private static IEnumerable<string> GetPublicProperties(Type type)
    {
        var properties = type
            .GetProperties(BindingFlags.Public | BindingFlags.IgnoreCase)
            .Where(p => p.GetMethod is not null && !p.IsSpecialName)
            .Where(p => !p.GetCustomAttributes().OfType<System.Text.Json.Serialization.JsonIgnoreAttribute>().Any())
            .Select(p =>
            {
                var jsonNameAttr = p.GetCustomAttribute<System.Text.Json.Serialization.JsonPropertyNameAttribute>();
                return jsonNameAttr?.Name ?? p.Name;
            });

        return properties;
    }

    /// <summary>Convert a PascalCase name to camelCase.</summary>
    private static string ToCamelCase(string pascalCaseName)
    {
        if (string.IsNullOrEmpty(pascalCaseName))
        {
            return pascalCaseName;
        }

        return char.ToLowerInvariant(pascalCaseName[0]) + pascalCaseName.Substring(1);
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
