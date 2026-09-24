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
/// <c>bin/</c> content lands in the directory this fixture owns, and only the SDK's own small (about
/// 1 KB) <c>build-start.cache</c> marker file -- no subdirectories -- remains directly under
/// <c>%TEMP%\dotnet\runfile\&lt;script-hash&gt;\</c>. That marker belongs to the SDK and is left
/// alone (see <see cref="DotnetRunScriptRunner"/>'s own remarks and
/// <c>docs/tasks/T74.md</c> Done-when 2's amendment for why).
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
    /// Deletes this fixture's own <see cref="ArtifactsPath"/> -- the real build output (<c>obj/</c>,
    /// <c>bin/</c>) every dotnet-run test in the collection wrote there. This is the whole of
    /// Done-when 2's "isolated output... which is deleted afterwards": nothing under
    /// <c>%TEMP%\dotnet\runfile\</c> is touched here, or anywhere else in this class -- see
    /// <see cref="DotnetRunScriptRunner"/>'s own remarks for why (round 3, <c>docs/tasks/T74.md</c>
    /// Done-when 2's amendment: the SDK's own small <c>build-start.cache</c> marker entry there
    /// belongs to the SDK, is never a build-output leak, and the runner deletes nothing under
    /// <c>runfile\</c>).
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
    /// <b>This method deletes nothing under <c>%TEMP%\dotnet\runfile\</c>.</b> Rounds 0-2 tried to:
    /// snapshotting the whole directory per fixture lifetime (round 0), narrowing that to one
    /// <c>Run</c> call's window plus an "entry holds no subdirectory" check (round 1), and finally
    /// computing this call's own entry name exactly via a reverse-engineered, undocumented SDK naming
    /// formula, deleting only that precise path (round 2). Round 1's "no subdirectory" check was
    /// disproven directly: the SDK writes its marker file immediately but <c>obj/</c> only appears
    /// once the build reaches restore, 2-900 ms later depending on the script, so the check still hit
    /// other live builds (18 of 96 unrelated builds, and an <c>origin/main</c> worktree's live entry
    /// twice, in the reviewer's 6-round reproduction). Round 2's exact-name approach fixed that (zero
    /// unrelated deletions in a re-run of the same reproduction) but the naming formula does not hold
    /// on every platform -- it failed CI's Linux runner, where the derivation differs from what round
    /// 2 reverse-engineered on Windows.
    /// </para>
    /// <para>
    /// <b>Round 3 (the user's decision at escalation): drop the marker cleanup instead of chasing a
    /// third platform-specific derivation.</b> <c>docs/tasks/T74.md</c> Done-when 2 was amended to
    /// match: it now asks that no run leave <em>build output</em> behind (nothing under the per-run
    /// <c>--artifacts-path</c> directory survives, and no <c>obj/</c> or <c>bin/</c> appears under
    /// <c>%TEMP%\dotnet\runfile\</c>), not that the runner also delete the SDK's own small
    /// <c>build-start.cache</c> marker entry there. That marker belongs to the SDK, was never build
    /// output, and this runner leaves it alone -- on every platform, unconditionally, with no
    /// snapshot, no computed name, and no deletion logic to go wrong.
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

        return new Result(process.ExitCode, stdoutTask.Result, stderrTask.Result);
    }
}
