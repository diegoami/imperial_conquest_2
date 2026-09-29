using System.Text;
using Godot;
using IC2.Data;

namespace IC2.Slice.Checks;

/// <summary>
/// <c>docs/tasks/T94.md</c> Done-when 2: <see cref="MapViewer"/>'s "details unavailable" fallbacks
/// (the recruitment / nation / calendar <c>catch (InvalidDataException)</c> blocks) also catch
/// <see cref="IC2.Data.UnrecognizedSaveFormatException"/>, so a save that is neither DAT- nor
/// SAV-shaped degrades instead of escaping <c>_Ready</c> as an uncaught exception. This check feeds
/// two deliberately malformed files — both generated here, never an original game file — through the
/// viewer's own load path, and exits 0 or 1 with a <c>PASS</c>/<c>FAIL</c> line per assertion.
/// </summary>
/// <remarks>
/// <para>
/// Run headless via:
/// <code>
/// godot --headless --path godot res://Checks/MalformedSaveCheck.tscn --quit-after 5
/// </code>
/// Each generated file is written under <c>user://t94-malformed-save-check/</c> and deleted in the
/// <c>finally</c> block, so the check leaves no state behind and needs neither
/// <c>assets.local.ini</c> nor the original DAT (without one, <see cref="MapViewer"/>'s own
/// <c>_Ready</c> reports the missing config and the load path below still runs).
/// </para>
/// <para>
/// The first file is a syntactically valid <see cref="WorldPrefix"/> (the shared DAT/SAV map + city
/// prefix) followed by bytes that make a 0xABAB army count: it parses as a world but not as a save,
/// so <see cref="SaveFormat.Detect"/> raises <see cref="UnrecognizedSaveFormatException"/> — the
/// exact exception the three detail-table parsers throw for it, and the one their catches now cover.
/// The second is shorter than the shared prefix, which <see cref="WorldPrefix.Parse"/> rejects with
/// <see cref="InvalidDataException"/>; the viewer's outer catch must degrade on that too.
/// </para>
/// </remarks>
public partial class MalformedSaveCheck : Node
{
    private readonly List<string> _generated = new();
    private bool _ok = true;

    public override void _Ready()
    {
        try
        {
            Run();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"MalformedSaveCheck: unhandled exception: {ex}");
            _ok = false;
        }
        finally
        {
            CleanUp();
        }

        var exitCode = _ok ? 0 : 1;
        GD.Print($"MalformedSaveCheck: exiting with code {exitCode}.");
        GetTree().Quit(exitCode);
    }

    private void Run()
    {
        var directory = Path.Combine(ProjectSettings.GlobalizePath("user://"), "t94-malformed-save-check");
        Directory.CreateDirectory(directory);

        var unrecognizedShaped = BuildValidPrefixWithGarbageTail();
        var unrecognizedPath = Path.Combine(directory, "unrecognized-shape.sav");
        File.WriteAllBytes(unrecognizedPath, unrecognizedShaped);
        _generated.Add(unrecognizedPath);

        // The failure this file triggers really is the new exception type: Detect rejects it outright.
        try
        {
            SaveFormat.Detect(unrecognizedShaped);
            Check(false, "the generated unrecognized-shape file is rejected by SaveFormat.Detect");
        }
        catch (UnrecognizedSaveFormatException)
        {
            Check(true, "the generated unrecognized-shape file raises UnrecognizedSaveFormatException");
        }

        // All three optional detail tables degrade through the same parse path LoadSource uses.
        var failures = MapViewer.ParseSaveDetailsForCheck(unrecognizedShaped, "unrecognized-shape.sav");
        Check(
            failures.Count == 3,
            $"all three detail tables degrade instead of throwing (messages: {string.Join(" | ", failures)})");
        Check(
            failures.All(f => f.Contains("unavailable for unrecognized-shape.sav", StringComparison.Ordinal)),
            "each degradation message names the file it came from");

        // End to end: the viewer's own load path reports the failure and keeps running.
        var viewer = new MapViewer();
        AddChild(viewer);
        var unrecognizedStatus = viewer.LoadSourceForCheck(unrecognizedPath);
        Check(
            unrecognizedStatus.StartsWith("Could not load", StringComparison.Ordinal),
            $"MapViewer degrades on the unrecognized-shape file instead of crashing (status: '{unrecognizedStatus}')");

        var truncationPath = Path.Combine(directory, "truncated.sav");
        File.WriteAllBytes(truncationPath, Enumerable.Repeat((byte)0xAB, 128).ToArray());
        _generated.Add(truncationPath);
        var truncationStatus = viewer.LoadSourceForCheck(truncationPath);
        Check(
            truncationStatus.StartsWith("Could not load", StringComparison.Ordinal),
            $"MapViewer degrades on a truncated file instead of crashing (status: '{truncationStatus}')");
    }

    /// <summary>
    /// A file long enough to parse as a <see cref="WorldPrefix"/> (valid map cells and city records)
    /// but shaped as neither a DAT nor a SAV: the byte after the shared prefix makes the army count
    /// an implausible 0xABAB, so <see cref="SaveFormat.Detect"/> fails the SAV walk and the length is
    /// not the DAT's fixed one. Generated rather than committed so no fixture is mistaken for an
    /// original game file.
    /// </summary>
    private static byte[] BuildValidPrefixWithGarbageTail()
    {
        var data = new byte[120_000];
        for (var i = WorldPrefix.SharedPrefixLength; i < data.Length; i++)
        {
            data[i] = 0xAB;
        }

        for (var i = 0; i < WorldPrefix.CityCount; i++)
        {
            var offset = WorldPrefix.CityStart + i * WorldPrefix.CityRecordLength;
            var name = Encoding.ASCII.GetBytes($"city{i}");
            Array.Copy(name, 0, data, offset, name.Length);
            // The two coordinate words at +14/+16 stay (0, 0), inside the candidate 320 x 140 map.
        }

        return data;
    }

    private void CleanUp()
    {
        try
        {
            foreach (var path in _generated)
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }

            var directory = Path.GetDirectoryName(_generated.FirstOrDefault());
            if (directory is not null && Directory.Exists(directory) && Directory.GetFileSystemEntries(directory).Length == 0)
            {
                Directory.Delete(directory);
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"MalformedSaveCheck: could not clean up generated files: {ex.Message}");
        }
    }

    private bool Check(bool condition, string description)
    {
        GD.Print(condition ? $"PASS: {description}" : $"FAIL: {description}");
        _ok &= condition;
        return condition;
    }
}
