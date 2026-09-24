using System.Diagnostics;
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
/// </remarks>
public class ExportScriptToyGuardTests
{
    [SkippableFact]
    public void The_scripts_own_toy_guard_throws_before_writing_when_a_toy_note_is_reintroduced()
    {
        Skip.IfNot(OriginalFilesAvailability.IsConfigured, OriginalFilesAvailability.SkipReason);

        var scratchDir = Path.Combine(Path.GetTempPath(), "ic2-export-toyguard-" + Guid.NewGuid().ToString("N"));
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

    private static (int ExitCode, string Stdout, string Stderr) RunScript(string scriptPath, string iniPath, string workingDirectory)
    {
        var psi = new ProcessStartInfo("dotnet", $"run \"{scriptPath}\" \"{iniPath}\"")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start dotnet run.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdout, stderr);
    }
}
