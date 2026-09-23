using IC2.Data;
using IC2.Inspect;

namespace IC2.Engine.Tests.Import;

/// <summary>
/// Resolves an original-save fixture by name alone, through the one shared search order
/// <see cref="CorpusFileLocator"/> already implements (and <c>tests/IC2.Data.Tests/FixtureResolver</c>
/// also delegates to) — never a folder-prefixed path (operating-guide.md §1.2, issue #203/#213).
/// </summary>
internal static class OriginalFixture
{
    /// <summary>Resolves <paramref name="fileName"/> against <see cref="LocalOriginalAssets.Settings"/>,
    /// or throws naming every location searched. Every call site sits behind
    /// <c>Skip.IfNot(LocalOriginalAssets.IsConfigured, ...)</c>.</summary>
    public static string ResolveOrThrow(string fileName)
    {
        var settings = LocalOriginalAssets.Settings
            ?? throw new FileNotFoundException(
                $"Fixture '{fileName}' was not found: no asset directory is configured.");

        var hit = CorpusFileLocator.TryResolve(settings, fileName);
        if (hit is not null)
        {
            return hit;
        }

        var tried = CorpusFileLocator.SearchedLocations(settings, fileName).ToList();
        throw new FileNotFoundException(
            $"Fixture '{fileName}' was not found. Searched, in order: " + string.Join(" | ", tried) + ".");
    }

    /// <summary>As <see cref="ResolveOrThrow"/>, but returns null (for a per-case Skip) rather than
    /// throwing when the file is not present in the configured corpus — mirrors
    /// <c>CorpusSweepTests</c>' own <c>TryResolve</c>-then-<c>Skip.If</c> pattern, for a fixture (e.g. an
    /// <c>IP*.sav</c> file) that is local-only and not part of CI's 54-save fixtures repository.</summary>
    public static string? TryResolve(string fileName) =>
        LocalOriginalAssets.Settings is { } settings ? CorpusFileLocator.TryResolve(settings, fileName) : null;
}
