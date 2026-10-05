using System.Text.Json;
using IC2.Engine.Assets;

namespace IC2.Slice.Assets;

/// <summary>
/// T94 folded follow-up (issue
/// <see href="https://github.com/diegoami/imperial_conquest_2/issues/454">#454</see>, item 1) — the
/// Godot-<em>free</em> "load a pack manifest, or degrade without throwing" step behind
/// <see cref="AssetPackTextureLoader.TryLoadPack"/> and
/// <see cref="AssetPackTextureLoader.TryLoadPlaceholderPack"/>. Bug
/// <see href="https://github.com/diegoami/imperial_conquest_2/issues/517">#517</see> added the
/// id-to-directory resolution and the authored→placeholder fallback order here too, for the same
/// testability reason.
/// </summary>
/// <remarks>
/// <para>
/// This exists as its own Godot-free class for the same reason <see cref="AssetKeyResolver"/> does:
/// <see cref="AssetPackTextureLoader"/> itself cannot be linked into <c>tests/IC2.Engine.Tests</c>
/// (it references <c>Godot.Texture2D</c>, and <c>godot/IC2.MapViewer.csproj</c> is outside
/// <c>IC2.sln</c>), so the "a corrupt manifest degrades, never throws" contract would otherwise only
/// be exercised by hand. Linking this file in its place keeps a single implementation of the contract.
/// </para>
/// <para>
/// <strong>Bug #517: which pack, and what happens when it is not there.</strong> The map's loader
/// resolves a pack id (the Settings screen's own choice, or none) to
/// <c>&lt;repositoryRoot&gt;/assets/packs/&lt;id&gt;/manifest.json</c> through
/// <see cref="ResolvePackDirectory"/>, which rejects any id that is not a single plain directory name
/// so a selection can never escape the packs directory. The fallback order is
/// <see cref="DefaultPackId"/> (<c>authored</c>) when nothing was selected and it is present and
/// valid, otherwise <see cref="FallbackPackId"/> (<c>placeholder</c>): a selected pack that is
/// missing, malformed or not a plain id is reported through the caller's own <c>onFailure</c> and the
/// placeholder is loaded in its place. Nothing here throws.
/// </para>
/// <para>
/// <strong>Why the catch covers more than a missing file.</strong>
/// <see cref="AssetLoader.LoadManifest"/> throws <see cref="FileNotFoundException"/> for a missing
/// manifest, <see cref="InvalidOperationException"/> when deserialization returns null,
/// <see cref="JsonException"/> for malformed JSON, and <see cref="ArgumentException"/> (as its
/// <see cref="ArgumentNullException"/> subclass) when the JSON is valid but has no asset table:
/// <c>{"assets": null}</c> deserializes to a null <c>Assets</c> dictionary, which
/// <c>ToAssetPack</c> then passes to <c>ToFrozenDictionary()</c>. The original catch covered only the
/// first two, so a corrupt manifest crashed the caller despite the contract; that is the defect this
/// class closes.
/// </para>
/// </remarks>
public static class AssetPackManifestLoader
{
    /// <summary>
    /// Bug <see href="https://github.com/diegoami/imperial_conquest_2/issues/517">#517</see>: the pack
    /// the map loads when no pack has been selected — T51's generated <c>assets/packs/authored/</c>
    /// (<c>scripts/generate-authored-assets.py</c>'s <c>DEFAULT_OUT_DIR</c>) — when it is present and
    /// its manifest is valid. The id is the pack's directory name under <c>assets/packs/</c>, and is
    /// what the Settings screen's picker lists.
    /// </summary>
    public const string DefaultPackId = "authored";

    /// <summary>
    /// T11's placeholder pack id: the fallback for every missing, malformed or unresolvable pack,
    /// and the pack the deterministic tests read (<c>assets/packs/placeholder/</c>).
    /// </summary>
    public const string FallbackPackId = "placeholder";

    /// <summary>
    /// Loads <paramref name="manifestPath"/> through <see cref="AssetLoader.LoadManifest"/>, returning
    /// <see langword="null"/> — never throwing — when the manifest is missing, unreadable as JSON,
    /// deserializes to nothing, or carries no asset table. <paramref name="failureMessage"/> names the
    /// path and the underlying reason for the caller's own log line.
    /// </summary>
    public static AssetPack? TryLoad(string manifestPath, out string? failureMessage)
    {
        try
        {
            failureMessage = null;
            return AssetLoader.LoadManifest(manifestPath);
        }
        catch (Exception ex) when (
            ex is FileNotFoundException or InvalidOperationException or JsonException or ArgumentException)
        {
            failureMessage = $"asset pack manifest at '{manifestPath}': {ex.Message}";
            return null;
        }
    }

    /// <summary>
    /// Bug <see href="https://github.com/diegoami/imperial_conquest_2/issues/517">#517</see>: resolves
    /// <paramref name="packId"/> to its directory under <c>&lt;repositoryRoot&gt;/assets/packs/</c>.
    /// Returns <see langword="null"/> for anything that is not a single plain directory name — blank, a
    /// path separator, <c>.</c>, <c>..</c>, a rooted path — so no selected id can ever point outside
    /// the packs directory.
    /// </summary>
    public static string? ResolvePackDirectory(string repositoryRoot, string packId)
    {
        if (!IsPlainPackId(packId))
        {
            return null;
        }

        return Path.Combine(repositoryRoot, "assets", "packs", packId);
    }

    /// <summary>
    /// Bug <see href="https://github.com/diegoami/imperial_conquest_2/issues/517">#517</see>: the
    /// by-id load path behind <see cref="AssetPackTextureLoader.TryLoadPack"/>. Loads
    /// <paramref name="requestedPackId"/> (a <see langword="null"/> id means "no selection": the
    /// default <see cref="DefaultPackId"/> when present and valid, otherwise
    /// <see cref="FallbackPackId"/>). A requested pack that is missing, malformed or not a plain
    /// directory name is reported through <paramref name="onFailure"/> and the placeholder pack is
    /// loaded in its place, so this returns <see langword="null"/> only when even the placeholder
    /// cannot be loaded. Never throws.
    /// </summary>
    /// <param name="repositoryRoot">The repository root the packs directory sits in.</param>
    /// <param name="requestedPackId">The selected pack id, or <see langword="null"/> for the default.</param>
    /// <param name="onFailure">Reports each attempted pack the fallback replaced, and a failed
    /// placeholder, with the pack's path and the underlying reason.</param>
    /// <param name="resolvedPackId">The id of the pack actually loaded, or <see langword="null"/> when
    /// none could be.</param>
    /// <param name="resolvedPackDirectory">The directory of the pack actually loaded, or
    /// <see langword="null"/> when none could be.</param>
    public static AssetPack? TryLoadPack(
        string repositoryRoot,
        string? requestedPackId,
        Action<string> onFailure,
        out string? resolvedPackId,
        out string? resolvedPackDirectory)
    {
        resolvedPackId = null;
        resolvedPackDirectory = null;

        var candidateId = requestedPackId ?? DefaultPackId;
        var candidateDirectory = ResolvePackDirectory(repositoryRoot, candidateId);
        if (candidateDirectory is null)
        {
            if (requestedPackId is not null)
            {
                onFailure($"asset pack id '{requestedPackId}' is not a plain pack directory name");
            }
        }
        else
        {
            var candidateManifest = Path.Combine(candidateDirectory, "manifest.json");
            if (File.Exists(candidateManifest))
            {
                var candidate = TryLoad(candidateManifest, out var candidateFailure);
                if (candidate is not null)
                {
                    resolvedPackId = candidateId;
                    resolvedPackDirectory = candidateDirectory;
                    return candidate;
                }

                // Present but malformed: always reported, whether explicitly selected or the default.
                onFailure(candidateFailure!);
            }
            else if (requestedPackId is not null)
            {
                onFailure($"asset pack '{candidateId}': no manifest at '{candidateManifest}'");
            }
        }

        if (string.Equals(candidateId, FallbackPackId, StringComparison.Ordinal))
        {
            // The fallback itself is what failed; the report above is its reason.
            return null;
        }

        var fallbackDirectory = ResolvePackDirectory(repositoryRoot, FallbackPackId)!;
        var fallbackManifest = Path.Combine(fallbackDirectory, "manifest.json");
        var fallback = TryLoad(fallbackManifest, out var fallbackFailure);
        if (fallback is null)
        {
            onFailure(fallbackFailure!);
            return null;
        }

        resolvedPackId = FallbackPackId;
        resolvedPackDirectory = fallbackDirectory;
        return fallback;
    }

    /// <summary>
    /// True when <paramref name="packId"/> is one plain directory name: not blank, not <c>.</c> or
    /// <c>..</c>, and carrying no separator or other invalid file-name character. Both separators are
    /// named explicitly rather than relying on <see cref="Path.GetInvalidFileNameChars"/>, whose
    /// contents differ between Windows and Unix.
    /// </summary>
    private static bool IsPlainPackId(string packId) =>
        !string.IsNullOrWhiteSpace(packId)
        && packId is not "." and not ".."
        && packId.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && packId.IndexOf('\\') < 0
        && packId.IndexOf('/') < 0;
}
