using IC2.Engine.Tests.Export;
using Xunit;

namespace IC2.Engine.Tests.TestInfrastructure;

/// <summary>
/// Names the single, non-parallel xUnit collection every test that invokes
/// <c>dotnet run &lt;script&gt;.cs</c> belongs to (bug #320 / T74, Done-when 1).
/// </summary>
/// <remarks>
/// <para>
/// <c>dotnet run</c> on a file-based app (<c>scripts/export-classical-world.cs</c>) compiles into a
/// content-hashed cache directory under <c>%TEMP%\dotnet\runfile\</c>. Before this collection existed,
/// each test that ran the script either sat in xUnit's default (parallel) collection
/// (<c>ExportScriptReproducibilityTests</c>) or in its own narrowly-named, ad hoc non-parallel
/// collection reused piecemeal by later tests (<c>WorldTerrainExportScriptCollection</c>,
/// <c>ExportScriptToyGuardTests</c> joining it in review round 2). Two of those tests running at the
/// same time collide in that shared cache directory -- T62's review reproduced <c>CS2012: Cannot open
/// ... for writing</c> 3/3 by running two of them together, and bug #320 records four further,
/// non-reproducing failures (a resource-file build error among them) consistent with the identical
/// race. Every dotnet-run test now shares this one collection, defined in this one place, so no later
/// test can reintroduce the race by picking its own separate non-parallel collection.
/// </para>
/// <para>
/// <see cref="DotnetRunArtifactsFixture"/> is attached here (<see cref="ICollectionFixture{TFixture}"/>)
/// so every test in the collection shares one isolated build-output directory for the lifetime of one
/// <c>dotnet test</c> process (Done-when 2) -- constructed once, before the first test in the
/// collection runs, and disposed once after the last one finishes.
/// </para>
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DotnetRunScriptCollection : ICollectionFixture<DotnetRunArtifactsFixture>
{
    /// <summary>The collection name every dotnet-run test declares via <c>[Collection(Name)]</c>.</summary>
    public const string Name = "dotnet-run-script";
}

/// <summary>
/// One isolated build-output root, shared by every test in <see cref="DotnetRunScriptCollection"/> for
/// the lifetime of one <c>dotnet test</c> process, and deleted when that process's tests finish.
/// </summary>
/// <remarks>
/// <para>
/// <b>What isolates the build, and why.</b> <c>dotnet run</c>'s own <c>--artifacts-path</c> option
/// (.NET SDK docs, "dotnet run command - .NET CLI",
/// https://learn.microsoft.com/dotnet/core/tools/dotnet-run#options : "All build output files from
/// the executed command will go in subfolders under the specified path, separated by project. ...
/// Available since .NET 8 SDK." -- cross-referencing the SDK's "Artifacts Output Layout" page)
/// redirects a file-based app's build output away from the default, content-hashed
/// <c>%TEMP%\dotnet\runfile\&lt;script-hash&gt;\obj</c> directory -- the exact directory two concurrent
/// <c>dotnet run</c> invocations of the same script were shown to collide in. Measured against this
/// repository's own export script: without the flag, a run writes roughly 1.8 MB of <c>obj/</c> and
/// <c>bin/</c> content into that shared directory (matching review round 1's measurement for
/// <c>ExportScriptToyGuardTests</c>); with the flag pointed at a directory this fixture owns, the real
/// <c>obj/</c> and <c>bin/</c> content lands there instead, and only a small (about 1 KB)
/// <c>build-start.cache</c> marker remains under <c>%TEMP%\dotnet\runfile\</c> (see
/// <see cref="DisposeRunfileMarkers"/> for what removes even that).
/// </para>
/// <para>
/// <b>Why one shared directory, not one per test.</b> Re-pointing <c>--artifacts-path</c> costs a cold
/// build the first time a given script is compiled against a given artifacts path: measured at ~37 s
/// cold vs ~5 s once the same (script, artifacts-path) pair has been built before. A fresh directory
/// per individual test would pay the cold-build cost on every one of the several dotnet-run tests in
/// this collection, every run -- the task's own hazard note calls this out explicitly ("a large
/// slowdown is a reason to share one isolated directory inside the collection, not to drop
/// isolation"). Sharing one fixture-owned directory across the whole collection, for the lifetime of
/// one <c>dotnet test</c> process, pays the cold build once per distinct script per run instead: still
/// fully isolated from any other worktree's or developer's concurrent <c>dotnet test</c> run (each
/// process constructs its own fixture, with its own freshly-named directory), while keeping the
/// suite's own wall time close to what it was before isolation.
/// </para>
/// </remarks>
public sealed class DotnetRunArtifactsFixture : IDisposable
{
    /// <summary>The directory every dotnet-run test in the collection passes to <c>--artifacts-path</c>.</summary>
    public string ArtifactsPath { get; }

    private readonly HashSet<string> _runfileEntriesBeforeThisFixture;

    public DotnetRunArtifactsFixture()
    {
        ArtifactsPath = Path.Combine(
            Path.GetTempPath(),
            "ic2-dotnet-run-artifacts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(ArtifactsPath);

        // Snapshotted before any test in the collection runs, so Dispose can tell "a marker this
        // fixture's own runs created" apart from "a marker that predates this process entirely" (a
        // leftover from a previous, unrelated run -- never this fixture's to delete) or "a marker a
        // different, concurrent dotnet test process creates mid-flight" (also never this fixture's).
        _runfileEntriesBeforeThisFixture = SnapshotRunfileEntries();
    }

    public void Dispose()
    {
        if (Directory.Exists(ArtifactsPath))
        {
            Directory.Delete(ArtifactsPath, recursive: true);
        }

        DisposeRunfileMarkers();
    }

    /// <summary>
    /// Removes the small marker directories <c>dotnet run</c> still leaves under
    /// <c>%TEMP%\dotnet\runfile\</c> even with <c>--artifacts-path</c> set (Done-when 2: "no run
    /// leaves a directory behind"), by deleting only the entries that were not present when this
    /// fixture was constructed. This is a diff, not a wipe of the whole <c>runfile</c> directory,
    /// specifically so a concurrent, unrelated <c>dotnet run</c> (another worktree, another developer)
    /// is never touched.
    /// </summary>
    private void DisposeRunfileMarkers()
    {
        foreach (var entry in SnapshotRunfileEntries())
        {
            if (_runfileEntriesBeforeThisFixture.Contains(entry))
            {
                continue;
            }

            try
            {
                Directory.Delete(entry, recursive: true);
            }
            catch (IOException)
            {
                // Best effort: another process may still be using it.
            }
            catch (UnauthorizedAccessException)
            {
                // Best effort, same reason.
            }
        }
    }

    private static HashSet<string> SnapshotRunfileEntries()
    {
        var runfileRoot = Path.Combine(Path.GetTempPath(), "dotnet", "runfile");
        return Directory.Exists(runfileRoot)
            ? new HashSet<string>(Directory.GetDirectories(runfileRoot), StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Runs a file-based C# script (<c>dotnet run &lt;script&gt;.cs</c>) as a subprocess, against the one
/// isolated build-output directory <see cref="DotnetRunArtifactsFixture"/> hands out. Every test that
/// starts a <c>dotnet run</c> script goes through this one method, so
/// <c>DotnetRunScriptCollectionGuardTests</c> can find every one of them by reflection.
/// </summary>
public static class DotnetRunScriptRunner
{
    /// <summary>The exit code and captured output of one <c>dotnet run</c> invocation.</summary>
    public readonly record struct Result(int ExitCode, string Stdout, string Stderr);

    /// <summary>
    /// Runs <paramref name="scriptPath"/> with <paramref name="artifactsPath"/> as its isolated build
    /// output (created if it does not already exist), in <paramref name="workingDirectory"/>
    /// (defaults to the repository root), passing <paramref name="scriptArguments"/> through to the
    /// script's own <c>Main</c>.
    /// </summary>
    /// <remarks>
    /// Callers that share a <see cref="DotnetRunArtifactsFixture"/> must each pass their own
    /// subdirectory of <see cref="DotnetRunArtifactsFixture.ArtifactsPath"/> (a distinct one per
    /// distinct script), never that shared root directly: two different scripts with the same file
    /// name (this project has exactly this case -- a scratch, rewritten copy of
    /// <c>export-classical-world.cs</c> alongside the real one) would otherwise write their build
    /// output to the same path inside one artifacts root and corrupt each other's up-to-date check.
    /// </remarks>
    public static Result Run(
        string scriptPath,
        string artifactsPath,
        string? workingDirectory = null,
        params string[] scriptArguments)
    {
        Directory.CreateDirectory(artifactsPath);

        var arguments = $"run \"{scriptPath}\" --artifacts-path \"{artifactsPath}\"";
        if (scriptArguments.Length > 0)
        {
            arguments += " -- " + string.Join(' ', scriptArguments.Select(a => $"\"{a}\""));
        }

        var psi = new System.Diagnostics.ProcessStartInfo("dotnet", arguments)
        {
            WorkingDirectory = workingDirectory ?? ExportedDataPaths.RepositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = System.Diagnostics.Process.Start(psi)
                             ?? throw new InvalidOperationException("Failed to start dotnet run.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new Result(process.ExitCode, stdout, stderr);
    }
}
