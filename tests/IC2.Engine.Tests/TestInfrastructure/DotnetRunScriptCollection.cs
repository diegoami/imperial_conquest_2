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
    /// <summary>
    /// The exit code and captured output of one <c>dotnet run</c> invocation, plus whether its
    /// computed <c>%TEMP%\dotnet\runfile\</c> marker entry was found and cleaned up
    /// (<see cref="RunfileEntryNamingTests"/> is the regression test this backs).
    /// </summary>
    public readonly record struct Result(int ExitCode, string Stdout, string Stderr, bool RunfileMarkerWasCleanedUp);

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
    /// <b>Review round 1, finding B1 (the shared marker cache), and round 2 (still open).</b>
    /// <c>dotnet run</c> still leaves a small <c>build-start.cache</c> file directly under
    /// <c>%TEMP%\dotnet\runfile\&lt;entry&gt;\</c> even with <c>--artifacts-path</c> set (Done-when 2
    /// asks that no run leave a directory behind). Round 0's cleanup snapshotted the whole
    /// <c>runfile</c> directory once per fixture lifetime and deleted every new entry; round 1
    /// narrowed the window to one <c>Run</c> call and added an "entry holds no subdirectory" check,
    /// reasoning that a real, non-isolated build's entry always has an <c>obj/</c> subdirectory by the
    /// time it could be seen. Round 2's review measured that gap directly: the SDK creates the entry
    /// and writes <c>build-start.cache</c> immediately, and <c>obj/</c> appears only once the build
    /// reaches restore -- 2 ms to 900 ms later depending on the script. A build that starts inside any
    /// one <c>Run</c> call's window is, at that call's after-snapshot, a new entry with no
    /// subdirectory yet. Across 6 rounds of a <c>FileSystemWatcher</c>-instrumented reproduction, the
    /// reviewer's round-1 code deleted 18 of 96 unrelated live builds' entries and an
    /// <c>origin/main</c> worktree's live entry twice. Every victim build recreated its directory and
    /// finished, but "a deletion that can hit another live build" is the defect Done-when 2 exists to
    /// prevent, and "no subdirectory yet" is not proof of ownership at any point before a build reaches
    /// restore.
    /// </para>
    /// <para>
    /// <b>Fixed by computing this call's own entry name exactly, instead of inferring "new" from a
    /// snapshot.</b> The .NET SDK derives a file-based app's runfile entry name as
    /// <c>&lt;script file name without extension&gt;-&lt;lowercase hex SHA-256 of the UTF-8 bytes of
    /// the script's full path, upper-invariant&gt;</c> (see <see cref="ComputeRunfileEntryName"/>).
    /// This is not documented SDK behaviour -- it was established empirically: the review reproduced
    /// the formula on three independent paths on its machine, and this repository's own worktree path
    /// hashes to the exact directory name (<c>897bab08b1e24ff26a490c51275596ce5ebec0836b8c6f4d416cdd8c19c99a34</c>)
    /// this class's own earlier, ad hoc probes had already observed for <c>export-classical-world.cs</c>
    /// there. Because the scheme is undocumented and could change with a future SDK version, deletion
    /// is fail-safe: this method computes the one name that <em>this call's own script path</em> maps
    /// to, and deletes that exact directory only if (a) it did not exist immediately before this call's
    /// subprocess started and (b) it holds no subdirectory when checked immediately after the
    /// subprocess exits. Nothing else under <c>%TEMP%\dotnet\runfile\</c> is ever enumerated, listed, or
    /// touched -- no set difference over the folder, so an unrelated build's entry (which never
    /// happens to have this call's own computed name) cannot be deleted no matter its timing. If the
    /// computed name does not exist after the run -- the SDK changed how it derives the name -- this
    /// method deletes nothing and leaves the (now un-recognised) marker behind rather than guess;
    /// <see cref="RunfileEntryNamingTests.The_computed_runfile_entry_name_matches_what_the_SDK_actually_creates"/>
    /// is the test that would then start failing, surfacing the mismatch instead of it silently
    /// reintroducing marker debris or, worse, a wrong guess deleting something real.
    /// </para>
    /// <para>
    /// <b>Concurrency.</b> Two processes running <em>the same script path from the same worktree</em>
    /// at the same time would compute and share this one entry name, and could still race on it (the
    /// scenario the non-parallel collection already exists to prevent within one process). Two
    /// worktrees never collide here: each has its own absolute path for the script, so each computes
    /// and owns a different entry name. This method does not defend against two dotnet-run tests in
    /// the *same* worktree running concurrently outside this collection; nothing in this codebase does
    /// that today.
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

        var runfileRoot = Path.Combine(Path.GetTempPath(), "dotnet", "runfile");
        var computedEntryPath = Path.Combine(runfileRoot, ComputeRunfileEntryName(scriptPath));
        var computedEntryExistedBeforeThisCall = Directory.Exists(computedEntryPath);

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

        var cleanedUp = !computedEntryExistedBeforeThisCall
                         && DeleteIfMarkerOnly(computedEntryPath);

        return new Result(process.ExitCode, stdoutTask.Result, stderrTask.Result, cleanedUp);
    }

    /// <summary>
    /// The SDK's (undocumented, empirically established -- see <see cref="Run"/>'s own remarks)
    /// runfile entry name for the file-based app at <paramref name="scriptPath"/>.
    /// </summary>
    internal static string ComputeRunfileEntryName(string scriptPath)
    {
        var fullPath = Path.GetFullPath(scriptPath);
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(fullPath.ToUpperInvariant()));
        var hex = Convert.ToHexString(hash).ToLowerInvariant();
        var stem = Path.GetFileNameWithoutExtension(fullPath);
        return $"{stem}-{hex}";
    }

    /// <summary>
    /// Deletes <paramref name="entryPath"/> if, and only if, it exists and holds no subdirectory
    /// (<see cref="Run"/>'s own remarks explain why "no subdirectory yet" is checked only on this one,
    /// precisely computed path rather than used to classify an arbitrary "new" entry). Returns whether
    /// it was deleted.
    /// </summary>
    private static bool DeleteIfMarkerOnly(string entryPath)
    {
        try
        {
            if (!Directory.Exists(entryPath) || Directory.GetDirectories(entryPath).Length != 0)
            {
                return false;
            }

            Directory.Delete(entryPath, recursive: true);
            return true;
        }
        catch (IOException)
        {
            return false; // Best effort: another process may still be using it.
        }
        catch (UnauthorizedAccessException)
        {
            return false; // Best effort, same reason.
        }
    }
}
