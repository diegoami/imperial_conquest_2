using Godot;
using IC2.Slice.Assets;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>T271: optional era visuals decode through the actual shipped Godot texture loader.</summary>
public partial class PeriodUnitAssetsCheck : Node
{
    public override void _Ready()
    {
        var ok = true;
        try
        {
            foreach (var pack in new[] { "authored", "placeholder" })
            {
                var failures = new List<string>();
                var loader = AssetPackTextureLoader.TryLoadPack(GameDataContext.RepositoryRoot, pack, failures.Add)
                    ?? throw new InvalidOperationException($"Could not load {pack}.");
                foreach (var visual in new[] { "light_chariot", "heavy_chariot" })
                foreach (var size in new[] { "icon", "small", "medium", "large" })
                {
                    var key = size == "icon" ? $"unit.{visual}.icon" : BattleAssetKeys.UnitIcon(visual, Array.IndexOf(new[] { "small", "medium", "large" }, size));
                    var texture = loader.TryGetTexture(key);
                    if (texture is null || texture.GetWidth() != 32 || texture.GetHeight() != 32)
                        throw new InvalidOperationException($"Invalid texture {pack}/{key}.");
                    GD.Print($"PeriodUnitAssetsCheck: PASS {pack}/{key}");
                }
                if (failures.Count > 0) throw new InvalidOperationException(string.Join(", ", failures));
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"PeriodUnitAssetsCheck: {ex}");
            ok = false;
        }
        GD.Print($"PeriodUnitAssetsCheck: exiting with code {(ok ? 0 : 1)}.");
        CheckExit.Quit(this, ok ? 0 : 1);
    }
}
