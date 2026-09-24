using Xunit;

namespace IC2.Engine.Tests.TestInfrastructure;

/// <summary>
/// The regression test <see cref="DotnetRunScriptRunner.Run"/>'s own remarks promise: proves, against
/// a real <c>dotnet run</c> of a trivial, uniquely-named scratch script (no DAT required), that
/// <see cref="DotnetRunScriptRunner.ComputeRunfileEntryName"/> still predicts the exact
/// <c>%TEMP%\dotnet\runfile\</c> directory name the installed SDK actually creates. If a future SDK
/// version changes how it derives that name, this test starts failing -- loudly, in CI, on every run
/// -- rather than the runner's fail-safe check silently leaving a 1 KB marker behind forever because
/// it can no longer find (and was never designed to guess at) the entry it used to clean up.
/// </summary>
[Collection(DotnetRunScriptCollection.Name)]
public class RunfileEntryNamingTests
{
    public RunfileEntryNamingTests(DotnetRunArtifactsFixture artifacts)
    {
        _artifacts = artifacts;
    }

    private readonly DotnetRunArtifactsFixture _artifacts;

    [Fact]
    public void The_computed_runfile_entry_name_matches_what_the_SDK_actually_creates()
    {
        // A fresh, uniquely-named scratch script every run: its computed runfile entry name can only
        // ever be this test's own, so there is no ambiguity about whose entry gets checked.
        var scriptDirectory = Path.Combine(
            Path.GetTempPath(), "ic2-runfile-naming-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scriptDirectory);
        var scriptPath = Path.Combine(scriptDirectory, "probe.cs");
        File.WriteAllText(scriptPath, "System.Console.WriteLine(\"ic2-runfile-naming-probe-ok\");" + Environment.NewLine);

        try
        {
            var result = DotnetRunScriptRunner.Run(
                scriptPath,
                Path.Combine(_artifacts.ArtifactsPath, "runfile-naming-probe"),
                scriptDirectory);

            Assert.True(
                result.ExitCode == 0,
                $"the scratch probe script exited {result.ExitCode}.\nstdout:\n{result.Stdout}\nstderr:\n{result.Stderr}");

            Assert.True(
                result.RunfileMarkerWasCleanedUp,
                "DotnetRunScriptRunner.ComputeRunfileEntryName's computed name did not match the "
                + "runfile entry the SDK actually created for this run, so the fail-safe check in "
                + "Run() correctly did nothing -- but that means the naming scheme this repository "
                + "reverse-engineered (see Run()'s own remarks) no longer matches the installed SDK, "
                + "and Run()'s runfile cleanup has silently stopped working.");
        }
        finally
        {
            if (Directory.Exists(scriptDirectory))
            {
                Directory.Delete(scriptDirectory, recursive: true);
            }

            // Belt and braces: if the formula was right and cleanup already ran, this is a no-op: the
            // entry is already gone. If the formula was wrong, this test has already failed above and
            // there is nothing of this test's own left under %TEMP%\dotnet\runfile\ to leak, since
            // Run() never touches an entry it cannot compute by name.
        }
    }
}
