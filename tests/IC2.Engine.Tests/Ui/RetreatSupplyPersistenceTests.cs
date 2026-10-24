using System.Diagnostics;
using IC2.Engine.Model;
using IC2.Engine.Persistence;
using IC2.Engine.Presentation;
using IC2.Engine.Tests.Import;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Ui;

public sealed class RetreatSupplyPersistenceTests
{
    [Fact]
    public void Per_unit_mode_survives_native_save_and_older_saves_load_with_no_mode()
    {
        var session = new GameSession(RealGameData.World, RealGameData.Ruleset,
            RealGameData.Scenario, seedOverride: 23, humanSeatNationId: "rome");
        var army = session.State.Armies.First(a => a.Nation == "rome" && !a.IsEmbarked);
        var plan = new RetreatSupplyPlan(army.Id, army.Nation, IsFleet: false);
        Assert.Empty(session.RetreatSupplyPlans);
        session.SetRetreatSupplyPlan(plan);
        var path = Path.Combine(Path.GetTempPath(), $"ic2-recovery-{Guid.NewGuid():N}.json");
        try
        {
            Assert.Contains(session.Submit($"save {path}").Lines,
                line => line.StartsWith("Saved to", StringComparison.Ordinal));
            var save = SaveManager.LoadFile(path, session.World, session.Ruleset);
            Assert.Equal(plan, new GameSession(session.World, session.Ruleset, session.Scenario,
                save).RetreatSupplyPlanFor(army.Id, isFleet: false));
            Assert.Empty(new GameSession(session.World, session.Ruleset, session.Scenario,
                save with { RetreatSupplyPlans = null }).RetreatSupplyPlans);
            session.ClearRetreatSupplyPlan(army.Id, isFleet: false);
            Assert.Contains(session.Submit($"load {path}").Lines,
                line => line.StartsWith("Loaded", StringComparison.Ordinal));
            Assert.Equal(plan, session.RetreatSupplyPlanFor(army.Id, isFleet: false));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void An_army_mode_requires_explicit_confirmation_before_replacing_a_fixed_target()
    {
        var session = new GameSession(RealGameData.World, RealGameData.Ruleset,
            RealGameData.Scenario, seedOverride: 23, humanSeatNationId: "rome");
        var army = session.State.Armies.First(a => a.Nation == "rome" && !a.IsEmbarked);
        session.SetArmyTravelTarget(new ArmyTravelTarget(army.Id, army.Nation, army.X, army.Y));

        Assert.Throws<InvalidOperationException>(() => session.SetRetreatSupplyPlan(
            new RetreatSupplyPlan(army.Id, army.Nation, IsFleet: false)));
        Assert.Empty(session.RetreatSupplyPlans);
        Assert.True(session.ClearArmyTravelTarget(army.Id));
        session.SetRetreatSupplyPlan(new RetreatSupplyPlan(army.Id, army.Nation, IsFleet: false));
        Assert.NotNull(session.RetreatSupplyPlanFor(army.Id, isFleet: false));
    }

    [Fact]
    public async Task A_second_CLI_process_loads_and_resaves_the_campaign_mode()
    {
        var session = new GameSession(RealGameData.World, RealGameData.Ruleset,
            RealGameData.Scenario, seedOverride: 23, humanSeatNationId: "rome");
        var army = session.State.Armies.First(a => a.Nation == "rome" && !a.IsEmbarked);
        var plan = new RetreatSupplyPlan(army.Id, army.Nation, IsFleet: false);
        session.SetRetreatSupplyPlan(plan);
        var directory = Path.Combine(Path.GetTempPath(), $"ic2-recovery-process-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var input = Path.Combine(directory, "before.json");
        var output = Path.Combine(directory, "after.json");
        var script = Path.Combine(directory, "save-script.txt");
        try
        {
            Assert.Contains(session.Submit($"save {input}").Lines,
                line => line.StartsWith("Saved to", StringComparison.Ordinal));
            File.WriteAllText(script, $"save {output}\nquit\n");
            var testOutput = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var configuration = testOutput.Parent?.Name ?? "Debug";
            var cliDll = Path.Combine(ModelTestPaths.RepositoryRoot, "src", "IC2.Cli", "bin",
                configuration, testOutput.Name, "IC2.Cli.dll");
            Assert.True(File.Exists(cliDll), "Build IC2.sln before the process check.");
            var start = new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, WorkingDirectory = ModelTestPaths.RepositoryRoot,
            };
            foreach (var argument in new[] { cliDll, "--load", input, "--script", script })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start);
            Assert.NotNull(process);
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await process.WaitForExitAsync(timeout.Token);
            Assert.True(process.ExitCode == 0, await stderr);
            Assert.Contains("Saved to", await stdout);
            var saved = SaveManager.LoadFile(output, session.World, session.Ruleset);
            Assert.Equal(plan, Assert.Single(saved.RetreatSupplyPlans!));
            Assert.Equal(plan, new GameSession(session.World, session.Ruleset, session.Scenario,
                saved).RetreatSupplyPlanFor(army.Id, isFleet: false));
        }
        finally
        {
            foreach (var file in new[] { input, output, script })
                if (File.Exists(file)) File.Delete(file);
            if (Directory.Exists(directory)) Directory.Delete(directory);
        }
    }
}
