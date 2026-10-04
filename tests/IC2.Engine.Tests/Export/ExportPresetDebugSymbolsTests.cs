using Xunit;

namespace IC2.Engine.Tests.Export;

/// <summary>
/// Bug #652 (split from #646, item R3): the release package must ship no <c>.pdb</c> debug symbols.
/// The Godot export preset <c>godot/export_presets.cfg</c> controls this through
/// <c>dotnet/include_debug_symbols</c>; when a preset sets it to <c>true</c>, the Windows export
/// copies the .NET debug symbols into the packaged game.
/// </summary>
/// <remarks>
/// <para>
/// Bug #652's Done-when 2 asks for a test that fails before the fix: "it asserts that no export
/// preset in <c>godot/export_presets.cfg</c> sets <c>dotnet/include_debug_symbols=true</c>". On the
/// pre-fix file, <c>[preset.0.options]</c> sets it to <c>true</c> and this test fails.
/// </para>
/// <para>
/// The test also requires at least one preset to set the key <em>explicitly</em> to <c>false</c>.
/// Without that, deleting the line (rather than correcting it) would satisfy "no preset sets true"
/// while leaving the shipped behaviour to Godot's default — a silent pass this test must not allow.
/// </para>
/// <para>
/// This is a config-file test, not a Godot-runtime one: it reads the committed <c>.cfg</c> text in
/// place, the same way the other tests in this folder read committed repository files. It is placed
/// beside them in <c>tests/IC2.Engine.Tests/Export/</c> because no packaging test project exists;
/// bug #652's Done-when 2 explicitly allows "a minimal test in the closest existing test project".
/// </para>
/// </remarks>
public sealed class ExportPresetDebugSymbolsTests
{
    private const string IncludeDebugSymbolsKey = "dotnet/include_debug_symbols";
    private const string PresetConfigRelativePath = "godot/export_presets.cfg";

    /// <summary>
    /// Every export preset that declares <c>dotnet/include_debug_symbols</c> declares it
    /// <c>false</c>, and at least one preset declares it — so neither a <c>true</c> from a future
    /// preset nor the line's removal can pass silently.
    /// </summary>
    [Fact]
    public void Release_export_presets_ship_no_debug_symbols()
    {
        var settings = ReadDebugSymbolSettings();

        Assert.True(
            settings.Count > 0,
            $"'{PresetConfigRelativePath}' declares no '{IncludeDebugSymbolsKey}' setting at all; "
            + "the release preset(s) must set it explicitly to false.");

        Assert.DoesNotContain(
            (IEnumerable<KeyValuePair<string, bool>>)settings,
            setting => setting.Value);

        Assert.Contains(false, settings.Values);
    }

    /// <summary>
    /// Reads <c>dotnet/include_debug_symbols</c> out of every preset in
    /// <c>godot/export_presets.cfg</c>, keyed by the preset's own section id (for example
    /// <c>preset.0</c>). Presets that do not declare the key are absent from the result, so the
    /// caller can tell "explicitly false" from "absent".
    /// </summary>
    private static IReadOnlyDictionary<string, bool> ReadDebugSymbolSettings()
    {
        var path = Path.Combine(ExportedDataPaths.RepositoryRoot, PresetConfigRelativePath);
        Assert.True(File.Exists(path), $"Expected export preset configuration at '{path}'.");

        var settings = new Dictionary<string, bool>(StringComparer.Ordinal);
        string? preset = null;

        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                preset = PresetId(line[1..^1]);
                continue;
            }

            if (preset is null)
            {
                continue;
            }

            var equals = line.IndexOf('=');
            if (equals < 0 ||
                !string.Equals(line[..equals].Trim(), IncludeDebugSymbolsKey, StringComparison.Ordinal))
            {
                continue;
            }

            settings[preset] = bool.Parse(line[(equals + 1)..].Trim());
        }

        return settings;
    }

    /// <summary>
    /// Reduces a section header to its preset id: <c>preset.0</c> and <c>preset.0.options</c> both
    /// become <c>preset.0</c>, so a key under either section is attributed to the same preset.
    /// </summary>
    private static string PresetId(string sectionHeader)
    {
        var parts = sectionHeader.Split('.');
        return parts.Length >= 2 ? parts[0] + "." + parts[1] : sectionHeader;
    }
}
