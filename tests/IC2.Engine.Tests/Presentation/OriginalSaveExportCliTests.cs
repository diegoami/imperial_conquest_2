using System.Diagnostics;
using System.Security.Cryptography;
using IC2.Data;
using IC2.Engine.Import;
using IC2.Engine.Persistence;
using IC2.Engine.Tests.Import;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Presentation;

/// <summary>
/// T185 Done-when 3: the built CLI's exclusive <c>--export-sav</c> mode — success with a full report, the input
/// left byte for byte, no output on a refusal, an existing destination never replaced, and every other
/// argument refused. The template is an original-format file this test writes itself (its interface settings
/// are all an export reads from it), so the test needs no private file.
/// </summary>
public sealed class OriginalSaveExportCliTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"ic2-export-cli-{Guid.NewGuid():N}");
    private static readonly string Classical = Path.Combine(ModelTestPaths.RepositoryRoot, "tests", "fixtures", "saves", "success-treaty-before-attack.json");
    private static readonly string Toy = Path.Combine(ModelTestPaths.RepositoryRoot, "tests", "fixtures", "saves", "toy-3city-turn-10.v1.json");

    public OriginalSaveExportCliTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Template()
    {
        var path = Path.Combine(_dir, "template.sav");
        var save = SaveManager.LoadFile(Classical, RealGameData.World, RealGameData.Ruleset);
        var document = OriginalSaveExporter.ToDocument(save, RealGameData.World, RealGameData.Ruleset, OriginalSaveExportTests.Ui).Document;
        File.WriteAllBytes(path, OriginalSaveCodec.Encode(document));
        return path;
    }

    private static (int Exit, string Out, string Err) Run(string arguments)
    {
        var dll = Path.Combine(ModelTestPaths.RepositoryRoot, "src", "IC2.Cli", "bin",
            new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)).Parent!.Name,
            new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)).Name, "IC2.Cli.dll");
        Skip.IfNot(File.Exists(dll), "IC2.Cli was not built in this configuration.");
        var process = Process.Start(new ProcessStartInfo("dotnet", $"\"{dll}\" {arguments}")
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
            WorkingDirectory = ModelTestPaths.RepositoryRoot,
        })!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return (process.ExitCode, stdout.Result, stderr.Result);
    }

    [SkippableFact]
    public void A_classical_save_exports_with_a_report_and_the_input_is_unchanged()
    {
        var template = Template();
        var output = Path.Combine(_dir, "out.sav");
        var before = SHA256.HashData(File.ReadAllBytes(Classical));
        var (exit, stdout, stderr) = Run($"--export-sav \"{Classical}\" --output \"{output}\" --template \"{template}\"");

        Assert.True(exit == 0, stderr);
        var bytes = File.ReadAllBytes(output);
        OriginalSaveLayout.Validate(bytes);
        Assert.Contains(Convert.ToHexString(SHA256.HashData(bytes)), stdout);
        Assert.Contains("not in the original format: Calendar.TurnIndex", stdout);
        Assert.Contains("separate check", stdout);
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(Classical)));
    }

    [SkippableFact]
    public void A_configured_original_template_exports_without_a_template_argument()
    {
        var template = Template();
        File.WriteAllBytes(Path.Combine(_dir, "Imperial Conquest 2.dat"), [1]);
        var config = Path.Combine(_dir, "assets.local.ini");
        File.WriteAllText(config, $"[assets]\ndirectory = \"{_dir}\"\n[sav-export]\ntemplate = {Path.GetFileName(template)}\n");
        var output = Path.Combine(_dir, "configured.sav");

        var (exit, stdout, stderr) = Run($"--export-sav \"{Classical}\" --output \"{output}\" --config \"{config}\"");

        Assert.True(exit == 0, stderr);
        Assert.Contains($"template '{template}'", stdout);
        OriginalSaveLayout.Validate(File.ReadAllBytes(output));
    }

    [SkippableFact]
    public void An_existing_output_is_never_replaced()
    {
        var template = Template();
        var output = Path.Combine(_dir, "taken.sav");
        File.WriteAllText(output, "mine");
        var (exit, _, stderr) = Run($"--export-sav \"{Classical}\" --output \"{output}\" --template \"{template}\"");
        Assert.Equal(2, exit);
        Assert.Contains("already exists", stderr);
        Assert.Equal("mine", File.ReadAllText(output));
    }

    [SkippableFact]
    public void A_toy_world_save_is_refused_with_its_reason_and_nothing_is_written()
    {
        var template = Template();
        var output = Path.Combine(_dir, "toy.sav");
        var (exit, _, stderr) = Run($"--export-sav \"{Toy}\" --output \"{output}\" --template \"{template}\"");
        Assert.Equal(2, exit);
        Assert.Contains("Refused: UNSUPPORTED_WORLD", stderr);
        Assert.False(File.Exists(output));
    }

    [SkippableTheory]
    [InlineData("--seed 3")]
    [InlineData("--template a.sav")]
    [InlineData("--output a.sav --output b.sav --template t.sav")]
    public void Any_other_argument_or_a_missing_one_is_refused(string rest)
    {
        var (exit, _, stderr) = Run($"--export-sav \"{Classical}\" {rest}");
        Assert.Equal(1, exit);
        Assert.Contains("Usage: IC2.Cli --export-sav", stderr);
        Assert.Empty(Directory.GetFiles(_dir));
    }
}
