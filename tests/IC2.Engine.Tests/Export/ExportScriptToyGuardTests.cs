using System.Security.Cryptography;
using System.Text;
using IC2.Engine.Tests.SerializationTests;
using IC2.Engine.Tests.TestInfrastructure;
using Xunit;

namespace IC2.Engine.Tests.Export;

/// <summary>
/// Review round 1 N2: proves that <c>scripts/export-classical-world.cs</c>'s own "#299 guard" --
/// the <c>if (toyMentions.Count &gt; 0) throw ...</c> check, immediately before the ruleset is
/// loaded -- actually does something. Before this test, no automated check depended on it: the
/// reviewer disabled that one line and all 30 <c>Export</c> tests still passed.
/// <see cref="NoToyProvenanceWordingTests"/> exercises its own, separate copy of the detector
/// against the already-committed files; it says nothing about whether the real script's own copy
/// still fires.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why a scratch copy, not the committed <c>data/rulesets/toy-ruleset.json</c>:</strong>
/// <see cref="ExportScriptReproducibilityTests"/> also runs this exact script as a real
/// <c>dotnet run</c> subprocess against the real, committed files, and bug #320 already documents
/// that two concurrent <c>dotnet run</c> invocations of this script can race. Mutating the
/// committed <c>toy-ruleset.json</c> in place -- even restored in a <c>finally</c> -- would let
/// this test's mutation window overlap a concurrently-running reproducibility test's read of that
/// same file, on top of the already-known subprocess race #320 describes. Instead, this test
/// copies the script (rewriting its two <c>#:project</c> lines to the real, absolute project
/// paths -- the referenced projects themselves are not copied) plus a small scratch mirror of
/// <c>data/rulesets/toy-ruleset.json</c> (mutated to reintroduce a toy-only note) and
/// <c>tests/fixtures/corpus.json</c> (copied unmodified, needed for the corpus cross-check) into a
/// throwaway directory, and runs entirely there. Nothing under version control is ever touched.
/// </para>
/// <para>
/// Review round 2 R1-N1: also runs a real <c>dotnet run</c> of this same file-based script, so it
/// joins the same non-parallel collection
/// <c>tests/IC2.Engine.Tests/Serialization/WorldTerrainSidecarTests.cs</c>'s own
/// <c>WorldTerrainExportReproducibilityTests</c> already uses for the identical reason (review
/// round 0 finding B1 there: two concurrent compiles of the same file-based app collide in its
/// shared content-hashed <c>obj/</c> directory). That file is outside this task's Owns list, so it
/// is only referenced here, never edited -- xUnit runs every non-parallel collection strictly
/// after every parallel collection (including <see cref="ExportScriptReproducibilityTests"/>'s own
/// default one) has finished, so joining the same named collection is enough to rule out a race
/// with that test too, without touching it either.
/// </para>
/// <para>
/// Review round 2 R1-N2: the scratch script's own path is now stable across runs (cleared and
/// recreated fresh each time) rather than GUID-named, so repeated runs reuse the same
/// <c>dotnet run</c> build cache directory instead of leaving a new one behind every time -- see
/// the method body for why a stable path is now safe given R1-N1's non-parallel collection.
/// </para>
/// <para>
/// Review round 3 R2-B1: that stable path is now suffixed with a short hash of this worktree's own
/// repository root, so it is stable per worktree rather than one single path shared by every
/// worktree on the machine -- xUnit's non-parallel collection (R1-N1) only serialises tests within
/// one <c>dotnet test</c> process, so two worktrees (an implementer's and a reviewer's, say)
/// running this test at the same time is a real, reproduced scenario, not a hypothetical one.
/// </para>
/// <para>
/// T74 (bug #320): joins <see cref="DotnetRunScriptCollection"/>, the single non-parallel
/// collection every dotnet-run test now shares in one place (superseding the narrower,
/// piecemeal-shared <c>WorldTerrainExportScriptCollection</c> this class used to join) -- the same
/// "runs strictly after every parallel collection" property this class's own R1-N1 note already
/// relied on still holds, now for every dotnet-run test rather than two of them. The script's own
/// <c>dotnet run</c> subprocess now goes through the shared <see cref="DotnetRunScriptRunner.Run"/>
/// against <see cref="DotnetRunArtifactsFixture"/>'s isolated build-output directory; the scratch
/// tree this test builds the rewritten script and mutated ruleset into (below) is a separate,
/// unrelated concern and is unchanged.
/// </para>
/// </remarks>
[Collection(DotnetRunScriptCollection.Name)]
public class ExportScriptToyGuardTests
{
    public ExportScriptToyGuardTests(DotnetRunArtifactsFixture artifacts)
    {
        _artifacts = artifacts;
    }

    private readonly DotnetRunArtifactsFixture _artifacts;

    [SkippableFact]
    public void The_scripts_own_toy_guard_throws_before_writing_when_a_toy_note_is_reintroduced()
    {
        Skip.IfNot(OriginalFilesAvailability.IsConfigured, OriginalFilesAvailability.SkipReason);

        // Review round 2 R1-N2: a stable path, not a GUID -- "dotnet run" on a file-based app keys
        // its build cache (%TEMP%\dotnet\runfile\<name>-<hash>\) off the script's own path, so a
        // fresh GUID directory every run means a fresh, never-reused cache directory every run,
        // left behind indefinitely (the reviewer measured +1 directory, ~1.8 MB, per run). A stable
        // path lets "dotnet run" reuse the same cache directory on every run of this test, the same
        // way the two hand-run "touch + rebuild" cycles during development already reused it.
        // Cleared unconditionally before use (never inside the try/finally below) so a prior run
        // that crashed before its own `finally` -- the one case a stable path could leave a stale
        // scratch tree behind -- can never affect this run's result.
        //
        // Review round 3 R2-B1: round 1's single fixed path (no per-worktree suffix) was shared by
        // every worktree on the machine -- an implementer's and a reviewer's worktree running this
        // test at the same time raced on both the scratch tree itself and the shared runfile cache
        // it produced (reproduced 3 of 4 concurrent pairs). Suffixing the path with a short hash of
        // this worktree's own repository root keeps it stable *within* one worktree (R1-N2's cache
        // reuse still holds -- same worktree, same path, every run) while giving every worktree on
        // the machine its own path and its own runfile cache, so two worktrees can no longer
        // collide on either.
        var worktreeHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ExportedDataPaths.RepositoryRoot)))[..12];
        var scratchDir = Path.Combine(Path.GetTempPath(), $"ic2-export-toyguard-scratch-{worktreeHash}");
        if (Directory.Exists(scratchDir))
        {
            Directory.Delete(scratchDir, recursive: true);
        }

        var tempIni = Path.Combine(Path.GetTempPath(), "ic2-export-toyguard-ini-" + Guid.NewGuid().ToString("N") + ".ini");
        try
        {
            var scratchScriptsDir = Path.Combine(scratchDir, "scripts");
            var scratchRulesetsDir = Path.Combine(scratchDir, "data", "rulesets");
            var scratchFixturesDir = Path.Combine(scratchDir, "tests", "fixtures");
            Directory.CreateDirectory(scratchScriptsDir);
            Directory.CreateDirectory(scratchRulesetsDir);
            Directory.CreateDirectory(scratchFixturesDir);

            // The script's own two project references, rewritten from relative ("../src/...") to
            // the real repository's absolute paths -- the projects themselves are not copied.
            var realScriptText = File.ReadAllText(ExportedDataPaths.ExportScript);
            var realDataProject = Path.GetFullPath(Path.Combine(ExportedDataPaths.RepositoryRoot, "src", "IC2.Data", "IC2.Data.csproj"));
            var realEngineProject = Path.GetFullPath(Path.Combine(ExportedDataPaths.RepositoryRoot, "src", "IC2.Engine", "IC2.Engine.csproj"));
            var scratchScriptText = realScriptText
                .Replace("#:project ../src/IC2.Data/IC2.Data.csproj", $"#:project {realDataProject}", StringComparison.Ordinal)
                .Replace("#:project ../src/IC2.Engine/IC2.Engine.csproj", $"#:project {realEngineProject}", StringComparison.Ordinal);
            Assert.NotEqual(realScriptText, scratchScriptText); // both #:project lines were actually found and rewritten
            var scratchScriptPath = Path.Combine(scratchScriptsDir, "export-classical-world.cs");
            File.WriteAllText(scratchScriptPath, scratchScriptText);

            // toy-ruleset.json, mutated to reintroduce bug #299's exact reported text (classical-faithful.json:870
            // on main at 7e18d83) -- the guard's whole job is to refuse to ship this.
            var toyRulesetPath = Path.Combine(ExportedDataPaths.RepositoryRoot, "data", "rulesets", "toy-ruleset.json");
            var mutatedToyRuleset = File.ReadAllText(toyRulesetPath)
                .Replace(
                    "the confirmed state machine only, with no AI opinion-score layer (audit Q3).",
                    "this toy ruleset is set the way classical-faithful is set, so tests exercise the confirmed behaviour by default (audit Q3).",
                    StringComparison.Ordinal);
            Assert.Contains("this toy ruleset", mutatedToyRuleset, StringComparison.Ordinal); // the replacement actually took
            File.WriteAllText(Path.Combine(scratchRulesetsDir, "toy-ruleset.json"), mutatedToyRuleset);

            File.Copy(ExportedDataPaths.CorpusFile, Path.Combine(scratchFixturesDir, "corpus.json"));

            var datPath = OriginalFilesAvailability.DatPath
                          ?? throw new InvalidOperationException("DatPath is null despite IsConfigured being true.");
            var assetsDirectory = Path.GetDirectoryName(datPath)
                                   ?? throw new InvalidOperationException($"'{datPath}' has no directory component.");
            File.WriteAllText(tempIni, $"[assets]{Environment.NewLine}directory = {assetsDirectory}{Environment.NewLine}");

            var (exitCode, stdout, stderr) = RunScript(scratchScriptPath, tempIni, scratchDir);

            Assert.NotEqual(0, exitCode);
            var combined = stdout + stderr;
            Assert.Contains("mention \"toy\"", combined, StringComparison.Ordinal);
            Assert.Contains("flags._provenance.diplomacyModel", combined, StringComparison.Ordinal);

            // The guard runs before the only write step -- classical-faithful.json must not exist
            // in the scratch tree at all.
            var scratchClassicalFaithful = Path.Combine(scratchDir, "data", "rulesets", "classical-faithful.json");
            Assert.False(File.Exists(scratchClassicalFaithful),
                "The script wrote classical-faithful.json despite the reintroduced toy wording -- the guard did not fire.");
        }
        finally
        {
            if (File.Exists(tempIni))
            {
                File.Delete(tempIni);
            }

            if (Directory.Exists(scratchDir))
            {
                Directory.Delete(scratchDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// T74 (bug #320): routes through the shared <see cref="DotnetRunScriptRunner.Run"/>, in its own
    /// "export-toy-guard" subdirectory of <see cref="DotnetRunArtifactsFixture.ArtifactsPath"/> --
    /// never the fixture's shared root directly, since this test's scratch copy of the script has the
    /// same file name as the real <c>export-classical-world.cs</c>
    /// <see cref="ExportScriptReproducibilityTests"/> also builds through the same fixture, and
    /// sharing one output path between two differently-sourced same-named scripts would corrupt
    /// MSBuild's up-to-date check for both.
    /// </summary>
    private (int ExitCode, string Stdout, string Stderr) RunScript(string scriptPath, string iniPath, string workingDirectory)
    {
        var result = DotnetRunScriptRunner.Run(
            scriptPath,
            Path.Combine(_artifacts.ArtifactsPath, "export-toy-guard"),
            workingDirectory,
            iniPath);
        return (result.ExitCode, result.Stdout, result.Stderr);
    }
}
