using Godot;
using IC2.Slice.Assets;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>
/// T292d: the Settings "Original assets" section with a synthetic installation made here (no original bytes).
/// Status text per state, Rebuild and Clear, and the sound player preferring the cache. Headless:
/// <code>godot --headless --path godot res://Checks/OriginalAssetsSettingsCheck.tscn --quit-after 300</code>
/// </summary>
public partial class OriginalAssetsSettingsCheck : Node
{
    private bool _ok = true;

    private bool Check(bool condition, string what)
    {
        GD.Print($"OriginalAssetsSettingsCheck: {(condition ? "ok" : "FAIL")} - {what}");
        if (!condition) _ok = false;
        return condition;
    }

    public override void _Ready()
    {
        var scratch = Path.Combine(GameDataContext.RepositoryRoot, "rendered", "original-assets-check");
        try
        {
            Run(scratch);
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"OriginalAssetsSettingsCheck: unhandled exception: {ex}");
            _ok = false;
        }
        finally
        {
            if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true);
        }
        GD.Print($"OriginalAssetsSettingsCheck: exiting with code {(_ok ? 0 : 1)}.");
        CheckExit.Quit(this, _ok ? 0 : 1);
    }

    private static byte[] Wave()
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8); w.Write(40); w.Write("WAVE"u8); w.Write("fmt "u8); w.Write(16); w.Write((ushort)1);
        w.Write((ushort)1); w.Write(44100); w.Write(88200); w.Write((ushort)2); w.Write((ushort)16);
        w.Write("data"u8); w.Write(4); w.Write(0);
        return ms.ToArray();
    }

    private Node? Find(Node root, string name) => root.FindChild(name, recursive: true, owned: false);

    private void Run(string scratch)
    {
        if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true);
        var install = Path.Combine(scratch, "install");
        Directory.CreateDirectory(Path.Combine(install, "WAVS"));
        File.WriteAllBytes(Path.Combine(install, "WAVS", "SOUND1.WAV"), Wave());
        var originalCache = OriginalAssetCache.CacheDirectory;
        OriginalAssetCache.CacheDirectory = Path.Combine(scratch, "cache", "original");
        SettingsScreen.Commit(SettingsScreen.Current with { OriginalInstallPath = null });

        var appRoot = new AppRoot();
        AddChild(appRoot);
        appRoot.ShowSettings();
        var screen = appRoot.CurrentScreen as SettingsScreen;
        if (!Check(screen is not null, "Settings opens")) return;

        var status = Find(screen!, SettingsScreen.OriginalStatusName) as Label;
        var rebuild = Find(screen!, SettingsScreen.OriginalRebuildName) as Button;
        var clear = Find(screen!, SettingsScreen.OriginalClearName) as Button;
        if (!Check(status is not null && rebuild is not null && clear is not null, "section, status and buttons exist")) return;

        Check(status!.Text.Contains("No original installation"), "no folder: says authored art is used");
        Check(rebuild!.Disabled, "no folder: Rebuild is disabled");
        Check(SettingsScreen.Current.OriginalInstallPath is null, "no folder is saved by default");

        SettingsScreen.Commit(SettingsScreen.Current with { OriginalInstallPath = install });
        rebuild.Disabled = false;
        rebuild.EmitSignal(BaseButton.SignalName.Pressed);
        Check(status.Text.Contains("1 of"), $"rebuild reports partial coverage (got '{status.Text}')");
        Check(OriginalAssetCache.ActiveDirectory(SettingsScreen.Current) is not null, "partial cache is in use");

        var player = new Audio.SoundPlayer(Path.Combine(GameDataContext.RepositoryRoot, "assets", "packs", "authored"),
            OriginalAssetCache.ActiveDirectory(SettingsScreen.Current));
        AddChild(player);
        Check(player.HasCue(IC2.Engine.Assets.AssetKeys.SfxUnitMove), "cue the cache supplies plays");
        Check(player.HasCue(IC2.Engine.Assets.AssetKeys.SfxBattle), "cue the cache lacks still plays from the authored pack");

        clear!.Disabled = false;
        clear.EmitSignal(BaseButton.SignalName.Pressed);
        Check(!Directory.Exists(OriginalAssetCache.CacheDirectory), "Clear removes the cache");
        Check(OriginalAssetCache.ActiveDirectory(SettingsScreen.Current) is null, "after Clear nothing is layered");

        Directory.Delete(install, recursive: true);
        Check(OriginalAssetCache.Status(SettingsScreen.Current).State == IC2.Engine.Assets.OriginalCacheState.SourceMissing,
            "a vanished folder is reported and falls back");

        // Textures: a tile the cache supplies is read from it (an 8x8 PNG here, the authored tile is far larger);
        // a key it lacks still comes from the authored pack.
        var tileCache = Path.Combine(scratch, "tile-cache", "original");
        Directory.CreateDirectory(Path.Combine(tileCache, "terrain"));
        File.WriteAllBytes(Path.Combine(tileCache, "terrain", "plain.png"), IC2.Engine.Assets.PngEncoder.EncodeRgb(8, 8, new byte[8 * 8 * 3]));
        File.WriteAllText(Path.Combine(tileCache, "manifest.json"),
            "{\"schemaVersion\":1,\"name\":\"t\",\"description\":\"t\",\"assets\":{\"terrain.plain.tile\":\"terrain/plain.png\"}}");
        Directory.CreateDirectory(install);
        OriginalAssetCache.CacheDirectory = tileCache;
        SettingsScreen.Commit(SettingsScreen.Current with { OriginalInstallPath = install });
        var loader = AssetPackTextureLoader.TryLoadPack(GameDataContext.RepositoryRoot, "authored", _ => { });
        Check(loader?.TryGetTexture(IC2.Engine.Assets.AssetKeys.TerrainPlainTile)?.GetSize() == new Vector2(8, 8), "a tile in the cache is the one the map loads");
        Check(loader?.TryGetTexture(IC2.Engine.Assets.AssetKeys.TerrainDesertTile)?.GetSize().X > 8, "a tile the cache lacks comes from the authored pack");
        SettingsScreen.Commit(SettingsScreen.Current with { OriginalInstallPath = null });
        Check(AssetPackTextureLoader.TryLoadPack(GameDataContext.RepositoryRoot, "authored", _ => { })?.TryGetTexture(IC2.Engine.Assets.AssetKeys.TerrainPlainTile)?.GetSize().X > 8,
            "with no folder chosen the authored tile is used");

        OriginalAssetCache.CacheDirectory = originalCache;
        SettingsScreen.Commit(SettingsScreen.Current with { OriginalInstallPath = null });
    }
}
