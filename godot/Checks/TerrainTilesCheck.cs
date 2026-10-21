using Godot;
using IC2.Engine.Presentation;
using IC2.Slice.Assets;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>
/// The "Classic tiles" map style (the owner's request of 2026-10-21), run headless via:
/// <code>
/// godot --headless --path godot res://Checks/TerrainTilesCheck.tscn --quit-after 600
/// </code>
/// It drives the real <see cref="GameMapView"/> on the shipped <c>classical-mediterranean</c> world and
/// asserts, from what <c>_Draw</c> recorded: the default style draws one tile per cell with the six tile
/// keys bound, through a shader that samples a texture array with explicit gradients; committing
/// "Painted" in the settings redraws the map in T148's painted surface without reattaching; and
/// committing "Classic tiles" again brings the tiles back.
/// </summary>
public partial class TerrainTilesCheck : Control
{
    private const int SettleFrames = 4;

    private GameMapView _map = null!;
    private bool _ok = true;
    private int _frame;
    private int _step;

    public override void _Ready()
    {
        SettingsScreen.SetMapStyleForCheck(MapStyles.DefaultId);
        var resolved = GameDataContext.Repository.Resolve("classical-mediterranean");
        Size = GetViewport().GetVisibleRect().Size;
        _map = new GameMapView { Size = Size };
        AddChild(_map);
        _map.Attach(new GameSession(resolved.World, resolved.Ruleset, resolved.Scenario), GameDataContext.RepositoryRoot);
    }

    public override void _Process(double delta)
    {
        if (++_frame % SettleFrames != 0)
        {
            return;
        }

        switch (_step++)
        {
            case 0:
                Check(MapStyles.DefaultId == MapStyles.Tiles, "Classic tiles is the default map style");
                CheckTiles("the default style");
                var code = _map.TerrainSurfaceShaderCodeForCheck;
                Check(code.Contains("sampler2DArray tiles", StringComparison.Ordinal), "the tile shader samples a texture array");
                Check(code.Contains("textureGrad", StringComparison.Ordinal), "the tile shader takes its mip level from the continuous world position");
                SettingsScreen.Commit(SettingsScreen.Current with { MapStyle = MapStyles.Painted });
                break;
            case 1:
                Check(_map.TerrainSurfaceForCheck.Kind == TerrainSurfaceKind.Shader,
                    $"committing Painted redraws the painted surface (drew {_map.TerrainSurfaceForCheck.Kind})");
                SettingsScreen.Commit(SettingsScreen.Current with { MapStyle = MapStyles.Tiles });
                break;
            case 2:
                CheckTiles("committing Classic tiles again");
                GD.Print($"TerrainTilesCheck: exiting with code {(_ok ? 0 : 1)}.");
                CheckExit.Quit(this, _ok ? 0 : 1);
                break;
        }
    }

    private void CheckTiles(string when)
    {
        var state = _map.TerrainSurfaceForCheck;
        Check(state.Kind == TerrainSurfaceKind.Tiles, $"{when}: the map drew tiles (drew {state.Kind})");
        Check(state.BoundSurfaceKeys.SequenceEqual(TerrainSplatMap.TileKeysByLayer),
            $"{when}: the six tile keys are bound in layer order");
    }

    private void Check(bool condition, string description)
    {
        GD.Print(condition ? $"PASS: {description}" : $"FAIL: {description}");
        _ok &= condition;
    }
}
