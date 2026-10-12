using System.Collections.Generic;
using System.Linq;
using Godot;
using IC2.Engine.Assets;
using IC2.Engine.Model;
using IC2.Slice.Assets;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>
/// T163 (#1008) Done-when 4: on the real game screen a new game starts, the loaded ruleset's
/// thresholds are the confirmed [25, 50, 100], towns of different populations resolve to four
/// different non-capital marker keys (and a capital to its own) through <see cref="MapMarkerKeys"/>,
/// the very call <c>GameMapView.DrawCity</c> makes, and the placeholder pack's textures for those
/// five keys are pixel-different, so the tiers draw as different markers. Run headless via:
/// <code>
/// godot --headless --path godot res://Checks/TownTiersCheck.tscn --quit-after 30
/// </code>
/// </summary>
public partial class TownTiersCheck : Node
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
            GD.PrintErr($"TownTiersCheck: unhandled exception: {ex}");
            _ok = false;
        }

        var exitCode = _ok ? 0 : 1;
        GD.Print($"TownTiersCheck: exiting with code {exitCode}.");
        GetTree().Quit(exitCode);
    }

    private void Run()
    {
        var appRoot = new AppRoot();
        AddChild(appRoot);

        appRoot.ShowNewGameFlow();
        if (!Check(appRoot.CurrentScreen is NewGameFlow, "New Game reaches the ruleset chooser"))
        {
            return;
        }

        var flow = (NewGameFlow)appRoot.CurrentScreen!;
        flow.Chooser!.ConfirmSelection();
        if (!Check(flow.SeatScreen is not null, "the seat step is reached"))
        {
            return;
        }

        flow.SeatScreen!.ConfirmSeatAndStart();
        if (!Check(appRoot.CurrentScreen is MainGameScreen, "Start Game reaches the main game screen"))
        {
            return;
        }

        var session = ((MainGameScreen)appRoot.CurrentScreen!).Session;
        var state = session.State;
        var rules = session.Ruleset.MapMarkers;

        Check(
            rules.CityPopulationTierThresholds.SequenceEqual(new[] { 25, 50, 100 }),
            $"the game's ruleset ships [25, 50, 100] (got [{string.Join(", ", rules.CityPopulationTierThresholds)}])");

        // The real towns of the loaded world: the keys the map would draw for them right now.
        var capitalIds = state.Nations.Select(n => n.CapitalCityId).Where(id => id is not null).ToHashSet();
        var townKeys = state.Cities
            .Where(c => !capitalIds.Contains(c.Id))
            .Select(c => MapMarkerKeys.CityIcon(state, c, rules))
            .Distinct()
            .ToList();
        Check(townKeys.Count >= 2, $"the world's own towns already draw several tiers (keys: {string.Join(", ", townKeys)})");

        // The boundaries on a real town of that world: 24, 25, 49, 50, 99, 100 thousand.
        var town = state.Cities.First(c => !capitalIds.Contains(c.Id));
        var expected = new (int Population, string Key)[]
        {
            (24, AssetKeys.CityTier1Icon), (25, AssetKeys.CityTier2Icon), (49, AssetKeys.CityTier2Icon),
            (50, AssetKeys.CityTier3Icon), (99, AssetKeys.CityTier3Icon), (100, AssetKeys.CityTier4Icon),
        };
        foreach (var (population, key) in expected)
        {
            var changed = town with { PopulationThousands = population };
            var changedState = state with
            {
                Cities = ValueList.From(state.Cities.Select(c => c.Id == town.Id ? changed : c)),
            };
            var actual = MapMarkerKeys.CityIcon(changedState, changed, rules);
            Check(actual == key, $"a town of {population}k draws {key} (got {actual})");
        }

        var capitalId = capitalIds.First()!;
        var capital = state.CityById(capitalId)!;
        var hugeCapital = capital with { PopulationThousands = 500 };
        var smallCapital = capital with { PopulationThousands = 1 };
        Check(
            MapMarkerKeys.CityIcon(state, hugeCapital, rules) == AssetKeys.CityCapitalIcon
            && MapMarkerKeys.CityIcon(state, smallCapital, rules) == AssetKeys.CityCapitalIcon,
            "a capital draws the capital marker at any population");

        // The textures the map would draw: the placeholder pack's five city markers differ.
        var failures = new List<string>();
        var loader = AssetPackTextureLoader.TryLoadPack(GameDataContext.RepositoryRoot, "placeholder", failures.Add);
        if (!Check(loader is not null && failures.Count == 0, "the placeholder pack loads"))
        {
            return;
        }

        var keys = new[]
        {
            AssetKeys.CityTier1Icon, AssetKeys.CityTier2Icon, AssetKeys.CityTier3Icon,
            AssetKeys.CityTier4Icon, AssetKeys.CityCapitalIcon,
        };
        var pixels = new List<byte[]>();
        foreach (var key in keys)
        {
            var texture = loader!.TryGetTexture(key);
            Check(texture is not null, $"{key} resolves to a texture");
            pixels.Add(texture?.GetImage()?.GetData() ?? System.Array.Empty<byte>());
        }

        for (var i = 0; i < keys.Length; i++)
        {
            for (var j = i + 1; j < keys.Length; j++)
            {
                Check(!pixels[i].SequenceEqual(pixels[j]), $"{keys[i]} and {keys[j]} are drawn with different pixels");
            }
        }
    }

    private bool Check(bool condition, string description)
    {
        GD.Print(condition ? $"PASS: {description}" : $"FAIL: {description}");
        _ok &= condition;
        return condition;
    }
}
