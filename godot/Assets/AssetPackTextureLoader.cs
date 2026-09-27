using Godot;
using IC2.Engine.Assets;

namespace IC2.Slice.Assets;

/// <summary>
/// T48 "Draw armies and cities from the asset pack" — the Godot-side half of the pack loader: turns
/// an <see cref="AssetKeys"/> key into a <see cref="Texture2D"/>, through T11's own
/// <see cref="AssetLoader"/>/<see cref="AssetPack"/> and <see cref="AssetKeyResolver"/> (never a
/// second manifest parser, never a hardcoded file path).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Where the pack lives, and why.</strong> The placeholder pack (T11's
/// <c>assets/packs/placeholder/</c>) sits at the repository root, outside <c>godot/</c>, so it is
/// not reachable as <c>res://...</c> once Godot is launched with <c>--path godot</c>. This loader
/// resolves it the same way <c>Slice.cs</c> already resolves <c>data/</c> — a filesystem path built
/// from <c>ProjectSettings.GlobalizePath("res://")/..</c> — rather than inventing a second
/// convention. <strong>This is a placeholder-development-time answer, not a packaging one</strong>:
/// T27 (export/packaging) has no task yet, and an exported build has no repository directory
/// beside it at all, so whatever T27 ships will need to embed or copy the pack into the export
/// (for example as an exported "remote source" data directory, or copied into the <c>.pck</c>) —
/// that is explicitly T27's problem to solve, flagged here rather than guessed at.
/// </para>
/// <para>
/// <strong>Why a runtime <see cref="Image"/> load, not the Godot importer.</strong> T11 chose
/// <c>.bmp</c> deliberately (its PNG generator twice produced invalid files CI still passed —
/// <c>assets/packs/placeholder/</c>'s own history). The pack lives outside <c>res://</c>, so
/// Godot's import pipeline never sees these files at all — import only ever runs over files inside
/// the project. A runtime <c>Image.Load(path)</c> from the resolved filesystem path is therefore
/// the only route regardless of format, and it was confirmed against this pack's actual 32×32
/// 24bpp uncompressed BMPs during this task (see the PR body) rather than assumed.
/// </para>
/// </remarks>
public sealed class AssetPackTextureLoader
{
    private readonly AssetKeyResolver _resolver;
    private readonly Dictionary<string, Texture2D?> _textureCache = new(StringComparer.Ordinal);

    private AssetPackTextureLoader(AssetKeyResolver resolver)
    {
        _resolver = resolver;
    }

    /// <summary>
    /// Loads T11's placeholder pack (<c>assets/packs/placeholder/manifest.json</c>) relative to
    /// <paramref name="repositoryRoot"/>, through <see cref="AssetLoader.LoadManifest"/> — never a
    /// second JSON parser of this task's own. Returns <see langword="null"/> instead of throwing
    /// when the manifest itself cannot be loaded (a pack that is entirely missing or corrupt is the
    /// same "degrade, don't throw" contract as a single missing key — every marker then falls back
    /// to T47's coloured shape), logging once via <paramref name="onFailure"/>.
    /// </summary>
    public static AssetPackTextureLoader? TryLoadPlaceholderPack(string repositoryRoot, Action<string> onFailure)
    {
        var packDirectory = Path.Combine(repositoryRoot, "assets", "packs", "placeholder");
        var manifestPath = Path.Combine(packDirectory, "manifest.json");

        AssetPack pack;
        try
        {
            pack = AssetLoader.LoadManifest(manifestPath);
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidOperationException)
        {
            onFailure($"asset pack manifest at '{manifestPath}': {ex.Message}");
            return null;
        }

        var resolver = new AssetKeyResolver(pack, packDirectory, onFailure);
        return new AssetPackTextureLoader(resolver);
    }

    /// <summary>
    /// Resolves <paramref name="assetKey"/> to a texture, caching both hits and misses so a marker
    /// redrawn every frame neither re-reads the file nor re-logs the failure. Returns
    /// <see langword="null"/> — never throws — for a missing key, a missing file, or a file
    /// <see cref="Image.Load"/> cannot read; the caller's own fallback (T47's coloured shape) is
    /// what ends up on screen in every one of those cases.
    /// </summary>
    public Texture2D? TryGetTexture(string assetKey)
    {
        if (_textureCache.TryGetValue(assetKey, out var cached))
        {
            return cached;
        }

        Texture2D? texture = null;
        if (_resolver.TryResolveFile(assetKey, out var fullPath))
        {
            var image = new Image();
            var error = image.Load(fullPath);
            if (error == Error.Ok)
            {
                texture = ImageTexture.CreateFromImage(image);
            }
            else
            {
                _resolver.ReportFailureOnce(assetKey);
            }
        }

        _textureCache[assetKey] = texture;
        return texture;
    }
}
