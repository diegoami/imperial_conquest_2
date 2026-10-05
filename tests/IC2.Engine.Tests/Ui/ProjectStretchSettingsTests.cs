using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Ui;

/// <summary>
/// Bug #498 part 2, the user's triage decision of 2026-09-29: a window whose shape is not the
/// configured 1500:850 must not get letterboxed. Godot only removes the black side strips when
/// <c>godot/project.godot</c> sets <c>window/stretch/aspect="expand"</c> alongside the existing
/// <c>window/stretch/mode="canvas_items"</c>; with no aspect set, Godot defaults to <c>keep</c> and
/// letterboxes. <see cref="MainGameScreen"/>'s own container layout then gives the extra room to the
/// map, which clips to its own rect (part 1).
/// </summary>
/// <remarks>
/// Reads the committed project file directly (resolved from the repository root, never a copy: this
/// test project does not copy <c>godot/</c> into its output), so CI fails if either line is dropped,
/// renamed or changed. This is the CI-runnable half of part 2; the headless
/// <c>godot/Checks/MapClipCheck.cs</c> additionally asserts that Godot itself resolves the setting,
/// which this text check cannot prove.
/// </remarks>
public sealed class ProjectStretchSettingsTests
{
    private static readonly string ProjectFile =
        Path.Combine(ModelTestPaths.RepositoryRoot, "godot", "project.godot");

    [Fact]
    public void Display_stretch_expands_the_canvas_to_the_window_shape()
    {
        Assert.True(File.Exists(ProjectFile), $"expected the committed Godot project at '{ProjectFile}'");

        var display = ReadSection(ProjectFile, "[display]");

        Assert.True(
            display.TryGetValue("window/stretch/mode", out var mode),
            "godot/project.godot's [display] section must keep window/stretch/mode");
        Assert.Equal("canvas_items", mode);

        Assert.True(
            display.TryGetValue("window/stretch/aspect", out var aspect),
            "godot/project.godot's [display] section must set window/stretch/aspect=expand "
            + "(bug #498 part 2: no letterboxing in a window that is not 1500:850)");
        Assert.Equal("expand", aspect);
    }

    /// <summary>
    /// The key/value pairs of one <c>[section]</c> of a Godot <c>project.godot</c>, with the quotes
    /// around string values stripped and later duplicates (which Godot itself would not write)
    /// resolving to the last line, exactly as Godot's own parser does.
    /// </summary>
    private static Dictionary<string, string> ReadSection(string path, string section)
    {
        var settings = new Dictionary<string, string>(StringComparer.Ordinal);
        var inSection = false;

        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                inSection = string.Equals(line, section, StringComparison.Ordinal);
                continue;
            }

            if (!inSection || line.Length == 0 || line.StartsWith(';'))
            {
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim().Trim('"');
            settings[key] = value;
        }

        return settings;
    }
}
