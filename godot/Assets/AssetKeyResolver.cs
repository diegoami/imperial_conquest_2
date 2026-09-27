using IC2.Engine.Assets;

namespace IC2.Slice.Assets;

/// <summary>
/// T48 "Draw armies and cities from the asset pack" — the Godot-<em>free</em> half of the pack
/// loader. Resolves an <see cref="AssetKeys"/> key to an absolute file path through T11's own
/// <see cref="AssetPack.TryResolveAsset"/> — never a second JSON parser, never a hardcoded path —
/// and degrades a missing key or a missing file to a single logged failure instead of an exception.
/// </summary>
/// <remarks>
/// <para>
/// This class deliberately has no <c>Godot.*</c> reference so it can be exercised by a plain xunit
/// test in <c>tests/IC2.Engine.Tests/Assets/</c>. <c>godot/IC2.MapViewer.csproj</c> is itself
/// deliberately excluded from <c>IC2.sln</c> (T47's own scope note — it needs the Godot.NET SDK,
/// which CI does not install) and a CI-covered test project cannot add a project reference to it
/// without pulling that SDK requirement into every `dotnet test IC2.sln` run. So the actual
/// <c>Godot.Image</c>/<c>Texture2D</c> loading in <see cref="AssetPackTextureLoader"/> can only be
/// exercised by the manual windowed run this task's screenshot (DoD 7) comes from — but everything
/// at or below this seam (key → path resolution, the missing-key/missing-file fallback, the
/// log-once behaviour) is covered by an ordinary test that runs in CI.
/// </para>
/// </remarks>
public sealed class AssetKeyResolver
{
    private readonly AssetPack _pack;
    private readonly string _packDirectory;
    private readonly Action<string> _onFailure;
    private readonly HashSet<string> _loggedFailures = new(StringComparer.Ordinal);

    /// <param name="pack">The manifest already loaded through <see cref="AssetLoader.LoadManifest"/>.</param>
    /// <param name="packDirectory">The directory <paramref name="pack"/>'s relative paths are rooted at.</param>
    /// <param name="onFailure">
    /// Called exactly once per distinct key the first time it fails to resolve — never once per
    /// call, so a marker drawn every frame does not spam the log for the same missing key.
    /// </param>
    public AssetKeyResolver(AssetPack pack, string packDirectory, Action<string> onFailure)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(packDirectory);
        ArgumentNullException.ThrowIfNull(onFailure);

        _pack = pack;
        _packDirectory = packDirectory;
        _onFailure = onFailure;
    }

    /// <summary>
    /// Resolves <paramref name="assetKey"/> to an existing file's absolute path. Returns
    /// <see langword="false"/> — never throws — when the key is not in the manifest at all, or
    /// when the manifest names a file that is not actually there; either way the caller's own
    /// fallback (T47's coloured shape) is what ends up on screen.
    /// </summary>
    public bool TryResolveFile(string assetKey, out string fullPath)
    {
        if (_pack.TryResolveAsset(assetKey, out var relativePath))
        {
            var candidate = Path.Combine(_packDirectory, relativePath);
            if (File.Exists(candidate))
            {
                fullPath = candidate;
                return true;
            }
        }

        fullPath = string.Empty;
        ReportFailureOnce(assetKey);
        return false;
    }

    /// <summary>
    /// Lets a caller that resolved a file but then failed to <em>read</em> it (a Godot
    /// <c>Image.Load</c> error — the "unreadable" half of DoD 4, which this Godot-free class cannot
    /// itself detect) fold into the same once-per-key log, rather than keeping a second dedup set.
    /// </summary>
    public void ReportFailureOnce(string assetKey)
    {
        if (_loggedFailures.Add(assetKey))
        {
            _onFailure(assetKey);
        }
    }
}
