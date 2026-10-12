using IC2.Engine.Import;
using IC2.Engine.Model;
using IC2.Engine.Persistence;
using IC2.Engine.Presentation;
using IC2.Engine.Serialization;

namespace IC2.Slice.UI;

/// <summary>
/// T165 (#1021, from bug #1002): opens a chosen file path from the Load game screen. No Godot types, so
/// <c>tests/IC2.Engine.Tests/Ui/LoadGameModelTests.cs</c> links this file and exercises it in plain xunit.
/// A <c>.json</c> goes through <see cref="SaveManager.LoadFile"/> exactly as a listed save always did; a
/// <c>.sav</c> goes through <see cref="OriginalSaveImporter.Import"/> onto the shipped
/// <see cref="OriginalSaveImporter.RequiredWorldId"/> world and ruleset, with the shipped scenario of the
/// same id supplying the seed. Either returns a session or a message naming the file; nothing throws.
/// </summary>
/// <remarks>
/// <strong>[designed]</strong> a <c>.sav</c> with no human seat is refused with a message. Searched: the
/// importer seats each nation from the save's own human flag (<c>OriginalSaveImporter.cs</c>), and the
/// 54-save fixtures corpus; the original game always has at least one human nation, so a save without one
/// is corrupt or not a game in progress, and a session with no human seat has nobody to play.
/// </remarks>
public static class LoadGameModel
{
    /// <summary>The file extensions Browse offers, lower case.</summary>
    public static readonly IReadOnlyList<string> OpenableExtensions = new[] { ".json", ".sav" };

    /// <summary>What opening a path produced: a session (with the path it came from), or a message.</summary>
    public sealed record Outcome(GameSession? Session, string? Message, string Path)
    {
        public bool Succeeded => Session is not null;
    }

    /// <summary>True when <paramref name="path"/> ends in an extension Browse offers (case-insensitive).</summary>
    public static bool IsOpenable(string path) =>
        OpenableExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>The files of <paramref name="folder"/> (top level only) that Browse offers, sorted by name.</summary>
    public static IReadOnlyList<string> ListOpenable(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return Array.Empty<string>();
        }

        return Directory.GetFiles(folder, "*", SearchOption.TopDirectoryOnly)
            .Where(IsOpenable)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Opens <paramref name="path"/>; every failure becomes a message naming the file.</summary>
    public static Outcome Open(string path, GameDataRepository repository)
    {
        var name = Path.GetFileName(path);
        try
        {
            if (!File.Exists(path))
            {
                return Fail(path, $"Could not open '{name}': the file does not exist.");
            }

            return Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".json" => OpenJson(path, repository),
                ".sav" => OpenOriginal(path, repository),
                _ => Fail(path, $"Could not open '{name}': only .json and .sav files can be loaded."),
            };
        }
        catch (Exception ex)
        {
            return Fail(path, $"Could not open '{name}': {ex.Message}");
        }
    }

    private static Outcome OpenJson(string path, GameDataRepository repository)
    {
        var name = Path.GetFileName(path);
        SaveSummary summary;
        try
        {
            summary = SaveManager.PeekSummaryFile(path);
        }
        catch (Exception ex)
        {
            return Fail(path, $"Could not read '{name}': {ex.Message}");
        }

        var world = repository.WorldById(summary.WorldId);
        var ruleset = repository.RulesetById(summary.RulesetId);
        var scenario = repository.ScenarioById(summary.ScenarioId);
        if (world is null || ruleset is null || scenario is null)
        {
            return Fail(
                path,
                $"'{name}' names a world/ruleset/scenario this build does not have "
                + $"(world '{summary.WorldId}', ruleset '{summary.RulesetId}', scenario '{summary.ScenarioId}').");
        }

        SaveGame save;
        try
        {
            save = SaveManager.LoadFile(path, world, ruleset);
        }
        catch (Exception ex)
        {
            return Fail(path, $"Could not load '{name}': {ex.Message}");
        }

        return new Outcome(new GameSession(world, ruleset, scenario, save), null, path);
    }

    private static Outcome OpenOriginal(string path, GameDataRepository repository)
    {
        var name = Path.GetFileName(path);
        var world = repository.WorldById(OriginalSaveImporter.RequiredWorldId);
        var ruleset = repository.RulesetById(OriginalSaveImporter.RequiredRulesetId);
        var scenario = repository.ScenarioById(OriginalSaveImporter.RequiredWorldId);
        if (world is null || ruleset is null || scenario is null)
        {
            return Fail(path, $"Could not import '{name}': this build lacks the classical-mediterranean world, scenario or the classical-faithful ruleset.");
        }

        SaveGame save;
        try
        {
            var bytes = File.ReadAllBytes(path);
            var stem = Path.GetFileNameWithoutExtension(path);
            save = OriginalSaveImporter.Import(
                bytes, name, world, ruleset, scenario, saveId: "imported-" + stem, saveLabel: "Imported " + stem).Save;
        }
        catch (SaveContextMismatchException ex)
        {
            return Fail(path, $"Could not import '{name}': {ex.Message}");
        }
        catch (InvalidDataException ex)
        {
            return Fail(path, $"Could not import '{name}': {ex.Message}");
        }

        if (!save.State.Nations.Any(n => n.Control == SeatControl.Human))
        {
            return Fail(path, $"Could not import '{name}': the save has no human nation to play.");
        }

        return new Outcome(new GameSession(world, ruleset, scenario, save), null, path);
    }

    private static Outcome Fail(string path, string message) => new(null, message, path);
}
