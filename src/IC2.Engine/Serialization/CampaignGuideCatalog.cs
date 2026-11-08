using System.Text.Json;
using System.Text.Json.Serialization;

namespace IC2.Engine.Serialization;

/// <summary>Authored selection guidance, deliberately separate from simulation and saves.</summary>
public sealed record CampaignGuide(
    int SchemaVersion, string Id, string[] ScenarioIds, string Kind, string Period,
    string Background, string Abstractions, string Brief, string Validation,
    CampaignPowerAdvice[] Powers, CampaignGuideSource[] Sources);

public sealed record CampaignPowerAdvice(string NationId, string Focus, string Advice,
    bool Suggested, bool RequiresStartingFleet = false);
public sealed record CampaignGuideSource(string Title, string Url);

public sealed class CampaignGuideCatalog
{
    private readonly Dictionary<string, CampaignGuide> _byScenario = new(StringComparer.Ordinal);
    public CampaignGuide? ForScenario(string id) => _byScenario.GetValueOrDefault(id);

    public static CampaignGuideCatalog Load(GameDataRepository repository)
    {
        var catalog = new CampaignGuideCatalog();
        var folder = Path.Combine(repository.DataRoot, "campaign-guides");
        if (!Directory.Exists(folder)) return catalog;
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        var guideIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.GetFiles(folder, "*.json").Order(StringComparer.Ordinal))
        {
            var guide = JsonSerializer.Deserialize<CampaignGuide>(File.ReadAllText(file), options)
                ?? throw new InvalidDataException($"{file}: empty campaign guide");
            Validate(guide, repository);
            if (!guideIds.Add(guide.Id)) throw new InvalidDataException($"Duplicate guide {guide.Id}");
            foreach (var scenario in guide.ScenarioIds)
                if (!catalog._byScenario.TryAdd(scenario, guide))
                    throw new InvalidDataException($"Duplicate guidance for {scenario}");
        }
        return catalog;
    }

    public static void Validate(CampaignGuide guide, GameDataRepository repository)
    {
        static void Text(string? text, string field, int maximum = 900)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length > maximum)
                throw new InvalidDataException($"Campaign guide needs readable {field} (1–{maximum} characters)");
        }
        if (guide.SchemaVersion != 1) throw new InvalidDataException("Unsupported campaign guide schema");
        Text(guide.Id, "id", 100);
        if (guide.Kind is not ("historical" or "generated")) throw new InvalidDataException("Unknown campaign guide kind");
        Text(guide.Period, "period", 120); Text(guide.Background, "background");
        Text(guide.Abstractions, "abstractions"); Text(guide.Validation, "validation");
        Text(guide.Brief, "brief", 200);
        if (!guide.Brief.StartsWith("docs/", StringComparison.Ordinal) || guide.Brief.Contains(".."))
            throw new InvalidDataException("Campaign guide brief must be a repository docs path");
        if (guide.ScenarioIds is not { Length: > 0 } || guide.Powers is not { Length: > 0 }
            || guide.Sources is null || (guide.Kind == "historical" && guide.Sources.Length == 0))
            throw new InvalidDataException("Campaign guide needs scenarios, powers and historical sources");
        if (guide.ScenarioIds.Distinct(StringComparer.Ordinal).Count() != guide.ScenarioIds.Length
            || guide.Powers.Any(p => p is null) || guide.Sources.Any(s => s is null)
            || guide.Powers.Select(p => p.NationId).Distinct(StringComparer.Ordinal).Count() != guide.Powers.Length)
            throw new InvalidDataException("Duplicate campaign guide scenario/power");
        if (!guide.Powers.Any(p => p.Suggested)) throw new InvalidDataException("Campaign guide needs a suggested playable power");
        if (guide.Sources.Select(s => s.Url).Distinct(StringComparer.Ordinal).Count() != guide.Sources.Length)
            throw new InvalidDataException("Duplicate campaign guide source");
        foreach (var id in guide.ScenarioIds) Text(id, "scenario id", 100);
        foreach (var source in guide.Sources)
        {
            Text(source.Title, "source title", 200);
            if (!Uri.TryCreate(source.Url, UriKind.Absolute, out var uri) || uri.Scheme != "https")
                throw new InvalidDataException("Campaign guide source must use HTTPS");
        }
        foreach (var power in guide.Powers)
        {
            Text(power.NationId, "nation id", 100); Text(power.Focus, "focus", 100); Text(power.Advice, "advice", 600);
            foreach (var id in guide.ScenarioIds)
            {
                var resolved = repository.Resolve(id);
                if (resolved.Scenario.SeatFor(power.NationId) is null || resolved.World.NationById(power.NationId) is null
                    || power.NationId == resolved.Scenario.Barbarians?.NationId)
                    throw new InvalidDataException($"{id}: guide power {power.NationId} is not playable");
                if (power.RequiresStartingFleet && !resolved.World.StartingFleets.Any(f => f.Nation == power.NationId))
                    throw new InvalidDataException($"{id}: {power.NationId} has no starting fleet for naval advice");
            }
        }
    }
}
