using Godot;
using IC2.Slice.Assets;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>
/// Bug <see href="https://github.com/diegoami/imperial_conquest_2/issues/517">#517</see>'s chain-level
/// reproduction: the real <see cref="AppRoot"/> → <see cref="SettingsScreen"/> →
/// <see cref="MainGameScreen"/> → <see cref="GameMapView"/> path. With the repository's real packs
/// present it asserts that a game's map resolves the default <c>authored</c> pack; that choosing
/// <c>placeholder</c> in the real Settings picker makes the <em>next</em> game's map resolve
/// <c>placeholder</c>; and that a missing or malformed selected pack falls back to the placeholder
/// through the same loader, reported and never thrown. Run headless via:
/// <code>
/// Godot_..._console.exe --headless --path godot res://Checks/AssetPackSelectionCheck.tscn --quit-after 30
/// </code>
/// </summary>
/// <remarks>
/// Drives the real classes through the same public hooks <c>godot/Checks/RulesetFlowCheck.cs</c> and
/// <c>godot/Checks/SaveResumeCheck.cs</c> already establish (<c>ConfirmSelection</c>,
/// <c>ConfirmSeatAndStart</c>) and <see cref="GameMapView.LoadedPackIdForCheck"/>, this fix's own
/// accessor — never a hand-built mirror of the UI, and never simulated mouse clicks at hardcoded
/// coordinates. The malformed/missing packs are written under the git-ignored <c>rendered/</c>
/// scratch directory (never under <c>assets/</c>) and removed before the check exits.
/// </remarks>
public partial class AssetPackSelectionCheck : Node
{
    private bool _ok = true;

    public override void _Ready()
    {
        try
        {
            Run();
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"AssetPackSelectionCheck: unhandled exception: {ex}");
            _ok = false;
        }

        var exitCode = _ok ? 0 : 1;
        GD.Print($"AssetPackSelectionCheck: exiting with code {exitCode}.");
        GetTree().Quit(exitCode);
    }

    private void Run()
    {
        var appRoot = new AppRoot();
        AddChild(appRoot);

        // A fresh process: the Settings screen has not run yet, so nothing is selected.
        Check(SettingsScreen.SelectedPackId is null, "no pack is selected before Settings runs");

        // ---- default: the repository ships assets/packs/authored, so the map's own loader resolves it ----
        var firstGame = StartGameFromTheMainMenu(appRoot);
        if (firstGame is null)
        {
            return;
        }

        Check(
            firstGame.MapView.LoadedPackIdForCheck == "authored",
            $"a new game's map resolves the default 'authored' pack (got '{firstGame.MapView.LoadedPackIdForCheck ?? "<none>"}')");
        Check(
            firstGame.MapView.LoadedPackIdForCheck != "placeholder",
            "the map does not resolve the placeholder while the authored pack is present and valid");

        // ---- Settings: choose placeholder through the real picker (bug #517's own flow) ----
        appRoot.ShowSettings();
        if (!Check(appRoot.CurrentScreen is SettingsScreen, "AppRoot shows the real Settings screen"))
        {
            return;
        }

        var picker = FindOptionButton((SettingsScreen)appRoot.CurrentScreen!);
        if (!Check(picker is not null, "the Settings screen renders its pack picker"))
        {
            return;
        }

        var packItems = Enumerable.Range(0, picker!.ItemCount).Select(picker.GetItemText).ToList();
        Check(packItems.Contains("authored"), $"the picker lists 'authored' (items: {string.Join(", ", packItems)})");
        Check(packItems.Contains("placeholder"), $"the picker lists 'placeholder' (items: {string.Join(", ", packItems)})");

        var placeholderIndex = picker.GetItemIndex("placeholder");
        picker.Select(placeholderIndex);
        picker.EmitSignal(OptionButton.SignalName.ItemSelected, placeholderIndex);
        Check(
            SettingsScreen.SelectedPackId == "placeholder",
            $"choosing 'placeholder' in the picker records it as the selection (got '{SettingsScreen.SelectedPackId ?? "<none>"}')");

        // ---- the next game's map picks the new selection up; the previous game is untouched ----
        var secondGame = StartGameFromTheMainMenu(appRoot);
        if (secondGame is null)
        {
            return;
        }

        Check(!ReferenceEquals(firstGame, secondGame), "the second game builds a fresh MainGameScreen");
        Check(
            secondGame.MapView.LoadedPackIdForCheck == "placeholder",
            $"the next game's map resolves the selected 'placeholder' pack (got '{secondGame.MapView.LoadedPackIdForCheck ?? "<none>"}')");

        // ---- the Godot-side loader's own fallback: missing, then malformed, selected pack ----
        CheckLoaderFallbackOnAScratchRepository();
    }

    /// <summary>
    /// The same <see cref="AssetPackTextureLoader.TryLoadPack"/> the map calls, against a scratch
    /// repository root under <c>rendered/</c>: a selected pack that is absent, and one whose manifest
    /// is malformed, each fall back to <c>placeholder</c> with a report through <c>onFailure</c>.
    /// </summary>
    private void CheckLoaderFallbackOnAScratchRepository()
    {
        var scratchRoot = Path.Combine(GameDataContext.RepositoryRoot, "rendered", "fix-517-asset-pack-selection-check");
        try
        {
            var realPlaceholderManifest = File.ReadAllText(
                Path.Combine(GameDataContext.RepositoryRoot, "assets", "packs", "placeholder", "manifest.json"));
            WriteScratchPack(scratchRoot, "placeholder", realPlaceholderManifest);

            var missingFailures = new List<string>();
            var missingLoader = AssetPackTextureLoader.TryLoadPack(scratchRoot, "authored", missingFailures.Add);
            Check(
                missingLoader?.PackId == "placeholder",
                $"a missing selected pack falls back to 'placeholder' (got '{missingLoader?.PackId ?? "<none>"}')");
            Check(
                missingFailures.Count == 1 && missingFailures[0].Contains("authored"),
                "the missing selected pack is reported through onFailure");

            WriteScratchPack(scratchRoot, "authored", "{ \"assets\": [ this is not json ]");
            var malformedFailures = new List<string>();
            var malformedLoader = AssetPackTextureLoader.TryLoadPack(scratchRoot, "authored", malformedFailures.Add);
            Check(
                malformedLoader?.PackId == "placeholder",
                $"a malformed selected pack falls back to 'placeholder' (got '{malformedLoader?.PackId ?? "<none>"}')");
            Check(
                malformedFailures.Count == 1 && malformedFailures[0].Contains("authored"),
                "the malformed selected pack is reported through onFailure");
        }
        finally
        {
            if (Directory.Exists(scratchRoot))
            {
                Directory.Delete(scratchRoot, recursive: true);
            }
        }
    }

    private MainGameScreen? StartGameFromTheMainMenu(AppRoot appRoot)
    {
        appRoot.ShowNewGameFlow();
        if (!Check(appRoot.CurrentScreen is NewGameFlow, "New Game reaches the ruleset chooser"))
        {
            return null;
        }

        var flow = (NewGameFlow)appRoot.CurrentScreen!;
        flow.Chooser!.ConfirmSelection();
        if (!Check(flow.SeatScreen is not null, "the seat step is reached after the ruleset chooser"))
        {
            return null;
        }

        flow.SeatScreen!.ConfirmSeatAndStart();
        if (!Check(appRoot.CurrentScreen is MainGameScreen, "Start Game reaches the main game screen"))
        {
            return null;
        }

        return (MainGameScreen)appRoot.CurrentScreen!;
    }

    private static void WriteScratchPack(string scratchRoot, string packId, string manifestJson)
    {
        var directory = Path.Combine(scratchRoot, "assets", "packs", packId);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "manifest.json"), manifestJson);
    }

    private static OptionButton? FindOptionButton(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            if (child is OptionButton option)
            {
                return option;
            }

            var nested = FindOptionButton(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private bool Check(bool condition, string description)
    {
        GD.Print(condition ? $"PASS: {description}" : $"FAIL: {description}");
        _ok &= condition;
        return condition;
    }
}
