using IC2.Engine.Model;

namespace IC2.Engine.Serialization;

/// <summary>A scenario together with the world and ruleset its ids resolve to.</summary>
public sealed record ResolvedScenario(Scenario Scenario, World World, Ruleset Ruleset);

/// <summary>
/// The set of worlds, rulesets and scenarios found under a data directory, indexed by the id each file
/// declares (not by its filename), so a scenario's <c>world</c>/<c>ruleset</c> references resolve.
/// </summary>
/// <remarks>
/// Files are read in ordinal filename order and kept in that order, so two runs over the same directory
/// produce the same repository — the determinism rule applies to loading as much as to play.
/// </remarks>
public sealed class GameDataRepository
{
    /// <summary>The conventional sub-directory names under a data root.</summary>
    public const string WorldsDirectoryName = "worlds";

    /// <inheritdoc cref="WorldsDirectoryName"/>
    public const string RulesetsDirectoryName = "rulesets";

    /// <inheritdoc cref="WorldsDirectoryName"/>
    public const string ScenariosDirectoryName = "scenarios";

    private GameDataRepository(
        string dataRoot,
        ValueList<World> worlds,
        ValueList<Ruleset> rulesets,
        ValueList<Scenario> scenarios)
    {
        DataRoot = dataRoot;
        Worlds = worlds;
        Rulesets = rulesets;
        Scenarios = scenarios;
    }

    /// <summary>The directory this repository was loaded from.</summary>
    public string DataRoot { get; }

    /// <summary>Every world found, in ordinal filename order.</summary>
    public ValueList<World> Worlds { get; }

    /// <summary>Every ruleset found, in ordinal filename order.</summary>
    public ValueList<Ruleset> Rulesets { get; }

    /// <summary>Every scenario found, in ordinal filename order.</summary>
    public ValueList<Scenario> Scenarios { get; }

    /// <summary>
    /// Loads every <c>*.json</c> under <paramref name="dataRoot"/>'s <c>worlds</c>, <c>rulesets</c> and
    /// <c>scenarios</c> directories. A directory that does not exist contributes nothing; a file that
    /// does exist but cannot be read throws.
    /// </summary>
    /// <exception cref="GameDataException">A file in the data set is not loadable.</exception>
    public static GameDataRepository Load(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);

        var worlds = LoadAll<World>(Path.Combine(dataRoot, WorldsDirectoryName));
        var rulesets = LoadAll<Ruleset>(Path.Combine(dataRoot, RulesetsDirectoryName));
        var scenarios = LoadAll<Scenario>(Path.Combine(dataRoot, ScenariosDirectoryName));

        RequireDistinctIds(worlds, w => w.Id, "world");
        RequireDistinctIds(rulesets, r => r.Id, "ruleset");
        RequireDistinctIds(scenarios, s => s.Id, "scenario");

        return new GameDataRepository(dataRoot, worlds, rulesets, scenarios);
    }

    /// <summary>Finds a world by the id declared inside it, or <see langword="null"/>.</summary>
    public World? WorldById(string id) => Worlds.FindById(w => w.Id, id);

    /// <summary>Finds a ruleset by the id declared inside it, or <see langword="null"/>.</summary>
    public Ruleset? RulesetById(string id) => Rulesets.FindById(r => r.Id, id);

    /// <summary>Finds a scenario by the id declared inside it, or <see langword="null"/>.</summary>
    public Scenario? ScenarioById(string id) => Scenarios.FindById(s => s.Id, id);

    /// <summary>Resolves a scenario's world and ruleset references.</summary>
    /// <exception cref="UnresolvedReferenceException">
    /// The scenario, or a world/ruleset it names, is not present in this data set.
    /// </exception>
    public ResolvedScenario Resolve(string scenarioId)
    {
        var scenario = ScenarioById(scenarioId)
                       ?? throw new UnresolvedReferenceException(DataRoot, "scenario", scenarioId);
        var world = WorldById(scenario.WorldId)
                    ?? throw new UnresolvedReferenceException(scenario.Id, "world", scenario.WorldId);
        var ruleset = RulesetById(scenario.RulesetId)
                      ?? throw new UnresolvedReferenceException(scenario.Id, "ruleset", scenario.RulesetId);
        return new ResolvedScenario(scenario, world, ruleset);
    }

    /// <summary>Resolves a scenario and builds the state it starts from.</summary>
    public GameState CreateInitialState(string scenarioId)
    {
        var resolved = Resolve(scenarioId);
        return GameStateFactory.CreateInitial(resolved.World, resolved.Ruleset, resolved.Scenario);
    }

    private static ValueList<T> LoadAll<T>(string directory)
        where T : IVersionedDocument
    {
        if (!Directory.Exists(directory))
        {
            return ValueList<T>.Empty;
        }

        var files = Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly);
        Array.Sort(files, StringComparer.Ordinal);

        var documents = new T[files.Length];
        for (var i = 0; i < files.Length; i++)
        {
            documents[i] = GameDataLoader.LoadFile<T>(files[i]);
        }

        return ValueList<T>.Of(documents);
    }

    private static void RequireDistinctIds<T>(ValueList<T> documents, Func<T, string> idSelector, string kind)
    {
        if (documents.FirstDuplicateId(idSelector) is { } duplicate)
        {
            throw new MalformedGameDataException(
                kind, $"two {kind} files both declare id '{duplicate}'.");
        }
    }
}
