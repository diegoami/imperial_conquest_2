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
/// <c>dotnet run</c> invocations of the same script were shown to collide in. Confirmed against a
/// script path never built before on this machine: with the flag, the real <c>obj/</c> and
/// <c>bin/</c> content lands in the directory this fixture owns, and only a single, small (about 1 KB)
/// <c>build-start.cache</c> file -- no subdirectories -- remains directly under
/// <c>%TEMP%\dotnet\runfile\&lt;script-hash&gt;\</c> (see <see cref="DotnetRunScriptRunner"/>'s own
/// remarks for what removes even that, and why only that).
/// </para>
/// <para>
/// <b>Why one shared directory, not one per test.</b> Review round 1 measured a fresh
/// <c>--artifacts-path</c> build of the export script at about 2.9 s with warm build servers --
/// refuting this class's own earlier, cold-machine estimate of ~37 s. Across a full
/// <c>dotnet test</c> run the isolation still costs a few seconds end to end (round 1's own alternating
/// measurement: <c>origin/main</c> warm at ~15 s for <c>IC2.Engine.Tests</c>, this branch warm at
/// ~18-19 s), because only the <em>first</em> dotnet-run test to build a given script within one
/// <c>dotnet test</c> process pays the fresh-directory build; every other test that runs the same
/// script within the same process reuses the result. Sharing one fixture-owned directory across the
/// whole collection is what keeps that cost to once per distinct script per <c>dotnet test</c> process
/// rather than once per test -- still fully isolated from any other worktree's or developer's
/// concurrent <c>dotnet test</c> run, since each process constructs its own fixture with its own
/// freshly-named directory.
/// </para>
/// </remarks>
public sealed class DotnetRunArtifactsFixture : IDisposable
{
    /// <summary>The directory every dotnet-run test in the collection passes to <c>--artifacts-path</c>.</summary>
    public string ArtifactsPath { get; }

    public DotnetRunArtifactsFixture()
    {
        ArtifactsPath = Path.Combine(
            Path.GetTempPath(),
            "ic2-dotnet-run-artifacts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(ArtifactsPath);
    }

    /// <summary>
    /// Deletes this fixture's own <see cref="ArtifactsPath"/>. Nothing under
    /// <c>%TEMP%\dotnet\runfile\</c> is touched here -- see <see cref="DotnetRunScriptRunner"/>'s own
    /// remarks for why that cleanup happens per <c>Run</c> call instead, and review round 1 finding B1
    /// for why a fixture-wide, construction-time snapshot was the wrong place for it: this fixture's
    /// own lifetime (one whole <c>dotnet test</c> process, often tens of seconds) is far longer than
    /// any one <c>dotnet run</c> subprocess, so a diff taken across the whole fixture's lifetime could
    /// -- and, reproduced by the reviewer, did -- delete a directory an unrelated, concurrent
    /// <c>dotnet run</c> (another worktree, another developer, or a worktree still on pre-T74 code)
    /// created and was still actively using.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(ArtifactsPath))
        {
            Directory.Delete(ArtifactsPath, recursive: true);
        }
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
    /// <para>
    /// Callers that share a <see cref="DotnetRunArtifactsFixture"/> must each pass their own
    /// subdirectory of <see cref="DotnetRunArtifactsFixture.ArtifactsPath"/> (a distinct one per
    /// distinct script), never that shared root directly: two different scripts with the same file
    /// name (this project has exactly this case -- a scratch, rewritten copy of
    /// <c>export-classical-world.cs</c> alongside the real one) would otherwise write their build
    /// output to the same path inside one artifacts root and corrupt each other's up-to-date check.
    /// </para>
    /// <para>
    /// <b>Review round 1, finding B1.</b> <c>dotnet run</c> still leaves a small
    /// <c>build-start.cache</c> file directly under <c>%TEMP%\dotnet\runfile\&lt;script-hash&gt;\</c>
    /// even with <c>--artifacts-path</c> set (Done-when 2 asks that no run leave a directory behind).
    /// The first version of this cleanup snapshotted <c>%TEMP%\dotnet\runfile\</c> once, when the
    /// fixture was constructed, and deleted every entry that was new when the fixture was disposed --
    /// a diff over the entire collection's lifetime, on a directory every `dotnet run` process on the
    /// machine shares. The reviewer reproduced real damage from it: a concurrent, unrelated,
    /// non-isolated <c>dotnet run</c> (an <c>origin/main</c> worktree, still without
    /// <c>--artifacts-path</c>, so its own <em>real</em> <c>obj/</c>/<c>bin/</c> content lives directly
    /// under its runfile entry) had that entry deleted out from under it while it was still building,
    /// and failed with <c>FileNotFoundException</c>.
    /// </para>
    /// <para>
    /// Fixed by narrowing the cleanup two ways, both required: the snapshot is now taken immediately
    /// before <em>this one call</em> starts its subprocess and compared immediately after that same
    /// subprocess exits -- a window bounded by one <c>dotnet run</c> invocation (a few seconds to tens
    /// of seconds), not by the whole collection's run, and calls within the collection are already
    /// serialised (<see cref="DotnetRunScriptCollection"/> is <c>DisableParallelization = true</c>), so
    /// no other call of this fixture's own can overlap it. And every newly-appeared entry is deleted
    /// only if it holds no subdirectory at all (<see cref="IsMarkerOnly"/>) -- an isolated run's own
    /// entry is exactly one small file, while a real, non-isolated build's entry always has an
    /// <c>obj/</c> subdirectory (and usually <c>bin/</c> too), so this check refuses to delete real
    /// build content even if the narrow window were somehow still hit. A concurrent process's own
    /// isolated marker (also just one file, also newly-appeared) could in principle still be deleted by
    /// this narrow window; unlike deleting active build content, that only costs that other process one
    /// avoidable cold rebuild the next time it runs the same script, never a broken run.
    /// </para>
    /// </remarks>
    public static Result Run(
        string scriptPath,
        string artifactsPath,
        string? workingDirectory = null,
        params string[] scriptArguments)
    {
        Directory.CreateDirectory(artifactsPath);

        var arguments = new List<string> { "run", scriptPath, "--artifacts-path", artifactsPath };
        if (scriptArguments.Length > 0)
        {
            arguments.Add("--");
            arguments.AddRange(scriptArguments);
        }

        var psi = new System.Diagnostics.ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory ?? ExportedDataPaths.RepositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        var runfileEntriesBeforeThisCall = SnapshotRunfileEntries();

        using var process = System.Diagnostics.Process.Start(psi)
                             ?? throw new InvalidOperationException("Failed to start dotnet run.");

        // Read stdout and stderr concurrently, not one after the other: a script that writes more
        // than the OS pipe buffer to stderr while stdout is still being drained sequentially would
        // otherwise deadlock (the child blocks writing to a full stderr pipe that nobody is reading,
        // while this process blocks waiting for stdout to finish).
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WaitAll(stdoutTask, stderrTask);

        var result = new Result(process.ExitCode, stdoutTask.Result, stderrTask.Result);

        DeleteMarkerOnlyEntriesCreatedDuringThisCall(runfileEntriesBeforeThisCall);

        return result;
    }

    /// <summary>See the remarks on <see cref="Run"/> ("Review round 1, finding B1") for why this is
    /// scoped to one call's own before/after snapshot, and to marker-only entries.</summary>
    private static void DeleteMarkerOnlyEntriesCreatedDuringThisCall(HashSet<string> runfileEntriesBefore)
    {
        foreach (var entry in SnapshotRunfileEntries())
        {
            if (runfileEntriesBefore.Contains(entry))
            {
                continue;
            }

            if (!IsMarkerOnly(entry))
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

    /// <summary>
    /// Whether <paramref name="runfileEntryDirectory"/> holds nothing but the small
    /// <c>build-start.cache</c> marker file an isolated <c>dotnet run</c> still leaves under
    /// <c>%TEMP%\dotnet\runfile\</c> -- no subdirectory at all. A real, non-isolated build's entry
    /// always has an <c>obj/</c> subdirectory (and usually <c>bin/</c>), so this is enough to tell the
    /// two apart without depending on file names or sizes that could change with the SDK version.
    /// Returns <see langword="false"/> (never delete) if the check itself cannot be completed.
    /// </summary>
    private static bool IsMarkerOnly(string runfileEntryDirectory)
    {
        try
        {
            return Directory.GetDirectories(runfileEntryDirectory).Length == 0;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
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
