using System.Text.Json;
using IC2.Engine.Assets;

namespace IC2.Slice.Assets;

/// <summary>
/// T94 folded follow-up (issue
/// <see href="https://github.com/diegoami/imperial_conquest_2/issues/454">#454</see>, item 1) — the
/// Godot-<em>free</em> "load a pack manifest, or degrade without throwing" step behind
/// <see cref="AssetPackTextureLoader.TryLoadPlaceholderPack"/>.
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
}
